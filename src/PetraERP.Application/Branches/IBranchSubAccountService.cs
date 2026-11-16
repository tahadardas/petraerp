namespace PetraERP.Application.Branches;

/// <param name="ModelBranchId">The branch whose current account mappings are the template.</param>
/// <param name="EffectiveFrom">First day the new mappings apply (never retroactive).</param>
public sealed record ApplyBranchSubAccountsCommand(Guid ModelBranchId, DateOnly EffectiveFrom, string Reason);

public sealed record BranchSubAccountApplyItem(BranchAccountRole Role, Guid AccountId, string AccountCode, bool AccountCreated, bool LinkChanged);

/// <summary>
/// Creates the target branch's sub-accounts under the parents the model branch uses and maps them with an effective date.
/// Accounts are created through the one chart-of-accounts service (no parallel account logic). The operation is idempotent per
/// (parent, branch code): after a partial failure a retry reuses accounts already created and completes the remaining ones.
/// </summary>
public interface IBranchSubAccountService
{
    Task<IReadOnlyList<BranchSubAccountApplyItem>> ApplyAsync(Guid companyId, Guid targetBranchId, Guid actorUserId, ApplyBranchSubAccountsCommand command,
        CancellationToken cancellationToken = default);
}
