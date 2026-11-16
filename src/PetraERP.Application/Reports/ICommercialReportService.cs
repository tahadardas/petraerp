using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Domain.Inventory;

namespace PetraERP.Application.Reports;

public enum PartyStatementKind
{
    Customer = 1,
    Supplier = 2,
}

public enum SalesAnalysisGrouping
{
    Pattern = 1,
    Customer = 2,
    Item = 3,
}

public enum PurchaseAnalysisGrouping
{
    Pattern = 1,
    Supplier = 2,
    Item = 3,
}

public sealed record StatementAccountLine(Guid AccountId, string Code, string NameArabic, string? NameEnglish, AccountTypeCode Type, decimal Amount);

public sealed record IncomeStatementResult(DateOnly From, DateOnly To, string BaseCurrency, IReadOnlyList<StatementAccountLine> Lines,
    decimal TotalRevenue, decimal TotalExpenses, decimal NetIncome);

/// <summary>Balanced is true when assets equal liabilities + equity + unclosed current earnings (a GL integrity reconciliation).</summary>
public sealed record BalanceSheetResult(DateOnly AsOf, string BaseCurrency, IReadOnlyList<StatementAccountLine> Lines, decimal TotalAssets,
    decimal TotalLiabilities, decimal TotalEquity, decimal CurrentEarnings, bool Balanced);

public sealed record PartyStatementLine(DateTimeOffset DateUtc, string DocumentType, Guid DocumentId, string? DocumentNumber, string Description,
    decimal Debit, decimal Credit, decimal Balance);

/// <summary>Amounts are in the statement currency. A positive balance is owed by the customer / owed to the supplier.</summary>
public sealed record PartyStatementResult(Guid PartyId, string PartyCode, string PartyName, PartyStatementKind Kind, string CurrencyCode,
    DateOnly From, DateOnly To, decimal Opening, IReadOnlyList<PartyStatementLine> Lines, decimal TotalDebit, decimal TotalCredit, decimal Closing,
    bool Truncated);

public sealed record AgingDocument(Guid PartyId, Guid InvoiceId, string? InvoiceNumber, DateOnly InvoiceDate, DateOnly DueDate, int DaysOverdue,
    decimal Outstanding);

public sealed record AgingRow(Guid PartyId, string PartyCode, string PartyName, decimal NotDue, decimal Days1To30, decimal Days31To60,
    decimal Days61To90, decimal Over90, decimal Advance, decimal Net);

/// <summary>
/// Customer and supplier aging use actual posted applications effective at the reporting cut-off.
/// Unapplied money is shown separately as an advance; no inferred FIFO settlement changes invoice balances.
/// </summary>
public sealed record AgingResult(PartyStatementKind Kind, DateOnly AsOf, string CurrencyCode, IReadOnlyList<AgingRow> Rows,
    IReadOnlyList<AgingDocument> Documents, AgingRow Totals);

public sealed record SalesAnalysisRow(Guid? KeyId, string KeyCode, string KeyName, string? UnitCode, decimal Quantity, int DocumentCount,
    decimal NetBase, decimal AdditionsBase, decimal TaxBase, decimal TotalBase);

/// <summary>
/// Document revenue (net + additions, base currency) is reconciled with revenue-type GL lines of the same posted journals. The
/// reconciliation is null when an item filter is applied, because a journal also carries the invoice's other items.
/// </summary>
public sealed record SalesAnalysisResult(DateOnly From, DateOnly To, SalesAnalysisGrouping Grouping, string BaseCurrency, IReadOnlyList<SalesAnalysisRow> Rows,
    decimal NetBase, decimal AdditionsBase, decimal TaxBase, decimal TotalBase, decimal? GlRevenueBase, decimal? RevenueDifference);

public sealed record PurchaseAnalysisRow(Guid? KeyId, string KeyCode, string KeyName, string? BaseUnitCode, decimal BaseQuantity, int DocumentCount,
    decimal NetBase, decimal TaxBase, decimal TotalBase, decimal StockCostBase);

public sealed record PurchaseAnalysisResult(DateOnly From, DateOnly To, PurchaseAnalysisGrouping Grouping, string BaseCurrency,
    IReadOnlyList<PurchaseAnalysisRow> Rows, decimal NetBase, decimal TaxBase, decimal TotalBase, decimal StockCostBase);

public sealed record InventoryValuationRow(Guid WarehouseId, string WarehouseCode, Guid ItemId, string ItemCode, string ItemName, string BaseUnitCode,
    decimal Quantity, decimal Value, decimal? AverageCost);

/// <summary>Valuation from authoritative movements, reconciled with the GL balance of every pattern-mapped Inventory account.</summary>
public sealed record InventoryValuationResult(DateOnly AsOf, string BaseCurrency, IReadOnlyList<InventoryValuationRow> Rows, decimal TotalValue,
    IReadOnlyList<Guid> InventoryAccountIds, decimal GlInventoryBalance, decimal Difference);

public sealed record InventoryMovementLine(DateTimeOffset DateUtc, StockMovementKind Kind, string DocumentType, Guid? DocumentId, string? DocumentNumber,
    Guid WarehouseId, string WarehouseCode, decimal Quantity, decimal Value, decimal BalanceQuantity, decimal BalanceValue);

public sealed record InventoryMovementReportResult(Guid ItemId, string ItemCode, string ItemName, string BaseUnitCode, Guid? WarehouseId, DateOnly From,
    DateOnly To, decimal OpeningQuantity, decimal OpeningValue, IReadOnlyList<InventoryMovementLine> Lines, decimal InQuantity, decimal OutQuantity,
    decimal ClosingQuantity, decimal ClosingValue, bool Truncated);

public sealed record CashAccountResult(Guid AccountId, string Code, string NameArabic, string Role);

public sealed record CashStatementLine(DateOnly Date, Guid JournalId, long? PostingSequence, string Description, string? SourceType, Guid? SourceId,
    string? SourceNumber, string TransactionCurrency, decimal TransactionDebit, decimal TransactionCredit, decimal BaseDebit, decimal BaseCredit,
    decimal BaseBalance);

public sealed record CashStatementResult(Guid AccountId, string AccountCode, string AccountName, DateOnly From, DateOnly To, string BaseCurrency,
    decimal OpeningBase, IReadOnlyList<CashStatementLine> Lines, decimal TotalDebit, decimal TotalCredit, decimal ClosingBase, bool Truncated);

/// <summary>
/// Read-only financial and commercial reports. GL statements read posted (and reversed) journal lines only; commercial reports read
/// posted documents and authoritative stock movements. Every query is limited to the route company and branch, except GL statements
/// with <see cref="LedgerReportScope.Consolidated"/>, which require a scope grant for every branch.
/// </summary>
public interface ICommercialReportService
{
    Task<IncomeStatementResult> GetIncomeStatementAsync(LedgerReportScope scope, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);

    Task<BalanceSheetResult> GetBalanceSheetAsync(LedgerReportScope scope, DateOnly asOf, CancellationToken cancellationToken = default);

    Task<PartyStatementResult> GetPartyStatementAsync(LedgerReportScope scope, Guid partyId, PartyStatementKind kind, string currencyCode, DateOnly fromDate,
        DateOnly toDate, CancellationToken cancellationToken = default);

    Task<AgingResult> GetAgingAsync(LedgerReportScope scope, PartyStatementKind kind, string currencyCode, DateOnly asOf,
        CancellationToken cancellationToken = default);

    Task<AgingResult> GetPartyAgingAsync(LedgerReportScope scope, Guid partyId, PartyStatementKind kind, string currencyCode, DateOnly asOf,
        CancellationToken cancellationToken = default);

    Task<SalesAnalysisResult> GetSalesAnalysisAsync(LedgerReportScope scope, DateOnly fromDate, DateOnly toDate, SalesAnalysisGrouping grouping,
        Guid? patternRevisionId, Guid? customerId, Guid? itemId, CancellationToken cancellationToken = default);

    Task<PurchaseAnalysisResult> GetPurchaseAnalysisAsync(LedgerReportScope scope, DateOnly fromDate, DateOnly toDate, PurchaseAnalysisGrouping grouping,
        Guid? patternRevisionId, Guid? supplierId, Guid? itemId, CancellationToken cancellationToken = default);

    Task<InventoryValuationResult> GetInventoryValuationAsync(LedgerReportScope scope, DateOnly asOf, Guid? warehouseId,
        CancellationToken cancellationToken = default);

    Task<InventoryMovementReportResult> GetInventoryMovementsAsync(LedgerReportScope scope, Guid itemId, Guid? warehouseId, DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CashAccountResult>> GetCashAccountsAsync(LedgerReportScope scope, CancellationToken cancellationToken = default);

    Task<CashStatementResult> GetCashStatementAsync(LedgerReportScope scope, Guid accountId, DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken = default);
}
