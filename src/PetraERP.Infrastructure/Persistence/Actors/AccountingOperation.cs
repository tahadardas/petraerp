using PetraERP.Application.Common;
using System.Linq;
using PetraERP.Application.Security;

namespace PetraERP.Infrastructure.Persistence.Actors;

public enum AccountingOperation
{
    ProvisionWorkspace = 1,
    ManageAccounts, ManageCostCenters, ManagePeriods, ReopenPeriods,
    ManageJournals, ApproveJournal, PostJournal, ReverseJournal,
    ManagePatterns, PublishPatterns, ManageParties, ManageItems, ManageWarehouses,
    ManageTransfers, ManageAdjustments,
    ManageSalesInvoices, ApproveSalesInvoice, PostSalesInvoice, ReverseSalesInvoice,
    ManagePurchaseInvoices, ApprovePurchaseInvoice, PostPurchaseInvoice, ReturnPurchaseInvoice,
    ManageTreasuryVouchers, ApproveTreasuryVoucher, PostTreasuryVoucher, ReverseTreasuryVoucher, AllocateTreasuryVoucher,
    ExecuteFxRevaluation,
    ManageBankReconciliations,
    ManagePostingBatches,
    ManageCompanyOptions, RequestNegativeIssue, ReviewNegativeIssue,
    ManageBranches,
    RequestDocumentNumberChange, ApproveDocumentNumberChange,
}

/// <summary>The complete allowlist of annual write operations; read and security permissions cannot authorize writes.</summary>
public static class AccountingOperationPolicy
{
    public static string Permission(AccountingOperation operation) => operation switch
    {
        AccountingOperation.ProvisionWorkspace => PermissionCatalog.DatabaseManage,
        AccountingOperation.ManageAccounts => PermissionCatalog.AccountsManage,
        AccountingOperation.ManageCostCenters => PermissionCatalog.CostCentersManage,
        AccountingOperation.ManagePeriods => PermissionCatalog.PeriodsManage,
        AccountingOperation.ReopenPeriods => PermissionCatalog.PeriodsReopen,
        AccountingOperation.ManageJournals => PermissionCatalog.JournalsManage,
        AccountingOperation.ApproveJournal => PermissionCatalog.JournalsApprove,
        AccountingOperation.PostJournal => PermissionCatalog.JournalsPost,
        AccountingOperation.ReverseJournal => PermissionCatalog.JournalsReverse,
        AccountingOperation.ManagePatterns => PermissionCatalog.PatternsManage,
        AccountingOperation.PublishPatterns => PermissionCatalog.PatternsPublish,
        AccountingOperation.ManageParties => PermissionCatalog.PartiesManage,
        AccountingOperation.ManageItems => PermissionCatalog.InventoryItemsManage,
        AccountingOperation.ManageWarehouses => PermissionCatalog.InventoryWarehousesManage,
        AccountingOperation.ManageTransfers => PermissionCatalog.InventoryTransfersManage,
        AccountingOperation.ManageAdjustments => PermissionCatalog.InventoryAdjustmentsManage,
        AccountingOperation.ManageSalesInvoices => PermissionCatalog.SalesInvoicesManage,
        AccountingOperation.ApproveSalesInvoice => PermissionCatalog.SalesInvoicesApprove,
        AccountingOperation.PostSalesInvoice => PermissionCatalog.SalesInvoicesPost,
        AccountingOperation.ReverseSalesInvoice => PermissionCatalog.SalesInvoicesReverse,
        AccountingOperation.ManagePurchaseInvoices => PermissionCatalog.PurchaseInvoicesManage,
        AccountingOperation.ApprovePurchaseInvoice => PermissionCatalog.PurchaseInvoicesApprove,
        AccountingOperation.PostPurchaseInvoice => PermissionCatalog.PurchaseInvoicesPost,
        AccountingOperation.ReturnPurchaseInvoice => PermissionCatalog.PurchaseInvoicesReturn,
        AccountingOperation.ManageTreasuryVouchers => PermissionCatalog.TreasuryVouchersManage,
        AccountingOperation.ApproveTreasuryVoucher => PermissionCatalog.TreasuryVouchersApprove,
        AccountingOperation.PostTreasuryVoucher => PermissionCatalog.TreasuryVouchersPost,
        AccountingOperation.ReverseTreasuryVoucher => PermissionCatalog.TreasuryVouchersReverse,
        AccountingOperation.AllocateTreasuryVoucher => PermissionCatalog.TreasuryVouchersAllocate,
        AccountingOperation.ExecuteFxRevaluation => PermissionCatalog.FxRevaluationExecute,
        AccountingOperation.ManagePostingBatches => PermissionCatalog.PostingBatchesManage,
        AccountingOperation.ManageCompanyOptions => PermissionCatalog.CompanyOptionsManage,
        AccountingOperation.ManageBankReconciliations => PermissionCatalog.BankReconciliationsManage,
        AccountingOperation.ManageBranches => PermissionCatalog.BranchesManage,
        AccountingOperation.RequestDocumentNumberChange => PermissionCatalog.DocumentNumbersRequest,
        AccountingOperation.ApproveDocumentNumberChange => PermissionCatalog.DocumentNumbersApprove,
        AccountingOperation.RequestNegativeIssue => PermissionCatalog.InventoryNegativeRequest,
        AccountingOperation.ReviewNegativeIssue => PermissionCatalog.InventoryNegativeApprove,
        _ => throw Denied(),
    };

    public static AccountingOperation FromPermission(string permission)
    {
        if (TryFromPermission(permission, out var operation)) return operation;
        throw Denied();
    }

    public static bool TryFromPermission(string permission, out AccountingOperation operation)
    {
        foreach (var candidate in Enum.GetValues<AccountingOperation>())
            if (string.Equals(Permission(candidate), permission, StringComparison.Ordinal))
            {
                operation = candidate;
                return true;
            }
        operation = default;
        return false;
    }

    // An endpoint may require several permissions (a return needs both Reverse and Post grants). The governing
    // write operation is the strongest composed operation: a correction (reverse/return) outranks a post, which
    // outranks approve, which outranks plain management. Reads map to no operation and bind no write scope.
    public static AccountingOperation? ResolveEndpointWrite(IReadOnlyList<AccountingOperation> operations)
    {
        if (operations.Count == 0) return null;
        static int Rank(AccountingOperation op) => op switch
        {
            AccountingOperation.ReverseJournal or AccountingOperation.ReverseSalesInvoice
                or AccountingOperation.ReturnPurchaseInvoice or AccountingOperation.ReverseTreasuryVoucher => 0,
            AccountingOperation.PostJournal or AccountingOperation.PostSalesInvoice or AccountingOperation.PostPurchaseInvoice
                or AccountingOperation.PostTreasuryVoucher or AccountingOperation.AllocateTreasuryVoucher
                or AccountingOperation.ManagePostingBatches => 1,
            AccountingOperation.ApproveJournal or AccountingOperation.ApproveSalesInvoice
                or AccountingOperation.ApprovePurchaseInvoice or AccountingOperation.ApproveTreasuryVoucher => 2,
            _ => 3,
        };
        return operations.OrderBy(Rank).ThenBy(static op => (int)op).First();
    }

    internal static BusinessRuleException Denied() => new("accounting.operation_denied",
        "The bound accounting operation does not authorize this financial write.");
}
