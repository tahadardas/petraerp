using PetraERP.Application.Branches;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;

namespace PetraERP.Infrastructure.Organizations;

public sealed class SqlBranchSubAccountService(IBranchAdminService branches, IChartOfAccountsService chart, TimeProvider? clock = null) : IBranchSubAccountService
{
    public async Task<IReadOnlyList<BranchSubAccountApplyItem>> ApplyAsync(Guid companyId, Guid targetBranchId, Guid actorUserId, ApplyBranchSubAccountsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new BusinessRuleException("branch.reason_required", "A reason is required.", "reason");
        if (command.ModelBranchId == targetBranchId)
            throw new BusinessRuleException("branch.model_is_target", "The model branch must differ from the target branch.", "modelBranchId");
        var target = await branches.GetAsync(companyId, targetBranchId, cancellationToken).ConfigureAwait(false);
        if (!target.IsActive) throw new BusinessRuleException("branch.inactive", "Sub-accounts cannot be created for an inactive branch.", "branchId");
        _ = await branches.GetAsync(companyId, command.ModelBranchId, cancellationToken).ConfigureAwait(false);
        var today = DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);
        var modelLinks = await branches.ListAccountLinksAsync(companyId, command.ModelBranchId, today, cancellationToken).ConfigureAwait(false);
        if (modelLinks.Count == 0)
            throw new BusinessRuleException("branch.model_has_no_links", "The model branch has no current account mappings.", "modelBranchId");

        var scope = LedgerMasterScope.Create(companyId, actorUserId, targetBranchId);
        var tree = (await chart.GetAccountTreeAsync(scope, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id);
        var byCode = tree.Values.ToDictionary(a => a.Code, StringComparer.Ordinal);
        var existingLinks = (await branches.ListAccountLinksAsync(companyId, targetBranchId, null, cancellationToken).ConfigureAwait(false))
            .Where(l => l.EffectiveTo is null).ToDictionary(l => l.Role);
        var result = new List<BranchSubAccountApplyItem>();
        try
        {
        foreach (var link in modelLinks.OrderBy(l => l.Role))
        {
            if (!tree.TryGetValue(link.AccountId, out var model) || model.ParentId is not { } parentId || !tree.TryGetValue(parentId, out var parent))
                continue; // a mapping to a root account has no parent to hang a branch sub-account under
            var code = $"{parent.Code}-{target.Code}";
            var created = false;
            if (byCode.TryGetValue(code, out var existing))
            {
                if (existing.ParentId != parent.Id)
                    throw new BusinessRuleException("branch.subaccount_code_conflict", $"The account code {code} is already used under another parent.", "code");
            }
            else
            {
                existing = await chart.CreateAccountAsync(scope, new AccountInput
                {
                    Code = code, NameArabic = $"{model.NameArabic} - {target.NameArabic}",
                    NameEnglish = model.NameEnglish is null || target.NameEnglish is null ? null : $"{model.NameEnglish} - {target.NameEnglish}",
                    AccountType = model.AccountType, PostingAllowed = true, CostCenterPolicy = model.CostCenterPolicy,
                    CurrencyMode = model.CurrencyMode, FixedCurrencyCode = model.FixedCurrencyCode, ParentId = parent.Id,
                }, cancellationToken).ConfigureAwait(false);
                byCode[code] = existing; created = true;
            }

            var alreadyLinked = existingLinks.TryGetValue(link.Role, out var current) && current.AccountId == existing.Id;
            if (!alreadyLinked)
                await branches.SetAccountLinkAsync(companyId, targetBranchId, actorUserId,
                    new SetBranchAccountLinkCommand(link.Role, existing.Id, command.EffectiveFrom, command.Reason), cancellationToken).ConfigureAwait(false);
            result.Add(new(link.Role, existing.Id, existing.Code, created, !alreadyLinked));
        }
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or BusinessRuleException) && result.Count > 0)
        {
            // Account creation and link registration are separate committed units. Re-running is safe: existing sub-accounts are reused by code
            // and already-current links are skipped; the failure states exactly which roles were completed so the partial state is never silent.
            var done = string.Join(", ", result.Select(r => r.Role.ToString()));
            throw new BusinessRuleException("branch.subaccounts_partial",
                $"Applying branch sub-accounts stopped after completing roles [{done}]: {exception.Message} Re-run the same request to resume safely.", "modelBranchId");
        }

        return result;
    }
}
