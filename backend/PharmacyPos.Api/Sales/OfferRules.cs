using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Catalogue;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Stock;

namespace PharmacyPos.Api.Sales;

public sealed class OfferRule
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public Guid? MedicineId { get; set; }
    public Medicine? Medicine { get; set; }
    public string Kind { get; set; } = "Percentage";
    public decimal Value { get; set; }
    public string Unit { get; set; } = "Piece";
    public int Units { get; set; } = 1;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal MinimumSubtotal { get; set; }
    public string RequestHash { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? StoppedBy { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
}
public static class OfferRules
{
    public sealed record Input(Guid RequestId, string? Name, Guid? MedicineId, string Kind, decimal Value,
        string Unit, int Units, DateOnly StartDate, DateOnly? EndDate, decimal MinimumSubtotal = 0);
    public static void MapOffers(this ModelBuilder model)
    {
        var r = model.Entity<OfferRule>();
        r.ToTable("offer_rules", t => {
            t.HasCheckConstraint("ck_offer_value", "\"Value\" > 0 AND \"Value\" <= 1000000 AND (\"Kind\" <> 'Percentage' OR \"Value\" <= 100)");
            t.HasCheckConstraint("ck_offer_kind", "\"Kind\" IN ('Percentage', 'Fixed')");
            t.HasCheckConstraint("ck_offer_unit", "\"Unit\" IN ('Piece', 'Strip', 'Box') AND \"Units\" BETWEEN 1 AND 1000000 AND (\"Unit\" <> 'Piece' OR \"Units\" = 1)");
            t.HasCheckConstraint("ck_offer_dates", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
            t.HasCheckConstraint("ck_offer_minimum", "\"MinimumSubtotal\" BETWEEN 0 AND 1000000000 AND (\"MedicineId\" IS NULL OR \"MinimumSubtotal\" = 0)");
            t.HasCheckConstraint("ck_offer_pack_scope", "(\"MedicineId\" IS NOT NULL AND \"Kind\" = 'Fixed') OR (\"Unit\" = 'Piece' AND \"Units\" = 1)");
        });
        r.HasKey(x => x.Id);
        r.Property(x => x.Name).HasMaxLength(100); r.Property(x => x.NameKey).HasMaxLength(100);
        r.Property(x => x.Kind).HasMaxLength(20); r.Property(x => x.Unit).HasMaxLength(20);
        r.Property(x => x.Value).HasPrecision(12, 2); r.Property(x => x.MinimumSubtotal).HasPrecision(12, 2);
        r.Property(x => x.RequestHash).HasMaxLength(64);
        r.HasIndex(x => x.NameKey).IsUnique().HasFilter("\"StoppedAt\" IS NULL");
        r.HasOne(x => x.Medicine).WithMany().HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.StoppedBy).OnDelete(DeleteBehavior.Restrict);
    }
    public static void MapOfferRules(this WebApplication app)
    {

        app.MapGet("/api/offers", async (PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            var today = StockReceiving.ShopToday();
            return Results.Ok(await db.OfferRules.AsNoTracking().OrderByDescending(x => x.CreatedAt).Select(x => new {
                x.Id, x.Name, x.MedicineId, medicineName = x.Medicine == null ? null : x.Medicine.BrandName,
                x.Kind, x.Value, x.Unit, x.Units, x.StartDate, x.EndDate, x.MinimumSubtotal, x.StoppedAt,
                status = x.StoppedAt != null ? "Stopped" : x.StartDate > today ? "Scheduled" : x.EndDate < today ? "Expired" : "Active"
            }).ToListAsync(ct));
        }).RequireAuthorization("AdminOnly");
        app.MapPost("/api/offers", async (Input input, PharmacyDbContext db, HttpContext http, IAntiforgery csrf, CancellationToken ct) => {
            try {
                await csrf.ValidateRequestAsync(http);
                var name = string.Join(" ", (input.Name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (input.RequestId == Guid.Empty || name.Length is < 1 or > 100 || input.Kind is not ("Percentage" or "Fixed")
                    || input.Value <= 0 || input.Value > 1000000 || decimal.Round(input.Value, 2) != input.Value || (input.Kind == "Percentage" && input.Value > 100)
                    || input.Unit is not ("Piece" or "Strip" or "Box") || input.Units is < 1 or > 1000000 || (input.Unit == "Piece" && input.Units != 1)
                    || ((input.MedicineId == null || input.Kind == "Percentage") && (input.Unit != "Piece" || input.Units != 1))
                    || input.StartDate == default || input.EndDate < input.StartDate || input.MinimumSubtotal < 0 || input.MinimumSubtotal > 1000000000
                    || decimal.Round(input.MinimumSubtotal, 2) != input.MinimumSubtotal || (input.MedicineId != null && input.MinimumSubtotal != 0))
                    return Results.BadRequest(new { message = "Check the offer name, positive two-decimal discount, dates, pack size and minimum bill. Percentages cannot exceed 100." });
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input with { Name = name }))));
                var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425914)", ct);
                var existing = await db.OfferRules.SingleOrDefaultAsync(x => x.Id == input.RequestId, ct);
                if (existing != null) return existing.RequestHash == hash && existing.CreatedBy == actor ? Results.Ok(new { existing.Id })
                    : Results.Conflict(new { message = "This save request already belongs to a different offer. Refresh before creating another." });
                var key = name.ToUpperInvariant();
                if (await db.OfferRules.AnyAsync(x => x.NameKey == key && x.StoppedAt == null, ct))
                    return Results.Conflict(new { message = "An unstopped offer already uses this name. Stop it or choose another name." });
                if (input.MedicineId is Guid id) {
                    var medicine = await db.Medicines.SingleOrDefaultAsync(x => x.Id == id && x.IsActive && x.ReviewStatus == CatalogueReviewStatus.Approved, ct);
                    if (medicine == null) return Results.BadRequest(new { message = "Select an approved, active medicine." });
                    if (input.Unit == "Strip" && medicine.BaseUnit is not (StockUnit.Tablet or StockUnit.Capsule))
                        return Results.BadRequest(new { message = "Strip offers are only supported for tablets and capsules." });
                }
                db.OfferRules.Add(new OfferRule { Id = input.RequestId, Name = name, NameKey = key, MedicineId = input.MedicineId, Kind = input.Kind,
                    Value = input.Value, Unit = input.Unit, Units = input.Units, StartDate = input.StartDate, EndDate = input.EndDate,
                    MinimumSubtotal = input.MinimumSubtotal, RequestHash = hash, CreatedBy = actor, CreatedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Ok(new { id = input.RequestId });
            } catch (AntiforgeryValidationException) { return Results.BadRequest(new { message = "Refresh and sign in again before saving." }); }
            catch (Exception e) when (e is NpgsqlException or DbUpdateException) { return Results.Json(new { message = "Save was not confirmed. Retry the unchanged offer." }, statusCode: 503); }
        }).RequireAuthorization("AdminOnly");
        app.MapPost("/api/offers/{id:guid}/stop", async (Guid id, PharmacyDbContext db, HttpContext http, IAntiforgery csrf, CancellationToken ct) => {
            try {
                await csrf.ValidateRequestAsync(http);
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425914)", ct);
                var rule = await db.OfferRules.SingleOrDefaultAsync(x => x.Id == id, ct);
                if (rule == null) return Results.NotFound();
                if (rule.StoppedAt == null) { rule.StoppedAt = DateTimeOffset.UtcNow; rule.StoppedBy = http.User.FindFirstValue(ClaimTypes.NameIdentifier); await db.SaveChangesAsync(ct); }
                await tx.CommitAsync(ct); return Results.Ok(new { rule.Id });
            } catch (AntiforgeryValidationException) { return Results.BadRequest(new { message = "Refresh before stopping the offer." }); }
        }).RequireAuthorization("AdminOnly");
    }
}
