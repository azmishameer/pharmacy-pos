using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PharmacyPos.Api.Catalogue;
using PharmacyPos.Api.Data;

namespace PharmacyPos.Api.Stock;

public static class StockViews
{
    public static void MapStockViews(this WebApplication app)
    {
        app.MapStockDisposal();
        app.MapGet("/api/stock/medicines/{id:guid}", async (Guid id, PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            var medicine = await db.Medicines.AsNoTracking().Where(x => x.Id == id && x.IsActive && x.ReviewStatus == CatalogueReviewStatus.Approved)
                .Select(x => new { x.Id, x.BrandName, manufacturer = x.Manufacturer.Name, dosageForm = x.DosageForm.Name, baseUnit = x.BaseUnit.ToString(),
                    batches = x.Batches.OrderByDescending(b => b.ExpiryDate).Select(b => new { b.Id, b.BatchNumber, b.ManufacturingDate, b.ExpiryDate }).ToList() }).SingleOrDefaultAsync(ct);
            return medicine is null ? Results.NotFound() : Results.Ok(new { medicine, today = StockReceiving.ShopToday() });
        }).RequireAuthorization("Staff");
        app.MapGet("/api/stock/receipts", async (string? filter, int? page, PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            var n = page ?? 1;
            if (n is < 1 or > 10000 || filter is not (null or "pending" or "all" or "price")) return Results.BadRequest();
            var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var query = db.StockReceiptRevisions.AsNoTracking().Where(x => x.Revision == x.Receipt.CurrentRevision);
            if (!http.User.IsInRole("Admin")) query = query.Where(x => x.CreatedByUserId == actor || x.Receipt.Revisions.Any(r => r.Revision == 1 && r.CreatedByUserId == actor));
            if (filter is null or "pending") query = query.Where(x => x.Status == ReceivingStatus.PendingApproval || x.Status == ReceivingStatus.Returned);
            if (filter == "price") query = query.Where(x => x.Status == ReceivingStatus.Approved && x.MrpVerifiedAt == null);
            var rows = await query.Include(x => x.Medicine).ThenInclude(m => m.Manufacturer).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((n - 1) * 25).Take(26).ToListAsync(ct);
            return Results.Ok(new { items = rows.Take(25).Select(Detail), page = n, hasMore = rows.Count > 25, today = StockReceiving.ShopToday() });
        }).RequireAuthorization("Staff");
        app.MapGet("/api/stock/receipts/{id:guid}", async (Guid id, PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var all = db.StockReceiptRevisions.AsNoTracking().Where(x => x.ReceiptId == id);
            if (!http.User.IsInRole("Admin") && !await all.AnyAsync(x => x.CreatedByUserId == actor, ct)) return Results.NotFound();
            var rows = await all.Include(x => x.Medicine).ThenInclude(m => m.Manufacturer).OrderByDescending(x => x.Revision).ToListAsync(ct);
            return rows.Count == 0 ? Results.NotFound() : Results.Ok(rows.Select(Detail));
        }).RequireAuthorization("Staff");
        app.MapGet("/api/stock/inventory", async (string? search, string? availability, int? page, PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            var n = page ?? 1; var term = search?.Trim() ?? "";
            if (n is < 1 or > 10000 || term.Length > 100 || availability is not (null or "all" or "available" or "held")) return Results.BadRequest();
            var today = StockReceiving.ShopToday();
            var query = db.ReceivingMovements.AsNoTracking().Where(x => !db.StockDisposals.Any(d => d.ReceiptId == x.ReceiptId) && x.Quantity + (db.ReturnedItems.Where(i => i.ReceiptId == x.ReceiptId && i.Status == "Restocked").Sum(i => (long?)i.Quantity) ?? 0) > (db.SaleStockMovements.Where(m => m.ReceiptId == x.ReceiptId).Sum(m => (long?)m.Quantity) ?? 0));
            if (availability == "available") query = query.Where(x => x.Batch.ExpiryDate >= today && x.Revision.MrpVerifiedAt != null && x.Revision.Medicine.IsActive && x.Revision.Medicine.ReviewStatus == CatalogueReviewStatus.Approved);
            if (availability == "held") query = query.Where(x => x.Batch.ExpiryDate < today || x.Revision.MrpVerifiedAt == null || !x.Revision.Medicine.IsActive || x.Revision.Medicine.ReviewStatus != CatalogueReviewStatus.Approved);
            if (term.Length > 0) query = query.Where(x => x.Revision.Medicine.BrandName.ToUpper().Contains(term.ToUpper()));
            var rows = await query.OrderBy(x => x.Batch.ExpiryDate).ThenBy(x => x.Id).Skip((n - 1) * 25).Take(26)
                .Select(x => new {
                    lotId = x.ReceiptId, x.BatchId, x.Batch.BatchNumber, x.Batch.ExpiryDate,
                    brandName = x.Revision.Medicine.BrandName, manufacturer = x.Revision.Medicine.Manufacturer.Name,
                    baseUnit = x.Revision.BaseUnit.ToString(), receivedUnits = x.Quantity, onHandUnits = x.Quantity + (db.ReturnedItems.Where(i => i.ReceiptId == x.ReceiptId && i.Status == "Restocked").Sum(i => (long?)i.Quantity) ?? 0) - (db.SaleStockMovements.Where(m => m.ReceiptId == x.ReceiptId).Sum(m => (long?)m.Quantity) ?? 0),
                    sellableUnits = x.Batch.ExpiryDate >= today && x.Revision.MrpVerifiedAt != null && x.Revision.Medicine.IsActive && x.Revision.Medicine.ReviewStatus == CatalogueReviewStatus.Approved ? x.Quantity + (db.ReturnedItems.Where(i => i.ReceiptId == x.ReceiptId && i.Status == "Restocked").Sum(i => (long?)i.Quantity) ?? 0) - (db.SaleStockMovements.Where(m => m.ReceiptId == x.ReceiptId).Sum(m => (long?)m.Quantity) ?? 0) : 0,
                    availability = x.Batch.ExpiryDate < today ? "Expired — cannot be sold" : !x.Revision.Medicine.IsActive || x.Revision.Medicine.ReviewStatus != CatalogueReviewStatus.Approved ? "Medicine inactive" : x.Revision.MrpVerifiedAt == null ? "MRP verification pending" : "Available",
                    x.Revision.UnitsPerStrip, x.Revision.UnitsPerBox, x.Revision.MrpAmount, x.Revision.MrpUnit, x.Revision.MrpUnits,
                    mrpVerified = x.Revision.MrpVerifiedAt != null
                }).ToListAsync(ct);
            return Results.Ok(new { items = rows.Take(25), page = n, hasMore = rows.Count > 25, today });
        }).RequireAuthorization("Staff");
        app.MapGet("/api/stock/receipts/export", async (PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            var rows = await db.StockReceiptRevisions.AsNoTracking().Include(x => x.Medicine).ThenInclude(m => m.Manufacturer)
                .Where(x => x.Revision == x.Receipt.CurrentRevision && x.Status == ReceivingStatus.PendingApproval)
                .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(10001).ToListAsync(ct);
            if (rows.Count > 10000) return Results.BadRequest(new { message = "More than 10,000 pending entries. Review some entries before exporting." });
            var at = DateTimeOffset.UtcNow;
            var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var csv = new StringBuilder("Receipt ID,Revision ID,Revision,Medicine,Manufacturer,Supplier,Delivery reference,Received date,Batch,Manufacturing date,Expiry date,Base unit,Pieces,Strips,Boxes,Cartons,Units per strip,Strips per box,Units per box,Boxes per carton,Total units,Printed MRP,MRP unit,Units in MRP unit,MRP verified,Stock status,Entered by,Exported at UTC\r\n");
            foreach (var r in rows) csv.Append(string.Join(",", new object?[] {r.ReceiptId,r.Id,r.Revision,r.Medicine.BrandName,r.Medicine.Manufacturer.Name,r.Supplier,r.DeliveryReference,r.ReceivedDate,r.BatchNumber,r.ManufacturingDate,r.ExpiryDate,r.BaseUnit,r.Pieces,r.Strips,r.Boxes,r.Cartons,r.UnitsPerStrip,r.StripsPerBox,r.UnitsPerBox,r.BoxesPerCarton,r.TotalUnits,r.MrpAmount,r.MrpUnit,r.MrpUnits,r.MrpVerifiedAt!=null,r.Status,r.CreatedByUserId,at.ToString("O")}.Select(CsvCell))).Append("\r\n");
            db.StockExportEvents.Add(new StockExportEvent { ActorId = actor, At = at, RowCount = rows.Count });
            await db.SaveChangesAsync(ct);
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", $"pending-stock-{at:yyyyMMdd-HHmmss}.csv");
        }).RequireAuthorization("AdminOnly");
    }
    public static string CsvCell(object? value)
    {
        var s = value switch { DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => value?.ToString() ?? "" };
        if (value is string && s.TrimStart().Length > 0 && "=+-@".Contains(s.TrimStart()[0])) s = "'" + s;
        s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
    // Explicit public projection: future private cost fields must never leak through entity serialization.
    private static object Detail(StockReceiptRevision x) => new {
        x.Id,x.ReceiptId,x.Revision,x.MedicineId,brandName=x.Medicine.BrandName,manufacturer=x.Medicine.Manufacturer.Name,
        baseUnit=x.BaseUnit.ToString(),x.Supplier,x.DeliveryReference,x.ReceivedDate,x.BatchNumber,x.ManufacturingDate,x.ExpiryDate,
        x.UnitsPerStrip,x.StripsPerBox,x.UnitsPerBox,x.BoxesPerCarton,x.Pieces,x.Strips,x.Boxes,x.Cartons,x.TotalUnits,
        x.MrpAmount,x.MrpUnit,x.MrpUnits,mrpVerified=x.MrpVerifiedAt!=null,status=x.Status.ToString(),x.CreatedByUserId,
        x.ReviewNote,x.CorrectionReason,x.CreatedAt,x.ReviewedAt,x.ReviewedByUserId,x.AutomaticApproval
    };
}
