using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

/// <summary>
/// The single unit-conversion engine for every document (sales, purchases, inventory documents, stock queries): converts a
/// document quantity to the item's base unit with one explicit conversion, item-specific first, then company-wide, in
/// either stored direction (e.g. ton ↔ kg). No density or chained conversion is assumed; a quantity in a classified unit
/// must respect that unit's decimal places (whole pieces); missing conversions and inexact results are rejected.
/// </summary>
internal static class InventoryUnits
{
    internal static Task<decimal> BaseQuantityAsync(FinancialDbContext context, Guid companyId, Guid itemId, Guid unitId, Guid baseUnitId,
        decimal quantity, CancellationToken cancellationToken)
        => BaseQuantityAsync(context, companyId, itemId, unitId, baseUnitId, quantity, false, cancellationToken);

    internal static async Task<decimal> BaseQuantityAsync(FinancialDbContext context, Guid companyId, Guid itemId, Guid unitId, Guid baseUnitId,
        decimal quantity, bool allowZero, CancellationToken cancellationToken)
    {
        if (quantity < 0 || (!allowZero && quantity == 0) || quantity > UnitConversionPolicy.MaximumQuantity || decimal.Round(quantity, 4) != quantity)
            throw new BusinessRuleException("inventory.quantity_precision", "الكمية موجبة وبدقة أربع منازل / Quantity must be positive and exact at four decimals.");
        var units = await context.UnitsOfMeasure.AsNoTracking().Where(u => u.CompanyId == companyId && (u.Id == unitId || u.Id == baseUnitId))
            .Select(u => new { u.Id, u.Dimension, u.DecimalPlaces }).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (units.Count != (unitId == baseUnitId ? 1 : 2))
            throw new BusinessRuleException("inventory.unit_scope", "الوحدة خارج الشركة / Unit is outside company scope.");
        var documentUnit = units.Single(u => u.Id == unitId);
        Rule(() => UnitConversionPolicy.RequireUnitPrecision(quantity, (UnitDimension)documentUnit.Dimension, documentUnit.DecimalPlaces));
        if (unitId == baseUnitId) return quantity;

        var conversion = await FindAsync(context, companyId, itemId, unitId, baseUnitId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("inventory.unit_conversion_invalid", "يلزم تحويل وحدة صريح بمعامل موجب / An explicit positive unit conversion is required.", "lines");
        var converted = UnitConversionPolicy.Convert(quantity, conversion.Factor, conversion.FromUnitId == unitId);
        if (converted is not { } result || (result == 0 && quantity > 0))
            throw new BusinessRuleException("inventory.quantity_precision", "الكمية المحولة يجب أن تطابق دقة المخزون؛ الصفر للجرد فقط / Converted quantity must match stock precision; zero is allowed only for counts.", "lines");
        var baseUnit = units.Single(u => u.Id == baseUnitId);
        Rule(() => UnitConversionPolicy.RequireAllowed(conversion.ItemId, unitId, (UnitDimension)documentUnit.Dimension, baseUnitId, (UnitDimension)baseUnit.Dimension));
        Rule(() => UnitConversionPolicy.RequireUnitPrecision(result, (UnitDimension)baseUnit.Dimension, baseUnit.DecimalPlaces));
        return result;
    }

    /// <summary>The applicable conversion between two units for an item: item-specific beats company-wide, a stored forward row beats the inverse.</summary>
    internal static Task<UnitConversionRow?> FindAsync(FinancialDbContext context, Guid companyId, Guid itemId, Guid unitId, Guid otherUnitId,
        CancellationToken cancellationToken) =>
        context.UnitConversions.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Factor > 0 && (c.ItemId == itemId || c.ItemId == null)
                && ((c.FromUnitId == unitId && c.ToUnitId == otherUnitId) || (c.FromUnitId == otherUnitId && c.ToUnitId == unitId)))
            .OrderByDescending(c => c.ItemId.HasValue).ThenByDescending(c => c.FromUnitId == unitId).ThenBy(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static void Rule(Action action)
    {
        try { action(); }
        catch (InvalidOperationException exception) when (RuleViolation.CodeOf(exception) is { } code)
        {
            throw new BusinessRuleException(code, exception.Message, "lines");
        }
    }
}
