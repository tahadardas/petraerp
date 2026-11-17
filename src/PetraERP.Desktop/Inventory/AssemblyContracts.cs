using PetraERP.ApiClient;

namespace PetraERP.Desktop.Inventory;

/// <summary>
/// Mode of the manufacturing/assembly operation:
/// Assembly (تجميع): Issues raw components and receipts finished assembled product.
/// Disassembly (تفكيك): Issues assembled product and receipts separated raw components.
/// </summary>
public enum AssemblyDisassemblyMode
{
    Assembly = 1,
    Disassembly = 2
}

/// <summary>
/// Disassembly cost distribution policy according to Al-Ameen 10 design:
/// StandardBOM: based on component costs defined in BOM.
/// ProportionalSalesPrice: based on current component prices.
/// ManualPercentage: user assigns custom percentage to each line.
/// </summary>
public enum AssemblyCostDistributionMethod
{
    StandardBOM = 1,
    ProportionalSalesPrice = 2,
    ManualPercentage = 3
}

public sealed record AssemblyDocumentLineDto
{
    public int LineNumber { get; init; }
    public Guid ItemId { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public Guid? UnitId { get; init; }
    public string UnitName { get; init; } = string.Empty;
    public decimal StandardQuantity { get; init; }
    public decimal TotalQuantity { get; init; }
    public decimal UnitCost { get; init; }
    public decimal TotalCost { get; init; }
    public bool IsFlexible { get; init; } = true;
    public bool IsMandatory { get; init; }
    public string? AlternateGroupId { get; init; }
    public decimal CostPercentage { get; init; }
}

public sealed record AssemblyDocumentExpenseDto
{
    public int LineNumber { get; init; }
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? Notes { get; init; }
}

public sealed record AssemblyDocumentDto
{
    public Guid Id { get; init; }
    public string Number { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public Guid CompanyId { get; init; }
    public Guid BranchId { get; init; }
    public Guid PatternId { get; init; }
    public Guid PatternRevisionId { get; init; }
    public AssemblyDisassemblyMode Mode { get; init; } = AssemblyDisassemblyMode.Assembly;
    public Guid AssembledItemId { get; init; }
    public string AssembledItemCode { get; init; } = string.Empty;
    public string AssembledItemName { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public Guid? UnitId { get; init; }
    public string UnitName { get; init; } = string.Empty;
    public Guid? SourceWarehouseId { get; init; }
    public Guid? TargetWarehouseId { get; init; }
    public Guid? CostCenterId { get; init; }
    public Guid? OperatingAccountId { get; init; }
    public Guid? AssemblyExpenseAccountId { get; init; }
    public Guid? VarianceAccountId { get; init; }
    public List<AssemblyDocumentLineDto> Lines { get; init; } = [];
    public List<AssemblyDocumentExpenseDto> Expenses { get; init; } = [];
    public decimal TotalMaterialCost { get; init; }
    public decimal TotalExpenses { get; init; }
    public decimal GrandTotal { get; init; }
    public decimal UnitCost { get; init; }
    public string TafqeetArabic { get; init; } = string.Empty;
    public InventoryDocumentState State { get; init; } = InventoryDocumentState.Draft;
}
