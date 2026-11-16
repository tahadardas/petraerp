using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Inventory;

/// <summary>
/// The single rule set for unit conversions used by every document engine: an item-specific conversion wins over a
/// company-wide one, either direction may be stored, no density is ever assumed, and a converted quantity must be exact at
/// stock precision. The lookup itself stays in the persistence layer; these are the pure decisions.
/// </summary>
public static class UnitConversionPolicy
{
    public const string ConversionDimension = "inventory.conversion_dimension";
    public const string ConversionSameUnit = "inventory.conversion_same_unit";
    public const string ConversionDuplicate = "inventory.conversion_duplicate";
    public const string QuantityUnitPrecision = "inventory.quantity_unit_precision";
    public const decimal MaximumQuantity = 999999999999999.9999m;

    /// <summary>A company-wide conversion may not join two different known dimensions; an item-specific one may (its own density).</summary>
    public static void RequireAllowed(Guid? itemId, Guid fromUnitId, UnitDimension from, Guid toUnitId, UnitDimension to)
    {
        if (fromUnitId == toUnitId)
            throw RuleViolation.Create(ConversionSameUnit, "A conversion needs two different units.");
        if (itemId is null && from != UnitDimension.Unspecified && to != UnitDimension.Unspecified && from != to)
            throw RuleViolation.Create(ConversionDimension,
                "Converting between different dimensions (for example volume to mass) needs an item-specific conversion; no default density is assumed.");
    }

    /// <summary>A quantity in a classified unit must respect that unit's decimal places (for example whole pieces).</summary>
    public static void RequireUnitPrecision(decimal quantity, UnitDimension dimension, int decimalPlaces)
    {
        if (dimension == UnitDimension.Unspecified) return;
        if (decimal.Round(quantity, Math.Clamp(decimalPlaces, 0, 4)) != quantity)
            throw RuleViolation.Create(QuantityUnitPrecision, $"The quantity allows at most {decimalPlaces} decimal places in this unit.");
    }

    /// <summary>
    /// Converts with a stored factor (from-unit quantity × factor = to-unit quantity). <paramref name="forward"/> is true when
    /// the document unit is the stored from-unit. Returns null when the result is not exact at four decimals.
    /// </summary>
    public static decimal? Convert(decimal quantity, decimal factor, bool forward)
    {
        if (factor <= 0) return null;
        decimal converted;
        try { converted = forward ? checked(quantity * factor) : quantity / factor; }
        catch (OverflowException) { return null; }
        if (converted < 0 || converted > MaximumQuantity || decimal.Round(converted, 4) != converted) return null;
        return converted;
    }
}
