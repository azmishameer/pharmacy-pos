using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using PharmacyPos.Api.Data;

namespace PharmacyPos.Api.Catalogue;

public static class CatalogueReview
{
    public sealed record ReviewInput(bool Approve, string? Note);
    public static void MapCatalogueReview(this WebApplication app)
    {
        // Admin sees all pending submissions; staff can inspect only their own.
        app.MapGet("/api/catalogue/submissions", async (HttpContext http, PharmacyDbContext db, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var query = db.Medicines.AsNoTracking().Where(x => x.ReviewStatus != CatalogueReviewStatus.Approved);
            if (http.User.IsInRole("Admin")) query = query.Where(x => x.ReviewStatus == CatalogueReviewStatus.PendingReview);
            if (!http.User.IsInRole("Admin")) query = query.Where(x => x.CreatedByUserId == actor);
            return Results.Ok(await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(100).Select(x => new {
                x.Id, x.BrandName, manufacturer = x.Manufacturer.Name, dosageForm = x.DosageForm.Name,
                baseUnit = x.BaseUnit.ToString(), classification = x.Classification.ToString(),
                reviewStatus = x.ReviewStatus.ToString(), x.ReviewNote, x.CreatedAt,
                submittedBy = db.Users.Where(u => u.Id == x.CreatedByUserId).Select(u => u.UserName).FirstOrDefault(),
                ingredients = x.Ingredients.OrderBy(i => i.DisplayOrder).Select(i => new { name = i.GenericIngredient.Name, i.StrengthValue, i.StrengthUnit }).ToList(),
                batches = x.Batches.Select(b => new { b.BatchNumber, b.ManufacturingDate, b.ExpiryDate }).ToList()
            }).ToListAsync(ct));
        }).RequireAuthorization("Staff");
        app.MapPost("/api/catalogue/submissions/{id:guid}/review", async (Guid id, ReviewInput input, HttpContext http,
            IAntiforgery antiforgery, PharmacyDbContext db, CancellationToken ct) =>
        {
            try { await antiforgery.ValidateRequestAsync(http); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(new { message = "Refresh and sign in again before reviewing." }); }
            var note = input.Note?.Trim();
            if (note?.Length > 1000 || (!input.Approve && string.IsNullOrWhiteSpace(note)))
                return Results.BadRequest(new { message = "Rejection needs a reason. Notes must be at most 1,000 characters." });
            var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var status = input.Approve ? CatalogueReviewStatus.Approved : CatalogueReviewStatus.Rejected;
            // Conditional update prevents two reviewers from overwriting each other.
            var count = await db.Medicines.Where(x => x.Id == id && x.ReviewStatus == CatalogueReviewStatus.PendingReview)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.ReviewStatus, status)
                    .SetProperty(x => x.ReviewedByUserId, actor).SetProperty(x => x.ReviewedAt, DateTimeOffset.UtcNow)
                    .SetProperty(x => x.ReviewNote, note), ct);
            return count == 1 ? Results.NoContent() : Results.Conflict(new { message = "This submission is no longer pending. Refresh the list." });
        }).RequireAuthorization("AdminOnly");
    }
}
