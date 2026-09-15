using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Catalogue;
using PharmacyPos.Api.Data;

namespace PharmacyPos.Api.Stock;

public static class StockReceiving
{
    public sealed record ReceiveInput(Guid RequestId, Guid MedicineId, string? Supplier, string? DeliveryReference,
        DateOnly ReceivedDate, string? BatchNumber, DateOnly? ManufacturingDate, DateOnly ExpiryDate,
        int UnitsPerStrip, int StripsPerBox, int UnitsPerBox, int BoxesPerCarton,
        int Pieces, int Strips, int Boxes, int Cartons, decimal MrpAmount, string MrpUnit,
        bool VerifyMrp = false, Guid? ReceiptId = null, Guid? ReplacesRevisionId = null, string? CorrectionReason = null);
    public sealed record ReviewInput(Guid RevisionId, bool Approve, bool VerifyMrp = false, string? Note = null);
    public sealed record VerifyInput(Guid RevisionId);
    private sealed class InvalidEntry(string message) : Exception(message);
    private sealed class StaleEntry(string message) : Exception(message);

    public static void MapStockReceiving(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        app.MapStockViews();
        app.MapMrpCorrection();
        app.MapPost("/api/stock/receipts", Receive).RequireAuthorization("Staff");
        app.MapPost("/api/stock/receipts/{id:guid}/review", Review).RequireAuthorization("AdminOnly");
        app.MapPost("/api/stock/receipts/{id:guid}/verify-mrp", Verify).RequireAuthorization("AdminOnly");
    }

    public static DateOnly ShopToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Dhaka").DateTime);
    private static string Clean(string? value) => Regex.Replace(value?.Trim() ?? "", @"\s+", " ");
    private static string Actor(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static object Result(StockReceiptRevision r) => new { receiptId = r.ReceiptId, revisionId = r.Id, r.Revision,
        status = r.Status.ToString(), r.TotalUnits, mrpVerified = r.MrpVerifiedAt != null };
    private static IResult Failure(Exception e) => e switch {
        InvalidEntry => Results.BadRequest(new { message = e.Message }),
        StaleEntry => Results.Conflict(new { message = e.Message }),
        AntiforgeryValidationException => Results.BadRequest(new { message = "Refresh and sign in again before saving." }),
        _ => Results.Json(new { message = "Save was not confirmed. Keep the entry unchanged and retry, or refresh to check its status." }, statusCode: 503)
    };
    private static bool Expected(Exception e) => e is InvalidEntry or StaleEntry or AntiforgeryValidationException or NpgsqlException or DbUpdateException or OperationCanceledException;

    private static async Task<IResult> Receive(ReceiveInput input, HttpContext http, IAntiforgery csrf, PharmacyDbContext db, CancellationToken ct)
    {
        try
        {
            await csrf.ValidateRequestAsync(http);
            var isAdmin = http.User.IsInRole("Admin");
            if (input.VerifyMrp && !isAdmin) return Results.Forbid();
            var actor = Actor(http);
            var data = input with { Supplier = Clean(input.Supplier), DeliveryReference = Clean(input.DeliveryReference),
                BatchNumber = Clean(input.BatchNumber), CorrectionReason = Clean(input.CorrectionReason) };
            Validate(data);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data))));
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Consistent order with catalogue creation prevents competing batch inserts.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425911)", ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425912)", ct);
            var retry = await db.StockReceiptRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == data.RequestId, ct);
            if (retry is not null) return retry.RequestHash == hash && retry.CreatedByUserId == actor
                ? Results.Ok(Result(retry)) : Results.Conflict(new { message = "This save identifier was already used for different details." });
            var medicine = await db.Medicines.SingleOrDefaultAsync(x => x.Id == data.MedicineId && x.IsActive && x.ReviewStatus == CatalogueReviewStatus.Approved, ct)
                ?? throw new InvalidEntry("Choose an approved, active medicine.");
            if (data.UnitsPerStrip > 0 && medicine.BaseUnit is not (StockUnit.Tablet or StockUnit.Capsule))
                throw new InvalidEntry("Strip packaging is supported for tablets and capsules. Use individual stock units and boxes for this medicine.");
            var root = new StockReceipt { Id = data.RequestId, CurrentRevision = 1 };
            if (data.ReceiptId is Guid rootId)
            {
                root = await db.StockReceipts.SingleOrDefaultAsync(x => x.Id == rootId, ct) ?? throw new StaleEntry("Receiving entry no longer exists.");
                var old = await db.StockReceiptRevisions.SingleAsync(x => x.ReceiptId == rootId && x.Revision == root.CurrentRevision, ct);
                if (!isAdmin && old.CreatedByUserId != actor) return Results.Forbid();
                if (old.Id != data.ReplacesRevisionId || old.Status is not (ReceivingStatus.PendingApproval or ReceivingStatus.Returned))
                    throw new StaleEntry("This entry changed or was already approved. Refresh before correcting it.");
                old.Status = ReceivingStatus.Superseded;
                AddEvent(db, old, "Superseded", actor, data.CorrectionReason);
                root.CurrentRevision++;
            }
            else db.StockReceipts.Add(root);
            var quantity = Total(data);
            var revision = new StockReceiptRevision {
                Id = data.RequestId, Receipt = root, ReceiptId = root.Id, Revision = root.CurrentRevision, RequestHash = hash,
                Medicine = medicine, MedicineId = medicine.Id, Supplier = data.Supplier!, DeliveryReference = data.DeliveryReference,
                ReceivedDate = data.ReceivedDate, BatchNumber = data.BatchNumber!, ManufacturingDate = data.ManufacturingDate,
                ExpiryDate = data.ExpiryDate, BaseUnit = medicine.BaseUnit, UnitsPerStrip = data.UnitsPerStrip,
                StripsPerBox = data.StripsPerBox, UnitsPerBox = data.UnitsPerBox, BoxesPerCarton = data.BoxesPerCarton,
                Pieces = data.Pieces, Strips = data.Strips, Boxes = data.Boxes, Cartons = data.Cartons, TotalUnits = quantity,
                MrpAmount = data.MrpAmount, MrpUnit = data.MrpUnit, MrpUnits = data.MrpUnit == "Piece" ? 1 : data.MrpUnit == "Strip" ? data.UnitsPerStrip : data.UnitsPerBox,
                CreatedByUserId = actor, CreatedAt = DateTimeOffset.UtcNow, CorrectionReason = data.CorrectionReason,
                Status = ReceivingStatus.PendingApproval
            };
            db.StockReceiptRevisions.Add(revision);
            AddEvent(db, revision, "Submitted", actor, data.CorrectionReason);
            if (isAdmin) await Approve(db, revision, actor, true, data.VerifyMrp, null, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/stock/receipts/{root.Id}", Result(revision));
        }
        catch (Exception e) when (Expected(e)) { return Failure(e); }
    }

    private static long Total(ReceiveInput d) => checked((long)d.Pieces + (long)d.Strips * d.UnitsPerStrip + ((long)d.Boxes + (long)d.Cartons * d.BoxesPerCarton) * d.UnitsPerBox);
    private static void Validate(ReceiveInput d)
    {
        if (d.RequestId == Guid.Empty || d.MedicineId == Guid.Empty || d.Supplier!.Length is < 1 or > 200
            || d.BatchNumber!.Length is < 1 or > 100 || d.DeliveryReference!.Length > 100)
            throw new InvalidEntry("Enter a medicine, supplier (up to 200 characters) and batch number (up to 100 characters).");
        var today = ShopToday();
        if (d.ReceivedDate == default || d.ReceivedDate > today || d.ExpiryDate == default
            || d.ManufacturingDate > d.ExpiryDate || d.ManufacturingDate > d.ReceivedDate)
            throw new InvalidEntry("Check the dates. Receipt cannot be in the future; manufacturing must not be after receipt or expiry.");
        if (d.UnitsPerStrip is < 0 or > 1000000 || d.StripsPerBox is < 0 or > 1000000
            || d.UnitsPerBox is < 1 or > 1000000 || d.BoxesPerCarton is < 1 or > 1000000
            || (d.UnitsPerStrip == 0 ? d.StripsPerBox != 0 || d.Strips != 0 : d.StripsPerBox == 0 || (long)d.UnitsPerStrip * d.StripsPerBox != d.UnitsPerBox))
            throw new InvalidEntry("Check the packaging conversions. Strip size × strips per box must equal units per box.");
        if (new[] { d.Pieces, d.Strips, d.Boxes, d.Cartons }.Any(x => x is < 0 or > 1000000) || Total(d) is < 1 or > 1000000000)
            throw new InvalidEntry("Enter whole-number quantities totaling between 1 and 1,000,000,000 stock units.");
        if (d.MrpAmount <= 0 || d.MrpAmount > 1000000000m || decimal.Round(d.MrpAmount, 2) != d.MrpAmount
            || d.MrpUnit is not ("Piece" or "Strip" or "Box") || (d.MrpUnit == "Strip" && d.UnitsPerStrip == 0))
            throw new InvalidEntry("Enter the printed MRP, up to 2 decimal places, and its unit.");
        if ((d.ReceiptId.HasValue != d.ReplacesRevisionId.HasValue) || (d.ReceiptId.HasValue && d.CorrectionReason!.Length is < 1 or > 1000))
            throw new InvalidEntry("A correction needs the original revision and a reason (up to 1,000 characters).");
    }

    private static async Task<IResult> Review(Guid id, ReviewInput input, HttpContext http, IAntiforgery csrf, PharmacyDbContext db, CancellationToken ct)
    {
        try {
            await csrf.ValidateRequestAsync(http);
            var note = Clean(input.Note);
            if (note.Length > 1000 || (!input.Approve && note.Length == 0)) throw new InvalidEntry("A return needs a reason, up to 1,000 characters.");
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425911)", ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425912)", ct);
            var r = await Current(db, id, input.RevisionId, ct);
            var actor = Actor(http);
            // Safe retry of an already completed decision; a different decision is a conflict.
            if (r.Status == ReceivingStatus.Approved && input.Approve) return Results.Ok(Result(r));
            if (r.Status == ReceivingStatus.Returned && !input.Approve && r.ReviewNote == note) return Results.Ok(Result(r));
            if (r.Status != ReceivingStatus.PendingApproval) throw new StaleEntry("This revision is no longer pending. Refresh the list.");
            if (input.Approve) await Approve(db, r, actor, false, input.VerifyMrp, note, ct);
            else {
                r.Status = ReceivingStatus.Returned; r.ReviewedByUserId = actor; r.ReviewedAt = DateTimeOffset.UtcNow; r.ReviewNote = note;
                AddEvent(db, r, "Returned", actor, note);
            }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Results.Ok(Result(r));
        } catch (Exception e) when (Expected(e)) { return Failure(e); }
    }

    private static async Task<StockReceiptRevision> Current(PharmacyDbContext db, Guid id, Guid revisionId, CancellationToken ct) =>
        await db.StockReceiptRevisions.Include(x => x.Medicine).SingleOrDefaultAsync(x => x.ReceiptId == id && x.Id == revisionId && x.Revision == x.Receipt.CurrentRevision, ct)
            ?? throw new StaleEntry("The entry was corrected or no longer exists. Refresh to review the latest revision.");

    private static async Task Approve(PharmacyDbContext db, StockReceiptRevision r, string actor, bool automatic, bool verify, string? note, CancellationToken ct)
    {
        if (!r.Medicine.IsActive || r.Medicine.ReviewStatus != CatalogueReviewStatus.Approved) throw new InvalidEntry("The medicine must be active and approved before stock can be approved.");
        var upper = r.BatchNumber.ToUpperInvariant();
        var batch = await db.MedicineBatches.SingleOrDefaultAsync(x => x.MedicineId == r.MedicineId && x.BatchNumber.ToUpper() == upper, ct);
        if (batch is not null && (batch.ExpiryDate != r.ExpiryDate || (batch.ManufacturingDate.HasValue && r.ManufacturingDate.HasValue && batch.ManufacturingDate != r.ManufacturingDate)))
            throw new InvalidEntry("Dates do not match the saved batch. Return or correct this entry before approval.");
        batch ??= new MedicineBatch { MedicineId = r.MedicineId, BatchNumber = r.BatchNumber, ManufacturingDate = r.ManufacturingDate, ExpiryDate = r.ExpiryDate };
        if (db.Entry(batch).State == EntityState.Detached) db.MedicineBatches.Add(batch);
        r.Status = ReceivingStatus.Approved; r.ReviewedByUserId = actor; r.ReviewedAt = DateTimeOffset.UtcNow;
        r.AutomaticApproval = automatic; r.ReviewNote = note;
        db.ReceivingMovements.Add(new ReceivingMovement { ReceiptId = r.ReceiptId, RevisionId = r.Id, Revision = r, Batch = batch,
            Quantity = r.TotalUnits, PostedByUserId = actor, PostedAt = DateTimeOffset.UtcNow });
        AddEvent(db, r, automatic ? "AutomaticallyApproved" : "Approved", actor, note);
        if (verify) VerifyPrice(db, r, actor);
    }
    private static void VerifyPrice(PharmacyDbContext db, StockReceiptRevision r, string actor)
    {
        if (r.MrpVerifiedAt != null) return;
        r.MrpVerifiedByUserId = actor; r.MrpVerifiedAt = DateTimeOffset.UtcNow;
        AddEvent(db, r, "MrpVerified", actor, null);
    }
    private static async Task<IResult> Verify(Guid id, VerifyInput input, HttpContext http, IAntiforgery csrf, PharmacyDbContext db, CancellationToken ct)
    {
        try {
            await csrf.ValidateRequestAsync(http);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425912)", ct);
            var r = await Current(db, id, input.RevisionId, ct);
            if (r.Status != ReceivingStatus.Approved) throw new StaleEntry("Approve the stock entry before verifying its MRP here.");
            VerifyPrice(db, r, Actor(http));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Results.Ok(Result(r));
        } catch (Exception e) when (Expected(e)) { return Failure(e); }
    }
    private static void AddEvent(PharmacyDbContext db, StockReceiptRevision r, string action, string actor, string? note) =>
        db.StockReviewEvents.Add(new StockReviewEvent { Revision = r, RevisionId = r.Id, Action = action, ActorId = actor, At = DateTimeOffset.UtcNow, Note = note });
}
