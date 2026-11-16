using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;

namespace PetraERP.Infrastructure.GeneralLedger;

public sealed partial class SqlChartOfAccountsService
{
    public async Task<PagedResult<AccountHistoryDto>> GetAccountHistoryAsync(LedgerMasterScope scope, Guid accountId,
        PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(page);
        var events = context.AccountAudit.AsNoTracking().Where(row => row.CompanyId == scope.CompanyId && row.AccountId == accountId);
        // Retained history is readable after deletion; a missing/foreign id must not reveal whether another company has it.
        if (!await context.Accounts.AnyAsync(row => row.CompanyId == scope.CompanyId && row.Id == accountId, cancellationToken).ConfigureAwait(false)
            && !await events.AnyAsync(cancellationToken).ConfigureAwait(false))
            throw new ResourceNotFoundException("account");
        var total = await events.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await events.OrderByDescending(row => row.OccurredAtUtc).ThenByDescending(row => row.EventId)
            .Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = rows.Select(row => row.EventId).ToArray();
        var states = await context.AccountAuditStates.AsNoTracking().Where(row => ids.Contains(row.EventId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var actors = rows.Select(row => row.ActorUserId).Distinct().ToArray();
        var names = await context.ActorNamesAsync(actors, cancellationToken).ConfigureAwait(false);
        var snapshots = states.ToDictionary(row => (row.EventId, row.Stage));
        var result = rows.Select(row => new AccountHistoryDto
        {
            EventId = row.EventId, AccountId = row.AccountId, Action = (AccountAuditActionCode)row.Action,
            Code = row.Code, NameArabic = row.NameArabic, NameEnglish = row.NameEnglish,
            ActorUserId = row.ActorUserId,
            ActorNameArabic = names.GetValueOrDefault(row.ActorUserId)?.DisplayNameArabic,
            ActorNameEnglish = names.GetValueOrDefault(row.ActorUserId)?.DisplayNameEnglish,
            WorkingBranchId = row.WorkingBranchId, CorrelationId = row.CorrelationId,
            OccurredAtUtc = row.OccurredAtUtc, Reason = row.Reason,
            Before = snapshots.TryGetValue((row.EventId, AccountAuditStateRow.Before), out var before) ? State(before) : null,
            After = snapshots.TryGetValue((row.EventId, AccountAuditStateRow.After), out var after) ? State(after) : null,
        }).ToArray();
        return new(result, page.Page, page.PageSize, total);
    }

    private static AccountStateDto State(AccountAuditStateRow row) => new(row.Code, row.NameArabic, row.NameEnglish,
        (AccountTypeCode)row.AccountType, row.PostingAllowed, (CostCenterPolicyCode)row.CostCenterPolicy,
        (CurrencyModeCode)row.CurrencyMode, row.FixedCurrencyCode, row.ParentId, row.IsActive);

    private static AccountStateDto State(AccountRow row) => new(row.Code, row.NameArabic, row.NameEnglish,
        (AccountTypeCode)row.AccountType, row.PostingAllowed, (CostCenterPolicyCode)row.CostCenterPolicy,
        (CurrencyModeCode)row.CurrencyMode, row.FixedCurrencyCode, row.ParentId, row.IsActive);

    private void AuditAccount(LedgerMasterScope scope, Guid accountId, AccountAuditActionCode action,
        AccountStateDto? before, AccountStateDto? after, string? reason = null)
    {
        var identity = after ?? before ?? throw new ArgumentException("An account event needs a state snapshot.");
        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        context.AccountAudit.Add(new AccountAuditRow
        {
            EventId = id, CompanyId = scope.CompanyId, AccountId = accountId, Action = (byte)action,
            Code = identity.Code, NameArabic = identity.NameArabic, NameEnglish = identity.NameEnglish,
            AccountType = (byte)identity.AccountType, ParentId = identity.ParentId,
            ActorUserId = scope.ActorUserId, WorkingBranchId = scope.WorkingBranchId, CorrelationId = scope.CorrelationId,
            Reason = reason, OccurredAtUtc = now,
        });
        if (before is not null) context.AccountAuditStates.Add(Snapshot(id, AccountAuditStateRow.Before, before));
        if (after is not null) context.AccountAuditStates.Add(Snapshot(id, AccountAuditStateRow.After, after));
    }

    private static AccountAuditStateRow Snapshot(Guid id, byte stage, AccountStateDto state) => new()
    {
        EventId = id, Stage = stage, Code = state.Code, NameArabic = state.NameArabic, NameEnglish = state.NameEnglish,
        AccountType = (byte)state.AccountType, PostingAllowed = state.PostingAllowed, CostCenterPolicy = (byte)state.CostCenterPolicy,
        CurrencyMode = (byte)state.CurrencyMode, FixedCurrencyCode = state.FixedCurrencyCode,
        ParentId = state.ParentId, IsActive = state.IsActive,
    };
}
