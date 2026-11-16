namespace PetraERP.Infrastructure.Persistence;

public static class AccountingSchema
{
    public const string HistoryTable = "__AccountingMigrationsHistory";
    public const string HistorySchema = "system";
    public const string InitialMigration = "20261024120000_IndependentAccountingInitial";
    public const string FxRevaluationRequestsMigration = "20261025060915_FxRevaluationRequests";
    public const string CompanyPoliciesMigration = "20261025120000_CompanyPolicies";
    public const string PurchaseApprovalStatesMigration = "20261025210000_PurchaseApprovalStates";
    public const string AccountingCoreCompletionMigration = "20261026120000_AccountingCoreCompletion";
    public const string MaterialCostCenterManagementMigration = "20261027090339_MaterialCostCenterManagement";
    public const string DocumentPostingModeMigration = "20261028121000_DocumentPostingMode";
    public const string BulkPostingBatchesMigration = "20261028130000_BulkPostingBatches";
    public const string BulkPostingRequesterActorMigration = "20261028140000_BulkPostingRequesterActor";
    public const string AuthorizedNegativeStockMigration = "20261028150000_AuthorizedNegativeStock";
    public const string DocumentRequestBindingsMigration = "20261028172402_DocumentRequestBindings";
    public const string CoreBranchesFifoCurrencyNumberingMigration = "20261029084522_CoreBranchesFifoCurrencyNumbering";
    public const string SalesInvoiceAdjustmentsMigration = "20261031150736_SalesInvoiceAdjustments";
    public const string PurchaseAdjustmentsAndCounterAccountsMigration = "20261031154835_PurchaseAdjustmentsAndCounterAccounts";
    public const string VoucherSourceDocumentMigration = "20261031180150_VoucherSourceDocument";
    public const string AmeenCardsMigration = "20261101074528_AmeenCards";
    public const string AmeenInvoiceHeaderMigration = "20261101081617_AmeenInvoiceHeader";
    public const string AmeenDailyVoucherMigration = "20261101090044_AmeenDailyVoucher";
    public const string PurchaseInvoiceReversalLinksMigration = "20261102054622_PurchaseInvoiceReversalLinks";
    public const string InvoiceAccountAndPartyParentsMigration = "20261112005607_InvoiceAccountAndPartyParents";
    public const string BankReconciliationMigration = "20261115071856_BankReconciliation";
    public const string LedgerCoveringIndexesMigration = "20261116112228_LedgerCoveringIndexes";
    public static readonly string[] ApprovedMigrations = [
        InitialMigration,
        FxRevaluationRequestsMigration,
        CompanyPoliciesMigration,
        PurchaseApprovalStatesMigration,
        AccountingCoreCompletionMigration,
        MaterialCostCenterManagementMigration,
        DocumentPostingModeMigration,
        BulkPostingBatchesMigration,
        BulkPostingRequesterActorMigration,
        AuthorizedNegativeStockMigration,
        DocumentRequestBindingsMigration,
        CoreBranchesFifoCurrencyNumberingMigration,
        SalesInvoiceAdjustmentsMigration,
        PurchaseAdjustmentsAndCounterAccountsMigration,
        VoucherSourceDocumentMigration,
        AmeenCardsMigration,
        AmeenInvoiceHeaderMigration,
        AmeenDailyVoucherMigration,
        PurchaseInvoiceReversalLinksMigration,
        InvoiceAccountAndPartyParentsMigration,
        BankReconciliationMigration,
        LedgerCoveringIndexesMigration
    ];
}
