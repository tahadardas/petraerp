using System.Globalization;

namespace PetraERP.Desktop.Sales;

/// <summary>
/// Parser and calculator for cascading commercial discounts (e.g. "10% + 5% + 2%").
/// Implements standard Arabic & International commercial cascading multi-tier discounts.
/// </summary>
public static class CascadingDiscountCalculator
{
    private static readonly char[] Separators = ['+', '،', ','];

    /// <summary>
    /// Checks if the input is a valid single percentage or a multi-tier expression joined by '+'.
    /// </summary>
    public static bool IsValidExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;

        var parts = expression.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return true;

        foreach (var rawPart in parts)
        {
            var cleaned = rawPart.Replace("%", string.Empty).Trim();
            if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate))
            {
                return false;
            }

            if (rate is < 0m or > 100m)
            {
                return false;
            }

            if (decimal.Round(rate, 4) != rate)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses an expression into individual percentage tiers.
    /// </summary>
    public static bool TryParseTiers(string? expression, out List<decimal> tiers)
    {
        tiers = [];
        if (string.IsNullOrWhiteSpace(expression)) return true;

        var parts = expression.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawPart in parts)
        {
            var cleaned = rawPart.Replace("%", string.Empty).Trim();
            if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate))
            {
                tiers.Clear();
                return false;
            }

            if (rate is < 0m or > 100m || decimal.Round(rate, 4) != rate)
            {
                tiers.Clear();
                return false;
            }

            if (rate > 0m)
            {
                tiers.Add(rate);
            }
        }

        return true;
    }

    /// <summary>
    /// Calculates the compounded effective discount percentage from tiers.
    /// Formula: 100 * (1 - Product(1 - rate / 100)).
    /// </summary>
    public static decimal CalculateEffectivePercentage(string? expression)
    {
        if (!TryParseTiers(expression, out var tiers) || tiers.Count == 0)
        {
            return 0m;
        }

        if (tiers.Count == 1)
        {
            return tiers[0];
        }

        decimal remainingFactor = 1.0m;
        foreach (var tier in tiers)
        {
            remainingFactor *= (1.0m - (tier / 100m));
        }

        var effectiveRate = (1.0m - remainingFactor) * 100m;
        return Math.Round(effectiveRate, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Computes the exact tiered discount amount on a base amount, applying each tier sequentially to the remainder.
    /// </summary>
    public static decimal CalculateTieredDiscountAmount(decimal baseAmount, string? expression, out decimal effectivePercentage)
    {
        effectivePercentage = 0m;
        if (baseAmount <= 0m || !TryParseTiers(expression, out var tiers) || tiers.Count == 0)
        {
            return 0m;
        }

        decimal currentAmount = baseAmount;
        decimal totalDiscount = 0m;

        foreach (var tier in tiers)
        {
            var tierDiscount = Math.Round(currentAmount * (tier / 100m), 4, MidpointRounding.AwayFromZero);
            totalDiscount += tierDiscount;
            currentAmount -= tierDiscount;
            if (currentAmount <= 0m) break;
        }

        effectivePercentage = baseAmount > 0m
            ? Math.Round((totalDiscount / baseAmount) * 100m, 4, MidpointRounding.AwayFromZero)
            : CalculateEffectivePercentage(expression);

        return Math.Min(baseAmount, totalDiscount);
    }
}
