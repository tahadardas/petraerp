using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.DocumentNumbers;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger.OpeningBalances;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.CompanyOptions;
using PetraERP.Infrastructure.DocumentProcessing;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Parties.OpenItems;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Purchases;
using PetraERP.Infrastructure.Sales;
using PetraERP.Infrastructure.Treasury;

namespace PetraERP.Infrastructure.Persistence.Actors;

/// <summary>Persistence allowlist for the actual changed rows, including effects of composed document posting.</summary>
internal static class AccountingMutationPolicy
{
    internal static bool IsComposedPosting(AccountingOperation operation) => operation is
        AccountingOperation.PostSalesInvoice or AccountingOperation.ReverseSalesInvoice
        or AccountingOperation.PostPurchaseInvoice or AccountingOperation.ReturnPurchaseInvoice
        or AccountingOperation.PostTreasuryVoucher or AccountingOperation.ReverseTreasuryVoucher
        or AccountingOperation.AllocateTreasuryVoucher or AccountingOperation.ManageTransfers or AccountingOperation.ManageAdjustments
        or AccountingOperation.ExecuteFxRevaluation or AccountingOperation.ManagePostingBatches;

    internal static void Validate(EntityEntry entry, AccountingOperation operation, bool provisioning)
    {
        var type = entry.Metadata.ClrType;
        if (operation == AccountingOperation.ProvisionWorkspace)
        {
            if (!provisioning || entry.State != EntityState.Added || !InitialSeedTypes.Contains(type)) throw AccountingOperationPolicy.Denied();
            return;
        }
        if (provisioning || !AllowsType(operation, type)) throw AccountingOperationPolicy.Denied();
        if (entry.Entity is NegativeIssueApprovalRow approval)
        {
            if (operation == AccountingOperation.RequestNegativeIssue && (entry.State != EntityState.Added || approval.Status != "Pending" || approval.ReviewedByUserId is not null)
                || operation == AccountingOperation.ReviewNegativeIssue && (entry.State != EntityState.Modified
                    || (string?)entry.Property(nameof(NegativeIssueApprovalRow.Status)).OriginalValue != "Pending"
                    || approval.Status is not ("ApprovedPolicyOnly" or "Rejected")
                    || entry.Properties.Any(p=>p.IsModified && p.Metadata.Name is not ("Status" or "ReviewedByUserId" or "UpdatedAtUtc"))))
                throw AccountingOperationPolicy.Denied();
        }
        if (AppendOnlyTypes.Contains(type) && entry.State != EntityState.Added) throw AccountingOperationPolicy.Denied();
        // A completed bank reconciliation is history: it can neither change nor disappear.
        if (entry.Entity is Treasury.BankReconciliationRow && entry.State is EntityState.Modified or EntityState.Deleted
            && (string?)entry.Property(nameof(Treasury.BankReconciliationRow.Status)).OriginalValue != "Draft") throw AccountingOperationPolicy.Denied();
        ValidateDocumentNumberChange(entry, operation);
        if (entry.Entity is JournalEntryRow) ValidateJournal(entry, operation);
        if (entry.Entity is JournalAuditRow audit && !JournalStateAllowed(audit.NewState, operation)) throw AccountingOperationPolicy.Denied();
        if (entry.Entity is SalesInvoiceRow) ValidateDocument(entry, operation, AccountingOperation.ManageSalesInvoices,
            AccountingOperation.ApproveSalesInvoice, AccountingOperation.PostSalesInvoice, AccountingOperation.ReverseSalesInvoice);
        if (entry.Entity is PurchaseInvoiceRow) ValidateDocument(entry, operation, AccountingOperation.ManagePurchaseInvoices,
            AccountingOperation.ApprovePurchaseInvoice, AccountingOperation.PostPurchaseInvoice, AccountingOperation.ReturnPurchaseInvoice);
        if (entry.Entity is VoucherRow) ValidateDocument(entry, operation, AccountingOperation.ManageTreasuryVouchers,
            AccountingOperation.ApproveTreasuryVoucher, AccountingOperation.PostTreasuryVoucher, AccountingOperation.ReverseTreasuryVoucher);
        if (entry.Entity is AccountingPeriodRow && entry.State == EntityState.Modified)
        {
            var state = entry.Property(nameof(AccountingPeriodRow.State));
            if (Convert.ToInt32(state.CurrentValue, System.Globalization.CultureInfo.InvariantCulture)
                < Convert.ToInt32(state.OriginalValue, System.Globalization.CultureInfo.InvariantCulture)
                && operation != AccountingOperation.ReopenPeriods) throw AccountingOperationPolicy.Denied();
        }
    }

    internal static bool AllowsType(AccountingOperation operation, Type type)
    {
        if (LedgerTypes.Contains(type)) return operation is AccountingOperation.ManageJournals or AccountingOperation.ApproveJournal
            or AccountingOperation.PostJournal or AccountingOperation.ReverseJournal || IsComposedPosting(operation);
        if (type == typeof(PostingRequestRow) || type == typeof(PostingSequenceRow))
            return operation is AccountingOperation.PostJournal or AccountingOperation.ReverseJournal || IsComposedPosting(operation);
        if (type == typeof(StockMovementRow) || type == typeof(InventoryCostLayerRow) || type == typeof(InventoryCostAllocationRow)) return IsComposedPosting(operation);
        if (type == typeof(Currencies.FxRevaluationRequestRow)) return operation == AccountingOperation.ExecuteFxRevaluation;
        if (type == typeof(PatternNumberSequenceRow) || type == typeof(InventoryDocumentSequence)) return IsComposedPosting(operation);
        return operation switch
        {
            AccountingOperation.ManageBankReconciliations => type == typeof(Treasury.BankReconciliationRow) || type == typeof(Treasury.BankReconciliationLineRow) || type == typeof(CompanyPolicyAuditRow),
            AccountingOperation.ManageCompanyOptions => type == typeof(CompanyPolicyRow) || type == typeof(CompanyPolicyAuditRow),
            AccountingOperation.RequestNegativeIssue or AccountingOperation.ReviewNegativeIssue => type == typeof(NegativeIssueApprovalRow) || type == typeof(CompanyPolicyAuditRow),
            AccountingOperation.ManageAccounts => AccountTypes.Contains(type) || CurrencyTypes.Contains(type),
            AccountingOperation.ManageCostCenters => CostCenterTypes.Contains(type),
            AccountingOperation.ManagePeriods or AccountingOperation.ReopenPeriods => CalendarTypes.Contains(type),
            AccountingOperation.ManagePatterns or AccountingOperation.PublishPatterns => PatternTypes.Contains(type),
            AccountingOperation.ManageParties => type == typeof(PartyRow) || type == typeof(PartyPolicyAuditRow) || type == typeof(OpenItemRow),
            AccountingOperation.ManageItems => ItemTypes.Contains(type),
            AccountingOperation.RequestDocumentNumberChange => type == typeof(DocumentNumberChangeRow),
            AccountingOperation.ApproveDocumentNumberChange => type == typeof(DocumentNumberChangeRow) || type == typeof(DocumentNumberAuditRow)
                || type == typeof(SalesInvoiceRow) || type == typeof(PurchaseInvoiceRow) || type == typeof(VoucherRow),
            AccountingOperation.ManageBranches => type == typeof(BranchRow) || type == typeof(BranchAccountLinkRow) || type == typeof(BranchAuditRow),
            AccountingOperation.ManageWarehouses => type == typeof(WarehouseRow),
            AccountingOperation.ManageTransfers => type == typeof(StockTransferRow) || type == typeof(StockTransferLineRow)
                || type == typeof(InventoryDocumentRow) || type == typeof(InventoryDocumentLineRow),
            AccountingOperation.ManageAdjustments => type == typeof(StockAdjustmentRow) || type == typeof(StockAdjustmentLineRow)
                || type == typeof(InventoryDocumentRow) || type == typeof(InventoryDocumentLineRow),
            AccountingOperation.ManageSalesInvoices or AccountingOperation.ApproveSalesInvoice or AccountingOperation.PostSalesInvoice
                or AccountingOperation.ReverseSalesInvoice => SalesTypes.Contains(type),
            AccountingOperation.ManagePurchaseInvoices or AccountingOperation.ApprovePurchaseInvoice or AccountingOperation.PostPurchaseInvoice
                or AccountingOperation.ReturnPurchaseInvoice => PurchaseTypes.Contains(type),
            AccountingOperation.ManageTreasuryVouchers or AccountingOperation.ApproveTreasuryVoucher or AccountingOperation.PostTreasuryVoucher
                or AccountingOperation.ReverseTreasuryVoucher or AccountingOperation.AllocateTreasuryVoucher => TreasuryTypes.Contains(type),
            AccountingOperation.ManagePostingBatches => SalesTypes.Contains(type) || PurchaseTypes.Contains(type)
                || TreasuryTypes.Contains(type) || LedgerTypes.Contains(type)
                || type == typeof(StockMovementRow) || type == typeof(StockTransferRow) || type == typeof(StockTransferLineRow)
                || type == typeof(StockAdjustmentRow) || type == typeof(StockAdjustmentLineRow)
                || type == typeof(InventoryDocumentRow) || type == typeof(InventoryDocumentLineRow)
                || type == typeof(PatternNumberSequenceRow) || type == typeof(InventoryDocumentSequence)
                || type == typeof(BulkPostingBatchRow) || type == typeof(BulkPostingItemRow)
                || type == typeof(PostingRequestRow) || type == typeof(PostingSequenceRow),
            _ => false,
        };
    }

    private static bool JournalStateAllowed(byte state, AccountingOperation operation) => operation switch
    {
        AccountingOperation.ManageJournals => state is (byte)JournalEntryState.Draft or (byte)JournalEntryState.Validated,
        AccountingOperation.ApproveJournal => state == (byte)JournalEntryState.Approved,
        AccountingOperation.PostJournal => state == (byte)JournalEntryState.Posted,
        AccountingOperation.ReverseJournal => Enum.IsDefined((JournalEntryState)state),
        _ => IsComposedPosting(operation) && Enum.IsDefined((JournalEntryState)state),
    };

    private static void ValidateJournal(EntityEntry entry, AccountingOperation operation)
    {
        var state = entry.Property(nameof(JournalEntryRow.State));
        var current = (byte)state.CurrentValue!;
        if (!JournalStateAllowed(current, operation)) throw AccountingOperationPolicy.Denied();
        if (entry.State == EntityState.Added && operation is AccountingOperation.ApproveJournal or AccountingOperation.PostJournal)
            throw AccountingOperationPolicy.Denied();
        if (entry.State != EntityState.Modified) return;
        var original = (byte)state.OriginalValue!;
        if (operation == AccountingOperation.ApproveJournal && original != (byte)JournalEntryState.Validated
            || operation == AccountingOperation.PostJournal && original != (byte)JournalEntryState.Approved)
            throw AccountingOperationPolicy.Denied();
        if (original >= (byte)JournalEntryState.Posted)
        {
            if (operation != AccountingOperation.ReverseJournal && !IsComposedPosting(operation)) throw AccountingOperationPolicy.Denied();
            if (entry.Properties.Any(p => p.IsModified && !JournalReversalFields.Contains(p.Metadata.Name))) throw AccountingOperationPolicy.Denied();
        }
    }

    /// <summary>
    /// Renumbering may rewrite only the number text of an existing document, and a request moves only Pending -> Applied
    /// (approve) or is created Pending (request). Nothing else about a document can change under these operations.
    /// </summary>
    private static void ValidateDocumentNumberChange(EntityEntry entry, AccountingOperation operation)
    {
        var type = entry.Metadata.ClrType;
        var isDocument = entry.Entity is SalesInvoiceRow or PurchaseInvoiceRow or VoucherRow;
        if (isDocument && operation == AccountingOperation.ApproveDocumentNumberChange)
        {
            if (entry.State != EntityState.Modified) throw AccountingOperationPolicy.Denied();
            var allowed = entry.Entity is VoucherRow ? nameof(VoucherRow.VoucherNumber) : nameof(SalesInvoiceRow.InvoiceNumber);
            if (entry.Properties.Any(p => p.IsModified && p.Metadata.Name != allowed && p.Metadata.Name != "UpdatedAtUtc")) throw AccountingOperationPolicy.Denied();
            return;
        }
        if (type != typeof(DocumentNumberChangeRow)) return;
        var row = (DocumentNumberChangeRow)entry.Entity;
        if (operation == AccountingOperation.RequestDocumentNumberChange)
        {
            // Request creates a Pending row; the requester may also cancel their own pending request.
            var ok = entry.State == EntityState.Added && row.State == 1
                || entry.State == EntityState.Modified && (byte)entry.Property(nameof(DocumentNumberChangeRow.State)).OriginalValue! == 1 && row.State == 4;
            if (!ok) throw AccountingOperationPolicy.Denied();
        }
        else if (operation == AccountingOperation.ApproveDocumentNumberChange)
        {
            if (entry.State != EntityState.Modified || (byte)entry.Property(nameof(DocumentNumberChangeRow.State)).OriginalValue! != 1 || row.State is not (2 or 3))
                throw AccountingOperationPolicy.Denied();
            if (entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not (nameof(DocumentNumberChangeRow.State) or nameof(DocumentNumberChangeRow.DecidedByUserId)
                or nameof(DocumentNumberChangeRow.DecidedAtUtc) or nameof(DocumentNumberChangeRow.DecisionReason) or "UpdatedAtUtc")))
                throw AccountingOperationPolicy.Denied();
        }
    }

    private static void ValidateDocument(EntityEntry entry, AccountingOperation operation, AccountingOperation manage,
        AccountingOperation approve, AccountingOperation post, AccountingOperation reverse)
    {
        var status = entry.Property("Status");
        var current = Convert.ToInt32(status.CurrentValue, System.Globalization.CultureInfo.InvariantCulture);
        var original = Convert.ToInt32(status.OriginalValue, System.Globalization.CultureInfo.InvariantCulture);
        // Bulk posting drives the same document through its own approval (draft -> approved) and posting
        // (approved -> posted) transitions. Every permission is checked centrally per document.
        if (operation == AccountingOperation.ManagePostingBatches)
        {
            if (entry.State != EntityState.Modified || original is not (0 or 1) || current is not (1 or 2) || current < original)
                throw AccountingOperationPolicy.Denied();
            return;
        }
        // Sales and purchase posting services accept a draft when neither the pattern nor the company policy
        // requires approval, so the composed post operation must allow the same direct draft -> posted transition.
        if (operation == manage && (current != 0 && current != 4 || entry.State != EntityState.Added && original != 0)
            || operation == approve && (entry.State != EntityState.Modified || original != 0 || current != 1)
            || operation == post && (entry.State != EntityState.Modified || original is not (0 or 1) || current != 2)
            || operation == reverse && (entry.State == EntityState.Deleted || current is not (2 or 3)))
            throw AccountingOperationPolicy.Denied();
    }

    private static readonly HashSet<Type> InitialSeedTypes = [typeof(CompanyRow), typeof(BranchRow), typeof(CurrencyRow),
        typeof(CompanyAdditionalCurrencyRow), typeof(FiscalYearRow), typeof(AccountingPeriodRow), typeof(AccountRow), typeof(WarehouseRow), typeof(CostCenterRow), typeof(PartyRow), typeof(UnitOfMeasureRow),
        typeof(ItemGroupRow), typeof(ItemRow), typeof(UnitConversionRow), typeof(ItemBarcodeRow), typeof(ItemPriceRow),
        typeof(DocumentPatternRow), typeof(PatternRevisionRow), typeof(PatternBranchGrantRow), typeof(CompanyPolicyRow), typeof(CompanyPolicyAuditRow),
        typeof(OpeningBalanceBatchRow), typeof(OpeningBalanceLineRow), typeof(OpenItemRow)];
    private static readonly HashSet<Type> AccountTypes = [typeof(AccountRow), typeof(AccountAuditRow), typeof(AccountAuditStateRow), typeof(AccountProfileRow)];
    private static readonly HashSet<Type> CurrencyTypes = [typeof(CurrencyRow), typeof(ExchangeRateRow), typeof(CurrencyAuditRow), typeof(CompanyAdditionalCurrencyRow), typeof(CompanyRow)];
    private static readonly HashSet<Type> CostCenterTypes = [typeof(CostCenterRow), typeof(CostCenterAuditRow), typeof(CostCenterAuditStateRow)];
    private static readonly HashSet<Type> CalendarTypes = [typeof(FiscalYearRow), typeof(AccountingPeriodRow), typeof(PeriodAuditRow)];
    private static readonly HashSet<Type> LedgerTypes = [typeof(JournalEntryRow), typeof(JournalLineRow), typeof(JournalAuditRow), typeof(JournalVoucherHeaderRow), typeof(OpeningBalanceBatchRow), typeof(OpeningBalanceLineRow)];
    private static readonly HashSet<Type> PatternTypes = [typeof(DocumentPatternRow), typeof(PatternRevisionRow), typeof(PatternBranchGrantRow)];
    private static readonly HashSet<Type> ItemTypes = [typeof(ItemRow), typeof(ItemGroupRow), typeof(UnitOfMeasureRow), typeof(UnitConversionRow), typeof(ItemBarcodeRow), typeof(ItemPriceRow)];
    private static readonly HashSet<Type> SalesTypes = [typeof(SalesInvoiceRow), typeof(SalesInvoiceLineRow), typeof(SalesInvoiceAdjustmentRow), typeof(SalesPostingRequestRow), typeof(SalesCreditAuditRow), typeof(DocumentRequestBindingRow)];
    private static readonly HashSet<Type> PurchaseTypes = [typeof(PurchaseInvoiceRow), typeof(PurchaseInvoiceLineRow), typeof(PurchaseInvoiceAdjustmentRow), typeof(DocumentRequestBindingRow)];
    private static readonly HashSet<Type> TreasuryTypes = [typeof(VoucherRow), typeof(VoucherLineRow), typeof(VoucherAllocationRow),
        typeof(VoucherAllocationPlanRow), typeof(SettlementBatchRow), typeof(SettlementApplicationRow), typeof(CommercialPaperRow), typeof(OpenItemRow), typeof(DocumentRequestBindingRow)];
    private static readonly HashSet<Type> AppendOnlyTypes = [typeof(AccountAuditRow), typeof(AccountAuditStateRow), typeof(CostCenterAuditRow),
        typeof(CostCenterAuditStateRow), typeof(JournalAuditRow), typeof(PeriodAuditRow), typeof(CurrencyAuditRow), typeof(ExchangeRateRow),
        typeof(CompanyPolicyAuditRow), typeof(PartyPolicyAuditRow), typeof(SalesCreditAuditRow), typeof(SettlementApplicationRow), typeof(StockMovementRow), typeof(DocumentRequestBindingRow), typeof(BranchAuditRow), typeof(DocumentNumberAuditRow), typeof(InventoryCostAllocationRow)];
    private static readonly HashSet<string> JournalReversalFields = [nameof(JournalEntryRow.State), nameof(JournalEntryRow.ReversedByEntryId), nameof(JournalEntryRow.UpdatedAtUtc)];
}
