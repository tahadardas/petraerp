using Microsoft.EntityFrameworkCore;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.DocumentNumbers;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger.OpeningBalances;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Parties.OpenItems;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Sales;

namespace PetraERP.Infrastructure.Persistence;

/// <summary>Financial EF sets shared by the independent annual context and explicitly constructed Legacy tooling.
/// This base declares no central identity sets and never chooses a database.</summary>
public abstract class FinancialDbContext(DbContextOptions options) : DbContext(options)
{
    // system schema
    internal DbSet<CurrencyRow> Currencies => Set<CurrencyRow>();
    internal DbSet<CompanyRow> Companies => Set<CompanyRow>();
    internal DbSet<BranchRow> Branches => Set<BranchRow>();
    internal DbSet<BranchAccountLinkRow> BranchAccountLinks => Set<BranchAccountLinkRow>();
    internal DbSet<BranchAuditRow> BranchAudit => Set<BranchAuditRow>();
    internal DbSet<DocumentNumberChangeRow> DocumentNumberChanges => Set<DocumentNumberChangeRow>();
    internal DbSet<DocumentNumberAuditRow> DocumentNumberAudit => Set<DocumentNumberAuditRow>();
    internal DbSet<CompanyAdditionalCurrencyRow> CompanyAdditionalCurrencies => Set<CompanyAdditionalCurrencyRow>();
    internal DbSet<ExchangeRateRow> ExchangeRates => Set<ExchangeRateRow>();
    internal DbSet<CurrencyAuditRow> CurrencyAudit => Set<CurrencyAuditRow>();
    internal DbSet<FxRevaluationRequestRow> FxRevaluationRequests => Set<FxRevaluationRequestRow>();

    // ledger schema — calendars
    internal DbSet<FiscalYearRow> FiscalYears => Set<FiscalYearRow>();
    internal DbSet<AccountingPeriodRow> AccountingPeriods => Set<AccountingPeriodRow>();
    internal DbSet<PeriodAuditRow> PeriodAudit => Set<PeriodAuditRow>();

    // ledger schema — chart of accounts
    internal DbSet<AccountRow> Accounts => Set<AccountRow>();
    internal DbSet<PetraERP.Infrastructure.GeneralLedger.AccountProfileRow> AccountProfiles => Set<PetraERP.Infrastructure.GeneralLedger.AccountProfileRow>();
    internal DbSet<CostCenterRow> CostCenters => Set<CostCenterRow>();
    internal DbSet<CostCenterAllocationRow> CostCenterAllocations => Set<CostCenterAllocationRow>();
    internal DbSet<CostCenterAuditRow> CostCenterAudit => Set<CostCenterAuditRow>();
    internal DbSet<CostCenterAuditStateRow> CostCenterAuditStates => Set<CostCenterAuditStateRow>();
    internal DbSet<AccountAuditRow> AccountAudit => Set<AccountAuditRow>();
    internal DbSet<AccountAuditStateRow> AccountAuditStates => Set<AccountAuditStateRow>();

    // ledger schema — journals
    internal DbSet<JournalEntryRow> JournalEntries => Set<JournalEntryRow>();
    internal DbSet<JournalLineRow> JournalLines => Set<JournalLineRow>();
    internal DbSet<JournalAuditRow> JournalAudit => Set<JournalAuditRow>();
    internal DbSet<JournalVoucherHeaderRow> JournalVoucherHeaders => Set<JournalVoucherHeaderRow>();

    // ledger schema — opening balances
    internal DbSet<OpeningBalanceBatchRow> OpeningBalanceBatches => Set<OpeningBalanceBatchRow>();
    internal DbSet<OpeningBalanceLineRow> OpeningBalanceLines => Set<OpeningBalanceLineRow>();

    // ledger schema — idempotency and numbering
    internal DbSet<PostingRequestRow> PostingRequests => Set<PostingRequestRow>();
    internal DbSet<PostingSequenceRow> PostingSequences => Set<PostingSequenceRow>();

    // patterns schema — document pattern engine
    internal DbSet<DocumentPatternRow> DocumentPatterns => Set<DocumentPatternRow>();
    internal DbSet<PatternRevisionRow> PatternRevisions => Set<PatternRevisionRow>();
    internal DbSet<PatternBranchGrantRow> PatternBranchGrants => Set<PatternBranchGrantRow>();
    internal DbSet<PatternNumberSequenceRow> PatternNumberSequences => Set<PatternNumberSequenceRow>();

    // parties schema
    internal DbSet<PartyRow> Parties => Set<PartyRow>();
    internal DbSet<PartyPolicyAuditRow> PartyPolicyAudit => Set<PartyPolicyAuditRow>();
    internal DbSet<OpenItemRow> OpenItems => Set<OpenItemRow>();

    // inventory schema
    internal DbSet<ItemGroupRow> ItemGroups => Set<ItemGroupRow>();
    internal DbSet<UnitOfMeasureRow> UnitsOfMeasure => Set<UnitOfMeasureRow>();
    internal DbSet<WarehouseRow> Warehouses => Set<WarehouseRow>();
    internal DbSet<ItemRow> Items => Set<ItemRow>();
    internal DbSet<UnitConversionRow> UnitConversions => Set<UnitConversionRow>();
    internal DbSet<ItemBarcodeRow> ItemBarcodes => Set<ItemBarcodeRow>();
    internal DbSet<ItemPriceRow> ItemPrices => Set<ItemPriceRow>();
    internal DbSet<StockMovementRow> StockMovements => Set<StockMovementRow>();
    internal DbSet<InventoryCostLayerRow> InventoryCostLayers => Set<InventoryCostLayerRow>();
    internal DbSet<InventoryCostAllocationRow> InventoryCostAllocations => Set<InventoryCostAllocationRow>();
    internal DbSet<StockTransferRow> StockTransfers => Set<StockTransferRow>();
    internal DbSet<StockTransferLineRow> StockTransferLines => Set<StockTransferLineRow>();
    internal DbSet<StockAdjustmentRow> StockAdjustments => Set<StockAdjustmentRow>();
    internal DbSet<StockAdjustmentLineRow> StockAdjustmentLines => Set<StockAdjustmentLineRow>();
    internal DbSet<InventoryDocumentRow> InventoryDocuments => Set<InventoryDocumentRow>();
    internal DbSet<InventoryDocumentLineRow> InventoryDocumentLines => Set<InventoryDocumentLineRow>();
    internal DbSet<ItemAccountMappingRow> ItemAccountMappings => Set<ItemAccountMappingRow>();

    // sales schema
    internal DbSet<SalesInvoiceRow> SalesInvoices => Set<SalesInvoiceRow>();
    internal DbSet<SalesInvoiceLineRow> SalesInvoiceLines => Set<SalesInvoiceLineRow>();
    internal DbSet<SalesInvoiceAdjustmentRow> SalesInvoiceAdjustments => Set<SalesInvoiceAdjustmentRow>();
    internal DbSet<PetraERP.Infrastructure.Purchases.PurchaseInvoiceAdjustmentRow> PurchaseInvoiceAdjustments => Set<PetraERP.Infrastructure.Purchases.PurchaseInvoiceAdjustmentRow>();
    internal DbSet<SalesPostingRequestRow> SalesPostingRequests => Set<SalesPostingRequestRow>();
    internal DbSet<Treasury.VoucherRow> Vouchers => Set<Treasury.VoucherRow>();
    internal DbSet<Treasury.BankReconciliationRow> BankReconciliations => Set<Treasury.BankReconciliationRow>();
    internal DbSet<Treasury.BankReconciliationLineRow> BankReconciliationLines => Set<Treasury.BankReconciliationLineRow>();
    internal DbSet<Treasury.CommercialPaperRow> CommercialPapers => Set<Treasury.CommercialPaperRow>();
    internal DbSet<Treasury.VoucherAllocationRow> VoucherAllocations => Set<Treasury.VoucherAllocationRow>();
    internal DbSet<Treasury.VoucherLineRow> VoucherLines => Set<Treasury.VoucherLineRow>();
    internal DbSet<Treasury.VoucherAllocationPlanRow> VoucherAllocationPlans => Set<Treasury.VoucherAllocationPlanRow>();
    internal DbSet<Treasury.SettlementBatchRow> SettlementBatches => Set<Treasury.SettlementBatchRow>();
    internal DbSet<Treasury.SettlementApplicationRow> SettlementApplications => Set<Treasury.SettlementApplicationRow>();
    internal DbSet<Sales.SalesCreditAuditRow> SalesCreditAudit => Set<Sales.SalesCreditAuditRow>();
    internal DbSet<Purchases.PurchaseInvoiceRow> PurchaseInvoices => Set<Purchases.PurchaseInvoiceRow>();
    internal DbSet<Purchases.PurchaseInvoiceLineRow> PurchaseInvoiceLines => Set<Purchases.PurchaseInvoiceLineRow>();

    // company options & inventory approvals
    internal DbSet<CompanyOptions.CompanyPolicyRow> CompanyPolicies => Set<CompanyOptions.CompanyPolicyRow>();
    internal DbSet<CompanyOptions.CompanyPolicyAuditRow> CompanyPolicyAudit => Set<CompanyOptions.CompanyPolicyAuditRow>();
    internal DbSet<CompanyOptions.NegativeIssueApprovalRow> NegativeIssueApprovals => Set<CompanyOptions.NegativeIssueApprovalRow>();


    private CentralDataContext? centralIdentity;
    internal CentralDataContext Identity => centralIdentity ?? (this is PetraDbContext legacy
        ? (CentralDataContext)legacy : throw new InvalidOperationException("Central identity authority has not been bound."));

    private NegativeStockAuthorization? negativeStockAuthorization;

    /// <summary>The one negative-stock exception granted to this unit of work, if any. Only the posting service that
    /// verified the company policy, actor grant and reason may set it.</summary>
    internal NegativeStockAuthorization? NegativeStockAuthorization => negativeStockAuthorization;

    internal void AuthorizeNegativeStock(Guid companyId, Guid branchId, string reason, Guid warehouseId, Guid itemId)
    {
        if (negativeStockAuthorization is null)
            negativeStockAuthorization = new NegativeStockAuthorization(companyId, branchId, reason);
        else if (negativeStockAuthorization.CompanyId != companyId || negativeStockAuthorization.BranchId != branchId
            || !string.Equals(negativeStockAuthorization.Reason, reason, StringComparison.Ordinal))
            throw new InvalidOperationException("A unit of work cannot mix negative-stock exceptions from different scopes or reasons.");
        negativeStockAuthorization.Include(warehouseId, itemId);
    }

    internal void BindIdentity(SystemDbContext system)
    {
        ArgumentNullException.ThrowIfNull(system);
        centralIdentity = new CentralDataContext(system);
    }

    internal async Task<Dictionary<Guid, Actors.VerifiedAccountingActor>> ActorNamesAsync(IEnumerable<Guid> actorIds, CancellationToken cancellationToken = default)
    {
        var ids = actorIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, Actors.VerifiedAccountingActor>();
        if (this is PetraDbContext petra)
        {
            var users = await petra.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new Actors.VerifiedAccountingActor(u.Id, u.DisplayNameArabic, u.DisplayNameEnglish))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return users.ToDictionary(u => u.Id);
        }
        if (Model.FindEntityType(typeof(Actors.ActorReferenceRow)) is not null)
        {
            var rows = await Set<Actors.ActorReferenceRow>().AsNoTracking()
                .Where(a => ids.Contains(a.Id))
                .Select(a => new Actors.VerifiedAccountingActor(a.Id, a.DisplayNameArabic, a.DisplayNameEnglish))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return rows.ToDictionary(a => a.Id);
        }
        return new Dictionary<Guid, Actors.VerifiedAccountingActor>();
    }
}
