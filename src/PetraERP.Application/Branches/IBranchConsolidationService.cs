namespace PetraERP.Application.Branches;

/// <summary>Request for a branch-by-branch trial balance. <see cref="BranchIds"/> null means every branch of the company.</summary>
public sealed record BranchTrialBalanceQuery(DateOnly From, DateOnly To, IReadOnlyList<Guid>? BranchIds, bool EliminateInterBranch);

public sealed record BranchBalanceColumn(Guid BranchId, decimal Opening, decimal Debit, decimal Credit, decimal Closing);

public sealed record BranchTrialBalanceRow(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    bool IsInterBranchClearing,
    IReadOnlyList<BranchBalanceColumn> ByBranch,
    decimal TotalOpening,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal TotalClosing,
    /// <summary>Closing balance after elimination: zero for inter-branch clearing accounts when elimination is requested.</summary>
    decimal ConsolidatedClosing);

public sealed record BranchTrialBalanceBranch(Guid BranchId, string Code, string Name);

/// <summary>
/// Branch-by-branch posted balances of one company-year with an optional elimination of the inter-branch clearing accounts
/// (those mapped with the InterBranchClearing role). <see cref="InterBranchResidual"/> is the net of those accounts across the
/// selected branches: zero when every inter-branch transfer was recorded on both sides, otherwise unreconciled.
/// </summary>
public sealed record BranchTrialBalanceResult(
    DateOnly From,
    DateOnly To,
    string BaseCurrencyCode,
    bool Eliminated,
    IReadOnlyList<BranchTrialBalanceBranch> Branches,
    IReadOnlyList<BranchTrialBalanceRow> Rows,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal ConsolidatedClosingSum,
    decimal InterBranchResidual);

public interface IBranchConsolidationService
{
    Task<BranchTrialBalanceResult> TrialBalanceAsync(Guid companyId, Guid actorUserId, BranchTrialBalanceQuery query, CancellationToken cancellationToken = default);
}
