using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Data;

namespace PharmacyPos.Api.Stock;

// Correct a data-entry mistake on an approved lot. Official effective-dated price changes
// and any future sale receipt repricing require separate workflows.
public static class StockMrpCorrection
{
    public sealed record Input(Guid RequestId, Guid RevisionId, decimal ExpectedAmount, string ExpectedUnit,
        int ExpectedUnits, decimal Amount, string Unit, string? Reason, bool CheckedPackaging);
    public sealed record Change(decimal OldAmount, string OldUnit, int OldUnits, decimal NewAmount, string NewUnit, int NewUnits, string Reason);
    public static void MapMrpCorrection(this WebApplication app)
    {
        app.MapPost("/api/stock/receipts/{id:guid}/correct-mrp", async (Guid id, Input input, HttpContext http,
            IAntiforgery csrf, PharmacyDbContext db, CancellationToken ct) => {
            try {
                await csrf.ValidateRequestAsync(http);
                var reason = input.Reason?.Trim() ?? "";
                if (input.RequestId == Guid.Empty || !input.CheckedPackaging || reason.Length is < 1 or > 100
                    || input.Amount <= 0 || input.Amount > 1000000000m || decimal.Round(input.Amount, 2) != input.Amount
                    || input.Unit is not ("Piece" or "Strip" or "Box"))
                    return Results.BadRequest(new { message = "Check the packaging, select the MRP unit, enter a positive amount (up to 2 decimals), and a reason up to 100 characters." });
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425912)", ct);
                var r = await db.StockReceiptRevisions.SingleOrDefaultAsync(x => x.ReceiptId == id && x.Id == input.RevisionId && x.Revision == x.Receipt.CurrentRevision, ct);
                if (r == null) return Results.NotFound();
                var units = input.Unit == "Piece" ? 1 : input.Unit == "Strip" ? r.UnitsPerStrip : r.UnitsPerBox;
                if (units <= 0) return Results.BadRequest(new { message = "That MRP unit is not available for this packaging." });
                var change = new Change(input.ExpectedAmount, input.ExpectedUnit, input.ExpectedUnits, input.Amount, input.Unit, units, reason);
                var note = JsonSerializer.Serialize(change);
                if (note.Length > 1000) return Results.BadRequest(new { message = "Shorten the correction reason." });
                var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var retry = await db.StockReviewEvents.SingleOrDefaultAsync(x => x.Id == input.RequestId, ct);
                if (retry != null) return retry.Action == "MrpCorrected" && retry.RevisionId == r.Id && retry.ActorId == actor && retry.Note == note
                    ? Results.Ok(new { correctionId = retry.Id }) : Results.Conflict(new { message = "This save identifier was already used for different details." });
                if (r.Status != ReceivingStatus.Approved || await db.StockDisposals.AnyAsync(x => x.ReceiptId == id, ct))
                    return Results.Conflict(new { message = "Only approved stock that has not been disposed can have its MRP corrected here." });
                if (r.MrpAmount != input.ExpectedAmount || r.MrpUnit != input.ExpectedUnit || r.MrpUnits != input.ExpectedUnits)
                    return Results.Conflict(new { message = "The MRP changed since you opened this form. Refresh and check it again." });
                if (r.MrpAmount == input.Amount && r.MrpUnit == input.Unit)
                    return Results.BadRequest(new { message = "The amount and unit are already the same. No correction is needed." });
                var at = DateTimeOffset.UtcNow;
                // Quantity, packaging, receiving movement and original approval remain unchanged.
                r.MrpAmount = input.Amount; r.MrpUnit = input.Unit; r.MrpUnits = units;
                r.MrpVerifiedByUserId = actor; r.MrpVerifiedAt = at;
                db.StockReviewEvents.Add(new StockReviewEvent { Id = input.RequestId, RevisionId = r.Id,
                    Action = "MrpCorrected", ActorId = actor, At = at, Note = note });
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return Results.Ok(new { correctionId = input.RequestId });
            }
            catch (AntiforgeryValidationException) { return Results.BadRequest(new { message = "Refresh and sign in again before correcting MRP." }); }
            catch (Exception e) when (e is NpgsqlException or DbUpdateException or OperationCanceledException) {
                return Results.Json(new { message = "Correction was not confirmed. Keep the details unchanged and retry, or refresh to check the price history." }, statusCode: 503);
            }
        }).RequireAuthorization("AdminOnly");
        app.MapGet("/api/stock/receipts/{id:guid}/mrp-history", async (Guid id, HttpContext http, PharmacyDbContext db, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            var events = await db.StockReviewEvents.AsNoTracking().Where(x => x.Revision.ReceiptId == id && x.Action == "MrpCorrected")
                .OrderByDescending(x => x.At).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.At, x.Note, admin = db.Users.Where(u => u.Id == x.ActorId).Select(u => u.UserName).FirstOrDefault() }).ToListAsync(ct);
            return Results.Ok(events.Select(x => new { x.Id, x.At, x.admin, change = JsonSerializer.Deserialize<Change>(x.Note!) }));
        }).RequireAuthorization("AdminOnly");
    }
}
