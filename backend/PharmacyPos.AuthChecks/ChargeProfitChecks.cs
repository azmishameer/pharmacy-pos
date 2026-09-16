using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Sales;

public static class ChargeProfitChecks
{
    static void Check(bool ok,string message) { if(!ok)throw new Exception("CHARGE PROFIT CHECK FAILED: "+message); }
    static async Task<HttpResponseMessage> Post(HttpClient c,string url,object input) {
        var s=await c.GetFromJsonAsync<JsonElement>("/api/auth/session");var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(input)};
        r.Headers.Add("X-CSRF-TOKEN",s.GetProperty("csrfToken").GetString());return await c.SendAsync(r);
    }
    public static async Task Run(HttpClient admin,HttpClient op,HttpClient anon,IServiceProvider services) {
        var input=new ChargeRules.Input(Guid.NewGuid(),"Profit classification VAT","Percentage",5,true,[],true);
        Check((await Post(admin,"/api/charges",input)).IsSuccessStatusCode,"create excluded charge");
        Check((await Post(admin,"/api/charges",input with{ExcludeFromProfit=false})).StatusCode==HttpStatusCode.Conflict,"creation retry includes classification");
        var url=$"/api/charges/{input.RequestId}/profit-classification";
        var change=new ChargeClassification.Input(Guid.NewGuid(),null,false,"Service income correction");
        Check((await Post(op,url,change)).StatusCode==HttpStatusCode.Forbidden,"operator cannot classify");
        Check((await Post(anon,url,change)).StatusCode==HttpStatusCode.Unauthorized,"anonymous cannot classify");
        Check((await admin.PostAsJsonAsync(url,change)).StatusCode==HttpStatusCode.BadRequest,"CSRF enforced");
        Check((await Post(admin,url,change with{ExcludeFromProfit=null})).StatusCode==HttpStatusCode.BadRequest,"missing decision rejected");
        Check((await Post(admin,url,change with{Reason=" "})).StatusCode==HttpStatusCode.BadRequest,"reason required");
        var race=await Task.WhenAll(Post(admin,url,change),Post(admin,url,change));Check(race.All(r=>r.IsSuccessStatusCode),"safe simultaneous retries");
        Check((await Post(admin,url,change with{RequestId=Guid.NewGuid()})).StatusCode==HttpStatusCode.Conflict,"stale decision denied");
        Check((await Post(admin,"/api/charges",input)).IsSuccessStatusCode,"original create retry remains safe after change");
        var path=$"/api/charges/{input.RequestId}/profit-history";
        Check((await op.GetAsync(path)).StatusCode==HttpStatusCode.Forbidden,"operator history denied");
        Check((await anon.GetAsync(path)).StatusCode==HttpStatusCode.Unauthorized,"anonymous history denied");
        var history=await admin.GetFromJsonAsync<JsonElement>(path);
        Check(history.GetProperty("initialExcludeFromProfit").GetBoolean()&&history.GetProperty("items").GetArrayLength()==1,"original and correction preserved");
        Check(history.GetProperty("items")[0].GetProperty("actorName").GetString()=="admin-test","audit actor");
        await Post(admin,$"/api/charges/{input.RequestId}/stop",new{});
        Check((await Post(admin,url,change with{RequestId=Guid.NewGuid(),ExpectedVersion=change.RequestId,ExcludeFromProfit=true,Reason="Tax classification"})).IsSuccessStatusCode,"stopped charge review allowed");
        using(var scope=services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();var rule=await db.ChargeRules.SingleAsync(x=>x.Id==input.RequestId);
            Check(rule.Value==5&&rule.Kind=="Percentage"&&rule.StoppedAt!=null,"classification does not alter charge amount or reactivate");
            // Model an existing pre-migration charge: nullable columns must stay unreviewed.
            rule.ExcludeFromProfit=null;rule.InitialExcludeFromProfit=null;rule.ClassificationVersion=null;await db.SaveChangesAsync();
        }
        var list=await admin.GetFromJsonAsync<JsonElement>("/api/charges");Check(list.EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==input.RequestId).GetProperty("excludeFromProfit").ValueKind==JsonValueKind.Null,"legacy unclassified preserved");
        Check((await Post(admin,url,new ChargeClassification.Input(Guid.NewGuid(),null,true,"Review legacy VAT"))).IsSuccessStatusCode,"legacy review succeeds");
        Console.WriteLine("Charge profit checks passed: creation, legacy review, permissions, audit, retries and unchanged charges.");
    }
}
