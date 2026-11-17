namespace PetraERP.Desktop.Assets;

/// <summary>
/// One line in the periodic depreciation run.
/// </summary>
public sealed record AssetDepreciationRunLineDto
{
    public Guid AssetId { get; init; }
    public string AssetCode { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public decimal BookValueBefore { get; init; }
    public decimal DepreciationAmount { get; init; }
    public decimal BookValueAfter => Math.Max(0m, BookValueBefore - DepreciationAmount);
    public string CostCenterName { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

/// <summary>
/// Batch document for periodic depreciation run.
/// </summary>
public sealed record AssetDepreciationRunDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string VoucherNumber { get; init; } = string.Empty;
    public DateTime RunDate { get; init; } = DateTime.UtcNow;
    public int FiscalYear { get; init; }
    public int PeriodNumber { get; init; } // الشهر أو الفترة المالية
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<AssetDepreciationRunLineDto> Lines { get; init; } = [];
    public decimal TotalDepreciationAmount => Lines.Sum(l => l.DepreciationAmount);
    public bool IsPosted { get; init; }
}
