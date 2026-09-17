using PharmacyPos.Api.Sales;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Catalogue;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Stock;

namespace PharmacyPos.Api.Returns;

public static class ReturnEndpoints
{
    public static bool WithinReturnWindow(DateTimeOffset purchased, DateTimeOffset now) => now >= purchased && now <= purchased.AddDays(15);
    public sealed record Input(Guid RequestId, Guid SaleId, List<int>? LineIndexes, string? Reason, bool ItemsReceived, bool CashRefunded, bool ReceiptPresented = false, string RefundDestination = "Cash", List<RefundPayments.ReferenceInput>? PaymentReferences = null);
    public sealed record Review(string Action, string? Reason, bool Checked);
    private static object Item(ReturnedItem i) => new { i.Id, i.LineIndex, i.BrandName, i.BatchNumber, i.BaseUnit, i.Packs, i.Unit, i.Quantity, i.Refund, i.Status, i.ReviewerName, i.ReviewedAt, i.ReviewReason };
    private static object Result(SaleReturn r) => new { r.Id, r.SaleId, receiptNumber = $"POS-{r.Sale.Number:D8}", r.Amount, r.Method, r.RefundDestination, payments = r.Payments.OrderBy(p => Array.IndexOf(PaymentMethods.All, p.Method)).Select(p => new { p.Method, p.Amount, p.Reference }), r.ReceiptPresented, r.Reason, r.ActorName, r.At, items = r.Items.OrderBy(i => i.LineIndex).Select(Item) };
    public static void MapReturnEndpoints(this WebApplication app) {

        app.MapGet("/api/returns/sale", async (string? number, PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            var text = (number ?? "").Trim(); if (text.StartsWith("POS-",StringComparison.OrdinalIgnoreCase)) text = text[4..];
            if (!long.TryParse(text, out var n) || n <= 0) return Results.BadRequest(new { message = "Enter the receipt number, for example POS-00000001." });
            var sale = await db.Sales.AsNoTracking().Include(s => s.Payments).SingleOrDefaultAsync(s => s.Number == n, ct);
            if (sale == null) return Results.NotFound(new { message = "Receipt not found." });
            var returned = await db.ReturnedItems.AsNoTracking().Where(i => i.SaleId == sale.Id).Select(i => i.LineIndex).ToListAsync(ct);
            using var snapshot = JsonDocument.Parse(sale.Snapshot); var amounts = RefundAllocation.For(sale);
            var paymentShares = RefundPayments.For(sale);
            var lines = snapshot.RootElement.GetProperty("lines").EnumerateArray().Select((l,i) => new { index = i, details = l.Clone(), refund = amounts[i], originalPayments = paymentShares[i], alreadyReturned = returned.Contains(i) }).ToList();
            return Results.Ok(new { saleId = sale.Id, receiptNumber = $"POS-{sale.Number:D8}", sale.CompletedAt, sale.Total, returnDeadline = sale.CompletedAt.AddDays(15), canReturn = WithinReturnWindow(sale.CompletedAt, DateTimeOffset.UtcNow), lines });
        }).RequireAuthorization(p => p.RequireRole("Admin","Operator"));
        app.MapPost("/api/returns", async (Input input, PharmacyDbContext db, HttpContext http, IAntiforgery csrf, CancellationToken ct) => {
            try {
                await csrf.ValidateRequestAsync(http);
                var reason = input.Reason?.Trim() ?? ""; var indexes = input.LineIndexes?.Order().ToArray() ?? [];
                if (input.RequestId == Guid.Empty || input.SaleId == Guid.Empty || !input.ItemsReceived || !input.CashRefunded || reason.Length is < 1 or > 1000
                    || input.PaymentReferences?.Any(p => p == null) == true || input.RefundDestination is not ("Cash" or "Original") || indexes.Length is < 1 or > 100 || indexes.Any(i => i < 0) || indexes.Distinct().Count() != indexes.Length)
                    return Results.BadRequest(new { message = "Select complete receipt items, enter the reason, and confirm the goods and completed refund." });
                var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425912)",ct);
                var existing = await db.SaleReturns.Include(r => r.Sale).Include(r => r.Items).Include(r => r.Payments).SingleOrDefaultAsync(r => r.Id == input.RequestId, ct);
                if (existing != null) return existing.ActorId == actor && existing.SaleId == input.SaleId && existing.Reason == reason && existing.RefundDestination == input.RefundDestination
                    && existing.Payments.Where(p => p.Method != "Cash").Select(p => (p.Method, p.Reference)).OrderBy(p => p.Method)
                        .SequenceEqual((input.PaymentReferences ?? []).Select(p => (p.Method, Reference: PaymentMethods.Reference(p.Reference))).OrderBy(p => p.Method)) && existing.Items.Select(i => i.LineIndex).Order().SequenceEqual(indexes)
                    ? Results.Ok(Result(existing)) : Results.Conflict(new { message = "This refund reference was already used. Check the return history before starting another refund." });
                var sale = await db.Sales.Include(s => s.Payments).SingleOrDefaultAsync(s => s.Id == input.SaleId, ct);
                if (sale == null) return Results.NotFound();
                if (!WithinReturnWindow(sale.CompletedAt, DateTimeOffset.UtcNow)) return Results.Conflict(new { message = $"Receipt POS-{sale.Number:D8}: the 15-day return/refund period has ended. No refund was recorded." });
                if (!input.ReceiptPresented) return Results.BadRequest(new { message = "The original receipt must be presented before a return or refund." });
                using var json = JsonDocument.Parse(sale.Snapshot); var lines = json.RootElement.GetProperty("lines").EnumerateArray().ToArray();
                if (indexes.Any(i => i >= lines.Length)) return Results.BadRequest(new { message = "Select valid complete receipt items." });
                if (await db.ReturnedItems.AnyAsync(i => i.SaleId == sale.Id && indexes.Contains(i.LineIndex),ct)) return Results.Conflict(new { message = "An item was already returned. Reload the receipt before issuing another refund." });
                var allocations = RefundAllocation.For(sale);
                var refundPayments = RefundPayments.Validate(RefundPayments.Selected(sale, indexes, input.RefundDestination), input.PaymentReferences);
                var r = new SaleReturn { Id = input.RequestId, SaleId = sale.Id, Sale = sale, RefundDestination = input.RefundDestination, Payments = refundPayments, Method = refundPayments.Count > 1 ? "Split" : refundPayments.FirstOrDefault()?.Method ?? "Cash", ReceiptPresented = true, Reason = reason, ActorId = actor, ActorName = http.User.Identity?.Name ?? actor, At = DateTimeOffset.UtcNow, Amount = indexes.Sum(i => allocations[i]) };
                foreach (var index in indexes) {
                    var l = lines[index];
                    r.Items.Add(new ReturnedItem { SaleId = sale.Id, LineIndex = index, ReceiptId = l.GetProperty("lotId").GetGuid(),
                        BrandName = l.GetProperty("brandName").GetString()!, BatchNumber = l.GetProperty("batchNumber").GetString()!, BaseUnit = l.GetProperty("baseUnit").GetString()!,
                        Packs = l.GetProperty("quantity").GetInt32(), Unit = l.GetProperty("unit").GetString()!, Quantity = l.GetProperty("baseUnits").GetInt64(), Refund = allocations[index] });
                }
                db.SaleReturns.Add(r); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return Results.Ok(Result(r));
            } catch (SalesCounter.PricingFailure e) { return Results.Json(new { message = e.Message }, statusCode: e.Status); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(new { message = "Refresh your sign-in before recording a refund." }); }
            catch (Exception e) when (e is NpgsqlException or DbUpdateException or OperationCanceledException) { return Results.Json(new { message = "Refund result not confirmed. Retry the same reference; do not issue the refund again." },statusCode:503); }
        }).RequireAuthorization(p => p.RequireRole("Admin","Operator"));
        app.MapGet("/api/returns", async (int? page, PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store"; var n = page ?? 1; if (n is < 1 or > 10000) return Results.BadRequest();
            var rows = await db.SaleReturns.AsNoTracking().Include(r => r.Sale).Include(r => r.Items).Include(r => r.Payments).OrderByDescending(r => r.At).ThenBy(r => r.Id).Skip((n-1)*25).Take(26).ToListAsync(ct);
            return Results.Ok(new { items = rows.Take(25).Select(Result), hasMore = rows.Count > 25 });
        }).RequireAuthorization(p => p.RequireRole("Admin","Operator"));
        app.MapGet("/api/returns/stock", async (string? status, int? page, PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store"; var n = page ?? 1;
            if (n is < 1 or > 10000 || status is not (null or "Held" or "Restocked" or "Disposed")) return Results.BadRequest();
            var state = status ?? "Held"; var now = DateTimeOffset.UtcNow;
            var rows = await db.ReturnedItems.AsNoTracking().Where(i => i.Status == state && (state != "Disposed" || i.VisibleUntil > now))
                .OrderByDescending(i => i.Return.At).ThenBy(i => i.Id).Skip((n-1)*25).Take(26)
                .Select(i => new { i.Id, i.BrandName, i.BatchNumber, i.Quantity, i.BaseUnit, i.Status, i.ReviewerName, i.ReviewedAt, i.ReviewReason,
                    returnedBy = i.Return.ActorName, returnedAt = i.Return.At, reason = i.Return.Reason,
                    expiryDate = db.ReceivingMovements.Where(m => m.ReceiptId == i.ReceiptId).Select(m => m.Batch.ExpiryDate).FirstOrDefault() }).ToListAsync(ct);
            return Results.Ok(new { items = rows.Take(25), hasMore = rows.Count > 25 });
        }).RequireAuthorization(p => p.RequireRole("Admin","Operator"));
        app.MapPost("/api/returns/stock/{id:guid}/review", async (Guid id, Review input, PharmacyDbContext db, HttpContext http, IAntiforgery csrf, CancellationToken ct) => {
            try {
                await csrf.ValidateRequestAsync(http); var reason = input.Reason?.Trim() ?? "";
                if (!input.Checked || reason.Length is < 1 or > 1000 || input.Action is not ("Restocked" or "Disposed")) return Results.BadRequest(new { message = "Confirm your inspection or physical disposal, and enter a reason." });
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425911)",ct);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425912)",ct);
                var item = await db.ReturnedItems.SingleOrDefaultAsync(i => i.Id == id,ct); if (item == null) return Results.NotFound();
                var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                if (item.Status != "Held") return item.Status == input.Action && item.ReviewReason == reason && item.ReviewedBy == actor ? Results.Ok(Item(item)) : Results.Conflict(new { message = "This returned item was already reviewed." });
                if (input.Action == "Restocked") {
                    var lot = await db.ReceivingMovements.Include(m => m.Batch).Include(m => m.Revision).ThenInclude(r => r.Medicine).SingleOrDefaultAsync(m => m.ReceiptId == item.ReceiptId,ct);
                    if (lot == null || lot.Batch.ExpiryDate < StockReceiving.ShopToday() || lot.Revision.Status != ReceivingStatus.Approved || lot.Revision.MrpVerifiedAt == null || !lot.Revision.Medicine.IsActive || lot.Revision.Medicine.ReviewStatus != CatalogueReviewStatus.Approved || await db.StockDisposals.AnyAsync(d => d.ReceiptId == item.ReceiptId,ct))
                        return Results.Conflict(new { message = "This lot is expired, disposed, inactive or not price-verified. It cannot be returned to sellable stock." });
                }
                item.Status = input.Action; item.ReviewedBy = actor; item.ReviewerName = http.User.Identity?.Name ?? actor; item.ReviewedAt = DateTimeOffset.UtcNow; item.ReviewReason = reason;
                if (item.Status == "Disposed") item.VisibleUntil = item.ReviewedAt.Value.AddMonths(3);
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Ok(Item(item));
            } catch (AntiforgeryValidationException) { return Results.BadRequest(new { message = "Refresh your sign-in before reviewing stock." }); }
            catch (Exception e) when (e is NpgsqlException or DbUpdateException or OperationCanceledException) { return Results.Json(new { message = "Review result is not confirmed. Retry the same action." },statusCode:503); }
        }).RequireAuthorization("AdminOnly");
        app.MapGet("/api/returns/disposals/export", async (PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            var now = DateTimeOffset.UtcNow; http.Response.Headers.CacheControl = "no-store";
            var rows = await db.ReturnedItems.AsNoTracking().Where(i => i.Status == "Disposed" && i.VisibleUntil > now).OrderByDescending(i => i.ReviewedAt).Take(10001).ToListAsync(ct);
            if (rows.Count > 10000) return Results.BadRequest(new { message = "Too many records for one export." });
            var csv = new StringBuilder("Item ID,Medicine,Batch,Quantity,Unit,Disposed by,Disposed at UTC,Reason\r\n");
            foreach (var i in rows) csv.Append(string.Join(",",new object?[] { i.Id,i.BrandName,i.BatchNumber,i.Quantity,i.BaseUnit,i.ReviewerName,i.ReviewedAt,i.ReviewReason }.Select(StockViews.CsvCell))).Append("\r\n");
            db.StockExportEvents.Add(new() { ActorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!, At = now, RowCount = rows.Count }); await db.SaveChangesAsync(ct);
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),"text/csv; charset=utf-8","disposed-returns.csv");
        }).RequireAuthorization("AdminOnly");
    }
}
