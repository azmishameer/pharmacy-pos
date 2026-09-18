using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using PharmacyPos.Api.Data;

namespace PharmacyPos.Api.Catalogue;

public sealed class MedicineBarcode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public Guid MedicineId { get; set; }
    public Medicine Medicine { get; set; } = null!;
    public string Unit { get; set; } = "Piece";
    public int Units { get; set; } = 1;
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DisabledAt { get; set; }
    public string? DisabledBy { get; set; }
}
public static class Barcodes
{
    public sealed record Input(Guid MedicineId, string? Code, string Unit, int Units);
    public static bool Valid(string code) => code.Length is >= 1 and <= 100 && code.All(c => c is >= '!' and <= '~');
    public static void MapBarcodes(this WebApplication app)
    {
        app.MapGet("/api/barcodes/lookup", async (string code, PharmacyDbContext db, HttpContext http) => {
            http.Response.Headers.CacheControl = "no-store";
            code = code.Trim();
            if (!Valid(code)) return Results.BadRequest(new { message = "Scan a plain product barcode of 1–100 characters." });
            var row = await db.Set<MedicineBarcode>().AsNoTracking().Where(b => b.Code == code && b.DisabledAt == null && b.Medicine.IsActive && b.Medicine.ReviewStatus == CatalogueReviewStatus.Approved)
                .Select(b => new { b.Id, b.Code, b.MedicineId, b.Unit, b.Units, b.Medicine.BrandName }).SingleOrDefaultAsync();
            return row is null ? Results.NotFound(new { message = "Barcode not registered or inactive. Ask an admin to register the exact product and pack size." }) : Results.Ok(row);
        }).RequireAuthorization("Staff");
        app.MapGet("/api/barcodes", async (Guid medicineId, PharmacyDbContext db) => Results.Ok(await db.Set<MedicineBarcode>().AsNoTracking().Where(b => b.MedicineId == medicineId).OrderBy(b => b.Code).ToListAsync())).RequireAuthorization("AdminOnly");
        app.MapPost("/api/barcodes", async (Input input, PharmacyDbContext db, HttpContext http, IAntiforgery csrf) => {
            try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            var code = input.Code?.Trim() ?? "";
            if (!Valid(code) || input.Unit is not ("Piece" or "Strip" or "Box") || input.Units is < 1 or > 1000000 || input.Unit == "Piece" && input.Units != 1)
                return Results.BadRequest(new { message = "Enter a barcode, a selling unit and its number of individual stock units. A piece must contain one unit." });
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(915)");
            var old = await db.Set<MedicineBarcode>().SingleOrDefaultAsync(b => b.Code == code);
            if (old != null) return old.MedicineId == input.MedicineId && old.Unit == input.Unit && old.Units == input.Units && old.DisabledAt == null ? Results.Ok(old) : Results.Conflict(new { message = "This barcode is already registered or retired. It cannot be assigned to a different product or pack." });
            if (!await db.Medicines.AnyAsync(m => m.Id == input.MedicineId && m.IsActive && m.ReviewStatus == CatalogueReviewStatus.Approved)) return Results.BadRequest(new { message = "Choose an approved, active medicine." });
            var row = new MedicineBarcode { MedicineId = input.MedicineId, Code = code, Unit = input.Unit, Units = input.Units, CreatedBy = http.User.FindFirstValue(ClaimTypes.NameIdentifier)! };
            db.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(row);
        }).RequireAuthorization("AdminOnly");
        app.MapPost("/api/barcodes/{id:guid}/disable", async (Guid id, PharmacyDbContext db, HttpContext http, IAntiforgery csrf) => {
            try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            await db.Set<MedicineBarcode>().Where(b => b.Id == id && b.DisabledAt == null).ExecuteUpdateAsync(s => s.SetProperty(b => b.DisabledAt, DateTimeOffset.UtcNow).SetProperty(b => b.DisabledBy, http.User.FindFirstValue(ClaimTypes.NameIdentifier)));
            return Results.Ok(new { disabled = true });
        }).RequireAuthorization("AdminOnly");
    }
}
