using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Returns;
using PharmacyPos.Api.Sales;
using PharmacyPos.Api.Stock;

public static class ReturnChecks
{
    static void Check(bool p,string m) { if (!p) throw new Exception("RETURN CHECK FAILED: "+m); }
    static async Task<HttpResponseMessage> Post(HttpClient c,string url,object data) {
        var session=await c.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var req=new HttpRequestMessage(HttpMethod.Post,url) {Content=JsonContent.Create(data)};
        req.Headers.Add("X-CSRF-TOKEN",session.GetProperty("csrfToken").GetString()); return await c.SendAsync(req);
    }
    public static async Task Run(HttpClient admin,HttpClient op,HttpClient anonymous,IServiceProvider services,Guid medicineId) {
        foreach (var o in (await admin.GetFromJsonAsync<JsonElement>("/api/offers")).EnumerateArray()) await Post(admin,$"/api/offers/{o.GetProperty("id").GetGuid()}/stop",new {});
        foreach (var c in (await admin.GetFromJsonAsync<JsonElement>("/api/charges")).EnumerateArray()) await Post(admin,$"/api/charges/{c.GetProperty("id").GetGuid()}/stop",new {});
        var today=StockReceiving.ShopToday();
        var r=new StockReceiving.ReceiveInput(Guid.NewGuid(),medicineId,"Return supplier","RETURNS",today,"RETURNS-A",today.AddDays(-1),today.AddDays(30),10,3,30,10,200,0,0,0,600,"Box",true);
        Check((await Post(admin,"/api/stock/receipts",r)).IsSuccessStatusCode,"stock fixture");
        var offer=new OfferRules.Input(Guid.NewGuid(),"Return discount",medicineId,"Percentage",10,"Piece",1,today,null);
        var charge=new ChargeRules.Input(Guid.NewGuid(),"Return charge","Percentage",5,true,[]);
        await Post(admin,"/api/offers",offer); await Post(admin,"/api/charges",charge);
        var lines=new List<SalesCounter.CartLine> {new(r.RequestId,2,"Strip"),new(r.RequestId,3,"Piece")};
        var quote=await (await Post(admin,"/api/sales/quote",new SalesCounter.CartInput(lines))).Content.ReadFromJsonAsync<JsonElement>();
        var saleRequest=new CashCheckout.Input(Guid.NewGuid(),lines,quote.GetProperty("quoteHash").GetString(),1000);
        var sale=await (await Post(admin,"/api/sales/checkout",saleRequest)).Content.ReadFromJsonAsync<JsonElement>();
        var number=sale.GetProperty("receiptNumber").GetString();
        Check((await anonymous.GetAsync($"/api/returns/sale?number={number}")).StatusCode==HttpStatusCode.Unauthorized,"anonymous lookup denied");
        var lookup=await op.GetFromJsonAsync<JsonElement>($"/api/returns/sale?number={number}");
        var originalTotal=sale.GetProperty("total").GetDecimal();
        Check(lookup.GetProperty("lines").EnumerateArray().Sum(l=>l.GetProperty("refund").GetDecimal())==originalTotal,"allocations sum to original paid bill");
        var shares=lookup.GetProperty("lines").EnumerateArray().Select(l=>l.GetProperty("refund").GetDecimal()).ToArray();
        Check(shares[0]==378.26m && shares[1]==56.74m,"discount, charge and rounding shares preserved");
        var input=new ReturnEndpoints.Input(Guid.NewGuid(),saleRequest.RequestId,[0],"Whole item returned",true,true);
        Check((await op.PostAsJsonAsync("/api/returns",input)).StatusCode==HttpStatusCode.BadRequest,"refund CSRF");
        Check((await Post(op,"/api/returns",input with {LineIndexes=[0,0]})).StatusCode==HttpStatusCode.BadRequest,"duplicate selection denied");
        Check((await Post(op,"/api/returns",input with {LineIndexes=[9]})).StatusCode==HttpStatusCode.BadRequest,"invalid line denied");
        Check((await Post(op,"/api/returns",input with {CashRefunded=false})).StatusCode==HttpStatusCode.BadRequest,"cash confirmation required");
        var race=await Task.WhenAll(Post(op,"/api/returns",input),Post(op,"/api/returns",input));
        Check(race.All(x=>x.IsSuccessStatusCode),"operator refund without approval and safe retry");
        var first=await race[0].Content.ReadFromJsonAsync<JsonElement>();
        var itemId=first.GetProperty("items")[0].GetProperty("id").GetGuid();
        Check(first.GetProperty("amount").GetDecimal()==shares[0] && first.GetProperty("items")[0].GetProperty("quantity").GetInt64()==20,"entire two-strip item returned");
        Check((await Post(admin,"/api/returns",input with {RequestId=Guid.NewGuid()})).StatusCode==HttpStatusCode.Conflict,"cannot refund same item again");
        Check((await Post(op,"/api/returns",input with {Reason="Changed"})).StatusCode==HttpStatusCode.Conflict,"changed retry denied");
        async Task<long> Balance() { var v=await op.GetFromJsonAsync<JsonElement>("/api/stock/inventory"); return v.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("lotId").GetGuid()==r.RequestId).GetProperty("onHandUnits").GetInt64(); }
        Check(await Balance()==177,"held returns do not increase main inventory");
        var review=new ReturnEndpoints.Review("Restocked","Inspected intact packaging",true);
        Check((await Post(op,$"/api/returns/stock/{itemId}/review",review)).StatusCode==HttpStatusCode.Forbidden,"operator cannot restock");
        Check((await admin.PostAsJsonAsync($"/api/returns/stock/{itemId}/review",review)).StatusCode==HttpStatusCode.BadRequest,"review CSRF");
        Check((await Post(admin,$"/api/returns/stock/{itemId}/review",review)).IsSuccessStatusCode,"admin restocks");
        Check((await Post(admin,$"/api/returns/stock/{itemId}/review",review)).IsSuccessStatusCode,"restock retry");
        Check(await Balance()==197,"restock credits units once");
        var search=await op.GetFromJsonAsync<JsonElement>("/api/sales/stock");
        Check(search.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("lotId").GetGuid()==r.RequestId).GetProperty("availableUnits").GetInt64()==197,"restocked units sellable");
        // Change current offer; remainder refund must still use historical paid amount.
        await Post(admin,$"/api/offers/{offer.RequestId}/stop",new {});
        await Post(admin,$"/api/charges/{charge.RequestId}/stop",new {});
        var secondInput=input with {RequestId=Guid.NewGuid(),LineIndexes=[1],Reason="Other complete item"};
        var second=await (await Post(admin,"/api/returns",secondInput)).Content.ReadFromJsonAsync<JsonElement>();
        Check(second.GetProperty("amount").GetDecimal()==shares[1],"later refund ignores current pricing");
        var secondId=second.GetProperty("items")[0].GetProperty("id").GetGuid();
        using(var scope=services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
            Check(await db.SaleReturns.Where(x=>x.SaleId==saleRequest.RequestId).SumAsync(x=>x.Amount)==originalTotal,"full refund equals original total");
            Check(await db.ReturnedItems.CountAsync(x=>x.SaleId==saleRequest.RequestId)==2,"two immutable returned lines");
            var batch=await db.MedicineBatches.SingleAsync(x=>x.BatchNumber=="RETURNS-A");batch.ExpiryDate=today.AddDays(-1);await db.SaveChangesAsync();
        }
        Check((await Post(admin,$"/api/returns/stock/{secondId}/review",review)).StatusCode==HttpStatusCode.Conflict,"expired return cannot restock");
        Check((await Post(admin,$"/api/returns/stock/{secondId}/review",new ReturnEndpoints.Review("Disposed","Physically disposed",true))).IsSuccessStatusCode,"returned goods disposal");
        Check((await admin.GetAsync("/api/returns/disposals/export")).IsSuccessStatusCode,"disposal export");
        Check((await op.GetAsync("/api/returns/disposals/export")).StatusCode==HttpStatusCode.Forbidden,"disposal export admin only");
        Check((await Post(admin,"/api/stock/disposals",new StockDisposalEndpoints.DisposeInput(Guid.NewGuid(),r.RequestId,"Remaining main stock disposed",true))).IsSuccessStatusCode,"main stock disposal after return");
        using(var scope=services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
            Check((await db.StockDisposals.SingleAsync(d=>d.ReceiptId==r.RequestId)).Quantity==197,"main disposal includes restocked units, excludes held units");
            var item=await db.ReturnedItems.SingleAsync(i=>i.Id==secondId);Check(item.ReviewedBy!=null && item.ReviewedAt!=null && item.VisibleUntil==item.ReviewedAt.Value.AddMonths(3),"review audit and 3 month retention");
            item.VisibleUntil=DateTimeOffset.UtcNow.AddSeconds(-1);await db.SaveChangesAsync();
        }
        var disposed=await admin.GetFromJsonAsync<JsonElement>("/api/returns/stock?status=Disposed");
        Check(!disposed.GetProperty("items").EnumerateArray().Any(x=>x.GetProperty("id").GetGuid()==secondId),"old disposal leaves operational list");
        var pure=new Sale {Total=1,Snapshot="{\"lines\":[{\"finalPrice\":0.5},{\"finalPrice\":0.5},{\"finalPrice\":0}]}"};
        Check(RefundAllocation.For(pure).SequenceEqual(new[]{.5m,.5m,0m}),"zero priced item receives no paid share");
        Console.WriteLine("PASS: whole-item refunds, exact original paid allocation, retry/duplicate safety, cashier audit, held stock, admin restock, expiry and disposal accounting.");
    }
}
