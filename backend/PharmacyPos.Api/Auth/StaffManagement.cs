using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Data;

namespace PharmacyPos.Api.Auth;

public sealed class StaffAccount
{
    public string UserId { get; set; } = "";
    public IdentityUser User { get; set; } = null!;
    public bool Disabled { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
public sealed class StaffAccountEvent
{
    public Guid Id { get; set; }
    public string TargetId { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public Guid ExpectedVersion { get; set; }
}
public static class StaffManagement
{
    public sealed record CreateInput(Guid RequestId, string? Username, string? Password);
    public sealed record ChangeInput(Guid RequestId, Guid ExpectedVersion, string Action, string? Password, string? Reason);
    public static void MapStaffAccounts(this ModelBuilder m) {
        var a=m.Entity<StaffAccount>(); a.ToTable("staff_accounts");a.HasKey(x=>x.UserId);
        a.HasOne(x=>x.User).WithOne().HasForeignKey<StaffAccount>(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
        var e=m.Entity<StaffAccountEvent>();e.ToTable("staff_account_events");e.HasKey(x=>x.Id);
        e.Property(x=>x.Action).HasMaxLength(30);e.Property(x=>x.Reason).HasMaxLength(1000);
        e.HasOne<IdentityUser>().WithMany().HasForeignKey(x=>x.TargetId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<IdentityUser>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x=>new{x.TargetId,x.At});
    }
    static IResult Errors(IdentityResult r)=>Results.BadRequest(new{message=string.Join(" ",r.Errors.Select(x=>x.Description))});
    public static void MapStaffManagement(this WebApplication app) {

        app.MapGet("/api/staff",async(int? page,string? search,PharmacyDbContext db,UserManager<IdentityUser> users,HttpContext http,CancellationToken ct)=>{
            http.Response.Headers.CacheControl="no-store";var n=page??1;var term=search?.Trim()??"";
            if(n is <1 or >10000||term.Length>100)return Results.BadRequest();
            var query=db.Users.AsNoTracking();if(term.Length>0)query=query.Where(u=>u.UserName!=null&&u.UserName.ToUpper().Contains(term.ToUpper()));
            var rows=await query.OrderBy(u=>u.UserName).ThenBy(u=>u.Id).Skip((n-1)*25).Take(26).ToListAsync(ct);
            var ids=rows.Select(u=>u.Id).ToArray();var accounts=await db.StaffAccounts.AsNoTracking().Where(a=>ids.Contains(a.UserId)).ToDictionaryAsync(a=>a.UserId,ct);
            var output=new List<object>();foreach(var u in rows.Take(25)) {
                var roles=await users.GetRolesAsync(u);accounts.TryGetValue(u.Id,out var a);
                output.Add(new{u.Id,username=u.UserName,roles,disabled=a?.Disabled??false,version=a?.Version??Guid.Empty,
                    lockedUntil=u.LockoutEnabled&&u.LockoutEnd>DateTimeOffset.UtcNow?u.LockoutEnd:null,
                    manageable=roles.Count==1&&roles.Contains("Operator")&&u.Id!=http.User.FindFirstValue(ClaimTypes.NameIdentifier)});
            }
            return Results.Ok(new{items=output,hasMore=rows.Count>25});
        }).RequireAuthorization("AdminOnly");
        app.MapGet("/api/staff/{id}/history",async(string id,PharmacyDbContext db,HttpContext http,CancellationToken ct)=>{
            http.Response.Headers.CacheControl="no-store";
            return Results.Ok(await db.StaffAccountEvents.AsNoTracking().Where(e=>e.TargetId==id).OrderByDescending(e=>e.At).Take(100)
                .Select(e=>new{e.Id,e.Action,e.Reason,e.At,actor=db.Users.Where(u=>u.Id==e.ActorId).Select(u=>u.UserName).FirstOrDefault()}).ToListAsync(ct));
        }).RequireAuthorization("AdminOnly");
        app.MapPost("/api/staff",async(CreateInput input,PharmacyDbContext db,UserManager<IdentityUser> users,RoleManager<IdentityRole> roles,HttpContext http,IAntiforgery csrf,CancellationToken ct)=>{
            try {
                await csrf.ValidateRequestAsync(http);var name=input.Username?.Trim()??"";
                if(input.RequestId==Guid.Empty||name.Length is <1 or >100||input.Password is null||input.Password.Length is <12 or >1024)
                    return Results.BadRequest(new{message="Enter a username and a password of 12–1,024 characters."});
                var actor=http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425910)",ct);
                var prior=await db.StaffAccountEvents.SingleOrDefaultAsync(e=>e.Id==input.RequestId,ct);
                if(prior!=null) {
                    var existing=await users.FindByIdAsync(prior.TargetId);
                    return prior.Action=="Created"&&prior.ActorId==actor&&existing?.UserName==name&&await users.CheckPasswordAsync(existing,input.Password)
                        ?Results.Ok(new{id=prior.TargetId}):Results.Conflict(new{message="This request was already used. Refresh the staff list before retrying."});
                }
                if(await users.FindByNameAsync(name)!=null)return Results.Conflict(new{message="That username is already in use."});
                if(!await roles.RoleExistsAsync("Operator"))return Results.Conflict(new{message="The Operator role is missing. Complete the initial admin setup first."});
                var user=new IdentityUser(name){LockoutEnabled=true};var created=await users.CreateAsync(user,input.Password);if(!created.Succeeded)return Errors(created);
                var assigned=await users.AddToRoleAsync(user,"Operator");if(!assigned.Succeeded)return Errors(assigned);
                db.StaffAccounts.Add(new(){UserId=user.Id});db.StaffAccountEvents.Add(new(){Id=input.RequestId,TargetId=user.Id,ActorId=actor,Action="Created",Reason="Operator account created",At=DateTimeOffset.UtcNow});
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Results.Ok(new{id=user.Id});
            }catch(AntiforgeryValidationException){return Results.BadRequest(new{message="Refresh your sign-in before managing staff."});}
            catch(Exception e)when(e is NpgsqlException or DbUpdateException){return Results.Json(new{message="Save was not confirmed. Retry unchanged details or check the staff list."},statusCode:503);}
        }).RequireAuthorization("AdminOnly");
        app.MapPost("/api/staff/{id}/change",async(string id,ChangeInput input,PharmacyDbContext db,UserManager<IdentityUser> users,HttpContext http,IAntiforgery csrf,CancellationToken ct)=>{
            try {
                await csrf.ValidateRequestAsync(http);var reason=input.Reason?.Trim()??"";
                if(input.RequestId==Guid.Empty||input.Action is not("Disable" or "Enable" or "ResetPassword")||reason.Length is <1 or >1000
                    ||(input.Action=="ResetPassword"&&(input.Password is null||input.Password.Length is <12 or >1024)))
                    return Results.BadRequest(new{message="Select an action, enter a reason, and use a valid new password when resetting."});
                var actor=http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425910)",ct);
                var user=await users.FindByIdAsync(id);if(user==null)return Results.NotFound();
                var userRoles=await users.GetRolesAsync(user);
                if(id==actor||userRoles.Count!=1||!userRoles.Contains("Operator"))return Results.BadRequest(new{message="This screen manages operator accounts only. Admin and manager accounts are protected."});
                var prior=await db.StaffAccountEvents.SingleOrDefaultAsync(e=>e.Id==input.RequestId,ct);
                if(prior!=null)return prior.TargetId==id&&prior.ActorId==actor&&prior.Action==input.Action&&prior.Reason==reason&&prior.ExpectedVersion==input.ExpectedVersion
                    &&(input.Action!="ResetPassword"||await users.CheckPasswordAsync(user,input.Password!))?Results.Ok(new{id}):Results.Conflict(new{message="This request was already used. Refresh the staff list."});
                var account=await db.StaffAccounts.SingleOrDefaultAsync(a=>a.UserId==id,ct);
                if((account?.Version??Guid.Empty)!=input.ExpectedVersion)return Results.Conflict(new{message="Another admin changed this account. Refresh before continuing."});
                if(account==null){account=new(){UserId=id};db.StaffAccounts.Add(account);}
                if(input.Action=="ResetPassword") {
                    var token=await users.GeneratePasswordResetTokenAsync(user);var reset=await users.ResetPasswordAsync(user,token,input.Password!);if(!reset.Succeeded)return Errors(reset);
                    var clear=await users.ResetAccessFailedCountAsync(user);if(!clear.Succeeded)return Errors(clear);
                    var unlock=await users.SetLockoutEndDateAsync(user,null);if(!unlock.Succeeded)return Errors(unlock);
                }else account.Disabled=input.Action=="Disable";
                var stamp=await users.UpdateSecurityStampAsync(user);if(!stamp.Succeeded)return Errors(stamp);
                account.Version=Guid.NewGuid();db.StaffAccountEvents.Add(new(){Id=input.RequestId,TargetId=id,ActorId=actor,Action=input.Action,Reason=reason,ExpectedVersion=input.ExpectedVersion,At=DateTimeOffset.UtcNow});
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Results.Ok(new{id});
            }catch(AntiforgeryValidationException){return Results.BadRequest(new{message="Refresh your sign-in before managing staff."});}
            catch(Exception e)when(e is NpgsqlException or DbUpdateException){return Results.Json(new{message="Change was not confirmed. Retry unchanged details or refresh the staff list."},statusCode:503);}
        }).RequireAuthorization("AdminOnly");
    }
}
