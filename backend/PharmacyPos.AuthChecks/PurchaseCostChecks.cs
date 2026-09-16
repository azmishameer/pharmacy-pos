using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Stock;
using PharmacyPos.Api.Sales;

public static class PurchaseCostChecks
{
    static void Check(bool ok,string message) { if(!ok)throw new Exception("PURCHASE COST CHECK FAILED: "+message); }
    static async Task<HttpResponseMessage> Post(HttpClient c,string url,object input) {
        var s=await c.GetFromJsonAsync<JsonElement>("/api/auth/session");var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(input)};
        r.Headers.Add("X-CSRF-TOKEN",s.GetProperty("csrfToken").GetString());return await c.SendAsync(r);
    }
    public static async Task Run(HttpClient admin,HttpClient op,HttpClient anon,IServiceProvider services,Guid medicine) {
        var today=StockReceiving.ShopToday();var receive=new StockReceiving.ReceiveInput(Guid.NewGuid(),medicine,"Cost supplier","COST-TEST",today,"COST-TEST",today.AddDays(-1),today.AddDays(90),10,3,30,10,0,0,1,0,600,"Box",true);
        Check((await Post(admin,"/api/stock/receipts",receive)).IsSuccessStatusCode,"approved receipt fixture");
        var id=receive.RequestId;var url=$"/api/purchase-costs/{id}";
        async Task<JsonElement> List() => await admin.GetFromJsonAsync<JsonElement>("/api/purchase-costs?search=COST-TEST");
        Check((await List()).GetProperty("items")[0].GetProperty("cost").ValueKind==JsonValueKind.Null,"missing distinct from zero");
        var cart=new SalesCounter.CartInput([new(id,1,"Piece")]);
        var quote=await (await Post(op,"/api/sales/quote",cart)).Content.ReadFromJsonAsync<JsonElement>();
        var sale=await Post(op,"/api/sales/checkout",new CashCheckout.Input(Guid.NewGuid(),cart.Lines,quote.GetProperty("quoteHash").GetString(),1000));
        Check(sale.IsSuccessStatusCode,"missing cost does not block checkout");
        var before=await (await Post(op,"/api/sales/quote",cart)).Content.ReadFromJsonAsync<JsonElement>();
        var input=new PurchaseCosts.Input(Guid.NewGuid(),null,450,"Invoice checked");
        foreach(var path in new[]{"/api/purchase-costs",url+"/history"}) {
            Check((await anon.GetAsync(path)).StatusCode==HttpStatusCode.Unauthorized,"anonymous read denied");
            Check((await op.GetAsync(path)).StatusCode==HttpStatusCode.Forbidden,"operator read denied");
        }
        Check((await Post(op,url,input)).StatusCode==HttpStatusCode.Forbidden,"operator write denied");
        Check((await Post(anon,url,input)).StatusCode==HttpStatusCode.Unauthorized,"anonymous write denied");
        Check((await admin.PostAsJsonAsync(url,input)).StatusCode==HttpStatusCode.BadRequest,"CSRF enforced");
        foreach(var bad in new[]{input with{TotalCost=-1},input with{TotalCost=0.001m},input with{Reason=" "},input with{TotalCost=1000000000001m}})
            Check((await Post(admin,url,bad)).StatusCode==HttpStatusCode.BadRequest,"invalid cost rejected");
        var pending=receive with{RequestId=Guid.NewGuid(),BatchNumber="COST-PENDING",VerifyMrp=false};
        Check((await Post(op,"/api/stock/receipts",pending)).IsSuccessStatusCode,"pending fixture");
        Check((await Post(admin,$"/api/purchase-costs/{pending.RequestId}",input)).StatusCode==HttpStatusCode.BadRequest,"pending cost rejected");
        var race=await Task.WhenAll(Post(admin,url,input),Post(admin,url,input));Check(race.All(r=>r.IsSuccessStatusCode),"concurrent retry saved once");
        var cost=await race[0].Content.ReadFromJsonAsync<JsonElement>();Check(cost.GetProperty("unitCost").GetDecimal()==15&&cost.GetProperty("receivedUnits").GetInt64()==30,"original quantity despite earlier sale");
        Check((await Post(admin,url,input with{TotalCost=400})).StatusCode==HttpStatusCode.Conflict,"changed retry denied");
        Check((await Post(admin,url,input with{RequestId=Guid.NewGuid()})).StatusCode==HttpStatusCode.Conflict,"stale revision denied");
        var corrected=input with{RequestId=Guid.NewGuid(),ExpectedId=input.RequestId,TotalCost=420,Reason="Invoice correction"};
        Check((await Post(admin,url,corrected)).IsSuccessStatusCode,"correction saved");
        var history=await admin.GetFromJsonAsync<JsonElement>(url+"/history");var entries=history.GetProperty("items");
        Check(entries.GetArrayLength()==2&&entries[0].GetProperty("totalCost").GetDecimal()==420&&entries[1].GetProperty("totalCost").GetDecimal()==450,"immutable previous cost");
        Check(entries.EnumerateArray().All(e=>e.GetProperty("actorName").GetString()=="admin-test"),"admin attribution");
        Check((await List()).GetProperty("items")[0].GetProperty("cost").GetProperty("unitCost").GetDecimal()==14,"latest cost displayed");
        var after=await (await Post(op,"/api/sales/quote",cart)).Content.ReadFromJsonAsync<JsonElement>();
        Check(before.GetProperty("quoteHash").GetString()==after.GetProperty("quoteHash").GetString(),"selling price unchanged");
        foreach(var publicUrl in new[]{"/api/stock/inventory","/api/stock/receipts","/api/sales/stock"}) {
            var text=await op.GetStringAsync(publicUrl);Check(!text.Contains("totalCost")&&!text.Contains("unitCost")&&!text.Contains("Invoice correction"),"public DTO hides cost");
        }
        using(var scope=services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
            Check(await db.ReceivingMovements.Where(m=>m.ReceiptId==id).Select(m=>m.Quantity).SingleAsync()==30,"receiving quantity unchanged");
            Check(await db.SaleStockMovements.Where(m=>m.ReceiptId==id).SumAsync(m=>m.Quantity)==1,"sold quantity unchanged");
        }
        var free=corrected with{RequestId=Guid.NewGuid(),ExpectedId=corrected.RequestId,TotalCost=0,Reason="Supplier supplied free stock"};
        Check((await Post(admin,url,free)).IsSuccessStatusCode,"explicit zero allowed");
        Check((await List()).GetProperty("items")[0].GetProperty("cost").GetProperty("totalCost").GetDecimal()==0,"zero distinct from missing");
        Console.WriteLine("Purchase cost checks passed: privacy, validation, retries, corrections, audit, missing/zero cost and unchanged sales/stock.");
    }
}
