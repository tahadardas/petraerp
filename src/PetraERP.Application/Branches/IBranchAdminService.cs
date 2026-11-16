using PetraERP.Application.Common;

namespace PetraERP.Application.Branches;

/// <summary>Posting role an account plays for one branch; each role has at most one account at any date.</summary>
public enum BranchAccountRole : byte
{
    Cash = 1,
    Bank = 2,
    Sales = 3,
    SalesReturns = 4,
    Purchases = 5,
    PurchaseReturns = 6,
    Inventory = 7,
    CostOfGoodsSold = 8,
    Receivable = 9,
    Payable = 10,
    InterBranchClearing = 11,
}

/// <summary>The branch card. Branches are never deleted; an unused or retired branch is deactivated.</summary>
public sealed record BranchCardResult(
    Guid Id,
    Guid CompanyId,
    string Code,
    string NameArabic,
    string? NameEnglish,
    bool IsActive,
    Guid? ParentBranchId,
    string? NumberPrefix,
    DateOnly? OpenedOn,
    string? Address,
    string? Phone,
    string? Email,
    string? Notes,
    Guid? DefaultWarehouseId,
    Guid? DefaultCashAccountId,
    Guid? DefaultBankAccountId,
    Guid? DefaultCostCenterId,
    byte[] RowVersion,
    DateTimeOffset UpdatedAtUtc);

public sealed record BranchTreeNode(BranchCardResult Branch, int Depth, IReadOnlyList<BranchTreeNode> Children);

public sealed record SaveBranchCommand(
    string Code,
    string NameArabic,
    string? NameEnglish,
    Guid? ParentBranchId,
    string? NumberPrefix,
    DateOnly? OpenedOn,
    string? Address,
    string? Phone,
    string? Email,
    string? Notes,
    Guid? DefaultWarehouseId,
    Guid? DefaultCashAccountId,
    Guid? DefaultBankAccountId,
    Guid? DefaultCostCenterId);

public sealed record UpdateBranchCommand(SaveBranchCommand Card, ConcurrencyToken ExpectedVersion, string Reason);

public sealed record SetBranchActiveCommand(bool IsActive, ConcurrencyToken ExpectedVersion, string Reason);

public sealed record BranchAccountLinkResult(
    Guid Id,
    Guid BranchId,
    BranchAccountRole Role,
    Guid AccountId,
    string AccountCode,
    string AccountNameArabic,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

/// <summary>Maps a role to an account from <see cref="EffectiveFrom"/>; the previous open mapping of the role is closed the day before.</summary>
public sealed record SetBranchAccountLinkCommand(BranchAccountRole Role, Guid AccountId, DateOnly EffectiveFrom, string Reason);

/// <summary>One suggested sub-account of the wizard: created under <see cref="ParentAccountId"/> for the new branch.</summary>
public sealed record BranchSubAccountPlan(BranchAccountRole Role, Guid ParentAccountId, string ParentCode, string SuggestedCode, string NameArabic, string? NameEnglish);

public interface IBranchAdminService
{
    Task<IReadOnlyList<BranchCardResult>> ListAsync(Guid companyId, bool includeInactive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BranchTreeNode>> GetTreeAsync(Guid companyId, bool includeInactive, CancellationToken cancellationToken = default);

    Task<BranchCardResult> GetAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken = default);

    Task<BranchCardResult> CreateAsync(Guid companyId, Guid userId, SaveBranchCommand command, CancellationToken cancellationToken = default);

    Task<BranchCardResult> UpdateAsync(Guid companyId, Guid branchId, Guid userId, UpdateBranchCommand command, CancellationToken cancellationToken = default);

    Task<BranchCardResult> SetActiveAsync(Guid companyId, Guid branchId, Guid userId, SetBranchActiveCommand command, CancellationToken cancellationToken = default);

    /// <summary>Idempotent: copies the branch card of this year database to the central database that authorizes branch scope.</summary>
    Task SyncToCentralAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BranchAccountLinkResult>> ListAccountLinksAsync(Guid companyId, Guid branchId, DateOnly? asOf, CancellationToken cancellationToken = default);

    Task<BranchAccountLinkResult> SetAccountLinkAsync(Guid companyId, Guid branchId, Guid userId, SetBranchAccountLinkCommand command, CancellationToken cancellationToken = default);

    /// <summary>Preview only: proposes sub-account codes/names under the parent accounts that the existing links of the model branch use. Creates nothing.</summary>
    Task<IReadOnlyList<BranchSubAccountPlan>> PreviewSubAccountsAsync(Guid companyId, Guid branchId, Guid modelBranchId, CancellationToken cancellationToken = default);

    /// <summary>The account a branch uses for a role at a date, or null when none is mapped (callers fall back to the pattern's account).</summary>
    Task<Guid?> ResolveAccountAsync(Guid companyId, Guid branchId, BranchAccountRole role, DateOnly asOf, CancellationToken cancellationToken = default);
}
