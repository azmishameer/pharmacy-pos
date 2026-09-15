using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PharmacyPos.Api.Auth;

public static class StaffChecks
{
    static void Check(bool ok,string message) { if(!ok)throw new Exception("STAFF CHECK FAILED: "+message); }
    static async Task<HttpResponseMessage> Post(HttpClient c,string url,object input) {
        var session=await c.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var request=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(input)};
        request.Headers.Add("X-CSRF-TOKEN",session.GetProperty("csrfToken").GetString());return await c.SendAsync(request);
    }
    public static async Task Run(HttpClient admin,HttpClient op,HttpClient anon,Func<HttpClient> client) {
        const string password="Staff-Test!1234", replacement="New-Staff!5678";
        var input=new StaffManagement.CreateInput(Guid.NewGuid(),"staff-check",password);
        Check((await anon.GetAsync("/api/staff")).StatusCode==HttpStatusCode.Unauthorized,"anonymous list denied");
        Check((await op.GetAsync("/api/staff")).StatusCode==HttpStatusCode.Forbidden,"operator list denied");
        Check((await Post(op,"/api/staff",input)).StatusCode==HttpStatusCode.Forbidden,"operator create denied");
        Check((await admin.PostAsJsonAsync("/api/staff",input)).StatusCode==HttpStatusCode.BadRequest,"create requires CSRF");
        Check((await Post(admin,"/api/staff",input with {Password="weakpassword"})).StatusCode==HttpStatusCode.BadRequest,"password complexity");
        var created=await Post(admin,"/api/staff",input);Check(created.IsSuccessStatusCode,"admin creates operator");
        var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        Check((await Post(admin,"/api/staff",input)).IsSuccessStatusCode,"create retry safe");
        Check((await Post(admin,"/api/staff",input with {Username="another"})).StatusCode==HttpStatusCode.Conflict,"changed retry rejected");
        Check((await Post(admin,"/api/staff",input with {RequestId=Guid.NewGuid(),Username="STAFF-CHECK"})).StatusCode==HttpStatusCode.Conflict,"duplicate case-insensitive name");
        async Task<JsonElement> Account() { var data=await admin.GetFromJsonAsync<JsonElement>("/api/staff?search=staff-check");return data.GetProperty("items").EnumerateArray().Single(); }
        var account=await Account();Check(account.GetProperty("roles").GetArrayLength()==1&&account.GetProperty("roles")[0].GetString()=="Operator","only operator role");
        Check(!account.ToString().Contains("password",StringComparison.OrdinalIgnoreCase)&&!account.ToString().Contains("stamp",StringComparison.OrdinalIgnoreCase),"list hides secrets");
        using var first=client();Check((await Post(first,"/api/auth/login",new{username="staff-check",password})).IsSuccessStatusCode,"new operator login");
        var change=new StaffManagement.ChangeInput(Guid.NewGuid(),account.GetProperty("version").GetGuid(),"ResetPassword",replacement,"Forgotten password");
        var url=$"/api/staff/{id}/change";
        Check((await Post(op,url,change)).StatusCode==HttpStatusCode.Forbidden,"operator cannot reset");
        Check((await op.GetAsync($"/api/staff/{id}/history")).StatusCode==HttpStatusCode.Forbidden,"operator cannot see account audit");
        Check((await admin.PostAsJsonAsync(url,change)).StatusCode==HttpStatusCode.BadRequest,"change requires CSRF");
        Check((await Post(admin,url,change with {ExpectedVersion=Guid.NewGuid()})).StatusCode==HttpStatusCode.Conflict,"stale version rejected");
        Check((await Post(admin,url,change)).IsSuccessStatusCode,"reset succeeds");
        Check((await Post(admin,url,change)).IsSuccessStatusCode,"reset retry safe");
        Check((await first.GetAsync("/api/sales/stock")).StatusCode==HttpStatusCode.Unauthorized,"reset invalidates old session immediately");
        using var second=client();Check((await Post(second,"/api/auth/login",new{username="staff-check",password})).StatusCode==HttpStatusCode.Unauthorized,"old password rejected");
        Check((await Post(second,"/api/auth/login",new{username="staff-check",password=replacement})).IsSuccessStatusCode,"new password works");
        account=await Account();var disable=change with {RequestId=Guid.NewGuid(),ExpectedVersion=account.GetProperty("version").GetGuid(),Action="Disable",Password=null,Reason="Staff left"};
        Check((await Post(admin,url,disable)).IsSuccessStatusCode,"disable succeeds");
        Check((await second.GetAsync("/api/sales/stock")).StatusCode==HttpStatusCode.Unauthorized,"disable invalidates session");
        using var third=client();Check((await Post(third,"/api/auth/login",new{username="staff-check",password=replacement})).StatusCode==HttpStatusCode.Unauthorized,"disabled login denied");
        account=await Account();var disabledReset=change with{RequestId=Guid.NewGuid(),ExpectedVersion=account.GetProperty("version").GetGuid(),Reason="Reset while disabled"};
        Check((await Post(admin,url,disabledReset)).IsSuccessStatusCode,"disabled reset allowed");
        account=await Account();Check(account.GetProperty("disabled").GetBoolean(),"reset does not enable account");
        Check((await Post(admin,url,disable with{RequestId=Guid.NewGuid(),ExpectedVersion=account.GetProperty("version").GetGuid(),Action="Enable",Reason="Rejoined"})).IsSuccessStatusCode,"enable succeeds");
        Check((await Post(third,"/api/auth/login",new{username="staff-check",password=replacement})).IsSuccessStatusCode,"enabled account can log in");
        var all=await admin.GetFromJsonAsync<JsonElement>("/api/staff");
        foreach(var protectedAccount in all.GetProperty("items").EnumerateArray().Where(a=>a.GetProperty("roles").EnumerateArray().Any(r=>r.GetString() is "Admin" or "Manager"))) {
            Check((await Post(admin,$"/api/staff/{protectedAccount.GetProperty("id").GetString()}/change",disable)).StatusCode==HttpStatusCode.BadRequest,"privileged account protected");
        }
        var history=await admin.GetFromJsonAsync<JsonElement>($"/api/staff/{id}/history");
        Check(history.GetArrayLength()==5,"one audit per action including retries");
        Check(history.EnumerateArray().All(e=>e.GetProperty("actor").GetString()=="admin-test"),"actor attribution");
        Check(!history.ToString().Contains(password)&&!history.ToString().Contains(replacement),"audit excludes passwords");
        Console.WriteLine("Staff checks passed: permissions, password resets, session revocation, enable/disable, retries and audit.");
    }
}
