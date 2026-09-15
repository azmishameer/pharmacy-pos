using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Sales;
using PharmacyPos.Api.Stock;

public static class SalesChecks
{
    private static void Check(bool pass, string message) { if (!pass) throw new Exception("SALES CHECK FAILED: " + message); }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, object payload) {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        return await client.SendAsync(request);
    }
    public static async Task Run(HttpClient admin, HttpClient op, HttpClient anonymous, IServiceProvider services, Guid medicineId) {
        var today = StockReceiving.ShopToday();
        var first = new StockReceiving.ReceiveInput(Guid.NewGuid(),medicineId,"Sales preview supplier","COUNTER",today,"COUNTER-A",today.AddDays(-1),today.AddDays(20),10,3,30,10,600,0,0,0,600m,"Box",true);
        Check((await Post(admin,"/api/stock/receipts",first)).IsSuccessStatusCode,"fixture receipt");
        var second = first with { RequestId = Guid.NewGuid(), BatchNumber = "COUNTER-B", ExpiryDate = today.AddDays(10), Pieces = 5, MrpAmount = 22m, MrpUnit = "Piece" };
        Check((await Post(admin,"/api/stock/receipts",second)).IsSuccessStatusCode,"separately priced lot");
        var unverified = first with { RequestId = Guid.NewGuid(), BatchNumber = "COUNTER-UNVERIFIED", VerifyMrp = false };
        Check((await Post(admin,"/api/stock/receipts",unverified)).IsSuccessStatusCode,"unverified fixture");
        Check((await anonymous.GetAsync("/api/sales/stock")).StatusCode == HttpStatusCode.Unauthorized,"sales lookup requires staff");
        var available = await op.GetFromJsonAsync<JsonElement>("/api/sales/stock?search=metformin");
        var rows = available.GetProperty("items").EnumerateArray().ToList();
        Check(rows.FindIndex(x => x.GetProperty("lotId").GetGuid()==second.RequestId) < rows.FindIndex(x => x.GetProperty("lotId").GetGuid()==first.RequestId),"earlier expiry first");
        Check(!rows.Any(x => x.GetProperty("lotId").GetGuid()==unverified.RequestId),"unverified prices excluded");
        var combined = new SalesCounter.CartInput([new(first.RequestId,1,"Box"),new(first.RequestId,1,"Strip"),new(first.RequestId,2,"Piece")]);
        Check((await op.PostAsJsonAsync("/api/sales/quote",combined)).StatusCode == HttpStatusCode.BadRequest,"quote CSRF enforced");
        var quote = await (await Post(op,"/api/sales/quote",combined)).Content.ReadFromJsonAsync<JsonElement>();
        Check(quote.GetProperty("subtotal").GetDecimal()==840m && quote.GetProperty("estimatedTotal").GetInt64()==840,"mixed pack total uses verified unit price");
        var split = new SalesCounter.CartInput([new(first.RequestId,2,"Piece"),new(second.RequestId,3,"Piece")]);
        quote = await (await Post(op,"/api/sales/quote",split)).Content.ReadFromJsonAsync<JsonElement>();
        Check(quote.GetProperty("subtotal").GetDecimal()==106m,"different batch prices kept separate");
        var tooMany = new SalesCounter.CartInput([new(first.RequestId,400,"Piece"),new(first.RequestId,400,"Piece")]);
        Check((await Post(op,"/api/sales/quote",tooMany)).StatusCode==HttpStatusCode.Conflict,"duplicate cart rows cannot exceed total lot quantity");
        Check((await Post(op,"/api/sales/quote",new SalesCounter.CartInput([new(unverified.RequestId,1,"Piece")]))).StatusCode==HttpStatusCode.Conflict,"unverified lot cannot be quoted directly");
        Check((await Post(op,"/api/sales/quote",new SalesCounter.CartInput([new(first.RequestId,0,"Piece")]))).StatusCode==HttpStatusCode.BadRequest,"zero quantity invalid");
        // Fractional amounts are summed exactly before half-up rounding once.
        var fractional = first with { RequestId = Guid.NewGuid(), BatchNumber = "COUNTER-FRACTION", UnitsPerStrip = 0, StripsPerBox = 0, UnitsPerBox = 6, MrpAmount = 13m };
        Check((await Post(admin,"/api/stock/receipts",fractional)).IsSuccessStatusCode,"fractional fixture");
        quote = await (await Post(op,"/api/sales/quote",new SalesCounter.CartInput([new(fractional.RequestId,1,"Piece"),new(fractional.RequestId,1,"Piece"),new(fractional.RequestId,1,"Piece")]))).Content.ReadFromJsonAsync<JsonElement>();
        Check(quote.GetProperty("subtotal").GetDecimal()==6.5m && quote.GetProperty("estimatedTotal").GetInt64()==7 && quote.GetProperty("roundingAdjustment").GetDecimal()==0.5m,"exact fractions half up only once");
        Check((await Post(op,"/api/sales/quote",new SalesCounter.CartInput([new(fractional.RequestId,1,"Strip")]))).StatusCode==HttpStatusCode.BadRequest,"unsupported strip rejected");
        var roundingDown = first with { RequestId = Guid.NewGuid(), BatchNumber = "COUNTER-ROUND-DOWN", MrpAmount = 92.49m, MrpUnit = "Piece" };
        Check((await Post(admin,"/api/stock/receipts",roundingDown)).IsSuccessStatusCode,"rounding fixture");
        quote = await (await Post(op,"/api/sales/quote",new SalesCounter.CartInput([new(roundingDown.RequestId,1,"Piece")]))).Content.ReadFromJsonAsync<JsonElement>();
        Check(quote.GetProperty("estimatedTotal").GetInt64()==92 && quote.GetProperty("roundingAdjustment").GetDecimal()==-0.49m,"rounding down represented explicitly");
        Check(quote.GetProperty("previewOnly").GetBoolean() && !quote.GetProperty("offersApplied").GetBoolean(),"preview status explicit");
        var correction = new StockMrpCorrection.Input(Guid.NewGuid(),first.RequestId,600m,"Box",30,660m,"Box","Preview price refresh check",true);
        Check((await Post(admin,$"/api/stock/receipts/{first.RequestId}/correct-mrp",correction)).IsSuccessStatusCode,"MRP changed between quotes");
        quote = await (await Post(op,"/api/sales/quote", new { lines = new[] { new { lotId=first.RequestId,quantity=3,unit="Piece",mrpAmount=0.01m } } })).Content.ReadFromJsonAsync<JsonElement>();
        Check(quote.GetProperty("subtotal").GetDecimal()==66m,"fresh server price overrides stale or supplied client price");
        using (var scope=services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
            Check((await db.ReceivingMovements.SingleAsync(x=>x.ReceiptId==first.RequestId)).Quantity==600,"cart never deducts inventory");
            var batch=await db.MedicineBatches.SingleAsync(x=>x.BatchNumber=="COUNTER-A");
            batch.ExpiryDate=today.AddDays(-1); await db.SaveChangesAsync();
        }
        Check((await Post(op,"/api/sales/quote",new SalesCounter.CartInput([new(first.RequestId,1,"Piece")]))).StatusCode==HttpStatusCode.Conflict,"expiry rechecked after cart creation");
        available=await op.GetFromJsonAsync<JsonElement>("/api/sales/stock?search=Jardimet");
        Check(!available.GetProperty("items").EnumerateArray().Any(x=>x.GetProperty("lotId").GetGuid()==first.RequestId),"expired lot leaves sales search");
        Console.WriteLine("PASS: sales preview stock eligibility, staff/CSRF, pack quantities, multi-batch prices, exact rounding, current prices and unchanged inventory.");
    }
}
