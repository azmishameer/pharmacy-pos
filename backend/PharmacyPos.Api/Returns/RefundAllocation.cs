using System.Numerics;
using System.Text.Json;
using PharmacyPos.Api.Sales;

namespace PharmacyPos.Api.Returns;

public static class RefundAllocation
{
    // Stable shares of the original paid total, calculated once from immutable receipt data.
    // Largest-remainder paisa allocation makes all complete-line refunds sum exactly to that total.
    public static decimal[] For(Sale sale) {
        using var json = JsonDocument.Parse(sale.Snapshot);
        var weights = json.RootElement.GetProperty("lines").EnumerateArray().Select(l => new BigInteger(l.GetProperty("finalPrice").GetDecimal() * 1000000m)).ToArray();
        var sum = weights.Aggregate(BigInteger.Zero, (a,b) => a+b);
        var total = new BigInteger(sale.Total * 100m);
        if (total == 0) return weights.Select(_ => 0m).ToArray();
        if (sum <= 0 || weights.Any(w => w < 0)) throw new InvalidOperationException("Receipt amounts cannot be allocated.");
        var floors = weights.Select(w => BigInteger.DivRem(total * w, sum, out _)).ToArray();
        var remainders = weights.Select((w,i) => (i, remainder: total*w % sum)).OrderByDescending(x => x.remainder).ThenBy(x => x.i).ToArray();
        var extra = (int)(total - floors.Aggregate(BigInteger.Zero,(a,b) => a+b));
        foreach (var r in remainders.Take(extra)) floors[r.i]++;
        return floors.Select(v => (decimal)v / 100m).ToArray();
    }
}
