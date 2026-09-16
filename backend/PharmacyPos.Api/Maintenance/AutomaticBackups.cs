using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Data;
namespace PharmacyPos.Api.Maintenance;

public sealed class BackupSetting { public int Id {get;set;}=1; public bool Paused {get;set;} public Guid Version {get;set;} }
public sealed class BackupSettingEvent {
    public Guid Id {get;set;} public Guid ExpectedVersion {get;set;} public bool Paused {get;set;}
    public string ActorId {get;set;}=""; public string ActorName {get;set;}=""; public string Reason {get;set;}=""; public DateTimeOffset At {get;set;}
}
public sealed class BackupRun {
    public Guid Id {get;set;}=Guid.NewGuid(); public DateOnly DueDate {get;set;} public string Status {get;set;}="Running";
    public DateTimeOffset StartedAt {get;set;} public DateTimeOffset? FinishedAt {get;set;}
    public string? Archive {get;set;} public string? Error {get;set;}
}
public static class AutomaticBackups
{
    public static DateOnly? DueDate(DateTimeOffset now) {
        var local=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now,"Asia/Dhaka");
        return local.TimeOfDay>=TimeSpan.FromHours(2)?DateOnly.FromDateTime(local.DateTime):null;
    }
    public static void MapBackupData(this ModelBuilder model) {
        var s=model.Entity<BackupSetting>();s.ToTable("backup_settings",t=>t.HasCheckConstraint("ck_backup_singleton","\"Id\" = 1"));s.HasKey(x=>x.Id);s.Property(x=>x.Id).ValueGeneratedNever();
        var e=model.Entity<BackupSettingEvent>();e.ToTable("backup_setting_events");e.HasKey(x=>x.Id);e.Property(x=>x.Reason).HasMaxLength(1000);e.Property(x=>x.ActorName).HasMaxLength(256);
        e.HasOne<IdentityUser>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.Restrict);
        var r=model.Entity<BackupRun>();r.ToTable("backup_runs",t=>t.HasCheckConstraint("ck_backup_run_status","\"Status\" IN ('Running','Completed','Failed')"));r.HasKey(x=>x.Id);r.Property(x=>x.Status).HasMaxLength(20);r.Property(x=>x.Archive).HasMaxLength(500);r.Property(x=>x.Error).HasMaxLength(500);
        r.HasIndex(x=>new{x.DueDate,x.StartedAt});r.HasIndex(x=>x.DueDate).IsUnique().HasFilter("\"Status\" = 'Completed'");
    }
    // Session lock spans the file operation; settings lock only spans the decision to start.
    public static async Task Tick(PharmacyDbContext db,DateTimeOffset now,Func<CancellationToken,Task<string>> create,CancellationToken ct) {
        var tickStarted=System.Diagnostics.Stopwatch.GetTimestamp();
        if(DueDate(now) is not DateOnly date)return;
        await db.Database.OpenConnectionAsync(ct);
        var connection=(NpgsqlConnection)db.Database.GetDbConnection();
        await using var acquire=new NpgsqlCommand("SELECT pg_try_advisory_lock(718425915)",connection);
        if(!(bool)(await acquire.ExecuteScalarAsync(ct))!)return;
        try {
            BackupRun run;
            await using(var tx=await db.Database.BeginTransactionAsync(ct)) {
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425916)",ct);
                if(await db.BackupSettings.AnyAsync(s=>s.Paused,ct))return;
                // Acquiring the session lock proves no other live worker owns a Running record.
                var stale=await db.BackupRuns.Where(r=>r.Status=="Running").ToListAsync(ct);
                foreach(var r in stale){r.Status="Failed";r.FinishedAt=now;r.Error="Server stopped before completion was recorded. A fresh backup will be attempted.";}
                if(await db.BackupRuns.AnyAsync(r=>r.DueDate==date&&r.Status=="Completed",ct)){await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return;}
                if(await db.BackupRuns.AnyAsync(r=>r.DueDate==date&&r.Status=="Failed"&&r.FinishedAt>now.AddMinutes(-15),ct)){await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return;}
                run=new(){DueDate=date,StartedAt=now};db.BackupRuns.Add(run);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            }
            try {run.Archive=Path.GetFileName(await create(ct));run.Status="Completed";}
            catch(Exception e) when(e is IOException or InvalidOperationException or NpgsqlException or System.ComponentModel.Win32Exception or OperationCanceledException or UnauthorizedAccessException) {
                run.Status="Failed";run.Error="Backup failed or was interrupted. Check PostgreSQL tools, database access, folder permissions and free disk space. It will retry after 15 minutes while enabled.";
            }
            run.FinishedAt=now+System.Diagnostics.Stopwatch.GetElapsedTime(tickStarted);
            // Record failure even if shutdown canceled the file operation.
            using var saveTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));await db.SaveChangesAsync(saveTimeout.Token);
        } finally {
            await using var release=new NpgsqlCommand("SELECT pg_advisory_unlock(718425915)",connection);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));await release.ExecuteScalarAsync(timeout.Token);
        }
    }
    public sealed record Input(Guid RequestId,Guid ExpectedVersion,bool Paused,string? Reason);
    public static void MapAutomaticBackups(this WebApplication app) {
        if(!app.Environment.IsDevelopment())return;
        app.MapGet("/api/backups",async(PharmacyDbContext db,HttpContext http,IConfiguration config,CancellationToken ct)=>{
            http.Response.Headers.CacheControl="no-store";
            var s=await db.BackupSettings.AsNoTracking().SingleOrDefaultAsync(ct);
            var runs=await db.BackupRuns.AsNoTracking().OrderByDescending(r=>r.StartedAt).Take(20).ToListAsync(ct);
            var history=await db.BackupSettingEvents.AsNoTracking().OrderByDescending(e=>e.At).Take(20).Select(e=>new{e.Paused,e.ActorName,e.Reason,e.At}).ToListAsync(ct);
            return Results.Ok(new{paused=s?.Paused??false,version=s?.Version??Guid.Empty,schedule="02:00",timeZone="Asia/Dhaka",workerEnabled=config.GetValue("Backup:AutomaticEnabled",true),runs,history});
        }).RequireAuthorization("AdminOnly");
        app.MapPost("/api/backups/schedule",async(Input input,PharmacyDbContext db,HttpContext http,IAntiforgery csrf,CancellationToken ct)=>{
            try {
                await csrf.ValidateRequestAsync(http);var reason=input.Reason?.Trim()??"";
                if(input.RequestId==Guid.Empty||reason.Length is <1 or >1000)return Results.BadRequest(new{message="Enter a reason for changing automatic backups."});
                var actor=http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425916)",ct);
                var prior=await db.BackupSettingEvents.SingleOrDefaultAsync(e=>e.Id==input.RequestId,ct);
                if(prior!=null)return prior.ActorId==actor&&prior.Paused==input.Paused&&prior.Reason==reason&&prior.ExpectedVersion==input.ExpectedVersion?Results.Ok(new{id=prior.Id}):Results.Conflict(new{message="Request already used. Refresh backup settings."});
                var s=await db.BackupSettings.SingleOrDefaultAsync(ct);
                if((s?.Version??Guid.Empty)!=input.ExpectedVersion)return Results.Conflict(new{message="Another admin changed the schedule. Refresh before continuing."});
                if(s==null){s=new();db.BackupSettings.Add(s);}s.Paused=input.Paused;s.Version=input.RequestId;
                db.BackupSettingEvents.Add(new(){Id=input.RequestId,ExpectedVersion=input.ExpectedVersion,Paused=input.Paused,Reason=reason,ActorId=actor,ActorName=http.User.Identity?.Name??actor,At=DateTimeOffset.UtcNow});
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Results.Ok(new{id=input.RequestId});
            }catch(AntiforgeryValidationException){return Results.BadRequest(new{message="Refresh your sign-in before changing backups."});}
            catch(Exception e)when(e is NpgsqlException or DbUpdateException){return Results.Json(new{message="Save was not confirmed. Retry unchanged details or refresh."},statusCode:503);}
        }).RequireAuthorization("AdminOnly");
    }
}
public sealed class BackupWorker(IServiceScopeFactory scopes,IConfiguration config,IWebHostEnvironment env,ILogger<BackupWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if(!config.GetValue("Backup:AutomaticEnabled",true))return;
        while(!stoppingToken.IsCancellationRequested) {
            try {using var scope=scopes.CreateScope();await AutomaticBackups.Tick(scope.ServiceProvider.GetRequiredService<PharmacyDbContext>(),DateTimeOffset.UtcNow,ct=>DatabaseBackup.RunAsync(config,env.ContentRootPath,ct),stoppingToken);}
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){logger.LogWarning("Automatic backup check failed. Check database connectivity and apply migrations. Retrying in one minute.");}
            try{await Task.Delay(TimeSpan.FromMinutes(1),stoppingToken);}catch(OperationCanceledException){break;}
        }
    }
}
