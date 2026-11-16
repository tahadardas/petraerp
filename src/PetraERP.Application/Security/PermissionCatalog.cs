using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace PetraERP.Application.Security;

/// <summary>
/// The complete set of permissions that implemented server operations enforce.
/// A permission is added only together with the operation that checks it; the database
/// seed is generated from this catalog, so a new entry requires a reviewed migration.
/// </summary>
public static class PermissionCatalog
{
    public const string DatabaseManage = "System.Database.Manage";
    public const string CompanyOptionsManage = "System.CompanyOptions.Manage";
    public const string BranchesView = "System.Branches.View";
    public const string BranchesManage = "System.Branches.Manage";
    public const string DocumentNumbersView = "Documents.Numbers.View";
    public const string DocumentNumbersRequest = "Documents.Numbers.Request";
    public const string DocumentNumbersApprove = "Documents.Numbers.Approve";
    public const string InventoryNegativeRequest = "Inventory.Negative.RequestApproval";
    public const string InventoryNegativeApprove = "Inventory.Negative.Approve";
    public const string UsersView = "System.Users.View";
    public const string UsersManage = "System.Users.Manage";
    public const string RolesView = "System.Roles.View";
    public const string RolesManage = "System.Roles.Manage";
    public const string SecurityAuditView = "Audit.Security.View";
    public const string JournalsPost = "Ledger.Journals.Post";
    public const string JournalsReverse = "Ledger.Journals.Reverse";

    public const string AccountsView = "Ledger.Accounts.View";
    public const string JournalsView = "Ledger.Journals.View";
    public const string JournalsManage = "Ledger.Journals.Manage";
    public const string JournalsApprove = "Ledger.Journals.Approve";

    public const string AccountsManage = "Ledger.Accounts.Manage";
    public const string CostCentersView = "Ledger.CostCenters.View";
    public const string CostCentersManage = "Ledger.CostCenters.Manage";
    public const string PeriodsView = "Ledger.Periods.View";
    public const string PeriodsManage = "Ledger.Periods.Manage";
    public const string PeriodsReopen = "Ledger.Periods.Reopen";
    public const string ReportsView = "Ledger.Reports.View";

    public const string PatternsView = "Patterns.Documents.View";
    public const string PatternsManage = "Patterns.Documents.Manage";
    public const string PatternsPublish = "Patterns.Documents.Publish";
    public const string PatternsManualNumbering = "Patterns.Documents.ManualNumbering";

    public const string PartiesView = "Parties.Directory.View";
    public const string PartiesManage = "Parties.Directory.Manage";

    public const string InventoryItemsView = "Inventory.Items.View";
    public const string InventoryItemsManage = "Inventory.Items.Manage";
    public const string InventoryWarehousesView = "Inventory.Warehouses.View";
    public const string InventoryWarehousesManage = "Inventory.Warehouses.Manage";
    public const string InventoryMovementsView = "Inventory.Movements.View";
    public const string InventoryTransfersManage = "Inventory.Transfers.Manage";
    public const string InventoryAdjustmentsManage = "Inventory.Adjustments.Manage";

    public const string SalesInvoicesView = "Sales.Invoices.View";
    public const string SalesInvoicesManage = "Sales.Invoices.Manage";
    public const string SalesInvoicesApprove = "Sales.Invoices.Approve";
    public const string SalesInvoicesPost = "Sales.Invoices.Post";
    public const string SalesInvoicesReverse = "Sales.Invoices.Reverse";
    public const string SalesCreditOverride = "Sales.Credit.Override";

    public const string TreasuryVouchersView = "Treasury.Vouchers.View";
    public const string TreasuryVouchersManage = "Treasury.Vouchers.Manage";
    public const string TreasuryVouchersPost = "Treasury.Vouchers.Post";
    public const string TreasuryVouchersReverse = "Treasury.Vouchers.Reverse";
    public const string TreasuryVouchersApprove = "Treasury.Vouchers.Approve";
    public const string TreasuryVouchersAllocate = "Treasury.Vouchers.Allocate";
    public const string BankReconciliationsManage = "Treasury.BankReconciliations.Manage";

    public const string PurchaseInvoicesView = "Purchases.Invoices.View";
    public const string PurchaseInvoicesManage = "Purchases.Invoices.Manage";
    public const string PurchaseInvoicesApprove = "Purchases.Invoices.Approve";
    public const string PurchaseInvoicesPost = "Purchases.Invoices.Post";
    public const string PurchaseInvoicesReturn = "Purchases.Invoices.Return";

    public const string FxRevaluationView = "Currencies.FxRevaluation.View";
    public const string FxRevaluationExecute = "Currencies.FxRevaluation.Execute";

    public const string PostingBatchesManage = "Posting.Batches.Manage";

    private static readonly PermissionDefinition[] Definitions =
    [
        new(DatabaseManage, PermissionScope.Installation),
        new(AccountsView, PermissionScope.Branch),
        new(AccountsManage, PermissionScope.Company),
        new(CostCentersView, PermissionScope.Company),
        new(CostCentersManage, PermissionScope.Company),
        new(PeriodsView, PermissionScope.Company),
        new(PeriodsManage, PermissionScope.Company),
        new(PeriodsReopen, PermissionScope.Company),
        new(ReportsView, PermissionScope.Branch),
        new(JournalsApprove, PermissionScope.Branch),
        new(JournalsManage, PermissionScope.Branch),
        new(JournalsPost, PermissionScope.Branch),
        new(JournalsReverse, PermissionScope.Branch),
        new(JournalsView, PermissionScope.Branch),
        new(PatternsView, PermissionScope.Branch),
        new(PatternsManage, PermissionScope.Company),
        new(PatternsPublish, PermissionScope.Company),
        new(PatternsManualNumbering, PermissionScope.Company),
        new(CompanyOptionsManage, PermissionScope.Company),
        new(BranchesView, PermissionScope.Company),
        new(BranchesManage, PermissionScope.Company),
        new(DocumentNumbersView, PermissionScope.Branch),
        new(DocumentNumbersRequest, PermissionScope.Branch),
        new(DocumentNumbersApprove, PermissionScope.Branch),
        new(InventoryNegativeRequest, PermissionScope.Branch),
        new(InventoryNegativeApprove, PermissionScope.Branch),
        new(PartiesView, PermissionScope.Branch),
        new(PartiesManage, PermissionScope.Company),
        new(InventoryItemsView, PermissionScope.Branch),
        new(InventoryItemsManage, PermissionScope.Company),
        new(InventoryWarehousesView, PermissionScope.Branch),
        new(InventoryWarehousesManage, PermissionScope.Company),
        new(InventoryMovementsView, PermissionScope.Branch),
        new(InventoryTransfersManage, PermissionScope.Branch),
        new(InventoryAdjustmentsManage, PermissionScope.Branch),
        new(SalesInvoicesView, PermissionScope.Branch),
        new(SalesInvoicesManage, PermissionScope.Branch),
        new(SalesInvoicesApprove, PermissionScope.Branch),
        new(SalesInvoicesPost, PermissionScope.Branch),
        new(SalesInvoicesReverse, PermissionScope.Branch),
        new(SalesCreditOverride, PermissionScope.Branch),
        new(TreasuryVouchersView, PermissionScope.Branch),
        new(TreasuryVouchersManage, PermissionScope.Branch),
        new(BankReconciliationsManage, PermissionScope.Branch),
        new(TreasuryVouchersPost, PermissionScope.Branch),
        new(TreasuryVouchersReverse, PermissionScope.Branch),
        new(TreasuryVouchersApprove, PermissionScope.Branch),
        new(TreasuryVouchersAllocate, PermissionScope.Branch),
        new(PurchaseInvoicesView, PermissionScope.Branch),
        new(PurchaseInvoicesManage, PermissionScope.Branch),
        new(PurchaseInvoicesApprove, PermissionScope.Branch),
        new(PurchaseInvoicesPost, PermissionScope.Branch),
        new(PurchaseInvoicesReturn, PermissionScope.Branch),
        new(FxRevaluationView, PermissionScope.Branch),
        new(FxRevaluationExecute, PermissionScope.Branch),
        new(PostingBatchesManage, PermissionScope.Branch),
        new(SecurityAuditView, PermissionScope.Installation),
        new(RolesManage, PermissionScope.Installation),
        new(RolesView, PermissionScope.Installation),
        new(UsersManage, PermissionScope.Installation),
        new(UsersView, PermissionScope.Installation),
    ];

    private static readonly FrozenDictionary<string, PermissionDefinition> ByCode =
        Definitions.ToFrozenDictionary(static definition => definition.Code, StringComparer.Ordinal);

    /// <summary>All permissions in stable ordinal code order.</summary>
    public static IReadOnlyList<PermissionDefinition> All { get; } =
        Array.AsReadOnly(Definitions.OrderBy(static definition => definition.Code, StringComparer.Ordinal).ToArray());

    /// <summary>Permissions that together keep an installation administrable.</summary>
    public static IReadOnlyList<string> AdministrationCore { get; } = Array.AsReadOnly(new[] { UsersManage, RolesManage });

    public static bool TryGet(string code, [MaybeNullWhen(false)] out PermissionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(code);
        return ByCode.TryGetValue(code, out definition);
    }

    public static PermissionDefinition Get(string code) =>
        TryGet(code, out var definition)
            ? definition
            : throw new ArgumentException("Unknown permission code.", nameof(code));
}
