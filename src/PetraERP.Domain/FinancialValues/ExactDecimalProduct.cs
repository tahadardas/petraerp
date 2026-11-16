using System.Numerics;

namespace PetraERP.Domain.FinancialValues;

internal static class ExactDecimalProduct
{
    private static readonly BigInteger MaximumMoneyUnits = BigInteger.Pow(10, FinancialPrecision.StoragePrecision) - 1;

    internal static decimal ToMoney(decimal left, decimal right, MidpointRounding rounding)
    {
        if (!Enum.IsDefined(rounding))
        {
            throw new ArgumentOutOfRangeException(nameof(rounding));
        }

        var (leftCoefficient, leftScale) = Components(left);
        var (rightCoefficient, rightScale) = Components(right);
        var product = leftCoefficient * rightCoefficient;
        var shift = leftScale + rightScale - FinancialPrecision.MoneyScale;
        var units = shift > 0
            ? RoundUnits(product, BigInteger.Pow(10, shift), rounding)
            : product * BigInteger.Pow(10, -shift);

        if (BigInteger.Abs(units) > MaximumMoneyUnits)
        {
            throw new OverflowException("Converted amount exceeds the supported money range.");
        }

        return (decimal)units / (decimal)BigInteger.Pow(10, FinancialPrecision.MoneyScale);
    }

    private static BigInteger RoundUnits(BigInteger product, BigInteger divisor, MidpointRounding rounding)
    {
        var quotient = BigInteger.DivRem(product, divisor, out var remainder);
        var comparison = (BigInteger.Abs(remainder) * 2).CompareTo(divisor);
        var increment = rounding switch
        {
            MidpointRounding.ToEven => comparison > 0 || (comparison == 0 && !quotient.IsEven),
            MidpointRounding.AwayFromZero => comparison >= 0,
            MidpointRounding.ToZero => false,
            MidpointRounding.ToNegativeInfinity => remainder.Sign < 0,
            MidpointRounding.ToPositiveInfinity => remainder.Sign > 0,
            _ => throw new ArgumentOutOfRangeException(nameof(rounding)),
        };

        return increment ? quotient + product.Sign : quotient;
    }

    private static (BigInteger Coefficient, int Scale) Components(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        var coefficient = (BigInteger)(uint)bits[0]
                          + ((BigInteger)(uint)bits[1] << 32)
                          + ((BigInteger)(uint)bits[2] << 64);
        return ((bits[3] & int.MinValue) != 0 ? -coefficient : coefficient, (bits[3] >> 16) & 0xFF);
    }
}
