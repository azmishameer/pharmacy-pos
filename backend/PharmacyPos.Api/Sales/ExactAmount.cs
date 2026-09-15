using System.Numerics;

namespace PharmacyPos.Api.Sales;

// Preserve repeating pack prices and charge allocations until the final half-up rounding.
public readonly record struct ExactAmount
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }
    public ExactAmount(BigInteger numerator, BigInteger denominator) {
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / gcd; Denominator = denominator / gcd;
    }
    public static ExactAmount Zero => new(0, 1);
    public static ExactAmount FromMoney(decimal value) => new(new BigInteger(value * 100m), 100);
    public static ExactAmount operator +(ExactAmount a, ExactAmount b) => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static ExactAmount operator *(ExactAmount a, ExactAmount b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    public static ExactAmount operator -(ExactAmount a, ExactAmount b) => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static ExactAmount operator /(ExactAmount a, ExactAmount b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    public static bool operator <(ExactAmount a, ExactAmount b) => a.Numerator * b.Denominator < b.Numerator * a.Denominator;
    public static bool operator >(ExactAmount a, ExactAmount b) => b < a;
    public static ExactAmount Min(ExactAmount a, ExactAmount b) => a < b ? a : b;
    public decimal Display => (decimal)BigInteger.DivRem(Numerator, Denominator, out var remainder)
        + (decimal)((remainder * 1000000 + Denominator / 2) / Denominator) / 1000000m;
    public BigInteger Rounded => (Numerator + Denominator / 2) / Denominator;
}
