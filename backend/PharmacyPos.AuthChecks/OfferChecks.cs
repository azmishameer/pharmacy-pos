using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Sales;
using PharmacyPos.Api.Stock;

public static class OfferChecks
{
    static void Check(bool pass, string text) { if (!pass) throw new Exception("OFFER CHECK FAILED: " + text); }
    static async Task<HttpResponseMessage> Post(HttpClient c, string url, object data) {
        var session = await c.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(data) };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString()); return await c.SendAsync(request);
    }
    public static async Task Run(HttpClient admin, HttpClient op, HttpClient anonymous, IServiceProvider services, Guid medicineId) {
        var today = StockReceiving.ShopToday();
        var r = new StockReceiving.ReceiveInput(Guid.NewGuid(),medicineId,"Offer supplier","OFFERS",today,"OFFERS-A",today.AddDays(-1),today.AddDays(20),10,3,30,10,600,0,0,0,600m,"Box",true);
        Check((await Post(admin,"/api/stock/receipts",r)).IsSuccessStatusCode,"fixture receipt");
        var offer = new OfferRules.Input(Guid.NewGuid(),"Strip saving",medicineId,"Fixed",15m,"Strip",10,today,null);
        Check((await anonymous.GetAsync("/api/offers")).StatusCode == HttpStatusCode.Unauthorized,"anonymous settings rejected");
        Check((await op.GetAsync("/api/offers")).StatusCode == HttpStatusCode.Forbidden,"operator cannot read settings");
        Check((await Post(op,"/api/offers",offer)).StatusCode == HttpStatusCode.Forbidden,"operator cannot create offers");
        Check((await admin.PostAsJsonAsync("/api/offers",offer)).StatusCode == HttpStatusCode.BadRequest,"creation CSRF");
        foreach (var invalid in new[] { offer with { Value = -1 }, offer with { Value = 1.001m }, offer with { Kind = "Percentage", Value = 101, Unit = "Piece", Units = 1 }, offer with { EndDate = today.AddDays(-1) }, offer with { Units = 0 }, offer with { MedicineId = Guid.NewGuid() }, offer with { MedicineId = null } })
            Check((await Post(admin,"/api/offers",invalid)).StatusCode == HttpStatusCode.BadRequest,"invalid offer rejected");
        var results = await Task.WhenAll(Post(admin,"/api/offers",offer),Post(admin,"/api/offers",offer));
        Check(results.All(x => x.IsSuccessStatusCode),"concurrent identical retry");
        Check((await Post(admin,"/api/offers",offer with { Value = 12 })).StatusCode == HttpStatusCode.Conflict,"changed retry rejected");
        Check((await Post(admin,"/api/offers",offer with { RequestId = Guid.NewGuid(), Name = " strip SAVING " })).StatusCode == HttpStatusCode.Conflict,"duplicate active name rejected");
        async Task<JsonElement> Quote(params SalesCounter.CartLine[] lines) {
            var response = await Post(op,"/api/sales/quote",new SalesCounter.CartInput(lines.ToList()));
            Check(response.IsSuccessStatusCode,"quote succeeded"); return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        var q = await Quote(new SalesCounter.CartLine(r.RequestId,5,"Piece"));
        Check(q.GetProperty("discountTotal").GetDecimal() == 7.5m && q.GetProperty("estimatedTotal").GetInt64() == 93,"prorated strip and half-up rounding");
        var percent = offer with { RequestId = Guid.NewGuid(), Name = "Ten percent", Kind = "Percentage", Value = 10, Unit = "Piece", Units = 1, EndDate = today };
        Check((await Post(admin,"/api/offers",percent)).IsSuccessStatusCode,"end date inclusive fixture");
        var future = percent with { RequestId = Guid.NewGuid(), Name = "Future", Value = 100, StartDate = today.AddDays(1), EndDate = null };
        var expired = percent with { RequestId = Guid.NewGuid(), Name = "Expired", Value = 100, StartDate = today.AddDays(-2), EndDate = today.AddDays(-1) };
        await Post(admin,"/api/offers",future); await Post(admin,"/api/offers",expired);
        q = await Quote(new SalesCounter.CartLine(r.RequestId,1,"Strip"),new(r.RequestId,2,"Piece"));
        Check(q.GetProperty("discountTotal").GetDecimal() == 24 && q.GetProperty("estimatedTotal").GetInt64() == 216,"best single medicine offer; future and expired excluded; end today included");
        var tax = new ChargeRules.Input(Guid.NewGuid(),"Offer test charge","Percentage",10m,true,[]);
        await Post(admin,"/api/charges",tax);
        q = await Quote(new SalesCounter.CartLine(r.RequestId,1,"Strip"));
        Check(q.GetProperty("chargeTotal").GetDecimal() == 18 && q.GetProperty("estimatedTotal").GetInt64() == 198,"percentage charge after discount");
        var whole = offer with { RequestId = Guid.NewGuid(), Name = "Bill saving", MedicineId = null, Unit = "Piece", Units = 1, Value = 30, MinimumSubtotal = 200 };
        Check((await Post(admin,"/api/offers",whole)).IsSuccessStatusCode,"whole-sale rule");
        q = await Quote(new SalesCounter.CartLine(r.RequestId,1,"Strip"));
        Check(q.GetProperty("offerStrategy").GetString() == "WholeSale" && q.GetProperty("discountTotal").GetDecimal() == 30 && q.GetProperty("estimatedTotal").GetInt64() == 187,"whole sale wins without stacking, threshold inclusive");
        q = await Quote(new SalesCounter.CartLine(r.RequestId,5,"Piece"));
        Check(q.GetProperty("offerStrategy").GetString() == "Medicine","whole-sale minimum checked before charges");
        Check((await Post(op,$"/api/offers/{whole.RequestId}/stop",new {})).StatusCode == HttpStatusCode.Forbidden,"operator cannot stop");
        Check((await admin.PostAsJsonAsync($"/api/offers/{whole.RequestId}/stop",new {})).StatusCode == HttpStatusCode.BadRequest,"stop CSRF");
        await Post(admin,$"/api/offers/{whole.RequestId}/stop",new {}); await Post(admin,$"/api/offers/{whole.RequestId}/stop",new {});
        q = await Quote(new SalesCounter.CartLine(r.RequestId,1,"Strip"));
        Check(q.GetProperty("offerStrategy").GetString() == "Medicine","stopping effective next quote");
        var capped = offer with { RequestId = Guid.NewGuid(), Name = "Capped", Unit = "Piece", Units = 1, Value = 1000 };
        await Post(admin,"/api/offers",capped);
        q = await Quote(new SalesCounter.CartLine(r.RequestId,1,"Piece"));
        Check(q.GetProperty("estimatedTotal").GetInt64() == 0 && q.GetProperty("discountTotal").GetDecimal() == 20,"discount capped, zero price safe");
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/offers");
        Check(list.GetArrayLength() == 6 && list.EnumerateArray().Any(x => x.GetProperty("status").GetString() == "Scheduled"),"list and safe retry");
        using (var scope = services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
            var saved = await db.OfferRules.SingleAsync(x => x.Id == whole.RequestId);
            Check(saved.StoppedBy != null && saved.StoppedAt != null && saved.CreatedBy != "","attribution retained");
            Check((await db.ReceivingMovements.SingleAsync(x => x.ReceiptId == r.RequestId)).Quantity == 600,"no stock deductions");
        }
        // Pure arithmetic: charge-sensitive alternative selection and exact proportional allocation.
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var lines = new List<OfferPricing.Line> { new(a,1,ExactAmount.FromMoney(100)),new(b,1,ExactAmount.FromMoney(100)) };
        var medicineOffer = new OfferRule { Id = Guid.NewGuid(), MedicineId = b, Kind = "Fixed", Units = 1, Value = 20 };
        var saleOffer = new OfferRule { Id = Guid.NewGuid(), Kind = "Fixed", Units = 1, Value = 19 };
        var selectedCharge = new ChargeRule { Kind = "Percentage", Value = 100, Medicines = [new() { MedicineId = a }] };
        var price = OfferPricing.Price(lines,[medicineOffer,saleOffer],[selectedCharge]);
        Check(price.Strategy == "WholeSale" && price.Total.Display == 271.5m,"compare final unrounded prices including charges, not raw discount");
        Console.WriteLine("PASS: admin offers, retries, dates, proration, best-price selection, no stacking, discounted charges, thresholds, caps and audit history.");
    }
}
