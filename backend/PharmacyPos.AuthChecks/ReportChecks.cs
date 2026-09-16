using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Sales;
using PharmacyPos.Api.Returns;

public static class ReportChecks
{
    static void Check(bool ok,string message) { if(!ok)throw new Exception("REPORT CHECK FAILED: "+message); }
    public static async Task Run(HttpClient admin,HttpClient op,HttpClient anon,IServiceProvider services) {
        var date=new DateOnly(2030,1,2);var (start,end)=SalesReports.Bounds(date,date);
        Check(start==new DateTimeOffset(2030,1,1,18,0,0,TimeSpan.Zero)&&end-start==TimeSpan.FromDays(1),"Dhaka midnight bounds");
        using(var scope=services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
            var adminId=await db.Users.Where(u=>u.UserName=="admin-test").Select(u=>u.Id).SingleAsync();
            var opId=await db.Users.Where(u=>u.UserName=="operator-test").Select(u=>u.Id).SingleAsync();
            Sale Make(DateTimeOffset at,decimal total)=>new(){Id=Guid.NewGuid(),OperatorId=adminId,OperatorName="admin-test",CompletedAt=at,Total=total,RequestHash=new string('a',64),Snapshot="{\"subtotal\":120,\"discountTotal\":30,\"chargeTotal\":9,\"roundingAdjustment\":1}",Payments=[new(){Method="Cash",Amount=total,Tendered=total+50,Change=50}]};
            var old=Make(start.AddSeconds(-1),100);db.Sales.AddRange(old,Make(start,100),Make(end,200));
            db.SaleReturns.Add(new(){Id=Guid.NewGuid(),SaleId=old.Id,Amount=25,Method="Cash",Reason="Report fixture",ActorId=opId,ActorName="operator-test",At=end.AddSeconds(-1)});
            db.SaleReturns.Add(new(){Id=Guid.NewGuid(),SaleId=old.Id,Amount=10,Method="Cash",Reason="Outside period",ActorId=opId,ActorName="operator-test",At=end});
            await db.SaveChangesAsync();
        }
        const string url="/api/reports/sales?from=2030-01-02&to=2030-01-02";
        foreach(var suffix in new[]{"","&format=csv"}) {
            Check((await anon.GetAsync(url+suffix)).StatusCode==HttpStatusCode.Unauthorized,"anonymous denied");
            Check((await op.GetAsync(url+suffix)).StatusCode==HttpStatusCode.Forbidden,"operator denied");
        }
        var response=await admin.GetAsync(url);Check(response.IsSuccessStatusCode,"admin report");
        Check(response.Headers.CacheControl?.NoStore==true,"no cache");
        var r=await response.Content.ReadFromJsonAsync<JsonElement>();var t=r.GetProperty("totals");
        Check(t.GetProperty("salesCount").GetInt32()==1&&t.GetProperty("refundCount").GetInt32()==1,"date boundaries");
        Check(t.GetProperty("sales").GetDecimal()==100&&t.GetProperty("refunds").GetDecimal()==25&&t.GetProperty("netSales").GetDecimal()==75,"earlier sale refunded in current period");
        Check(t.GetProperty("cashCollected").GetDecimal()==100&&t.GetProperty("netCash").GetDecimal()==75,"cash excludes tendered change");
        Check(t.GetProperty("subtotal").GetDecimal()==120&&t.GetProperty("discounts").GetDecimal()==30&&t.GetProperty("charges").GetDecimal()==9&&t.GetProperty("rounding").GetDecimal()==1,"immutable receipt breakdown");
        var staff=r.GetProperty("staff").EnumerateArray().ToArray();Check(staff.Length==2,"refund-only staff included");
        Check(staff.Single(s=>s.GetProperty("username").GetString()=="operator-test").GetProperty("totals").GetProperty("netSales").GetDecimal()==-25,"refund actor attribution");
        var csv=await admin.GetStringAsync(url+"&format=csv");Check(csv.Contains("TOTAL")&&csv.Contains("operator-test")&&csv.Split("\r\n")[1].Split(',').Skip(12).Select(v=>decimal.Parse(v,System.Globalization.CultureInfo.InvariantCulture)).SequenceEqual(new decimal[]{100,25,75,100,25,75}),"CSV totals agree");
        var zero=new SalesReports.Totals(0,0,0,0,0,0,0,0,0,0);
        Check(SalesReports.Csv(new(date,date,"Asia/Dhaka",DateTimeOffset.UtcNow,zero,[new("id","=formula",zero)])).Contains("\"'=formula\""),"CSV formula escaping");
        foreach(var query in new[]{"from=nope","from=2030-02-01&to=2030-01-01","from=2020-01-01&to=2030-01-01","to=9999-12-31","format=bad"})Check((await admin.GetAsync("/api/reports/sales?"+query)).StatusCode==HttpStatusCode.BadRequest,"invalid range/format rejected");
        var empty=await admin.GetFromJsonAsync<JsonElement>("/api/reports/sales?from=2031-01-01&to=2031-01-01");Check(empty.GetProperty("totals").GetProperty("sales").GetDecimal()==0&&empty.GetProperty("staff").GetArrayLength()==0,"empty report");
        Console.WriteLine("Report checks passed: access, Dhaka dates, immutable totals, refunds, cash/change, staff attribution and CSV.");
    }
}
