namespace PetraERP.Desktop.Shell;

/// <summary>
/// Opening size of each internal (classic MDI) window, taken from the proportions of the Al-Ameen windows in the reference photos
/// (DIPs at 96 dpi). A window still opens as a movable, resizable child of the workspace; only its first size is fixed here, and a
/// size the operator has already chosen (<see cref="WorkspaceDocument.MdiBounds"/>) always wins. Documents without an entry keep the
/// cascade size of <see cref="MdiLayoutEngine"/>.
/// </summary>
internal static class AmeenWindowSizes
{
    internal static (double Width, double Height)? For(WorkspaceDocument document) => document.Kind switch
    {
        WorkspaceDocumentKind.ItemCard => (665, 405),
        WorkspaceDocumentKind.ItemGroupCard => (670, 390),
        WorkspaceDocumentKind.AccountCard => (650, 540),
        WorkspaceDocumentKind.PartyCard => (520, 600),
        WorkspaceDocumentKind.SalesInvoiceCard => (880, 520),
        WorkspaceDocumentKind.Journal => (750, 415),
        WorkspaceDocumentKind.PatternCard => (720, 560),
        WorkspaceDocumentKind.Route => ForRoute(document.Route),
        _ => (780, 500),
    };

    private static (double Width, double Height)? ForRoute(string? route) => route switch
    {
        "sales-invoices" or "purchase-invoices" => (880, 520),
        "treasury-vouchers" or "daily-vouchers" or "vouchers" => (840, 510),
        "trial-balance" or "currency-balance" => (880, 580),
        "account-directory" or "item-directory" => (560, 520),
        "patterns" or "pattern-manager" => (820, 540),
        "item-limits" => (640, 480),
        "item-price-tool" => (480, 340),
        "account-ledger" => (920, 620),
        "customer-statement" or "supplier-statement" or "party-statement" => (920, 620),
        "accounts" => (750, 540),
        "items" => (780, 500),
        "parties" => (750, 520),
        "cost-centers" => (650, 450),
        "units" or "warehouses" => (650, 450),
        "periods" => (680, 450),
        "posting-management" => (820, 520),
        "financial-reports" => (760, 520),
        "executive-dashboard" => (1020, 680),
        "general-journal" => (780, 500),
        _ => (780, 500),
    };
}
