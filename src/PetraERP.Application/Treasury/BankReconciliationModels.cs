namespace PetraERP.Application.Treasury;

public static class BankReconciliationStatus
{
    public const string Draft = "Draft";
    public const string Completed = "Completed";
}

/// <summary>One posted line of the book account. <c>Amount</c> is signed in base currency (debit positive).</summary>
public sealed record BankReconciliationLineDto(Guid JournalLineId, Guid JournalId, DateOnly AccountingDate, long? PostingSequence,
    string Description, decimal Amount, bool Cleared);

/// <summary>
/// Reconciliation state. <c>Difference</c> = statement balance - (balance cleared by earlier completed reconciliations + lines cleared here);
/// completion is allowed only when it is exactly zero.
/// </summary>
public sealed record BankReconciliationDto(Guid Id, Guid BranchId, Guid AccountId, string AccountCode, DateOnly StatementDate,
    decimal StatementBalance, decimal PriorClearedBalance, decimal ClearedInThisReconciliation, decimal Difference, decimal BookBalanceAtStatementDate,
    string Status, string VersionBase64, IReadOnlyList<BankReconciliationLineDto> Lines);

public sealed record BankReconciliationSummaryDto(Guid Id, Guid AccountId, string AccountCode, DateOnly StatementDate,
    decimal StatementBalance, decimal Difference, string Status);

public interface IBankReconciliationService
{
    Task<BankReconciliationDto> StartAsync(Guid companyId, Guid fiscalYearId, Guid branchId, Guid actorId, Guid accountId, DateOnly statementDate,
        decimal statementBalance, CancellationToken cancellationToken);

    Task<BankReconciliationDto> GetAsync(Guid companyId, Guid branchId, Guid reconciliationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BankReconciliationSummaryDto>> ListAsync(Guid companyId, Guid branchId, Guid? accountId, CancellationToken cancellationToken);

    /// <summary>Marks or unmarks lines as cleared on a draft reconciliation. All ids must be eligible or nothing changes.</summary>
    Task<BankReconciliationDto> SetClearedAsync(Guid companyId, Guid fiscalYearId, Guid branchId, Guid actorId, Guid reconciliationId,
        IReadOnlyCollection<Guid> journalLineIds, bool cleared, string expectedVersion, CancellationToken cancellationToken);

    Task<BankReconciliationDto> CompleteAsync(Guid companyId, Guid fiscalYearId, Guid branchId, Guid actorId, Guid reconciliationId,
        string expectedVersion, CancellationToken cancellationToken);

    /// <summary>Discards a draft reconciliation and releases its cleared lines. Completed reconciliations cannot be deleted.</summary>
    Task DiscardAsync(Guid companyId, Guid fiscalYearId, Guid branchId, Guid actorId, Guid reconciliationId, string expectedVersion,
        CancellationToken cancellationToken);
}
