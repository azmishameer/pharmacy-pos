using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Data;
namespace PharmacyPos.Api.Sales;

public sealed class ChargeClassificationEvent
{
    public Guid Id { get; set; }
    public Guid ChargeId { get; set; }
    public Guid? PreviousId { get; set; }
    public bool? PreviousValue { get; set; }
    public bool ExcludeFromProfit { get; set; }
    public string Reason { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string ActorName { get; set; } = "";
    public DateTimeOffset At { get; set; }
}
public static class ChargeClassification
{
    public sealed record Input(Guid RequestId, Guid? ExpectedVersion, bool? ExcludeFromProfit, string? Reason);
    public static void MapChargeClassification(this ModelBuilder m) {
        var e=m.Entity<ChargeClassificationEvent>(); e.ToTable("charge_classification_events");e.HasKey(x=>x.Id);
        e.Property(x=>x.Reason).HasMaxLength(1000);e.Property(x=>x.ActorName).HasMaxLength(256);e.HasIndex(x=>new{x.ChargeId,x.At});
        e.HasOne<ChargeRule>().WithMany().HasForeignKey(x=>x.ChargeId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<IdentityUser>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
    public static void MapChargeClassification(this WebApplication app) {
        app.MapGet("/api/charges/{id:guid}/profit-history",async(Guid id,int? page,PharmacyDbContext db,HttpContext http,CancellationToken ct)=>{
            http.Response.Headers.CacheControl="no-store";var n=page??1;if(n is <1 or >10000)return Results.BadRequest();
            var rule=await db.ChargeRules.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);if(rule==null)return Results.NotFound();
            var rows=await db.ChargeClassificationEvents.AsNoTracking().Where(x=>x.ChargeId==id).OrderByDescending(x=>x.At).ThenBy(x=>x.Id).Skip((n-1)*25).Take(26)
                .Select(x=>new{x.Id,x.PreviousValue,x.ExcludeFromProfit,x.Reason,x.ActorName,x.At}).ToListAsync(ct);
            return Results.Ok(new{initialExcludeFromProfit=rule.InitialExcludeFromProfit,rule.CreatedAt,createdBy=await db.Users.Where(u=>u.Id==rule.CreatedBy).Select(u=>u.UserName).SingleAsync(ct),items=rows.Take(25),hasMore=rows.Count>25});
        }).RequireAuthorization("AdminOnly");
        app.MapPost("/api/charges/{id:guid}/profit-classification",async(Guid id,Input input,PharmacyDbContext db,HttpContext http,IAntiforgery csrf,CancellationToken ct)=>{
            try {
                await csrf.ValidateRequestAsync(http);var reason=input.Reason?.Trim()??"";
                if(input.RequestId==Guid.Empty||input.ExcludeFromProfit==null||reason.Length is <1 or >1000)return Results.BadRequest(new{message="Choose the profit treatment and enter a reason."});
                var actor=http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425913)",ct);
                var prior=await db.ChargeClassificationEvents.SingleOrDefaultAsync(x=>x.Id==input.RequestId,ct);
                if(prior!=null)return prior.ChargeId==id&&prior.ActorId==actor&&prior.PreviousId==input.ExpectedVersion&&prior.ExcludeFromProfit==input.ExcludeFromProfit&&prior.Reason==reason?Results.Ok(new{id=prior.Id}):Results.Conflict(new{message="This request was already used. Refresh charges."});
                var rule=await db.ChargeRules.SingleOrDefaultAsync(x=>x.Id==id,ct);if(rule==null)return Results.NotFound();
                if(rule.ClassificationVersion!=input.ExpectedVersion)return Results.Conflict(new{message="Another admin changed this classification. Refresh charges before continuing."});
                db.ChargeClassificationEvents.Add(new(){Id=input.RequestId,ChargeId=id,PreviousId=rule.ClassificationVersion,PreviousValue=rule.ExcludeFromProfit,ExcludeFromProfit=input.ExcludeFromProfit.Value,Reason=reason,ActorId=actor,ActorName=http.User.Identity?.Name??actor,At=DateTimeOffset.UtcNow});
                rule.ExcludeFromProfit=input.ExcludeFromProfit;rule.ClassificationVersion=input.RequestId;
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Results.Ok(new{id=input.RequestId});
            }catch(AntiforgeryValidationException){return Results.BadRequest(new{message="Refresh your sign-in before saving."});}
            catch(Exception e)when(e is NpgsqlException or DbUpdateException){return Results.Json(new{message="Save was not confirmed. Retry unchanged details or refresh charges."},statusCode:503);}
        }).RequireAuthorization("AdminOnly");
    }
}
