namespace PetraERP.Contracts.Ledger;

/// <summary>Monetary values are invariant decimal strings in the company base currency (ADR-0002).</summary>
public sealed record GeneralJournalLineResponse
{
    public required Guid JournalId { get; init; }
    public required Guid LineId { get; init; }
    public required Guid BranchId { get; init; }
    public required DateOnly AccountingDate { get; init; }
    public long? PostingSequence { get; init; }
    public required JournalState State { get; init; }
    public required string JournalDescription { get; init; }
    public required string LineDescription { get; init; }
    public required Guid AccountId { get; init; }
    public required string AccountCode { get; init; }
    public required string AccountNameArabic { get; init; }
    public string? AccountNameEnglish { get; init; }
    public Guid? CostCenterId { get; init; }
    public string? CostCenterCode { get; init; }
    public required string TransactionCurrencyCode { get; init; }
    public required string TransactionDebit { get; init; }
    public required string TransactionCredit { get; init; }
    public required string BaseDebit { get; init; }
    public required string BaseCredit { get; init; }
    public Guid? ReversesEntryId { get; init; }
}

public sealed record GeneralJournalResponse
{
    public required IReadOnlyList<GeneralJournalLineResponse> Lines { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required long TotalCount { get; init; }
    public required string TotalBaseDebit { get; init; }
    public required string TotalBaseCredit { get; init; }
    public required string BaseCurrencyCode { get; init; }
}

public sealed record AccountLedgerLineResponse
{
    public required Guid JournalId { get; init; }
    public required Guid LineId { get; init; }
    public required Guid BranchId { get; init; }
    public required DateOnly AccountingDate { get; init; }
    public long? PostingSequence { get; init; }
    public required string JournalDescription { get; init; }
    public required string LineDescription { get; init; }
    public required string AccountCode { get; init; }
    public string? CostCenterCode { get; init; }
    public required string TransactionCurrencyCode { get; init; }
    public required string TransactionDebit { get; init; }
    public required string TransactionCredit { get; init; }
    public required string BaseDebit { get; init; }
    public required string BaseCredit { get; init; }
    /// <summary>Signed: positive is a debit balance, negative a credit balance.</summary>
    public required string RunningBalance { get; init; }
    public Guid? ReversesEntryId { get; init; }
}

public sealed record AccountLedgerResponse
{
    public required AccountResponse Account { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required string OpeningBalance { get; init; }
    public required string PeriodDebit { get; init; }
    public required string PeriodCredit { get; init; }
    public required string ClosingBalance { get; init; }
    public required IReadOnlyList<AccountLedgerLineResponse> Lines { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required long TotalCount { get; init; }
    public required int IncludedAccountCount { get; init; }
    /// <summary>Pass back as <c>cursor</c> to read the next page by key; null on the last page or when keyset paging is unavailable.</summary>
    public string? NextCursor { get; init; }
    /// <summary>Highest posting sequence this ledger snapshot includes; constant across the cursor chain.</summary>
    public long SnapshotPostingSequence { get; init; }
}

public sealed record TrialBalanceRowResponse
{
    public required Guid AccountId { get; init; }
    public Guid? ParentId { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required AccountType AccountType { get; init; }
    public required bool PostingAllowed { get; init; }
    public required int Level { get; init; }
    public required string OpeningDebit { get; init; }
    public required string OpeningCredit { get; init; }
    public required string PeriodDebit { get; init; }
    public required string PeriodCredit { get; init; }
    public required string EndingDebit { get; init; }
    public required string EndingCredit { get; init; }
    public DateOnly? LastDebitDate { get; init; }
    public DateOnly? LastCreditDate { get; init; }
}

public sealed record TrialBalanceResponse
{
    public DateOnly? From { get; init; }
    public required DateOnly To { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required IReadOnlyList<TrialBalanceRowResponse> Rows { get; init; }
    public required string TotalOpeningDebit { get; init; }
    public required string TotalOpeningCredit { get; init; }
    public required string TotalPeriodDebit { get; init; }
    public required string TotalPeriodCredit { get; init; }
    public required string TotalEndingDebit { get; init; }
    public required string TotalEndingCredit { get; init; }
    public required bool IsBalanced { get; init; }
    /// <summary>Set when the amounts are shown in another currency (every amount was divided by <see cref="DisplayRate"/>).</summary>
    public string? DisplayCurrencyCode { get; init; }
    public string DisplayRate { get; init; } = "1.00000000";
}
