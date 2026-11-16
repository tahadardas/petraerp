namespace PetraERP.Contracts.Reports;

public enum ProfessionalAccountCategory { Assets = 1, Liabilities = 2, Equity = 3, Revenue = 4, Expenses = 5 }
public enum ProfessionalReportKind { AccountLedger = 1, CustomerStatement = 2, SupplierStatement = 3, TrialBalance = 4, CombinedPartyStatement = 5, CurrencyBalance = 6 }
public enum ProfessionalReportSource { ManualJournal = 1, SalesInvoice = 2, SalesReturn = 3, PurchaseInvoice = 4, PurchaseReturn = 5, ReceiptVoucher = 6, PaymentVoucher = 7, InventoryDocument = 8, Settlement = 9, UnclassifiedJournal = 10 }
public enum ProfessionalReportField { Date = 1, PostingSequence = 2, AccountCode = 3, AccountName = 4, Description = 5, Source = 6, DocumentNumber = 7, Currency = 8, Debit = 9, Credit = 10, ActualBalance = 11, DisplayBalance = 12, Opening = 13, Closing = 14, CostCenter = 15, PartyCode = 16, PartyName = 17, OppositeAccount = 18, TransactionDebit = 19, TransactionCredit = 20, OpeningDebit = 21, OpeningCredit = 22, ClosingDebit = 23, ClosingCredit = 24, TransactionCurrency = 25 }
public enum ProfessionalReportGrouping { None = 0, Account = 1, Month = 2, Source = 3 }
public enum ProfessionalReportOrder { Date = 1, PostingSequence = 2, AccountCode = 3 }
public enum ProfessionalBalancePresentation { Actual = 1, ZeroAtPeriodStart = 2 }
public enum ProfessionalCurrencyBasis { Base = 1, Transaction = 2 }
public enum ProfessionalReportOption { DescriptionContains = 1, DescriptionNotContains = 2, PostingDate = 3, CreatedDate = 4, OppositeAccount = 5, CostCenter = 6, AccountLevel = 7, IncludeParents = 8, IncludePostingAccounts = 9, IncludeEmptyAccounts = 10, OnlyNonZero = 11, InvoiceLineDetails = 12, Aging = 13, CommercialPapers = 14, ExchangeEquivalence = 15, Monthly = 16 }
public enum ProfessionalFilterLogic { And = 1, Or = 2 }
public enum ProfessionalFilterOperator
{
    Equals = 1,
    NotEquals = 2,
    Contains = 3,
    DoesNotContain = 4,
    StartsWith = 5,
    EndsWith = 6,
    IsEmpty = 7,
    IsNotEmpty = 8,
    GreaterThan = 9,
    GreaterThanOrEqual = 10,
    LessThan = 11,
    LessThanOrEqual = 12,
    Between = 13,
    In = 14,
    NotIn = 15,
}

/// <summary>A typed condition evaluated only against the authorized report-row allowlist. Values are never SQL fragments.</summary>
public sealed record ProfessionalReportFilterCondition
{
    public required ProfessionalReportField Field { get; init; }
    public required ProfessionalFilterOperator Operator { get; init; }
    public string? Value { get; init; }
    public string? SecondValue { get; init; }
    public IReadOnlyList<string> Values { get; init; } = [];
}

/// <summary>Recursive AND/OR expression. The server limits depth, node count and collection sizes before evaluation.</summary>
public sealed record ProfessionalReportFilterGroup
{
    public ProfessionalFilterLogic Logic { get; init; } = ProfessionalFilterLogic.And;
    public IReadOnlyList<ProfessionalReportFilterCondition> Conditions { get; init; } = [];
    public IReadOnlyList<ProfessionalReportFilterGroup> Groups { get; init; } = [];
}

public sealed record ProfessionalReportRequest
{
    public required ProfessionalReportKind Kind { get; init; }
    public required Guid FiscalYearId { get; init; }
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
    public Guid? AccountId { get; init; }
    public Guid? PartyId { get; init; }
    public Guid? OppositeAccountId { get; init; }
    public Guid? CostCenterId { get; init; }
    public string? CurrencyCode { get; init; }
    public ProfessionalCurrencyBasis CurrencyBasis { get; init; } = ProfessionalCurrencyBasis.Base;
    /// <summary>Optional currency to present the result in; converted through the exchange-rate register (any enabled currency to any other).</summary>
    public string? DisplayCurrencyCode { get; init; }
    /// <summary>Date whose effective register rates are used for the conversion; defaults to the report end date.</summary>
    public DateOnly? RateDate { get; init; }
    public IReadOnlyList<ProfessionalReportSource> Sources { get; init; } = [];
    public IReadOnlyList<ProfessionalReportField> Fields { get; init; } = [];
    public ProfessionalReportGrouping Grouping { get; init; }
    public ProfessionalReportOrder Order { get; init; } = ProfessionalReportOrder.Date;
    public ProfessionalBalancePresentation BalancePresentation { get; init; } = ProfessionalBalancePresentation.Actual;
    public string? DescriptionContains { get; init; }
    public string? DescriptionNotContains { get; init; }
    public DateOnly? PostingFrom { get; init; }
    public DateOnly? PostingTo { get; init; }
    public DateOnly? CreatedFrom { get; init; }
    public DateOnly? CreatedTo { get; init; }
    public long? FromSequence { get; init; }
    public long? ToSequence { get; init; }
    public int? AccountLevel { get; init; }
    public ProfessionalAccountCategory? AccountCategory { get; init; }
    public bool IncludeParents { get; init; }
    public bool IncludePostingAccounts { get; init; } = true;
    public bool IncludeEmptyAccounts { get; init; }
    public bool OnlyNonZero { get; init; }
    public bool IncludeInvoiceDetails { get; init; }
    public bool IncludeAging { get; init; }
    public IReadOnlyList<Guid> PatternRevisionIds { get; init; } = [];
    public ProfessionalReportFilterGroup? AdvancedFilter { get; init; }
}

public sealed record ProfessionalReportCatalogResponse
{
    public required IReadOnlyList<ProfessionalReportDefinitionDto> Reports { get; init; }
    public required IReadOnlyList<ProfessionalFiscalYearDto> FiscalYears { get; init; }
    public required IReadOnlyList<ProfessionalPatternDto> Patterns { get; init; }
    public required int MaximumRows { get; init; }
    public required string DescriptionSearchScope { get; init; }
    /// <summary>Trial-balance categories the server accepts in <see cref="ProfessionalReportRequest.AccountCategory"/>.</summary>
    public required IReadOnlyList<ProfessionalAccountCategory> AccountCategories { get; init; }
}
public sealed record ProfessionalPatternDto(Guid RevisionId, string Code, string NameArabic, ProfessionalReportSource Source);
public sealed record ProfessionalFiscalYearDto(Guid Id, string Code, DateOnly From, DateOnly To);
public sealed record ProfessionalReportDefinitionDto
{
    public required ProfessionalReportKind Kind { get; init; }
    public required string NameArabic { get; init; }
    public required string NameEnglish { get; init; }
    public required IReadOnlyList<ProfessionalSourceDefinitionDto> Sources { get; init; }
    public required IReadOnlyList<ProfessionalFieldDefinitionDto> Fields { get; init; }
    public required IReadOnlyList<ProfessionalOptionDefinitionDto> Options { get; init; }
}
public sealed record ProfessionalSourceDefinitionDto(ProfessionalReportSource Source, string LabelArabic, bool Supported, string? Reason);
public sealed record ProfessionalFieldDefinitionDto(ProfessionalReportField Field, string LabelArabic, bool Supported, bool DefaultSelected, string? Reason);
public sealed record ProfessionalOptionDefinitionDto(ProfessionalReportOption Option, string LabelArabic, bool Supported, string? Reason);

/// <summary>Complete immutable result. Preview and all exports consume this same instance; no silent truncation.</summary>
public sealed record ProfessionalReportExecutionResponse
{
    public required Guid ExecutionId { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }
    public required Guid CompanyId { get; init; }
    public required Guid BranchId { get; init; }
    public required Guid FiscalYearId { get; init; }
    public required string CompanyName { get; init; }
    public required string BranchName { get; init; }
    public required string FiscalYearCode { get; init; }
    public required string CurrencyCode { get; init; }
    public required string SubjectCode { get; init; }
    public required string SubjectName { get; init; }
    public required string BalanceConvention { get; init; }
    public required ProfessionalReportRequest Parameters { get; init; }
    public required IReadOnlyList<ProfessionalReportField> SelectedFields { get; init; }
    public required IReadOnlyList<ProfessionalReportRowDto> Rows { get; init; }
    public required ProfessionalReportSummaryDto Summary { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required IReadOnlyList<ProfessionalInvoiceDetailDto> InvoiceDetails { get; init; }
    public ProfessionalAgingDto? Aging { get; init; }
}
public sealed record ProfessionalReportRowDto
{
    public required Guid RowId { get; init; }
    public Guid? JournalId { get; init; }
    public Guid? DocumentId { get; init; }
    public ProfessionalReportSource? Source { get; init; }
    public string? DocumentNumber { get; init; }
    public DateOnly? Date { get; init; }
    public long? PostingSequence { get; init; }
    public Guid? AccountId { get; init; }
    public required string AccountCode { get; init; }
    public required string AccountName { get; init; }
    public string? PartyCode { get; init; }
    public string? PartyName { get; init; }
    public string? CostCenterCode { get; init; }
    public string? OppositeAccountCode { get; init; }
    public required string Description { get; init; }
    public required string CurrencyCode { get; init; }
    public required string Debit { get; init; }
    public required string Credit { get; init; }
    public required string ActualBalance { get; init; }
    public required string DisplayBalance { get; init; }
    public required string Opening { get; init; }
    public required string OpeningDebit { get; init; }
    public required string OpeningCredit { get; init; }
    public required string ClosingDebit { get; init; }
    public required string ClosingCredit { get; init; }
    public required string Closing { get; init; }
    public required string TransactionDebit { get; init; }
    public required string TransactionCredit { get; init; }
    public required string TransactionCurrencyCode { get; init; }
    public required string GroupKey { get; init; }
    public required bool IsAggregate { get; init; }
    public required bool CanDrillThrough { get; init; }
}
public sealed record ProfessionalReportSummaryDto
{
    public required string ActualOpening { get; init; }
    public required string ActualPeriodDebit { get; init; }
    public required string ActualPeriodCredit { get; init; }
    public required string ActualClosing { get; init; }
    public required string FilteredDebit { get; init; }
    public required string FilteredCredit { get; init; }
    public required string DisplayClosing { get; init; }
    public required int RowCount { get; init; }
    public required string OpeningDebit { get; init; }
    public required string OpeningCredit { get; init; }
    public required string ClosingDebit { get; init; }
    public required string ClosingCredit { get; init; }
}



public sealed record ProfessionalInvoiceDetailDto(Guid InvoiceId, Guid LineId, ProfessionalReportSource Source, string? InvoiceNumber, string ItemCode, string ItemName, string UnitCode, string Quantity, string UnitPrice, string Discount, string Tax, string LineTotal, string CurrencyCode);
public sealed record ProfessionalAgingDto(DateOnly AsOf, string CurrencyCode, string NotDue, string Days1To30, string Days31To60, string Days61To90, string Over90, string Advance, string Net);



