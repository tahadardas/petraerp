namespace PetraERP.Contracts.Treasury;

public sealed record StartBankReconciliationRequest
{
    public required Guid AccountId { get; init; }
    public required DateOnly StatementDate { get; init; }
    /// <summary>Statement closing balance as invariant decimal text, debit-positive like the book account.</summary>
    public required string StatementBalance { get; init; }
}

public sealed record SetBankReconciliationLinesRequest
{
    public required IReadOnlyList<Guid> JournalLineIds { get; init; }
    public required bool Cleared { get; init; }
    public required string ExpectedVersion { get; init; }
}

public sealed record BankReconciliationVersionRequest
{
    public required string ExpectedVersion { get; init; }
}

public sealed record BankReconciliationLineResponse
{
    public required Guid JournalLineId { get; init; }
    public required Guid JournalId { get; init; }
    public required DateOnly AccountingDate { get; init; }
    public long? PostingSequence { get; init; }
    public required string Description { get; init; }
    public required string Amount { get; init; }
    public required bool Cleared { get; init; }
}

public sealed record BankReconciliationResponse
{
    public required Guid Id { get; init; }
    public required Guid BranchId { get; init; }
    public required Guid AccountId { get; init; }
    public required string AccountCode { get; init; }
    public required DateOnly StatementDate { get; init; }
    public required string StatementBalance { get; init; }
    public required string PriorClearedBalance { get; init; }
    public required string ClearedInThisReconciliation { get; init; }
    /// <summary>Statement balance minus everything cleared; completion requires exactly zero.</summary>
    public required string Difference { get; init; }
    public required string BookBalanceAtStatementDate { get; init; }
    public required string Status { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<BankReconciliationLineResponse> Lines { get; init; }
}

public sealed record BankReconciliationSummaryResponse
{
    public required Guid Id { get; init; }
    public required Guid AccountId { get; init; }
    public required string AccountCode { get; init; }
    public required DateOnly StatementDate { get; init; }
    public required string StatementBalance { get; init; }
    public required string Difference { get; init; }
    public required string Status { get; init; }
}
