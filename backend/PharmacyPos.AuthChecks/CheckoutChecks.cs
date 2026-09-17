using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Sales;
using PharmacyPos.Api.Stock;

public static class CheckoutChecks
{
    static void Check(bool pass, string text) { if (!pass) throw new Exception("CHECKOUT CHECK FAILED: " + text); }
    static async Task<HttpResponseMessage> Post(HttpClient c, string url, object data) {
        var session = await c.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(data) };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString()); return await c.SendAsync(request);
    }
    public static async Task Run(HttpClient admin, HttpClient op, HttpClient anonymous, IServiceProvider services, Guid medicineId)
    {
        var brand = new ReceiptSettings.Input(Guid.NewGuid(),null,"Test Pharmacy","Dhaka, Bangladesh","data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAi0lEQVR4nO3XsQ2AMBAEQRdBORRB/81ADw5+LTQvbezTZF7vwF3PvdXErYlHAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABnAOyO+0sA6gF1AOoBdQDqAXUA6gF1AOoBdQDqAXUAJr6cu+MmDsDEIwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMAZAB84LwcUzPM9qwAAAABJRU5ErkJggg==");
        Check((await anonymous.GetAsync("/api/receipt-settings")).StatusCode==HttpStatusCode.Unauthorized,"branding requires sign-in");
        Check((await Post(op,"/api/receipt-settings",brand)).StatusCode==HttpStatusCode.Forbidden,"branding admin only");
        Check((await admin.PostAsJsonAsync("/api/receipt-settings",brand)).StatusCode==HttpStatusCode.BadRequest,"branding CSRF");
        Check((await Post(admin,"/api/receipt-settings",brand with {Logo="data:image/svg+xml,<svg/>"})).StatusCode==HttpStatusCode.BadRequest,"active image content rejected");
        Check((await Post(admin,"/api/receipt-settings",brand)).IsSuccessStatusCode,"branding saved");
        Check((await Post(admin,"/api/receipt-settings",brand)).IsSuccessStatusCode,"branding retry safe");
        // End previous scenarios' rules through their public admin workflow.
        var offers = await admin.GetFromJsonAsync<JsonElement>("/api/offers");
        foreach (var o in offers.EnumerateArray()) await Post(admin,$"/api/offers/{o.GetProperty("id").GetGuid()}/stop",new {});
        var charges = await admin.GetFromJsonAsync<JsonElement>("/api/charges");
        foreach (var c in charges.EnumerateArray()) await Post(admin,$"/api/charges/{c.GetProperty("id").GetGuid()}/stop",new {});
        var today = StockReceiving.ShopToday();
        var r = new StockReceiving.ReceiveInput(Guid.NewGuid(),medicineId,"Cash supplier","CASH",today,"CASH-A",today.AddDays(-1),today.AddDays(20),10,3,30,10,20,0,0,0,600m,"Box",true);
        Check((await Post(admin,"/api/stock/receipts",r)).IsSuccessStatusCode,"fixture receipt");
        var offer = new OfferRules.Input(Guid.NewGuid(),"Cash ten percent",medicineId,"Percentage",10,"Piece",1,today,null);
        await Post(admin,"/api/offers",offer);
        var charge = new ChargeRules.Input(Guid.NewGuid(),"Cash test tax","Percentage",5,true,[]);
        await Post(admin,"/api/charges",charge);
        async Task<CashCheckout.Input> Request(params SalesCounter.CartLine[] lines) {
            var response = await Post(op,"/api/sales/quote",new SalesCounter.CartInput(lines.ToList()));
            Check(response.IsSuccessStatusCode,"quote successful");
            var q = await response.Content.ReadFromJsonAsync<JsonElement>();
            return new(Guid.NewGuid(),lines.ToList(),q.GetProperty("quoteHash").GetString(),1000m);
        }
        var request = await Request(new SalesCounter.CartLine(r.RequestId,5,"Piece"));
        Check((await anonymous.PostAsJsonAsync("/api/sales/checkout",request)).StatusCode == HttpStatusCode.Unauthorized,"anonymous checkout denied");
        Check((await op.PostAsJsonAsync("/api/sales/checkout",request)).StatusCode == HttpStatusCode.BadRequest,"checkout CSRF");
        Check((await Post(op,"/api/sales/checkout",request with { CashReceived = 94 })).StatusCode == HttpStatusCode.BadRequest,"insufficient cash denied");
        Check((await Post(op,"/api/sales/checkout",request with { CashReceived = 1000.001m })).StatusCode == HttpStatusCode.BadRequest,"cash precision enforced");
        using (var scope = services.CreateScope()) Check(!await scope.ServiceProvider.GetRequiredService<PharmacyDbContext>().Sales.AnyAsync(),"rejected checkout has no sale");
        var race = await Task.WhenAll(Post(op,"/api/sales/checkout",request),Post(op,"/api/sales/checkout",request));
        Check(race.All(x => x.IsSuccessStatusCode),"identical concurrent retry succeeds");
        var receipt = await race[0].Content.ReadFromJsonAsync<JsonElement>();
        Check(receipt.GetProperty("total").GetDecimal() == 95 && receipt.GetProperty("payments")[0].GetProperty("change").GetDecimal() == 905,"saved cash and half-up payable");
        Check(receipt.GetProperty("pricing").GetProperty("discountTotal").GetDecimal() == 10 && receipt.GetProperty("pricing").GetProperty("chargeTotal").GetDecimal() == 4.5m,"receipt preserves discount and charge");
        Check((await race[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptNumber").GetString() == receipt.GetProperty("receiptNumber").GetString(),"retry returns same receipt number");
        Check((await Post(op,"/api/sales/checkout",request with { CashReceived = 2000 })).StatusCode == HttpStatusCode.Conflict,"changed duplicate request rejected");
        using (var scope = services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
            Check(await db.Sales.CountAsync() == 1 && await db.Set<SalePayment>().CountAsync() == 1 && await db.SaleStockMovements.SumAsync(m => m.Quantity) == 5,"one sale, payment and deduction");
            Check((await db.ReceivingMovements.SingleAsync(m => m.ReceiptId == r.RequestId)).Quantity == 20,"receiving audit is unchanged");
        }
        var inventory = await op.GetFromJsonAsync<JsonElement>("/api/stock/inventory");
        var item = inventory.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("lotId").GetGuid() == r.RequestId);
        Check(item.GetProperty("onHandUnits").GetInt64() == 15 && item.GetProperty("sellableUnits").GetInt64() == 15,"inventory remaining quantity");
        var saleStock = await op.GetFromJsonAsync<JsonElement>("/api/sales/stock");
        Check(saleStock.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("lotId").GetGuid() == r.RequestId).GetProperty("availableUnits").GetInt64() == 15,"counter remaining quantity");
        var stale = await Request(new SalesCounter.CartLine(r.RequestId,1,"Piece"));
        await Post(admin,$"/api/charges/{charge.RequestId}/stop",new {});
        Check((await Post(op,"/api/sales/checkout",stale)).StatusCode == HttpStatusCode.Conflict,"changed pricing requires review");
        Check((await Post(op,"/api/sales/checkout",request)).IsSuccessStatusCode,"original retry still works after price change");
        var old = await op.GetFromJsonAsync<JsonElement>($"/api/sales/receipts/{request.RequestId}");
        Check(JsonElement.DeepEquals(old.GetProperty("pricing"), receipt.GetProperty("pricing")),"receipt does not recalculate");
        Check(receipt.GetProperty("branding").GetProperty("name").GetString()=="Test Pharmacy","sale uses saved branding");
        Check(receipt.GetProperty("branding").GetProperty("logo").GetString()==brand.Logo,"PNG logo preserved on receipt");
        var brand2=brand with {Id=Guid.NewGuid(),ExpectedId=brand.Id,Name="Updated Hospital"};
        Check((await Post(admin,"/api/receipt-settings",brand2)).IsSuccessStatusCode,"branding updated");
        Check((await Post(admin,"/api/receipt-settings",brand with {Id=Guid.NewGuid()})).StatusCode==HttpStatusCode.Conflict,"stale branding update rejected");
        var historical=await op.GetFromJsonAsync<JsonElement>($"/api/sales/receipts/{request.RequestId}");
        Check(historical.GetProperty("branding").GetProperty("name").GetString()=="Test Pharmacy","historical branding immutable");
        var adminRequest = await Request(new SalesCounter.CartLine(r.RequestId,1,"Piece"));
        Check((await Post(admin,"/api/sales/checkout",adminRequest)).IsSuccessStatusCode,"admin can checkout");
        Check((await op.GetAsync($"/api/sales/receipts/{adminRequest.RequestId}")).StatusCode == HttpStatusCode.NotFound,"operator cannot read another cashier receipt");
        Check((await admin.GetAsync($"/api/sales/receipts/{request.RequestId}")).IsSuccessStatusCode,"admin can read cashier receipts");
        var last = await Request(new SalesCounter.CartLine(r.RequestId,14,"Piece"));
        var competitors = await Task.WhenAll(Post(op,"/api/sales/checkout",last),Post(admin,"/api/sales/checkout",last with { RequestId = Guid.NewGuid() }));
        Check(competitors.Count(x => x.IsSuccessStatusCode) == 1 && competitors.Count(x => x.StatusCode == HttpStatusCode.Conflict) == 1,"two cashiers cannot oversell final units");
        saleStock = await op.GetFromJsonAsync<JsonElement>("/api/sales/stock");
        Check(!saleStock.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("lotId").GetGuid() == r.RequestId),"sold-out stock hidden");
        var expired = r with { RequestId = Guid.NewGuid(), BatchNumber = "CASH-EXPIRY", Pieces = 10 };
        await Post(admin,"/api/stock/receipts",expired);
        var partial = await Request(new SalesCounter.CartLine(expired.RequestId,3,"Piece"));
        Check((await Post(op,"/api/sales/checkout",partial)).IsSuccessStatusCode,"partial sale before expiry");
        var beforeExpiry = await Request(new SalesCounter.CartLine(expired.RequestId,1,"Piece"));
        using (var scope = services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
            var batch = await db.MedicineBatches.SingleAsync(b => b.BatchNumber == "CASH-EXPIRY"); batch.ExpiryDate = today.AddDays(-1); await db.SaveChangesAsync();
        }
        Check((await Post(op,"/api/sales/checkout",beforeExpiry)).StatusCode == HttpStatusCode.Conflict,"expiry rechecked at checkout");
        Check((await Post(admin,"/api/stock/disposals",new StockDisposalEndpoints.DisposeInput(Guid.NewGuid(),expired.RequestId,"Expired remainder disposed",true))).IsSuccessStatusCode,"dispose remaining stock");
        using (var scope = services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
            Check((await db.StockDisposals.SingleAsync(d => d.ReceiptId == expired.RequestId)).Quantity == 7,"disposal excludes sold units");
            Check(!await db.Sales.AnyAsync(s => s.Id == beforeExpiry.RequestId || s.Id == stale.RequestId),"rejected sales do not persist");
        }
        Console.WriteLine("PASS: atomic cash checkout, payments, change, CSRF, immutable receipts, retry recovery, stock balances, competing cashiers, price/expiry changes and disposal remainder.");
    }
}
