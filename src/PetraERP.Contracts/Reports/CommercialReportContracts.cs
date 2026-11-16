using PetraERP.Contracts.Inventory;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Contracts.Reports;

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

/// <summary>One account in a financial statement; Amount is in base currency with the account type's natural sign.</summary>
public sealed record StatementAccountLineDto
{
    public Guid AccountId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public AccountType Type { get; init; }
    public string Amount { get; init; } = "0.0000";
}

public sealed record IncomeStatementResponse
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public string BaseCurrency { get; init; } = string.Empty;
    public IReadOnlyList<StatementAccountLineDto> Lines { get; init; } = [];
    public string TotalRevenue { get; init; } = "0.0000";
    public string TotalExpenses { get; init; } = "0.0000";
    public string NetIncome { get; init; } = "0.0000";
}

public sealed record BalanceSheetResponse
{
    public DateOnly AsOf { get; init; }
    public string BaseCurrency { get; init; } = string.Empty;
    public IReadOnlyList<StatementAccountLineDto> Lines { get; init; } = [];
    public string TotalAssets { get; init; } = "0.0000";
    public string TotalLiabilities { get; init; } = "0.0000";
    public string TotalEquity { get; init; } = "0.0000";
    public string CurrentEarnings { get; init; } = "0.0000";
    public bool Balanced { get; init; }
}

public sealed record PartyStatementLineDto
{
    public DateTimeOffset DateUtc { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public Guid DocumentId { get; init; }
    public string? DocumentNumber { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Debit { get; init; } = "0.0000";
    public string Credit { get; init; } = "0.0000";
    public string Balance { get; init; } = "0.0000";
}

public sealed record PartyStatementResponse
{
    public Guid PartyId { get; init; }
    public string PartyCode { get; init; } = string.Empty;
    public string PartyName { get; init; } = string.Empty;
    public PartyStatementKind Kind { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public string Opening { get; init; } = "0.0000";
    public IReadOnlyList<PartyStatementLineDto> Lines { get; init; } = [];
    public string TotalDebit { get; init; } = "0.0000";
    public string TotalCredit { get; init; } = "0.0000";
    public string Closing { get; init; } = "0.0000";
    public bool Truncated { get; init; }
}

public sealed record AgingDocumentDto
{
    public Guid PartyId { get; init; }
    public Guid InvoiceId { get; init; }
    public string? InvoiceNumber { get; init; }
    public DateOnly InvoiceDate { get; init; }
    public DateOnly DueDate { get; init; }
    public int DaysOverdue { get; init; }
    public string Outstanding { get; init; } = "0.0000";
}

public sealed record AgingRowDto
{
    public Guid PartyId { get; init; }
    public string PartyCode { get; init; } = string.Empty;
    public string PartyName { get; init; } = string.Empty;
    public string NotDue { get; init; } = "0.0000";
    public string Days1To30 { get; init; } = "0.0000";
    public string Days31To60 { get; init; } = "0.0000";
    public string Days61To90 { get; init; } = "0.0000";
    public string Over90 { get; init; } = "0.0000";
    public string Advance { get; init; } = "0.0000";
    public string Net { get; init; } = "0.0000";
}

public sealed record AgingResponse
{
    public PartyStatementKind Kind { get; init; }
    public DateOnly AsOf { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public IReadOnlyList<AgingRowDto> Rows { get; init; } = [];
    public IReadOnlyList<AgingDocumentDto> Documents { get; init; } = [];
    public AgingRowDto Totals { get; init; } = new();
}

public sealed record SalesAnalysisRowDto
{
    public Guid? KeyId { get; init; }
    public string KeyCode { get; init; } = string.Empty;
    public string KeyName { get; init; } = string.Empty;
    public string? UnitCode { get; init; }
    public string Quantity { get; init; } = "0.0000";
    public int DocumentCount { get; init; }
    public string NetBase { get; init; } = "0.0000";
    public string AdditionsBase { get; init; } = "0.0000";
    public string TaxBase { get; init; } = "0.0000";
    public string TotalBase { get; init; } = "0.0000";
}

public sealed record SalesAnalysisResponse
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public SalesAnalysisGrouping Grouping { get; init; }
    public string BaseCurrency { get; init; } = string.Empty;
    public IReadOnlyList<SalesAnalysisRowDto> Rows { get; init; } = [];
    public string NetBase { get; init; } = "0.0000";
    public string AdditionsBase { get; init; } = "0.0000";
    public string TaxBase { get; init; } = "0.0000";
    public string TotalBase { get; init; } = "0.0000";
    public string? GlRevenueBase { get; init; }
    public string? RevenueDifference { get; init; }
}

public sealed record PurchaseAnalysisRowDto
{
    public Guid? KeyId { get; init; }
    public string KeyCode { get; init; } = string.Empty;
    public string KeyName { get; init; } = string.Empty;
    public string? BaseUnitCode { get; init; }
    public string BaseQuantity { get; init; } = "0.0000";
    public int DocumentCount { get; init; }
    public string NetBase { get; init; } = "0.0000";
    public string TaxBase { get; init; } = "0.0000";
    public string TotalBase { get; init; } = "0.0000";
    public string StockCostBase { get; init; } = "0.0000";
}

public sealed record PurchaseAnalysisResponse
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public PurchaseAnalysisGrouping Grouping { get; init; }
    public string BaseCurrency { get; init; } = string.Empty;
    public IReadOnlyList<PurchaseAnalysisRowDto> Rows { get; init; } = [];
    public string NetBase { get; init; } = "0.0000";
    public string TaxBase { get; init; } = "0.0000";
    public string TotalBase { get; init; } = "0.0000";
    public string StockCostBase { get; init; } = "0.0000";
}

public sealed record InventoryValuationRowDto
{
    public Guid WarehouseId { get; init; }
    public string WarehouseCode { get; init; } = string.Empty;
    public Guid ItemId { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public string BaseUnitCode { get; init; } = string.Empty;
    public string Quantity { get; init; } = "0.0000";
    public string Value { get; init; } = "0.0000";
    public string? AverageCost { get; init; }
}

public sealed record InventoryValuationResponse
{
    public DateOnly AsOf { get; init; }
    public string BaseCurrency { get; init; } = string.Empty;
    public IReadOnlyList<InventoryValuationRowDto> Rows { get; init; } = [];
    public string TotalValue { get; init; } = "0.0000";
    public IReadOnlyList<Guid> InventoryAccountIds { get; init; } = [];
    public string GlInventoryBalance { get; init; } = "0.0000";
    public string Difference { get; init; } = "0.0000";
}

public sealed record InventoryMovementLineDto
{
    public DateTimeOffset DateUtc { get; init; }
    public StockMovementKind Kind { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public Guid? DocumentId { get; init; }
    public string? DocumentNumber { get; init; }
    public Guid WarehouseId { get; init; }
    public string WarehouseCode { get; init; } = string.Empty;
    public string Quantity { get; init; } = "0.0000";
    public string Value { get; init; } = "0.0000";
    public string BalanceQuantity { get; init; } = "0.0000";
    public string BalanceValue { get; init; } = "0.0000";
}

public sealed record InventoryMovementReportResponse
{
    public Guid ItemId { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public string BaseUnitCode { get; init; } = string.Empty;
    public Guid? WarehouseId { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public string OpeningQuantity { get; init; } = "0.0000";
    public string OpeningValue { get; init; } = "0.0000";
    public IReadOnlyList<InventoryMovementLineDto> Lines { get; init; } = [];
    public string InQuantity { get; init; } = "0.0000";
    public string OutQuantity { get; init; } = "0.0000";
    public string ClosingQuantity { get; init; } = "0.0000";
    public string ClosingValue { get; init; } = "0.0000";
    public bool Truncated { get; init; }
}

public sealed record CashAccountDto
{
    public Guid AccountId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
}

public sealed record CashStatementLineDto
{
    public DateOnly Date { get; init; }
    public Guid JournalId { get; init; }
    public long? PostingSequence { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? SourceType { get; init; }
    public Guid? SourceId { get; init; }
    public string? SourceNumber { get; init; }
    public string TransactionCurrency { get; init; } = string.Empty;
    public string TransactionDebit { get; init; } = "0.0000";
    public string TransactionCredit { get; init; } = "0.0000";
    public string BaseDebit { get; init; } = "0.0000";
    public string BaseCredit { get; init; } = "0.0000";
    public string BaseBalance { get; init; } = "0.0000";
}

public sealed record CashStatementResponse
{
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public string BaseCurrency { get; init; } = string.Empty;
    public string OpeningBase { get; init; } = "0.0000";
    public IReadOnlyList<CashStatementLineDto> Lines { get; init; } = [];
    public string TotalDebit { get; init; } = "0.0000";
    public string TotalCredit { get; init; } = "0.0000";
    public string ClosingBase { get; init; } = "0.0000";
    public bool Truncated { get; init; }
}
