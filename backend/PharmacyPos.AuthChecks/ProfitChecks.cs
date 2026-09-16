using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Sales;
using PharmacyPos.Api.Stock;
using PharmacyPos.Api.Returns;

public static class ProfitChecks
{
    static void Check(bool ok,string m) {if(!ok)throw new Exception("PROFIT CHECK FAILED: "+m);}
    static async Task<HttpResponseMessage> Post(HttpClient c,string url,object input) {
        var s=await c.GetFromJsonAsync<JsonElement>("/api/auth/session");var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(input)};
        r.Headers.Add("X-CSRF-TOKEN",s.GetProperty("csrfToken").GetString());return await c.SendAsync(r);
    }
    public static async Task Run(HttpClient admin,HttpClient op,HttpClient anon,IServiceProvider services,Guid medicine) {
        var today=StockReceiving.ShopToday();var receive=new StockReceiving.ReceiveInput(Guid.NewGuid(),medicine,"Profit supplier","PROFIT",today,"PROFIT-FIXTURE",today.AddDays(-1),today.AddDays(90),10,3,30,10,10,0,0,0,20,"Piece",true);
        Check((await Post(admin,"/api/stock/receipts",receive)).IsSuccessStatusCode,"stock fixture");
        var tax=new ChargeRules.Input(Guid.NewGuid(),"Profit test excluded","Percentage",25,true,[],true);
        var fee=new ChargeRules.Input(Guid.NewGuid(),"Profit test income","PerUnit",2,true,[],false);
        Check((await Post(admin,"/api/charges",tax)).IsSuccessStatusCode&&(await Post(admin,"/api/charges",fee)).IsSuccessStatusCode,"classification fixtures");
        var day=new DateOnly(2035,2,1);var(start,_)=SalesReports.Bounds(day,day);
        var saleId=Guid.NewGuid();var returnId=Guid.NewGuid();var itemId=Guid.NewGuid();var costId=Guid.NewGuid();
        using(var scope=services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();var actor=await db.Users.Where(u=>u.UserName=="admin-test").Select(u=>u.Id).SingleAsync();
            var line=new{lotId=receive.RequestId,baseUnits=1,finalPrice=27m,charges=new[]{new{id=tax.RequestId,name=tax.Name,amount=5m},new{id=fee.RequestId,name=fee.Name,amount=2m}}};
            db.Sales.Add(new(){Id=saleId,OperatorId=actor,OperatorName="admin-test",CompletedAt=start,Total=54,RequestHash=new string('b',64),Snapshot=JsonSerializer.Serialize(new{lines=new[]{line,line}})});
            db.SaleStockMovements.Add(new(){SaleId=saleId,ReceiptId=receive.RequestId,Quantity=2});
            db.PurchaseCostEntries.Add(new(){Id=costId,ReceiptId=receive.RequestId,Revision=1,TotalCost=150,ReceivedUnits=10,Reason="Profit fixture",ActorId=actor,ActorName="admin-test",At=start});
            db.SaleReturns.Add(new(){Id=returnId,SaleId=saleId,Amount=27,ActorId=actor,ActorName="admin-test",Reason="Refund fixture",At=start.AddDays(1)});
            db.ReturnedItems.Add(new(){Id=itemId,ReturnId=returnId,SaleId=saleId,ReceiptId=receive.RequestId,LineIndex=0,BrandName="Profit fixture",BatchNumber="PROFIT-FIXTURE",BaseUnit="Tablet",Packs=1,Unit="Piece",Quantity=1,Refund=27,Status="Held"});
            await db.SaveChangesAsync();
        }
        string Url(int startDay,int endDay)=>$"/api/reports/profit?from=2035-02-{startDay:D2}&to=2035-02-{endDay:D2}";
        async Task<JsonElement> Get(int a,int b)=>await admin.GetFromJsonAsync<JsonElement>(Url(a,b));
        foreach(var suffix in new[]{"","&format=csv"}) {Check((await op.GetAsync(Url(1,3)+suffix)).StatusCode==HttpStatusCode.Forbidden,"operator denied");Check((await anon.GetAsync(Url(1,3)+suffix)).StatusCode==HttpStatusCode.Unauthorized,"anonymous denied");}
        var r=await Get(1,1);Check(r.GetProperty("grossProfit").GetDecimal()==14&&r.GetProperty("netIncome").GetDecimal()==44&&r.GetProperty("excludedSalesCharges").GetDecimal()==10,"sale income excludes tax but includes fee");
        r=await Get(2,2);Check(r.GetProperty("grossProfit").GetDecimal()==-22&&r.GetProperty("recoveredCost").GetDecimal()==0,"held refund reduces income without cost reversal");
        r=await Get(1,3);Check(r.GetProperty("grossProfit").GetDecimal()==-8,"held return retains cost loss");
        async Task Restock(string status) {using var scope=services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();var i=await db.ReturnedItems.SingleAsync(i=>i.Id==itemId);i.Status=status;i.ReviewedAt=start.AddDays(2);await db.SaveChangesAsync();}
        await Restock("Restocked");r=await Get(3,3);Check(r.GetProperty("grossProfit").GetDecimal()==15&&r.GetProperty("recoveredCost").GetDecimal()==15,"cost reversed on approval day even with no sales");
        r=await Get(1,3);Check(r.GetProperty("grossProfit").GetDecimal()==7,"combined lifecycle");
        await Restock("Disposed");r=await Get(1,3);Check(r.GetProperty("grossProfit").GetDecimal()==-8,"disposed return recovers nothing");await Restock("Restocked");
        using(var scope=services.CreateScope()) {var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();var c=await db.PurchaseCostEntries.SingleAsync(x=>x.Id==costId);db.PurchaseCostEntries.Add(new(){Id=Guid.NewGuid(),ReceiptId=c.ReceiptId,Revision=2,PreviousId=c.Id,TotalCost=200,ReceivedUnits=10,Reason="Correction",ActorId=c.ActorId,ActorName=c.ActorName,At=DateTimeOffset.UtcNow});await db.SaveChangesAsync();}
        r=await Get(1,3);Check(r.GetProperty("grossProfit").GetDecimal()==2&&r.GetProperty("costOfSales").GetDecimal()==40,"latest cost updates historical report and recovery");
        using(var scope=services.CreateScope()) {var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();await db.PurchaseCostEntries.Where(x=>x.ReceiptId==receive.RequestId).ExecuteDeleteAsync();}
        r=await Get(1,3);Check(!r.GetProperty("complete").GetBoolean()&&r.GetProperty("grossProfit").ValueKind==JsonValueKind.Null&&r.GetProperty("costOfSales").ValueKind==JsonValueKind.Null,"missing cost not zero or partial profit");
        var csv=await admin.GetStringAsync(Url(1,3)+"&format=csv");Check(csv.Contains("\"Gross profit\",\"\"")&&csv.Contains("Incomplete"),"incomplete CSV leaves amount blank");
        using(var scope=services.CreateScope()) {var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();var c=await db.ChargeRules.SingleAsync(x=>x.Id==tax.RequestId);c.ExcludeFromProfit=null;await db.SaveChangesAsync();}
        r=await Get(2,2);Check(r.GetProperty("netIncome").ValueKind==JsonValueKind.Null&&r.GetProperty("issues").EnumerateArray().Any(i=>i.GetProperty("kind").GetString()=="Charge"),"unclassified charge blocks income on earlier-sale refund");
        r=await Get(5,5);Check(r.GetProperty("complete").GetBoolean()&&r.GetProperty("grossProfit").GetDecimal()==0,"empty report complete zero");
        foreach(var q in new[]{"from=bad","from=2035-02-03&to=2035-02-01","from=2030-01-01&to=2035-01-01","format=bad"})Check((await admin.GetAsync("/api/reports/profit?"+q)).StatusCode==HttpStatusCode.BadRequest,"invalid range rejected");
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(new{finalPrice=20.5m,charges=new[]{new{id=tax.RequestId,name="Tax",amount=0.5m}}}));
        var excluded=ProfitReports.ExcludedPart(json.RootElement,21,new Dictionary<Guid,bool?>{{tax.RequestId,true}},new Dictionary<string,ProfitReports.Issue>());
        Check(excluded==21m*0.5m/20.5m,"receipt rounding proportional allocation");
        Check(ProfitReports.ExcludedPart(json.RootElement,0,new Dictionary<Guid,bool?>(),new Dictionary<string,ProfitReports.Issue>())==0,"zero paid receipt excludes zero");
        using(var scope=services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();var actor=await db.Users.Where(u=>u.UserName=="admin-test").Select(u=>u.Id).SingleAsync();
            db.PurchaseCostEntries.Add(new(){Id=Guid.NewGuid(),ReceiptId=receive.RequestId,Revision=3,TotalCost=200,ReceivedUnits=10,Reason="Restored test cost",ActorId=actor,ActorName="admin-test",At=DateTimeOffset.UtcNow});
            (await db.ChargeRules.SingleAsync(x=>x.Id==tax.RequestId)).ExcludeFromProfit=true;
            var second=Guid.NewGuid();db.SaleReturns.Add(new(){Id=second,SaleId=saleId,Amount=27,ActorId=actor,ActorName="admin-test",Reason="Remaining line returned",At=start.AddDays(3)});
            db.ReturnedItems.Add(new(){ReturnId=second,SaleId=saleId,ReceiptId=receive.RequestId,LineIndex=1,BrandName="Profit fixture",BatchNumber="PROFIT-FIXTURE",BaseUnit="Tablet",Packs=1,Unit="Piece",Quantity=1,Refund=27,Status="Held"});
            await db.SaveChangesAsync();
        }
        r=await Get(1,4);Check(r.GetProperty("netIncome").GetDecimal()==0&&r.GetProperty("grossProfit").GetDecimal()==-20,"full refund reverses all income; one held item remains a cost");
        csv=await admin.GetStringAsync(Url(1,4)+"&format=csv");Check(csv.Contains("Complete")&&decimal.Parse(csv.Split("\r\n").Single(l=>l.Contains("\"Gross profit\"")).Split(',')[2].Trim('"'),System.Globalization.CultureInfo.InvariantCulture)==-20,"complete CSV agrees");
        Console.WriteLine("Profit checks passed: tax/income, timing, returns, disposal, corrected/missing costs, unclassified charges, permissions and CSV.");
    }
}
