using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Data;

namespace PharmacyPos.Api.Stock;

public static class StockDisposalEndpoints
{
    public sealed record DisposeInput(Guid RequestId, Guid LotId, string? Reason, bool PhysicallyDisposed);
    public static void MapStockDisposal(this WebApplication app)
    {
        app.MapPost("/api/stock/disposals", async (DisposeInput input, HttpContext http, IAntiforgery csrf, PharmacyDbContext db, CancellationToken ct) => {
            try {
                await csrf.ValidateRequestAsync(http);
                var reason = input.Reason?.Trim() ?? "";
                if (input.RequestId == Guid.Empty || !input.PhysicallyDisposed || reason.Length is < 1 or > 1000)
                    return Results.BadRequest(new { message = "Confirm physical disposal and enter a reason, up to 1,000 characters." });
                var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425912)", ct);
                var retry = await db.StockDisposals.SingleOrDefaultAsync(x => x.Id == input.RequestId, ct);
                if (retry != null) return retry.ReceiptId == input.LotId && retry.Reason == reason && retry.DisposedByUserId == actor
                    ? Results.Ok(new { retry.Id, retry.VisibleUntil }) : Results.Conflict(new { message = "This disposal identifier was already used for different details." });
                var movement = await db.ReceivingMovements.Include(x => x.Batch).SingleOrDefaultAsync(x => x.ReceiptId == input.LotId, ct);
                if (movement == null) return Results.NotFound();
                if (movement.Batch.ExpiryDate >= StockReceiving.ShopToday()) return Results.BadRequest(new { message = "Only expired stock can be disposed through this workflow." });
                if (await db.StockDisposals.AnyAsync(x => x.ReceiptId == input.LotId, ct)) return Results.Conflict(new { message = "This stock was already disposed. Refresh the inventory." });
                var remaining = movement.Quantity + (await db.ReturnedItems.Where(i => i.ReceiptId == input.LotId && i.Status == "Restocked").SumAsync(i => (long?)i.Quantity, ct) ?? 0) - (await db.SaleStockMovements.Where(m => m.ReceiptId == input.LotId).SumAsync(m => (long?)m.Quantity, ct) ?? 0);
                if (remaining <= 0) return Results.Conflict(new { message = "No stock remains in this lot to dispose." });
                var at = DateTimeOffset.UtcNow;
                var disposal = new StockDisposal { Id = input.RequestId, ReceiptId = input.LotId, Quantity = remaining,
                    Reason = reason, DisposedByUserId = actor, DisposedAt = at, VisibleUntil = at.AddMonths(3) };
                db.StockDisposals.Add(disposal);
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return Results.Ok(new { disposal.Id, disposal.VisibleUntil });
            }
            catch (AntiforgeryValidationException) { return Results.BadRequest(new { message = "Refresh and sign in again before recording disposal." }); }
            catch (Exception e) when (e is NpgsqlException or DbUpdateException or OperationCanceledException) {
                return Results.Json(new { message = "Disposal was not confirmed. Keep the details unchanged and retry, or refresh to check the history." }, statusCode: 503);
            }
        }).RequireAuthorization("AdminOnly");

        app.MapGet("/api/stock/disposals", async (int? page, HttpContext http, PharmacyDbContext db, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            var n = page ?? 1; if (n is < 1 or > 10000) return Results.BadRequest();
            var now = DateTimeOffset.UtcNow;
            var rows = await Rows(db, now).Skip((n - 1) * 25).Take(26).ToListAsync(ct);
            return Results.Ok(new { items = rows.Take(25), page = n, hasMore = rows.Count > 25 });
        }).RequireAuthorization("AdminOnly");
        app.MapGet("/api/stock/disposals/export", async (HttpContext http, PharmacyDbContext db, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            var now = DateTimeOffset.UtcNow;
            var rows = await Rows(db, now).Take(10001).ToListAsync(ct);
            if (rows.Count > 10000) return Results.BadRequest(new { message = "Too many disposal records for a single export." });
            var csv = new StringBuilder("Disposal ID,Lot ID,Medicine,Batch,Expiry date,Quantity,Stock unit,Disposal reason,Disposed by,Disposed at UTC,Visible until UTC\r\n");
            foreach (var r in rows) csv.Append(string.Join(",", new object?[] {r.Id,r.LotId,r.BrandName,r.BatchNumber,r.ExpiryDate,r.Quantity,r.BaseUnit,r.Reason,r.DisposedBy,r.DisposedAt.ToString("O"),r.VisibleUntil.ToString("O")}.Select(StockViews.CsvCell))).Append("\r\n");
            db.StockExportEvents.Add(new StockExportEvent { ActorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!, At = now, RowCount = rows.Count });
            await db.SaveChangesAsync(ct);
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", $"disposed-stock-{now:yyyyMMdd-HHmmss}.csv");
        }).RequireAuthorization("AdminOnly");
    }
    private sealed record DisposalRow(Guid Id, Guid LotId, string BrandName, string BatchNumber, DateOnly ExpiryDate, long Quantity, string BaseUnit, string Reason, string? DisposedBy, DateTimeOffset DisposedAt, DateTimeOffset VisibleUntil);
    private static IQueryable<DisposalRow> Rows(PharmacyDbContext db, DateTimeOffset now) =>
        from d in db.StockDisposals.AsNoTracking()
        join m in db.ReceivingMovements on d.ReceiptId equals m.ReceiptId
        where d.VisibleUntil > now
        orderby d.DisposedAt descending, d.Id
        select new DisposalRow(d.Id,d.ReceiptId,m.Revision.Medicine.BrandName,m.Batch.BatchNumber,m.Batch.ExpiryDate,d.Quantity,m.Revision.BaseUnit.ToString(),d.Reason,
            db.Users.Where(u => u.Id == d.DisposedByUserId).Select(u => u.UserName).FirstOrDefault(),d.DisposedAt,d.VisibleUntil);
}
