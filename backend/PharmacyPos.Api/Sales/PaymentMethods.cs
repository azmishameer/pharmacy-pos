namespace PharmacyPos.Api.Sales;

public static class PaymentMethods
{
    public static readonly string[] All = ["Cash", "Card", "bKash", "Nagad"];
    public sealed record Input(string Method, decimal Amount, decimal? Tendered = null, string? Reference = null, bool Confirmed = false);
    public static string? Reference(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public static bool ValidReference(string? value) => value is { Length: >= 1 and <= 100 } && !value.Any(char.IsControl);
    public static bool Money(decimal value) => value >= 0 && value <= 1000000000000m && decimal.Round(value, 2) == value;
    public static List<SalePayment> Validate(List<Input> inputs, decimal total)
    {
        void Fail(string text) => throw new SalesCounter.PricingFailure(400, text);
        if (inputs.Count is < 1 or > 4 || inputs.Any(p => p == null || !All.Contains(p.Method)) || inputs.Select(p => p.Method).Distinct().Count() != inputs.Count)
            Fail("Choose each payment method at most once, using Cash, Card, bKash or Nagad.");
        if (inputs.Any(p => !Money(p.Amount) || (p.Amount == 0 && !(total == 0 && inputs.Count == 1 && p.Method == "Cash"))))
            Fail("Enter positive payment amounts with at most two decimals. A zero-price sale uses Cash with amount zero.");
        if (inputs.Sum(p => p.Amount) != total) Fail("Payment amounts must add up exactly to the receipt total.");
        var result = new List<SalePayment>();
        foreach (var p in inputs) {
            var reference = Reference(p.Reference);
            if (!p.Confirmed) Fail("Confirm that every payment has been received before completing the sale.");
            if (p.Method == "Cash") {
                if (reference != null || p.Tendered is not decimal cash || !Money(cash) || cash < p.Amount)
                    Fail("Cash received must cover the cash portion. Transaction references are only for non-cash payments.");
            } else if (!ValidReference(reference) || (p.Tendered != null && p.Tendered != p.Amount)) {
                Fail("Enter the completed transaction reference for each non-cash payment. Non-cash payments cannot give change.");
            }
            var tendered = p.Method == "Cash" ? p.Tendered!.Value : p.Amount;
            result.Add(new SalePayment { Method = p.Method, Amount = p.Amount, Tendered = tendered, Change = tendered - p.Amount, Reference = reference, ReferenceKey = reference?.ToUpperInvariant() });
        }
        return result;
    }
}
