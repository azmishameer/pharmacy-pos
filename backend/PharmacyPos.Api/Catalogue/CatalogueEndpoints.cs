using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Data;

namespace PharmacyPos.Api.Catalogue;

public static class CatalogueEndpoints
{
    public static void MapCataloguePreview(this WebApplication app)
    {
        // Catalogue remains a development preview, with server-enforced staff access.

        app.MapCatalogueEntry();
        app.MapBarcodes();
        app.MapCatalogueReview();
        app.MapGet("/api/catalogue/options", () => Results.Ok(new { manufacturers = CatalogueOptions.Manufacturers, forms = CatalogueOptions.Forms, units = Enum.GetNames<StockUnit>() })).RequireAuthorization("Staff");
        app.MapGet("/api/medicines", async (PharmacyDbContext db, string? search,
            int? page, CancellationToken cancellationToken) =>
        {
            const int pageSize = 25;
            var pageNumber = page ?? 1;
            var term = search?.Trim() ?? "";
            if (pageNumber < 1 || pageNumber > 10000 || term.Length > 100)
                return Results.BadRequest(new { message = "Invalid catalogue search or page." });
            try
            {
                var query = db.Medicines.AsNoTracking().Where(x => x.IsActive && x.ReviewStatus == CatalogueReviewStatus.Approved);
                if (term.Length > 0)
                {
                    // Treat wildcard characters as literal search text.
                    var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                    query = query.Where(x => EF.Functions.ILike(x.BrandName, pattern, "\\")
                        || EF.Functions.ILike(x.Manufacturer.Name, pattern, "\\")
                        || x.Ingredients.Any(i => EF.Functions.ILike(i.GenericIngredient.Name, pattern, "\\")));
                }
                var rows = await query.OrderBy(x => x.BrandName).ThenBy(x => x.Id)
                    .Skip((pageNumber - 1) * pageSize).Take(pageSize + 1)
                    .Select(x => new
                    {
                        x.Id, x.BrandName,
                        Manufacturer = x.Manufacturer.Name,
                        DosageForm = x.DosageForm.Name,
                        Classification = x.Classification.ToString(),
                        Ingredients = x.Ingredients.OrderBy(i => i.DisplayOrder).Select(i => new
                        {
                            Name = i.GenericIngredient.Name, i.StrengthValue, i.StrengthUnit
                        }).ToList()
                    }).ToListAsync(cancellationToken);
                return Results.Ok(new { items = rows.Take(pageSize), page = pageNumber, hasMore = rows.Count > pageSize });
            }
            catch (NpgsqlException)
            {
                // A database failure must not be presented as an empty catalogue.
                return Results.Json(new { message = "The catalogue is unavailable. Check the database connection and migrations." }, statusCode: 503);
            }
        }).RequireAuthorization("Staff");
    }
}
