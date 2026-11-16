namespace PetraERP.Application.InterCompany;

/// <summary>
/// Master record pairing one account of this company with the matching account of a counterparty company, in an agreed
/// currency. The counterparty's balance is expected to mirror this one: the signed sum of both sides must be zero.
/// </summary>
public sealed record InterCompanyLinkResult(
    Guid Id,
    Guid CompanyId,
    Guid CounterpartyCompanyId,
    string CounterpartyCompanyName,
    string AccountCode,
    string CounterpartyAccountCode,
    string CurrencyCode,
    bool IsActive,
    string? Note,
    DateTimeOffset UpdatedAtUtc);

public sealed record SaveInterCompanyLinkCommand(Guid CounterpartyCompanyId, string AccountCode, string CounterpartyAccountCode, string CurrencyCode, string? Note);

public sealed record InterCompanyMovement(DateOnly Date, Guid JournalId, string Description, decimal Amount);

public sealed record InterCompanySide(Guid CompanyId, string DatabaseName, bool AccountFound, decimal Balance, IReadOnlyList<InterCompanyMovement> RecentMovements);

/// <summary>One suggested pairing: this side's amount equals the negated counterparty amount (signed from this side).</summary>
public sealed record InterCompanyMatchPair(InterCompanyMovement ThisMovement, InterCompanyMovement CounterpartyMovement);

/// <summary>
/// Amount-based line matching over the posted lines of both sides. Pairs are suggestions computed on demand (one-to-one,
/// nearest date); they are not persisted settlements. <see cref="Truncated"/> is true when a side exceeded the line cap.
/// </summary>
public sealed record InterCompanyMatching(
    IReadOnlyList<InterCompanyMatchPair> Pairs,
    IReadOnlyList<InterCompanyMovement> UnmatchedThis,
    IReadOnlyList<InterCompanyMovement> UnmatchedCounterparty,
    bool Truncated);

/// <summary>
/// Reconciliation of one link at a date. Balances are debit-positive amounts in the link currency (transaction amounts) from
/// posted and reversed journals of the fiscal-year database that contains the date. <see cref="Difference"/> = this side +
/// counterparty side; zero means the two companies agree.
/// </summary>
public sealed record InterCompanyReconciliationResult(
    Guid LinkId,
    DateOnly AsOfDate,
    string CurrencyCode,
    InterCompanySide ThisSide,
    InterCompanySide CounterpartySide,
    decimal Difference,
    bool Matched,
    string? WarningCode,
    InterCompanyMatching? Matching = null);

public interface IInterCompanyReconciliationService
{
    Task<IReadOnlyList<InterCompanyLinkResult>> ListLinksAsync(Guid companyId, bool includeInactive, CancellationToken cancellationToken = default);

    Task<InterCompanyLinkResult> SaveLinkAsync(Guid companyId, Guid userId, SaveInterCompanyLinkCommand command, CancellationToken cancellationToken = default);

    Task<InterCompanyLinkResult> SetActiveAsync(Guid companyId, Guid linkId, bool isActive, CancellationToken cancellationToken = default);

    Task<InterCompanyReconciliationResult> ReconcileAsync(Guid companyId, Guid userId, Guid linkId, DateOnly asOfDate, CancellationToken cancellationToken = default);
}
