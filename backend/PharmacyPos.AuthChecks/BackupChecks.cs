using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Maintenance;
public static class BackupChecks
{
    static void Check(bool ok,string m){if(!ok)throw new Exception("BACKUP CHECK FAILED: "+m);}
    static async Task<HttpResponseMessage> Post(HttpClient c,object input) {
        var s=await c.GetFromJsonAsync<JsonElement>("/api/auth/session");var r=new HttpRequestMessage(HttpMethod.Post,"/api/backups/schedule"){Content=JsonContent.Create(input)};
        r.Headers.Add("X-CSRF-TOKEN",s.GetProperty("csrfToken").GetString());return await c.SendAsync(r);
    }
    public static async Task Run(HttpClient admin,HttpClient op,HttpClient anon,IServiceProvider services) {
        var at=new DateTimeOffset(2038,1,2,20,0,0,TimeSpan.Zero); // January 3, 02:00 in Bangladesh
        Check(AutomaticBackups.DueDate(at)==new DateOnly(2038,1,3)&&AutomaticBackups.DueDate(at.AddSeconds(-1))==null,"exact 2 AM Dhaka boundary");
        var calls=0;
        async Task Tick(DateTimeOffset now,Func<CancellationToken,Task<string>>? writer=null) {
            using var scope=services.CreateScope();await AutomaticBackups.Tick(scope.ServiceProvider.GetRequiredService<PharmacyDbContext>(),now,writer??(_=>{Interlocked.Increment(ref calls);return Task.FromResult("test-archive.dump");}),CancellationToken.None);
        }
        Check((await op.GetAsync("/api/backups")).StatusCode==HttpStatusCode.Forbidden,"operator cannot view backups");
        Check((await anon.GetAsync("/api/backups")).StatusCode==HttpStatusCode.Unauthorized,"anonymous cannot view backups");
        var pause=new AutomaticBackups.Input(Guid.NewGuid(),Guid.Empty,true,"Schedule test");
        Check((await Post(op,pause)).StatusCode==HttpStatusCode.Forbidden,"operator cannot pause");
        Check((await Post(anon,pause)).StatusCode==HttpStatusCode.Unauthorized,"anonymous cannot pause");
        Check((await admin.PostAsJsonAsync("/api/backups/schedule",pause)).StatusCode==HttpStatusCode.BadRequest,"CSRF enforced");
        await Tick(at.AddSeconds(-1));Check(calls==0,"not before 2 AM");
        await Task.WhenAll(Tick(at),Tick(at));Check(calls==1,"one backup across concurrent workers");
        await Tick(at.AddHours(3));Check(calls==1,"no repeated same-day successful backup");
        Check((await Post(admin,pause)).IsSuccessStatusCode,"admin pause");Check((await Post(admin,pause)).IsSuccessStatusCode,"pause retry");
        await Tick(at.AddDays(1));Check(calls==1,"pause persists across worker scopes");
        Check((await Post(admin,pause with{RequestId=Guid.NewGuid(),Paused=false})).StatusCode==HttpStatusCode.Conflict,"stale admin rejected");
        var resume=pause with{RequestId=Guid.NewGuid(),ExpectedVersion=pause.RequestId,Paused=false,Reason="Resume test"};Check((await Post(admin,resume)).IsSuccessStatusCode,"resume");
        await Tick(at.AddDays(1).AddHours(4));Check(calls==2,"resume after 2 AM catches up");
        var failedAt=at.AddDays(2);
        await Tick(failedAt,_=>{calls++;throw new IOException("Test secret details must never be exposed");});Check(calls==3,"failure attempted");
        await Tick(failedAt.AddMinutes(5));Check(calls==3,"failure backs off");
        await Tick(failedAt.AddMinutes(16));Check(calls==4,"failed day retried");
        using(var scope=services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();db.BackupRuns.Add(new(){DueDate=new DateOnly(2038,1,6),StartedAt=at.AddDays(3),Status="Running"});await db.SaveChangesAsync();}
        await Tick(at.AddDays(3).AddHours(1));Check(calls==5,"interrupted run recovered");
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active=Tick(at.AddDays(4),async ct=>{entered.SetResult();await release.Task;calls++;return "in-flight.dump";});
        await entered.Task;
        var pauseAgain=pause with{RequestId=Guid.NewGuid(),ExpectedVersion=resume.RequestId,Reason="Pause while running"};Check((await Post(admin,pauseAgain)).IsSuccessStatusCode,"pause does not block on ongoing dump");
        release.SetResult();await active;Check(calls==6,"in-progress backup finishes");
        await Tick(at.AddDays(5));Check(calls==6,"no future run while paused");
        var state=await admin.GetFromJsonAsync<JsonElement>("/api/backups");Check(state.GetProperty("paused").GetBoolean()&&state.GetProperty("history").GetArrayLength()==3,"audited setting changes without duplicate retry");
        Check(!state.ToString().Contains("Test secret details"),"failure details sanitized");
        Check(state.GetProperty("runs").EnumerateArray().Count(r=>r.GetProperty("status").GetString()=="Completed")==5,"successful daily run records");
        Console.WriteLine("Backup checks passed: Dhaka schedule, concurrency, pause/resume, retry, crash recovery, audit and permissions.");
    }
}
