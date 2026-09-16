using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Catalogue;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Sales;
namespace PharmacyPos.Api.Stock;

public sealed class StockThreshold { public Guid MedicineId {get;set;} public long? Minimum {get;set;} public Guid Version {get;set;} }
public sealed class StockThresholdEvent {
    public Guid Id {get;set;} public Guid MedicineId {get;set;} public Guid ExpectedVersion {get;set;} public long? PreviousMinimum {get;set;} public long? Minimum {get;set;}
    public string Reason {get;set;}=""; public string ActorId {get;set;}=""; public string ActorName {get;set;}=""; public DateTimeOffset At {get;set;}
}
public static class StockAlerts
{
    public sealed record Input(Guid RequestId,Guid ExpectedVersion,long? Minimum,string? Reason);
    public static void MapStockAlertData(this ModelBuilder m) {
        var t=m.Entity<StockThreshold>();t.ToTable("stock_thresholds",x=>x.HasCheckConstraint("ck_stock_threshold","\"Minimum\" IS NULL OR \"Minimum\" >= 0"));t.HasKey(x=>x.MedicineId);
        t.HasOne<Medicine>().WithMany().HasForeignKey(x=>x.MedicineId).OnDelete(DeleteBehavior.Restrict);
        var e=m.Entity<StockThresholdEvent>();e.ToTable("stock_threshold_events");e.HasKey(x=>x.Id);e.Property(x=>x.Reason).HasMaxLength(1000);e.Property(x=>x.ActorName).HasMaxLength(256);e.HasIndex(x=>new{x.MedicineId,x.At});
        e.HasOne<Medicine>().WithMany().HasForeignKey(x=>x.MedicineId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<IdentityUser>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
    public static void MapStockAlerts(this WebApplication app) {
        if(!app.Environment.IsDevelopment())return;
        app.MapGet("/api/stock-alerts/low",async(string? search,int? page,bool? all,PharmacyDbContext db,HttpContext http,CancellationToken ct)=>{
            http.Response.Headers.CacheControl="no-store";var n=page??1;var term=search?.Trim()??"";var today=StockReceiving.ShopToday();
            if(n is <1 or >10000||term.Length>100)return Results.BadRequest();
            if(all==true&&!http.User.IsInRole("Admin"))return Results.Forbid();
            var eligible=SalesCounter.Eligible(db,today);
            var q=db.Medicines.AsNoTracking().Where(m=>m.IsActive&&m.ReviewStatus==CatalogueReviewStatus.Approved);
            if(term.Length>0)q=q.Where(m=>m.BrandName.ToUpper().Contains(term.ToUpper()));
            var rows=q.Select(m=>new{medicineId=m.Id,brandName=m.BrandName,manufacturer=m.Manufacturer.Name,baseUnit=m.BaseUnit.ToString(),
                minimum=db.StockThresholds.Where(t=>t.MedicineId==m.Id).Select(t=>t.Minimum).FirstOrDefault(),
                version=db.StockThresholds.Where(t=>t.MedicineId==m.Id).Select(t=>(Guid?)t.Version).FirstOrDefault()??Guid.Empty,
                availableUnits=eligible.Where(x=>x.Revision.MedicineId==m.Id).Sum(x=>(long?)(x.Quantity+(db.ReturnedItems.Where(i=>i.ReceiptId==x.ReceiptId&&i.Status=="Restocked").Sum(i=>(long?)i.Quantity)??0)-(db.SaleStockMovements.Where(s=>s.ReceiptId==x.ReceiptId).Sum(s=>(long?)s.Quantity)??0)))??0});
            if(all!=true)rows=rows.Where(r=>r.minimum!=null&&r.availableUnits<=r.minimum);
            var data=await rows.OrderBy(r=>r.brandName).ThenBy(r=>r.medicineId).Skip((n-1)*25).Take(26).ToListAsync(ct);
            return Results.Ok(new{items=data.Take(25),hasMore=data.Count>25,today,checkedAt=DateTimeOffset.UtcNow});
        }).RequireAuthorization("Staff");
        app.MapGet("/api/stock-alerts/expiry",async(string? search,int? page,PharmacyDbContext db,HttpContext http,CancellationToken ct)=>{
            http.Response.Headers.CacheControl="no-store";var n=page??1;var term=search?.Trim()??"";var today=StockReceiving.ShopToday();var until=today.AddDays(90);
            if(n is <1 or >10000||term.Length>100)return Results.BadRequest();
            var q=db.ReceivingMovements.AsNoTracking().Where(x=>x.Batch.ExpiryDate>=today&&x.Batch.ExpiryDate<=until&&!db.StockDisposals.Any(d=>d.ReceiptId==x.ReceiptId));
            if(term.Length>0)q=q.Where(x=>x.Revision.Medicine.BrandName.ToUpper().Contains(term.ToUpper())||x.Batch.BatchNumber.ToUpper().Contains(term.ToUpper()));
            var rows=await q.Select(x=>new{lotId=x.ReceiptId,medicineId=x.Revision.MedicineId,brandName=x.Revision.Medicine.BrandName,manufacturer=x.Revision.Medicine.Manufacturer.Name,baseUnit=x.Revision.BaseUnit.ToString(),x.Batch.BatchNumber,x.Batch.ExpiryDate,
                remainingUnits=x.Quantity+(db.ReturnedItems.Where(i=>i.ReceiptId==x.ReceiptId&&i.Status=="Restocked").Sum(i=>(long?)i.Quantity)??0)-(db.SaleStockMovements.Where(s=>s.ReceiptId==x.ReceiptId).Sum(s=>(long?)s.Quantity)??0),
                sellable=x.Revision.MrpVerifiedAt!=null&&x.Revision.Medicine.IsActive&&x.Revision.Medicine.ReviewStatus==CatalogueReviewStatus.Approved})
                .Where(x=>x.remainingUnits>0).OrderBy(x=>x.ExpiryDate).ThenBy(x=>x.lotId).Skip((n-1)*25).Take(26).ToListAsync(ct);
            return Results.Ok(new{items=rows.Take(25).Select(x=>new{x.lotId,x.medicineId,x.brandName,x.manufacturer,x.baseUnit,x.BatchNumber,x.ExpiryDate,x.remainingUnits,x.sellable,daysRemaining=x.ExpiryDate.DayNumber-today.DayNumber}),hasMore=rows.Count>25,today,until,checkedAt=DateTimeOffset.UtcNow});
        }).RequireAuthorization("Staff");
        app.MapGet("/api/stock-alerts/{id:guid}/history",async(Guid id,PharmacyDbContext db,HttpContext http,CancellationToken ct)=>{
            http.Response.Headers.CacheControl="no-store";return Results.Ok(await db.StockThresholdEvents.AsNoTracking().Where(e=>e.MedicineId==id).OrderByDescending(e=>e.At).ThenBy(e=>e.Id).Take(50).Select(e=>new{e.Id,e.PreviousMinimum,e.Minimum,e.ActorName,e.Reason,e.At}).ToListAsync(ct));
        }).RequireAuthorization("AdminOnly");
        app.MapPost("/api/stock-alerts/{id:guid}/threshold",async(Guid id,Input input,PharmacyDbContext db,HttpContext http,IAntiforgery csrf,CancellationToken ct)=>{
            try {
                await csrf.ValidateRequestAsync(http);var reason=input.Reason?.Trim()??"";
                if(input.RequestId==Guid.Empty||input.Minimum is <0 or >1000000000||reason.Length is <1 or >1000)return Results.BadRequest(new{message="Enter a whole-number minimum from 0 to 1,000,000,000, or disable the threshold, and enter a reason."});
                var actor=http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425917)",ct);
                var prior=await db.StockThresholdEvents.SingleOrDefaultAsync(e=>e.Id==input.RequestId,ct);
                if(prior!=null)return prior.MedicineId==id&&prior.ActorId==actor&&prior.ExpectedVersion==input.ExpectedVersion&&prior.Minimum==input.Minimum&&prior.Reason==reason?Results.Ok(new{id=prior.Id}):Results.Conflict(new{message="Request already used. Refresh thresholds."});
                if(!await db.Medicines.AnyAsync(m=>m.Id==id&&m.IsActive&&m.ReviewStatus==CatalogueReviewStatus.Approved,ct))return Results.BadRequest(new{message="Select an active, approved medicine."});
                var t=await db.StockThresholds.SingleOrDefaultAsync(x=>x.MedicineId==id,ct);
                if((t?.Version??Guid.Empty)!=input.ExpectedVersion)return Results.Conflict(new{message="Another admin changed this threshold. Refresh before continuing."});
                if(t==null){t=new(){MedicineId=id};db.StockThresholds.Add(t);}
                db.StockThresholdEvents.Add(new(){Id=input.RequestId,MedicineId=id,ExpectedVersion=input.ExpectedVersion,PreviousMinimum=t.Minimum,Minimum=input.Minimum,Reason=reason,ActorId=actor,ActorName=http.User.Identity?.Name??actor,At=DateTimeOffset.UtcNow});t.Minimum=input.Minimum;t.Version=input.RequestId;
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Results.Ok(new{id=input.RequestId});
            }catch(AntiforgeryValidationException){return Results.BadRequest(new{message="Refresh your sign-in before changing thresholds."});}
            catch(Exception e)when(e is NpgsqlException or DbUpdateException){return Results.Json(new{message="Save was not confirmed. Retry unchanged details or refresh."},statusCode:503);}
        }).RequireAuthorization("AdminOnly");
    }
}
