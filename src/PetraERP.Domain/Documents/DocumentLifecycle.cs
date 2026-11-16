namespace PetraERP.Domain.Documents;

/// <summary>The accounting document families that share one lifecycle.</summary>
public enum AccountingDocumentKind
{
    SalesInvoice = 1,
    PurchaseInvoice = 2,
    TreasuryVoucher = 3,
    GeneralJournal = 4,
    InventoryDocument = 5,
}

/// <summary>Canonical lifecycle state: Draft → Approved → Posted → Reversed, or Draft/Approved → Cancelled.</summary>
public enum DocumentLifecycleState
{
    Draft = 0,
    Approved = 1,
    Posted = 2,
    Reversed = 3,
    Cancelled = 4,
}

/// <summary>
/// Single verifiable transition table for every accounting document. Posted, Reversed and Cancelled documents are
/// immutable: the only legal move out of Posted is a reversal that keeps the original and its audit trail.
/// Journals map their pre-approval Validated step to Draft; inventory documents have no Approved step and sales
/// invoices are the only family with Cancelled.
/// </summary>
public static class DocumentLifecycle
{
    public static bool IsImmutable(DocumentLifecycleState state) =>
        state is DocumentLifecycleState.Posted or DocumentLifecycleState.Reversed or DocumentLifecycleState.Cancelled;

    public static bool CanTransition(AccountingDocumentKind kind, DocumentLifecycleState from, DocumentLifecycleState to)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(from) || !Enum.IsDefined(to)) return false;
        return (from, to) switch
        {
            (DocumentLifecycleState.Draft, DocumentLifecycleState.Approved) => kind != AccountingDocumentKind.InventoryDocument,
            (DocumentLifecycleState.Draft, DocumentLifecycleState.Posted) => true,
            (DocumentLifecycleState.Approved, DocumentLifecycleState.Posted) => kind != AccountingDocumentKind.InventoryDocument,
            (DocumentLifecycleState.Draft, DocumentLifecycleState.Cancelled) => kind == AccountingDocumentKind.SalesInvoice,
            (DocumentLifecycleState.Approved, DocumentLifecycleState.Cancelled) => kind == AccountingDocumentKind.SalesInvoice,
            (DocumentLifecycleState.Posted, DocumentLifecycleState.Reversed) => true,
            _ => false,
        };
    }

    public static void EnsureTransition(AccountingDocumentKind kind, DocumentLifecycleState from, DocumentLifecycleState to)
    {
        if (!CanTransition(kind, from, to))
            throw new InvalidOperationException($"{kind}: transition {from} -> {to} is not allowed.");
    }

    /// <summary>A body/lines edit is legal only while the document is not yet immutable.</summary>
    public static void EnsureEditable(AccountingDocumentKind kind, DocumentLifecycleState state)
    {
        if (IsImmutable(state))
            throw new InvalidOperationException($"{kind} in state {state} is immutable; correct it by reversal.");
    }

    public static DocumentLifecycleState From(Sales.SalesInvoiceStatus s) => (DocumentLifecycleState)(int)s;
    public static DocumentLifecycleState From(Purchases.PurchaseInvoiceStatus s) => (DocumentLifecycleState)(int)s;
    public static DocumentLifecycleState From(Treasury.VoucherStatus s) => (DocumentLifecycleState)(int)s;

    public static DocumentLifecycleState From(Inventory.InventoryDocumentState s) => s switch
    {
        Inventory.InventoryDocumentState.Draft => DocumentLifecycleState.Draft,
        Inventory.InventoryDocumentState.Posted => DocumentLifecycleState.Posted,
        Inventory.InventoryDocumentState.Reversed => DocumentLifecycleState.Reversed,
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    public static DocumentLifecycleState From(GeneralLedger.JournalEntryState s) => s switch
    {
        GeneralLedger.JournalEntryState.Draft or GeneralLedger.JournalEntryState.Validated => DocumentLifecycleState.Draft,
        GeneralLedger.JournalEntryState.Approved => DocumentLifecycleState.Approved,
        GeneralLedger.JournalEntryState.Posted => DocumentLifecycleState.Posted,
        GeneralLedger.JournalEntryState.Reversed => DocumentLifecycleState.Reversed,
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };
}
