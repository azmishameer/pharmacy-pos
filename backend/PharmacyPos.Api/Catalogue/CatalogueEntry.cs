using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Data;

namespace PharmacyPos.Api.Catalogue;

public static class CatalogueEntry
{
    public sealed record IngredientInput(string? Name, decimal StrengthValue, string? StrengthUnit);
    public sealed record MedicineInput(Guid RequestId, string? BrandName, string? Manufacturer,
        string? Classification, List<IngredientInput>? Ingredients, string DosageForm = "Tablet", string BaseUnit = "Tablet", BatchInput? FirstBatch = null);
    public sealed record BatchInput(string BatchNumber, DateOnly? ManufacturingDate, DateOnly ExpiryDate);

    public static void MapCatalogueEntry(this WebApplication app)
    {
        app.MapGet("/api/catalogue/lookups", async (string? kind, string? search, PharmacyDbContext db, CancellationToken ct) =>
        {
            var term = Normalize(search);
            if (term.Length > 200 || kind is not ("manufacturer" or "ingredient")) return Results.BadRequest();
            var names = kind == "manufacturer"
                ? db.Manufacturers.Where(x => x.IsActive).Select(x => x.Name)
                : db.GenericIngredients.Where(x => x.IsActive).Select(x => x.Name);
            var upper = term.ToUpperInvariant();
            var matches = await names.Where(x => x.ToUpper().Contains(upper)).OrderBy(x => x).Take(20).ToListAsync(ct);
            return Results.Ok(matches);
        }).RequireAuthorization("Staff");

        app.MapPost("/api/medicines", CreateAsync).RequireAuthorization("Staff");
    }

    private static async Task<IResult> CreateAsync(MedicineInput input, HttpContext http,
        IAntiforgery antiforgery, PharmacyDbContext db, CancellationToken ct)
    {
        try { await antiforgery.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { return Results.BadRequest(new { message = "Refresh the page and sign in again before saving." }); }

        var formName = Normalize(input.DosageForm);
        if (!CatalogueOptions.Forms.Contains(formName)) return Invalid("Select a supported dosage form.");
        if (!Enum.TryParse<StockUnit>(input.BaseUnit, out var baseUnit) || !Enum.IsDefined(baseUnit)) return Invalid("Select a stock unit.");
        if ((formName == "Tablet" && baseUnit != StockUnit.Tablet) || (formName == "Capsule" && baseUnit != StockUnit.Capsule))
            return Invalid("Tablets and capsules must use their matching stock unit.");
        var batchInput = input.FirstBatch;
        if (batchInput is not null && (Normalize(batchInput.BatchNumber).Length is < 1 or > 100
            || batchInput.ExpiryDate == default || batchInput.ManufacturingDate > batchInput.ExpiryDate))
            return Invalid("Enter a batch number and expiry date. Manufacturing must not be after expiry.");
        var brand = Normalize(input.BrandName);
        var manufacturerName = Normalize(input.Manufacturer);
        if (input.RequestId == Guid.Empty || brand.Length is < 1 or > 200 || manufacturerName.Length is < 1 or > 200)
            return Invalid("Enter a brand and manufacturer, each no longer than 200 characters.");
        if (input.Classification is not ("Otc" or "Prescription")) return Invalid("Select OTC or prescription.");
        if (input.Ingredients is null || input.Ingredients.Count is < 1 or > 20 || input.Ingredients.Any(x => x is null))
            return Invalid("Enter between 1 and 20 ingredients.");
        var ingredients = input.Ingredients.Select(x => new IngredientInput(Normalize(x.Name), x.StrengthValue, Normalize(x.StrengthUnit))).ToList();
        if (ingredients.Any(x => x.Name!.Length is < 1 or > 200 || x.StrengthValue <= 0
            || x.StrengthValue >= 1_000_000_000_000m || decimal.Round(x.StrengthValue, 6) != x.StrengthValue
            || x.StrengthUnit is not ("mg" or "g" or "mcg" or "IU" or "mg/mL" or "mg/5mL" or "mcg/mL" or "IU/mL" or "% w/w" or "% w/v" or "mg/dose" or "mcg/dose")))
            return Invalid("Each ingredient needs a name and positive strength (up to 6 decimal places), with a supported unit.");
        if (ingredients.Select(x => x.Name!.ToUpperInvariant()).Distinct().Count() != ingredients.Count)
            return Invalid("Enter each ingredient only once.");

        var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            brand = brand.ToUpperInvariant(), manufacturer = manufacturerName.ToUpperInvariant(),
            input.Classification, formName, baseUnit, firstBatch = batchInput is null ? null : batchInput with { BatchNumber = Normalize(batchInput.BatchNumber) },
            ingredients = ingredients.Select(x => new { name = x.Name!.ToUpperInvariant(), strength = x.StrengthValue.ToString("G29", CultureInfo.InvariantCulture), x.StrengthUnit })
        }))));

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Catalogue writes are infrequent. Serialize this creation path to prevent reference
            // duplicates and duplicate product submissions from concurrent administrators.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425911)", ct);
            var previous = await db.Medicines.FindAsync([input.RequestId], ct);
            if (previous is not null)
                return previous.CreationRequestHash == hash && previous.CreatedByUserId == actor
                    ? Results.Ok(new { previous.Id, previous.BrandName, reviewStatus = previous.ReviewStatus.ToString() })
                    : Results.Conflict(new { message = "This save identifier was already used for different details. Reload the form." });

            var makerUpper = manufacturerName.ToUpperInvariant();
            var manufacturer = await db.Manufacturers.FirstOrDefaultAsync(x => x.Name.ToUpper() == makerUpper, ct);
            if (manufacturer is { IsActive: false }) return Invalid("That manufacturer is inactive.");
            manufacturer ??= new Manufacturer { Name = manufacturerName };
            if (db.Entry(manufacturer).State == EntityState.Detached) db.Manufacturers.Add(manufacturer);
            var form = await db.DosageForms.FirstOrDefaultAsync(x => x.Name.ToUpper() == formName.ToUpper(), ct);
            if (form is { IsActive: false }) return Invalid("That dosage form is inactive.");
            form ??= new DosageForm { Name = formName };
            if (db.Entry(form).State == EntityState.Detached) db.DosageForms.Add(form);

            var medicine = new Medicine
            {
                Id = input.RequestId, BrandName = brand, Manufacturer = manufacturer,
                DosageForm = form, Classification = Enum.Parse<MedicineClassification>(input.Classification),
                BaseUnit = baseUnit,
                ReviewStatus = http.User.IsInRole("Admin") ? CatalogueReviewStatus.Approved : CatalogueReviewStatus.PendingReview,
                ReviewedByUserId = http.User.IsInRole("Admin") ? actor : null,
                ReviewedAt = http.User.IsInRole("Admin") ? DateTimeOffset.UtcNow : null,
                CreatedByUserId = actor, CreatedAt = DateTimeOffset.UtcNow, CreationRequestHash = hash
            };
            foreach (var (ingredient, order) in ingredients.Select((value, index) => (value, index)))
            {
                var upper = ingredient.Name!.ToUpperInvariant();
                var generic = await db.GenericIngredients.FirstOrDefaultAsync(x => x.Name.ToUpper() == upper, ct);
                if (generic is { IsActive: false }) return Invalid("One of the ingredients is inactive.");
                generic ??= new GenericIngredient { Name = ingredient.Name };
                if (db.Entry(generic).State == EntityState.Detached) db.GenericIngredients.Add(generic);
                medicine.Ingredients.Add(new MedicineIngredient
                {
                    GenericIngredient = generic, GenericIngredientId = generic.Id,
                    StrengthValue = ingredient.StrengthValue, StrengthUnit = ingredient.StrengthUnit!, DisplayOrder = order
                });
            }

            var brandUpper = brand.ToUpperInvariant();
            var candidates = await db.Medicines.Include(x => x.Ingredients)
                .Where(x => x.ReviewStatus != CatalogueReviewStatus.Rejected && x.BrandName.ToUpper() == brandUpper && x.ManufacturerId == manufacturer.Id && x.DosageFormId == form.Id)
                .ToListAsync(ct);
            if (candidates.Any(x => x.Ingredients.Count == medicine.Ingredients.Count
                && x.Ingredients.All(i => medicine.Ingredients.Any(j => j.GenericIngredientId == i.GenericIngredientId
                    && j.StrengthValue == i.StrengthValue && j.StrengthUnit == i.StrengthUnit))))
                return Results.Conflict(new { message = "This medicine and strength already exist. Search the catalogue instead of adding it again." });

            if (batchInput is not null) medicine.Batches.Add(new MedicineBatch {
                BatchNumber = Normalize(batchInput.BatchNumber), ManufacturingDate = batchInput.ManufacturingDate, ExpiryDate = batchInput.ExpiryDate
            });
            db.Medicines.Add(medicine);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Created($"/api/medicines?search={Uri.EscapeDataString(brand)}", new { medicine.Id, medicine.BrandName, reviewStatus = medicine.ReviewStatus.ToString() });
        }
        catch (Exception failure) when (failure is NpgsqlException or DbUpdateException or OperationCanceledException)
        {
            return Results.Json(new { message = "Save was not confirmed. Keep the form unchanged and retry to check the original save safely." }, statusCode: 503);
        }
    }

    private static string Normalize(string? text) => Regex.Replace(text?.Trim() ?? "", @"\s+", " ");
    private static IResult Invalid(string message) => Results.BadRequest(new { message });
}
