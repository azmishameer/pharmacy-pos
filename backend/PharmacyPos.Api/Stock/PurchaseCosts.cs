using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Data;

namespace PharmacyPos.Api.Stock;

public sealed class PurchaseCostEntry
{
    public Guid Id { get; set; }
    public Guid ReceiptId { get; set; }
    public int Revision { get; set; }
    public Guid? PreviousId { get; set; }
    public decimal TotalCost { get; set; }
    public long ReceivedUnits { get; set; }
    public string Reason { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string ActorName { get; set; } = "";
    public DateTimeOffset At { get; set; }
}
public static class PurchaseCosts
{
    public sealed record Input(Guid RequestId, Guid? ExpectedId, decimal TotalCost, string? Reason);
    public static void MapPurchaseCostsData(this ModelBuilder m) {
        var e=m.Entity<PurchaseCostEntry>();e.ToTable("purchase_cost_entries",t=>t.HasCheckConstraint("ck_purchase_cost", "\"TotalCost\" >= 0 AND \"ReceivedUnits\" > 0 AND \"Revision\" > 0"));
        e.HasKey(x=>x.Id);e.HasIndex(x=>new{x.ReceiptId,x.Revision}).IsUnique();e.Property(x=>x.TotalCost).HasPrecision(18,2);
        e.Property(x=>x.Reason).HasMaxLength(1000);e.Property(x=>x.ActorName).HasMaxLength(256);
        e.HasOne<StockReceipt>().WithMany().HasForeignKey(x=>x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<IdentityUser>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
    static object View(PurchaseCostEntry e)=>new{e.Id,e.Revision,e.TotalCost,e.ReceivedUnits,unitCost=decimal.Round(e.TotalCost/e.ReceivedUnits,6),e.Reason,e.ActorName,e.At};
    public static void MapPurchaseCosts(this WebApplication app) {
        if(!app.Environment.IsDevelopment())return;
        app.MapGet("/api/purchase-costs",async(int? page,string? search,PharmacyDbContext db,HttpContext http,CancellationToken ct)=>{
            http.Response.Headers.CacheControl="no-store";var n=page??1;var term=search?.Trim()??"";
            if(n is <1 or >10000||term.Length>100)return Results.BadRequest();
            // Original receiving movements survive sell-out/disposal; costs describe the whole delivery.
            var q=db.ReceivingMovements.AsNoTracking();
            if(term.Length>0)q=q.Where(x=>x.Revision.Medicine.BrandName.ToUpper().Contains(term.ToUpper())||x.Batch.BatchNumber.ToUpper().Contains(term.ToUpper()));
            var rows=await q.OrderByDescending(x=>x.Revision.ReceivedDate).ThenBy(x=>x.ReceiptId).Skip((n-1)*25).Take(26)
                .Select(x=>new{x.ReceiptId,brandName=x.Revision.Medicine.BrandName,x.Batch.BatchNumber,x.Revision.Supplier,x.Revision.DeliveryReference,x.Revision.ReceivedDate,receivedUnits=x.Quantity,baseUnit=x.Revision.BaseUnit.ToString()}).ToListAsync(ct);
            var ids=rows.Take(25).Select(x=>x.ReceiptId).ToArray();
            var costs=await db.PurchaseCostEntries.AsNoTracking().Where(e=>ids.Contains(e.ReceiptId)&&!db.PurchaseCostEntries.Any(newer=>newer.ReceiptId==e.ReceiptId&&newer.Revision>e.Revision)).ToDictionaryAsync(e=>e.ReceiptId,ct);
            return Results.Ok(new{items=rows.Take(25).Select(x=>new{x.ReceiptId,x.brandName,x.BatchNumber,x.Supplier,x.DeliveryReference,x.ReceivedDate,x.receivedUnits,x.baseUnit,cost=costs.TryGetValue(x.ReceiptId,out var cost)?View(cost):null}),hasMore=rows.Count>25});
        }).RequireAuthorization("AdminOnly");
        app.MapGet("/api/purchase-costs/{id:guid}/history",async(Guid id,int? page,PharmacyDbContext db,HttpContext http,CancellationToken ct)=>{
            http.Response.Headers.CacheControl="no-store";var n=page??1;if(n is <1 or >10000)return Results.BadRequest();
            var rows=await db.PurchaseCostEntries.AsNoTracking().Where(x=>x.ReceiptId==id).OrderByDescending(x=>x.Revision).Skip((n-1)*25).Take(26).ToListAsync(ct);
            return Results.Ok(new{items=rows.Take(25).Select(View),hasMore=rows.Count>25});
        }).RequireAuthorization("AdminOnly");
        app.MapPost("/api/purchase-costs/{id:guid}",async(Guid id,Input input,PharmacyDbContext db,HttpContext http,IAntiforgery csrf,CancellationToken ct)=>{
            http.Response.Headers.CacheControl="no-store";
            try {
                await csrf.ValidateRequestAsync(http);var reason=input.Reason?.Trim()??"";
                if(input.RequestId==Guid.Empty||input.TotalCost<0||input.TotalCost>1000000000000m||decimal.Round(input.TotalCost,2)!=input.TotalCost||reason.Length is <1 or >1000)
                    return Results.BadRequest(new{message="Enter a non-negative delivery cost with at most two decimal places and a reason."});
                var actor=http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425912)",ct);
                var prior=await db.PurchaseCostEntries.SingleOrDefaultAsync(x=>x.Id==input.RequestId,ct);
                if(prior!=null)return prior.ReceiptId==id&&prior.ActorId==actor&&prior.TotalCost==input.TotalCost&&prior.Reason==reason&&prior.PreviousId==input.ExpectedId
                    ?Results.Ok(View(prior)):Results.Conflict(new{message="That request was already used with different details. Refresh the cost list."});
                var movement=await db.ReceivingMovements.AsNoTracking().SingleOrDefaultAsync(x=>x.ReceiptId==id,ct);
                if(movement==null)return Results.BadRequest(new{message="Approve this delivery before recording its purchase cost."});
                var latest=await db.PurchaseCostEntries.Where(x=>x.ReceiptId==id).OrderByDescending(x=>x.Revision).FirstOrDefaultAsync(ct);
                if(latest?.Id!=input.ExpectedId)return Results.Conflict(new{message="Another admin updated this cost. Refresh and review the latest value."});
                var entry=new PurchaseCostEntry{Id=input.RequestId,ReceiptId=id,Revision=(latest?.Revision??0)+1,PreviousId=latest?.Id,TotalCost=input.TotalCost,ReceivedUnits=movement.Quantity,Reason=reason,ActorId=actor,ActorName=http.User.Identity?.Name??actor,At=DateTimeOffset.UtcNow};
                db.PurchaseCostEntries.Add(entry);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Results.Ok(View(entry));
            }catch(AntiforgeryValidationException){return Results.BadRequest(new{message="Refresh your sign-in before saving purchase costs."});}
            catch(Exception e)when(e is NpgsqlException or DbUpdateException){return Results.Json(new{message="Save was not confirmed. Retry unchanged details or refresh the cost list."},statusCode:503);}
        }).RequireAuthorization("AdminOnly");
    }
}
