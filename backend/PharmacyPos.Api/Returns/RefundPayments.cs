using System.Numerics;
using PharmacyPos.Api.Sales;

namespace PharmacyPos.Api.Returns;

public sealed class ReturnPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReturnId { get; set; }
    public SaleReturn Return { get; set; } = null!;
    public string Method { get; set; } = "Cash";
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
}
public static class RefundPayments
{
    public sealed record Share(string Method, decimal Amount);
    public sealed record ReferenceInput(string Method, string? Reference);

    // Allocate original paid cents to receipt lines once, in receipt order. Both row
    // and payment-column totals are exact; returning lines in another order changes nothing.
    public static List<Share>[] For(Sale sale)
    {
        var lines = RefundAllocation.For(sale);
        var payments = sale.Payments.OrderBy(p => Array.IndexOf(PaymentMethods.All, p.Method)).ToArray();
        if (payments.Sum(p => p.Amount) != sale.Total) throw new InvalidOperationException("Saved payment totals do not match the receipt.");
        var remaining = payments.Select(p => decimal.ToInt64(p.Amount * 100)).ToArray();
        var remainingTotal = remaining.Sum();
        var result = new List<Share>[lines.Length];
        for (var i = 0; i < lines.Length; i++) {
            long due = decimal.ToInt64(lines[i] * 100);
            var allocations = new long[payments.Length]; var fractions = new BigInteger[payments.Length];
            if (remainingTotal > 0) {
                for (var j = 0; j < payments.Length; j++) {
                    var product = (BigInteger)remaining[j] * due;
                    allocations[j] = (long)(product / remainingTotal); fractions[j] = product % remainingTotal;
                }
                var extra = due - allocations.Sum();
                foreach (var j in Enumerable.Range(0, payments.Length).OrderByDescending(j => fractions[j]).ThenBy(j => j).Take((int)extra)) allocations[j]++;
            }
            result[i] = [];
            for (var j = 0; j < payments.Length; j++) {
                remaining[j] -= allocations[j];
                if (allocations[j] > 0) result[i].Add(new(payments[j].Method, allocations[j] / 100m));
            }
            remainingTotal -= due;
        }
        return result;
    }
    public static List<Share> Selected(Sale sale, IEnumerable<int> indexes, string destination)
    {
        var selected = indexes.ToArray();
        if (destination == "Cash") { var amounts = RefundAllocation.For(sale); return [new("Cash", selected.Sum(i => amounts[i]))]; }
        var byLine = For(sale);
        return selected.SelectMany(i => byLine[i]).GroupBy(p => p.Method).Select(g => new Share(g.Key, g.Sum(p => p.Amount))).ToList();
    }
    public static List<ReturnPayment> Validate(List<Share> shares, List<ReferenceInput>? references)
    {
        var inputs = references ?? [];
        if (inputs.Any(r => r == null || r.Method == "Cash" || !shares.Any(s => s.Method == r.Method)) || inputs.Select(r => r.Method).Distinct().Count() != inputs.Count)
            throw new SalesCounter.PricingFailure(400, "Enter one refund reference for each non-cash refund method.");
        return shares.Select(s => {
            var reference = PaymentMethods.Reference(inputs.SingleOrDefault(r => r.Method == s.Method)?.Reference);
            if (s.Method != "Cash" && !PaymentMethods.ValidReference(reference))
                throw new SalesCounter.PricingFailure(400, $"Enter the completed {s.Method} refund reference.");
            return new ReturnPayment { Method = s.Method, Amount = s.Amount, Reference = reference };
        }).ToList();
    }
}
