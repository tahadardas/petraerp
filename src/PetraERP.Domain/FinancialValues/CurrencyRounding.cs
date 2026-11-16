namespace PetraERP.Domain.FinancialValues;

/// <summary>How an amount is rounded to a currency's decimal places. Values are persisted; never renumber.</summary>
public enum CurrencyRoundingMode : byte
{
    /// <summary>Midpoint away from zero (commercial rounding). Default.</summary>
    HalfAwayFromZero = 1,
    /// <summary>Midpoint to the even neighbour (banker's rounding).</summary>
    HalfToEven = 2,
    /// <summary>Always toward zero (truncate).</summary>
    TowardZero = 3,
    /// <summary>Always away from zero (round up in magnitude).</summary>
    AwayFromZero = 4,
}

/// <summary>Exact decimal rounding per currency card. No binary floating point is involved.</summary>
public static class CurrencyRounding
{
    public const int MaximumDecimalPlaces = FinancialPrecision.MoneyScale;

    public static bool IsValidPlaces(int places) => places is >= 0 and <= MaximumDecimalPlaces;

    public static decimal Round(decimal amount, int decimalPlaces, CurrencyRoundingMode mode)
    {
        if (!IsValidPlaces(decimalPlaces))
            throw new ArgumentOutOfRangeException(nameof(decimalPlaces), "Decimal places must be between 0 and 4.");
        return mode switch
        {
            CurrencyRoundingMode.HalfAwayFromZero => decimal.Round(amount, decimalPlaces, MidpointRounding.AwayFromZero),
            CurrencyRoundingMode.HalfToEven => decimal.Round(amount, decimalPlaces, MidpointRounding.ToEven),
            CurrencyRoundingMode.TowardZero => decimal.Round(amount, decimalPlaces, MidpointRounding.ToZero),
            CurrencyRoundingMode.AwayFromZero => RoundAway(amount, decimalPlaces),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), "Unknown rounding mode."),
        };
    }

    private static decimal RoundAway(decimal amount, int places)
    {
        var truncated = decimal.Round(amount, places, MidpointRounding.ToZero);
        if (truncated == amount) return amount;
        var unit = 1m;
        for (var i = 0; i < places; i++) unit /= 10m;
        return amount > 0 ? truncated + unit : truncated - unit;
    }
}
