namespace PharmacyPos.Api.Sales;

public static class OfferPricing
{
    public sealed record Line(Guid MedicineId, long Units, ExactAmount Mrp);
    public sealed record Selection(OfferRule? Offer, ExactAmount Discount);
    public sealed record ChargeAmount(ChargeRule Rule, ExactAmount Amount);
    public sealed record PricedLine(Line Line, Selection Discount, List<ChargeAmount> Charges, ExactAmount Final);
    public sealed record Quote(List<PricedLine> Lines, ExactAmount Total, string Strategy);
    public static Quote Price(List<Line> lines, List<OfferRule> offers, List<ChargeRule> charges)
    {
        var groups = lines.Select((line, i) => (line, i)).GroupBy(x => x.line.MedicineId).ToList();
        var subtotal = lines.Aggregate(ExactAmount.Zero, (sum, line) => sum + line.Mrp);
        var medicineUnits = groups.ToDictionary(g => g.Key, g => g.Sum(x => x.line.Units));
        Quote Calculate(Selection[] selection, string strategy) {
            var output = new List<PricedLine>(); var total = ExactAmount.Zero;
            for (var i = 0; i < lines.Count; i++) {
                var line = lines[i]; var discounted = line.Mrp - selection[i].Discount;
                var applied = charges.Where(c => c.AllMedicines || c.Medicines.Any(m => m.MedicineId == line.MedicineId)).Select(c =>
                    new ChargeAmount(c, ExactAmount.FromMoney(c.Value) * (c.Kind == "Percentage" ? discounted * new ExactAmount(1, 100)
                        : c.Kind == "PerUnit" ? new ExactAmount(line.Units, 1) : new ExactAmount(line.Units, medicineUnits[line.MedicineId])))).ToList();
                var final = applied.Aggregate(discounted, (sum, c) => sum + c.Amount);
                output.Add(new(line, selection[i], applied, final)); total += final;
            }
            return new(output, total, strategy);
        }
        // Ties prefer earlier-created offers, then stable ID. Medicine strategy wins an exact total tie.
        var ordered = offers.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).ToList();
        var selected = lines.Select(_ => new Selection(null, ExactAmount.Zero)).ToArray();
        foreach (var group in groups) {
            var best = ExactAmount.Zero;
            foreach (var offer in ordered.Where(o => o.MedicineId == group.Key)) {
                var allocations = group.Select(x => (x.i, amount: ExactAmount.Min(x.line.Mrp, ExactAmount.FromMoney(offer.Value)
                    * (offer.Kind == "Percentage" ? x.line.Mrp * new ExactAmount(1, 100) : new ExactAmount(x.line.Units, offer.Units))))).ToList();
                var sum = allocations.Aggregate(ExactAmount.Zero, (s, x) => s + x.amount);
                if (sum > best) { best = sum; foreach (var x in allocations) selected[x.i] = new(offer, x.amount); }
            }
        }
        var result = Calculate(selected, selected.Any(x => x.Offer != null) ? "Medicine" : "None");
        foreach (var offer in ordered.Where(o => o.MedicineId == null)) {
            if (subtotal < ExactAmount.FromMoney(offer.MinimumSubtotal)) continue;
            var discount = ExactAmount.Min(subtotal, ExactAmount.FromMoney(offer.Value) * (offer.Kind == "Percentage" ? subtotal * new ExactAmount(1, 100) : new ExactAmount(1, 1)));
            // Prorate whole-sale discounts by original MRP, preserving exact fractions.
            var alternative = Calculate(lines.Select(l => new Selection(offer, discount * l.Mrp / subtotal)).ToArray(), "WholeSale");
            if (alternative.Total < result.Total) result = alternative;
        }
        return result;
    }
}
