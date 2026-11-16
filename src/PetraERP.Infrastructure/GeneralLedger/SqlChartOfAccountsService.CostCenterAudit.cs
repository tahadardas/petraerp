using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;

namespace PetraERP.Infrastructure.GeneralLedger;

public sealed partial class SqlChartOfAccountsService
{
    // Cost-center permissions remain company-wide. A supplied working branch is audit context and must belong
    // to that authorized company; it does not change the permission catalog into branch-level access.
    private async Task ValidateCostCenterWorkingBranchAsync(LedgerMasterScope scope, CancellationToken cancellationToken)
    {
        if (scope.WorkingBranchId is { } id && !await context.Branches.AsNoTracking()
                .AnyAsync(row => row.Id == id && row.CompanyId == scope.CompanyId && row.IsActive, cancellationToken).ConfigureAwait(false))
            throw new ResourceNotFoundException("working branch");
    }

    public async Task<PagedResult<CostCenterHistoryDto>> GetCostCenterHistoryAsync(LedgerMasterScope scope, Guid costCenterId,
        PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(page);
        var events = context.CostCenterAudit.AsNoTracking().Where(row => row.CompanyId == scope.CompanyId && row.CostCenterId == costCenterId);
        if (!await context.CostCenters.AnyAsync(row => row.CompanyId == scope.CompanyId && row.Id == costCenterId, cancellationToken).ConfigureAwait(false)
            && !await events.AnyAsync(cancellationToken).ConfigureAwait(false)) throw new ResourceNotFoundException("cost center");
        var total = await events.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await events.OrderByDescending(row => row.OccurredAtUtc).ThenByDescending(row => row.EventId)
            .Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = rows.Select(row => row.EventId).ToArray();
        var states = await context.CostCenterAuditStates.AsNoTracking().Where(row => ids.Contains(row.EventId))
            .ToDictionaryAsync(row => new { row.EventId, row.Stage }, cancellationToken).ConfigureAwait(false);
        var actors = rows.Select(row => row.ActorUserId).Distinct().ToArray();
        var names = await context.ActorNamesAsync(actors, cancellationToken).ConfigureAwait(false);
        return new(rows.Select(row => new CostCenterHistoryDto
        {
            EventId = row.EventId, CostCenterId = row.CostCenterId, Action = (CostCenterAuditActionCode)row.Action,
            ActorUserId = row.ActorUserId, ActorNameArabic = names.GetValueOrDefault(row.ActorUserId)?.DisplayNameArabic,
            ActorNameEnglish = names.GetValueOrDefault(row.ActorUserId)?.DisplayNameEnglish,
            WorkingBranchId = row.WorkingBranchId, CorrelationId = row.CorrelationId, OccurredAtUtc = row.OccurredAtUtc,
            Before = states.TryGetValue(new { row.EventId, Stage = CostCenterAuditStateRow.Before }, out var before) ? State(before) : null,
            After = State(states[new { row.EventId, Stage = CostCenterAuditStateRow.After }]),
        }).ToArray(), page.Page, page.PageSize, total);
    }

    private static CostCenterStateDto State(CostCenterRow row) => new(row.Code, row.NameArabic, row.NameEnglish, row.IsActive);
    private static CostCenterStateDto State(CostCenterAuditStateRow row) => new(row.Code, row.NameArabic, row.NameEnglish, row.IsActive);

    private void AuditCostCenter(LedgerMasterScope scope, Guid costCenterId, CostCenterAuditActionCode action, CostCenterStateDto? before, CostCenterStateDto after)
    {
        var now = time.GetUtcNow(); var id = Guid.CreateVersion7(now);
        context.CostCenterAudit.Add(new() { EventId = id, CompanyId = scope.CompanyId, CostCenterId = costCenterId, Action = (byte)action,
            ActorUserId = scope.ActorUserId, WorkingBranchId = scope.WorkingBranchId, CorrelationId = scope.CorrelationId, OccurredAtUtc = now });
        if (before is not null) context.CostCenterAuditStates.Add(Snapshot(id, CostCenterAuditStateRow.Before, before));
        context.CostCenterAuditStates.Add(Snapshot(id, CostCenterAuditStateRow.After, after));
    }

    private static CostCenterAuditStateRow Snapshot(Guid id, byte stage, CostCenterStateDto state) => new()
    {
        EventId = id, Stage = stage, Code = state.Code, NameArabic = state.NameArabic, NameEnglish = state.NameEnglish, IsActive = state.IsActive,
    };
}
