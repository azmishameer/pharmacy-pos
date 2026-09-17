using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Catalogue;
using PharmacyPos.Api.Data;

namespace PharmacyPos.Api.Sales;

public sealed class ChargeRule
{
    public Guid Id { get; set; }
    public bool? ExcludeFromProfit { get; set; }
    public bool? InitialExcludeFromProfit { get; set; }
    public Guid? ClassificationVersion { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string Kind { get; set; } = "Percentage";
    public decimal Value { get; set; }
    public bool AllMedicines { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public string? StoppedBy { get; set; }
    public List<ChargeMedicine> Medicines { get; set; } = [];
}
public sealed class ChargeMedicine
{
    public Guid ChargeRuleId { get; set; }
    public ChargeRule ChargeRule { get; set; } = null!;
    public Guid MedicineId { get; set; }
    public Medicine Medicine { get; set; } = null!;
}
public static class ChargeRules
{
    public sealed record Input(Guid RequestId, string? Name, string Kind, decimal Value, bool AllMedicines, List<Guid>? MedicineIds, bool ExcludeFromProfit = false);
    public static void MapCharges(this ModelBuilder model)
    {
        model.MapChargeClassification();
        var rule = model.Entity<ChargeRule>();
        rule.ToTable("charge_rules", t => {
            t.HasCheckConstraint("ck_charge_value", "\"Value\" > 0 AND \"Value\" <= 1000000 AND (\"Kind\" <> 'Percentage' OR \"Value\" <= 100)");
            t.HasCheckConstraint("ck_charge_kind", "\"Kind\" IN ('Percentage', 'PerUnit', 'PerMedicine')");
        });
        rule.HasKey(x => x.Id);
        rule.Property(x => x.Name).HasMaxLength(100);
        rule.Property(x => x.NameKey).HasMaxLength(100);
        rule.Property(x => x.Kind).HasMaxLength(20);
        rule.Property(x => x.Value).HasPrecision(12, 2);
        rule.HasIndex(x => x.NameKey).IsUnique().HasFilter("\"StoppedAt\" IS NULL");
        rule.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        rule.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.StoppedBy).OnDelete(DeleteBehavior.Restrict);
        var medicine = model.Entity<ChargeMedicine>();
        medicine.ToTable("charge_medicines");
        medicine.HasKey(x => new { x.ChargeRuleId, x.MedicineId });
        medicine.HasOne(x => x.ChargeRule).WithMany(x => x.Medicines).HasForeignKey(x => x.ChargeRuleId).OnDelete(DeleteBehavior.Restrict);
        medicine.HasOne(x => x.Medicine).WithMany().HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
    }
    public static void MapChargeRules(this WebApplication app)
    {

        app.MapChargeClassification();
        app.MapGet("/api/charges", async (PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await db.ChargeRules.AsNoTracking().OrderBy(x => x.StoppedAt != null).ThenByDescending(x => x.CreatedAt)
                .Select(x => new { x.Id, x.Name, x.Kind, x.Value, x.AllMedicines, x.CreatedAt, x.StoppedAt, x.ExcludeFromProfit, x.ClassificationVersion,
                    medicines = x.Medicines.Select(m => new { id = m.MedicineId, name = m.Medicine.BrandName }).ToList() }).ToListAsync(ct));
        }).RequireAuthorization("AdminOnly");
        app.MapPost("/api/charges", async (Input input, PharmacyDbContext db, HttpContext http, IAntiforgery csrf, CancellationToken ct) => {
            try {
                await csrf.ValidateRequestAsync(http);
                var name = string.Join(" ", (input.Name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                var ids = (input.MedicineIds ?? []).Distinct().Order().ToArray();
                if (input.RequestId == Guid.Empty || name.Length is < 1 or > 100 || input.Kind is not ("Percentage" or "PerUnit" or "PerMedicine")
                    || input.Value <= 0 || input.Value > 1000000 || decimal.Round(input.Value, 2) != input.Value || (input.Kind == "Percentage" && input.Value > 100)
                    || ids.Length > 500 || (!input.AllMedicines && ids.Length == 0))
                    return Results.BadRequest(new { message = "Enter a name, a positive amount with up to two decimals (percentage at most 100), and the medicines this charge applies to." });
                var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425913)", ct);
                var existing = await db.ChargeRules.Include(x => x.Medicines).SingleOrDefaultAsync(x => x.Id == input.RequestId, ct);
                if (existing != null) {
                    if (existing.CreatedBy != actor || existing.Name != name || existing.Kind != input.Kind || existing.Value != input.Value
                        || existing.AllMedicines != input.AllMedicines || existing.InitialExcludeFromProfit != input.ExcludeFromProfit || !existing.Medicines.Select(x => x.MedicineId).Order().SequenceEqual(ids))
                        return Results.Conflict(new { message = "This request was already used for a different charge. Refresh before creating another." });
                    return Results.Ok(new { existing.Id });
                }
                var key = name.ToUpperInvariant();
                if (await db.ChargeRules.AnyAsync(x => x.NameKey == key && x.StoppedAt == null, ct))
                    return Results.Conflict(new { message = "An active charge already has this name. Stop it before creating its replacement." });
                if (await db.Medicines.CountAsync(x => ids.Contains(x.Id) && x.IsActive && x.ReviewStatus == CatalogueReviewStatus.Approved, ct) != ids.Length)
                    return Results.BadRequest(new { message = "Select approved, active medicines." });
                db.ChargeRules.Add(new ChargeRule { Id = input.RequestId, Name = name, NameKey = key, Kind = input.Kind, Value = input.Value,
                    ExcludeFromProfit = input.ExcludeFromProfit, InitialExcludeFromProfit = input.ExcludeFromProfit,
                    AllMedicines = input.AllMedicines, CreatedBy = actor, CreatedAt = DateTimeOffset.UtcNow,
                    Medicines = ids.Select(id => new ChargeMedicine { MedicineId = id }).ToList() });
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return Results.Ok(new { id = input.RequestId });
            } catch (AntiforgeryValidationException) { return Results.BadRequest(new { message = "Refresh and sign in again before saving." }); }
            catch (Exception e) when (e is NpgsqlException or DbUpdateException) { return Results.Json(new { message = "Save was not confirmed. Retry the unchanged entry." }, statusCode: 503); }
        }).RequireAuthorization("AdminOnly");
        app.MapPost("/api/charges/{id:guid}/stop", async (Guid id, PharmacyDbContext db, HttpContext http, IAntiforgery csrf, CancellationToken ct) => {
            try {
                await csrf.ValidateRequestAsync(http);
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425913)", ct);
                var rule = await db.ChargeRules.SingleOrDefaultAsync(x => x.Id == id, ct);
                if (rule == null) return Results.NotFound();
                if (rule.StoppedAt == null) {
                    rule.StoppedAt = DateTimeOffset.UtcNow; rule.StoppedBy = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
                    await db.SaveChangesAsync(ct);
                }
                await tx.CommitAsync(ct); return Results.Ok(new { rule.Id });
            } catch (AntiforgeryValidationException) { return Results.BadRequest(new { message = "Refresh and sign in again before stopping a charge." }); }
        }).RequireAuthorization("AdminOnly");
    }
}
