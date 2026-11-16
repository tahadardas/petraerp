namespace PetraERP.Application.Inventory;

/// <summary>
/// Al-Ameen "تعديل أسعار بطاقات المواد": raise or lower the general prices of active items by a percentage. Only price rows that apply to
/// everyone are touched (never a customer-specific or pattern-specific price). <see cref="PreviewOnly"/> counts what would change.
/// </summary>
public sealed record ItemPriceAdjustmentCommand(Guid? GroupId, string? PriceListCode, decimal Percent, int RoundingScale, bool PreviewOnly);

/// <summary>How many items and price rows changed (or would change, for a preview).</summary>
public sealed record ItemPriceAdjustmentResult(int ItemCount, int RowCount, bool Applied);
