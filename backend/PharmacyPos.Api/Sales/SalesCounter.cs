using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Catalogue;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Stock;

namespace PharmacyPos.Api.Sales;

// Pricing is shared by cart previews and atomic cash checkout.
public static class SalesCounter
{
    public sealed record CartLine(Guid LotId, int Quantity, string Unit);
    public sealed record CartInput(List<CartLine>? Lines);
    public static IQueryable<ReceivingMovement> Eligible(PharmacyDbContext db, DateOnly today) =>
        db.ReceivingMovements.AsNoTracking().Where(x => x.Quantity + (db.ReturnedItems.Where(i => i.ReceiptId == x.ReceiptId && i.Status == "Restocked").Sum(i => (long?)i.Quantity) ?? 0) > (db.SaleStockMovements.Where(m => m.ReceiptId == x.ReceiptId).Sum(m => (long?)m.Quantity) ?? 0) && x.Revision.Status == ReceivingStatus.Approved
            && x.Revision.MrpVerifiedAt != null && x.Revision.Medicine.IsActive && x.Revision.Medicine.ReviewStatus == CatalogueReviewStatus.Approved
            && x.Batch.ExpiryDate >= today && !db.StockDisposals.Any(d => d.ReceiptId == x.ReceiptId));

    public static void MapSalesCounter(this WebApplication app)
    {

        app.MapGet("/api/sales/stock", async (string? search, int? page, PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            var term = search?.Trim() ?? ""; var n = page ?? 1;
            if (term.Length > 100 || n is < 1 or > 10000) return Results.BadRequest();
            try {
                var query = Eligible(db, StockReceiving.ShopToday());
                if (term.Length > 0) {
                    var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                    query = query.Where(x => EF.Functions.ILike(x.Revision.Medicine.BrandName, pattern, "\\")
                        || EF.Functions.ILike(x.Revision.Medicine.Manufacturer.Name, pattern, "\\")
                        || x.Revision.Medicine.Ingredients.Any(i => EF.Functions.ILike(i.GenericIngredient.Name, pattern, "\\")));
                }
                var rows = await query.OrderBy(x => x.Batch.ExpiryDate).ThenBy(x => x.PostedAt).ThenBy(x => x.ReceiptId)
                    .Skip((n - 1) * 20).Take(21).Select(x => new {
                        lotId = x.ReceiptId, medicineId = x.Revision.MedicineId, brandName = x.Revision.Medicine.BrandName,
                        manufacturer = x.Revision.Medicine.Manufacturer.Name, dosageForm = x.Revision.Medicine.DosageForm.Name,
                        classification = x.Revision.Medicine.Classification.ToString(), baseUnit = x.Revision.BaseUnit.ToString(),
                        x.Batch.BatchNumber, x.Batch.ExpiryDate, availableUnits = x.Quantity + (db.ReturnedItems.Where(i => i.ReceiptId == x.ReceiptId && i.Status == "Restocked").Sum(i => (long?)i.Quantity) ?? 0) - (db.SaleStockMovements.Where(m => m.ReceiptId == x.ReceiptId).Sum(m => (long?)m.Quantity) ?? 0),
                        x.Revision.UnitsPerStrip, x.Revision.UnitsPerBox, x.Revision.MrpAmount, x.Revision.MrpUnit, x.Revision.MrpUnits,
                        ingredients = x.Revision.Medicine.Ingredients.OrderBy(i => i.DisplayOrder).Select(i => new { name = i.GenericIngredient.Name, i.StrengthValue, i.StrengthUnit }).ToList()
                    }).ToListAsync(ct);
                return Results.Ok(new { items = rows.Take(20), page = n, hasMore = rows.Count > 20 });
            } catch (NpgsqlException) { return Results.Json(new { message = "Available stock could not be loaded. Refresh to retry." }, statusCode: 503); }
        }).RequireAuthorization("Staff");

        app.MapPost("/api/sales/quote", async (CartInput input, PharmacyDbContext db, HttpContext http, IAntiforgery csrf, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            try {
                await csrf.ValidateRequestAsync(http);
                var quote = await BuildQuote(input, db, ct);
                return Results.Ok(quote.Public());
            }
            catch (PricingFailure e) { return Results.Json(new { message = e.Message }, statusCode: e.Status); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(new { message = "Refresh and sign in again to price the cart." }); }
            catch (Exception e) when (e is NpgsqlException or OperationCanceledException) { return Results.Json(new { message = "The cart could not be priced. Refresh prices to retry." }, statusCode: 503); }
        }).RequireAuthorization("Staff");
    }
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public sealed class PricingFailure(int status, string message) : Exception(message) { public int Status => status; }
    public sealed record Quotation(JsonElement Snapshot, string QuoteHash, long Payable, Dictionary<Guid, long> Quantities) {
        public Dictionary<string, object?> Public() {
            var result = Snapshot.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
            result["quoteHash"] = QuoteHash; result["quotedAt"] = DateTimeOffset.UtcNow; result["previewOnly"] = true;
            return result;
        }
    }
    public static async Task<Quotation> BuildQuote(CartInput input, PharmacyDbContext db, CancellationToken ct)
    {
        if (input.Lines is null || input.Lines.Count is < 1 or > 100 || input.Lines.Any(l => l is null || l.LotId == Guid.Empty || l.Quantity is < 1 or > 1000000 || l.Unit is not ("Piece" or "Strip" or "Box")))
            throw new PricingFailure(400, "Enter 1–100 cart lines with positive whole-number quantities and a valid selling unit.");
        var ids = input.Lines.Select(l => l.LotId).Distinct().ToArray();
        var stock = await Eligible(db, StockReceiving.ShopToday()).Include(x => x.Revision).ThenInclude(x => x.Medicine).Include(x => x.Batch)
            .Where(x => ids.Contains(x.ReceiptId)).ToDictionaryAsync(x => x.ReceiptId, ct);
        if (stock.Count != ids.Length) throw new PricingFailure(409, "A batch is no longer available, has expired, or needs MRP verification. Remove it and refresh the stock list.");
        var sold = await db.SaleStockMovements.Where(m => ids.Contains(m.ReceiptId)).GroupBy(m => m.ReceiptId)
            .Select(g => new { Id = g.Key, Quantity = g.Sum(m => m.Quantity) }).ToDictionaryAsync(x => x.Id, x => x.Quantity, ct);
        var restored = await db.ReturnedItems.Where(i => ids.Contains(i.ReceiptId) && i.Status == "Restocked").GroupBy(i => i.ReceiptId)
            .Select(g => new { Id = g.Key, Quantity = g.Sum(i => i.Quantity) }).ToListAsync(ct);
        foreach (var r in restored) sold[r.Id] = sold.GetValueOrDefault(r.Id) - r.Quantity;
        var used = new Dictionary<Guid, long>();
        var lines = new List<object>();
        var charges = await db.ChargeRules.AsNoTracking().Include(x => x.Medicines)
            .Where(x => x.StoppedAt == null).OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
        var today = StockReceiving.ShopToday();
        var offers = await db.OfferRules.AsNoTracking().Where(o => o.StoppedAt == null && o.StartDate <= today && (o.EndDate == null || o.EndDate >= today)).ToListAsync(ct);
        var pricingLines = new List<OfferPricing.Line>();
        foreach (var line in input.Lines) {
            var lot = stock[line.LotId]; var r = lot.Revision;
            var pack = line.Unit == "Piece" ? 1 : line.Unit == "Strip" ? r.UnitsPerStrip : r.UnitsPerBox;
            if (pack <= 0) throw new PricingFailure(400, "This batch does not support the selected selling unit.");
            var units = (long)line.Quantity * pack;
            used[line.LotId] = used.GetValueOrDefault(line.LotId) + units;
            if (used[line.LotId] > lot.Quantity - sold.GetValueOrDefault(line.LotId)) throw new PricingFailure(409, $"Not enough {r.Medicine.BrandName} in batch {lot.Batch.BatchNumber}. Only {lot.Quantity - sold.GetValueOrDefault(line.LotId)} {r.BaseUnit.ToString().ToLowerInvariant()}s are available across all cart lines.");
            var baseAmount = ExactAmount.FromMoney(r.MrpAmount) * new ExactAmount(units, r.MrpUnits);
            pricingLines.Add(new(r.MedicineId, units, baseAmount));
        }
        var priced = OfferPricing.Price(pricingLines, offers, charges);
        for (var i = 0; i < input.Lines.Count; i++) {
            var line = input.Lines[i]; var lot = stock[line.LotId]; var r = lot.Revision; var p = priced.Lines[i];
            lines.Add(new { line.LotId, line.Quantity, line.Unit, baseUnits = p.Line.Units, brandName = r.Medicine.BrandName,
                baseUnit = r.BaseUnit.ToString(), lot.Batch.BatchNumber, lot.Batch.ExpiryDate, availableUnits = lot.Quantity - sold.GetValueOrDefault(line.LotId),
                r.MrpAmount, r.MrpUnit, r.MrpUnits, lineTotal = p.Line.Mrp.Display,
                discountAmount = p.Discount.Discount.Display, offerId = p.Discount.Offer?.Id, offerName = p.Discount.Offer?.Name,
                charges = p.Charges.Select(c => new { c.Rule.Id, c.Rule.Name, c.Rule.Kind, c.Rule.Value, amount = c.Amount.Display }),
                chargeTotal = p.Charges.Aggregate(ExactAmount.Zero, (sum, c) => sum + c.Amount).Display, finalPrice = p.Final.Display });
        }
        var mrpSubtotal = pricingLines.Aggregate(ExactAmount.Zero, (sum, l) => sum + l.Mrp);
        var chargeTotal = priced.Lines.SelectMany(l => l.Charges).Aggregate(ExactAmount.Zero, (sum, c) => sum + c.Amount);
        var discountTotal = priced.Lines.Aggregate(ExactAmount.Zero, (sum, l) => sum + l.Discount.Discount);
        var total = priced.Total;
        if (total.Rounded > 1000000000000) throw new PricingFailure(400, "Cart value exceeds the preview limit of one trillion taka.");
        var estimatedTotal = (long)total.Rounded;
        var snapshot = JsonSerializer.SerializeToElement(new { lines, subtotal = mrpSubtotal.Display, chargeTotal = chargeTotal.Display, discountTotal = discountTotal.Display, offerStrategy = priced.Strategy,
            totalBeforeRounding = total.Display, roundingAdjustment = estimatedTotal - total.Display, estimatedTotal,
            offersApplied = discountTotal > ExactAmount.Zero }, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.GetRawText())));
        return new(snapshot, hash, estimatedTotal, used);
    }

}
