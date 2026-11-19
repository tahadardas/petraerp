namespace PetraERP.Contracts.Inventory;

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
    /// <summary>Invariant decimal string, never a binary floating-point number.</summary>
    public string StandardQuantity { get; init; } = "0";
    /// <summary>Invariant decimal string, never a binary floating-point number.</summary>
    public string TotalQuantity { get; init; } = "0";
    /// <summary>Invariant decimal string, never a binary floating-point number.</summary>
    public string UnitCost { get; init; } = "0";
    /// <summary>Invariant decimal string, never a binary floating-point number.</summary>
    public string TotalCost { get; init; } = "0";
    public bool IsFlexible { get; init; } = true;
    public bool IsMandatory { get; init; }
    public string? AlternateGroupId { get; init; }
    /// <summary>Invariant decimal string, never a binary floating-point number.</summary>
    public string CostPercentage { get; init; } = "0";
}

public sealed record AssemblyDocumentExpenseDto
{
    public int LineNumber { get; init; }
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    /// <summary>Invariant decimal string, never a binary floating-point number.</summary>
    public string Amount { get; init; } = "0";
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
    /// <summary>Invariant decimal string, never a binary floating-point number.</summary>
    public string Quantity { get; init; } = "0";
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
    /// <summary>Invariant decimal string, never a binary floating-point number.</summary>
    public string TotalMaterialCost { get; init; } = "0";
    /// <summary>Invariant decimal string, never a binary floating-point number.</summary>
    public string TotalExpenses { get; init; } = "0";
    /// <summary>Invariant decimal string, never a binary floating-point number.</summary>
    public string GrandTotal { get; init; } = "0";
    /// <summary>Invariant decimal string, never a binary floating-point number.</summary>
    public string UnitCost { get; init; } = "0";
    public string TafqeetArabic { get; init; } = string.Empty;
    public InventoryDocumentState State { get; init; } = InventoryDocumentState.Draft;
}
