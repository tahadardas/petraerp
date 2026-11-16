namespace PetraERP.Domain.Patterns;

/// <summary>
/// Coded business operation effect. Operation types share calculation and posting engines;
/// patterns configure business-facing accounts, numbering, defaults and overrides without modifying the operation engine.
/// </summary>
public enum BusinessOperationKind
{
    SalesInvoice = 1,
    SalesReturn = 2,
    PurchaseInvoice = 3,
    PurchaseReturn = 4,
    InventoryIssue = 5,
    InventoryReceipt = 6,
    CashReceipt = 7,
    CashPayment = 8,
    CashJournal = 9,
    ManualJournal = 10,
}

public enum BusinessOperationCategory
{
    Invoice = 1,
    Voucher = 2,
    Journal = 3,
    Inventory = 4,
}

/// <summary>
/// Catalog metadata for business operations. Unimplemented operations are visible in the roadmap catalog
/// as disabled templates, but cannot be activated or published into runnable desktop workflows.
/// </summary>
public static class BusinessOperationCatalog
{
    public static BusinessOperationCategory CategoryOf(BusinessOperationKind kind) => kind switch
    {
        BusinessOperationKind.SalesInvoice or BusinessOperationKind.SalesReturn or
        BusinessOperationKind.PurchaseInvoice or BusinessOperationKind.PurchaseReturn => BusinessOperationCategory.Invoice,

        BusinessOperationKind.CashReceipt or BusinessOperationKind.CashPayment => BusinessOperationCategory.Voucher,

        BusinessOperationKind.CashJournal or BusinessOperationKind.ManualJournal => BusinessOperationCategory.Journal,

        BusinessOperationKind.InventoryIssue or BusinessOperationKind.InventoryReceipt => BusinessOperationCategory.Inventory,

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown operation kind."),
    };

    /// <summary>
    /// True only when the underlying backend posting engine and workflow exist in the source code.
    /// ManualJournal, SalesInvoice, PurchaseInvoice and the CashReceipt/CashPayment vouchers are implemented; others stay disabled.
    /// </summary>
    public static bool IsRunnable(BusinessOperationKind kind) => kind switch
    {
        BusinessOperationKind.ManualJournal => true,
        BusinessOperationKind.SalesInvoice => true,
        BusinessOperationKind.CashReceipt => true,
        BusinessOperationKind.CashPayment => true,
        BusinessOperationKind.PurchaseInvoice => true,
        _ => false,
    };
}
