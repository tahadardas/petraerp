using System.Data;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Domain.GeneralLedger;

namespace PetraERP.Infrastructure.GeneralLedger;

public sealed partial class SqlChartOfAccountsService
{
    public async Task<PagedResult<CostCenterDto>> SearchCostCentersAsync(LedgerMasterScope scope, CostCenterQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        var centers = context.CostCenters.AsNoTracking().Where(c => c.CompanyId == scope.CompanyId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var text = query.Search.Trim();
            centers = centers.Where(c => c.Code.Contains(text) || c.NameArabic.Contains(text) || (c.NameEnglish != null && c.NameEnglish.Contains(text)));
        }

        if (query.IsActive is { } active) centers = centers.Where(c => c.IsActive == active);
        var total = await centers.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var page = await centers.OrderBy(c => c.Code).Skip(query.Page.Skip).Take(query.Page.PageSize)
            .Select(c => new
            {
                Row = c,
                Used = context.JournalLines.Any(l => l.CostCenterId == c.Id),
                ChildrenCount = context.CostCenters.Count(child => child.CompanyId == scope.CompanyId && child.ParentId == c.Id)
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<CostCenterDto>(page.Select(p => Map(p.Row, p.Used, p.ChildrenCount)).ToList().AsReadOnly(), query.Page.Page, query.Page.PageSize, total);
    }

    public async Task<IReadOnlyList<CostCenterDto>> GetCostCenterTreeAsync(LedgerMasterScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var centers = await context.CostCenters.AsNoTracking()
            .Where(c => c.CompanyId == scope.CompanyId)
            .OrderBy(c => c.Code)
            .Select(c => new
            {
                Row = c,
                Used = context.JournalLines.Any(l => l.CostCenterId == c.Id),
                ChildrenCount = context.CostCenters.Count(child => child.CompanyId == scope.CompanyId && child.ParentId == c.Id)
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return centers.Select(c => Map(c.Row, c.Used, c.ChildrenCount)).ToList().AsReadOnly();
    }

    public async Task<CostCenterDto> GetCostCenterAsync(LedgerMasterScope scope, Guid costCenterId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var result = await context.CostCenters.AsNoTracking().Where(c => c.Id == costCenterId && c.CompanyId == scope.CompanyId)
            .Select(c => new
            {
                Row = c,
                Used = context.JournalLines.Any(l => l.CostCenterId == c.Id),
                ChildrenCount = context.CostCenters.Count(child => child.CompanyId == scope.CompanyId && child.ParentId == c.Id)
            })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? throw new ResourceNotFoundException("cost center");
        return Map(result.Row, result.Used, result.ChildrenCount);
    }

    public async Task<CostCenterDto> CreateCostCenterAsync(LedgerMasterScope scope, CostCenterInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(input);
        await ValidateCostCenterWorkingBranchAsync(scope, cancellationToken).ConfigureAwait(false);
        if (input.ParentId.HasValue && input.ParentId.Value != Guid.Empty)
        {
            var parentExists = await context.CostCenters.AsNoTracking()
                .AnyAsync(c => c.CompanyId == scope.CompanyId && c.Id == input.ParentId.Value, cancellationToken).ConfigureAwait(false);
            if (!parentExists) throw new BusinessRuleException("ledger.cost_center_parent_not_found", "The specified parent cost center does not exist.");
        }

        var company = await LedgerRowMapping.LoadCompanyAsync(context, scope.CompanyId, cancellationToken).ConfigureAwait(false);
        var center = CostCenter.Create(
            company,
            LedgerRowMapping.Code(input.Code),
            LedgerRowMapping.Name(input.NameArabic, input.NameEnglish),
            isActive: true,
            parentId: input.ParentId,
            centerType: input.CenterType,
            description: input.Description,
            startDate: input.StartDate,
            endDate: input.EndDate,
            responsiblePerson: input.ResponsiblePerson);

        var row = new CostCenterRow
        {
            Id = center.Id,
            CompanyId = center.CompanyId,
            ParentId = center.ParentId,
            CenterType = center.CenterType,
            Code = center.Code.Value,
            NameArabic = center.Name.Arabic,
            NameEnglish = center.Name.English,
            Description = center.Description,
            StartDate = center.StartDate,
            EndDate = center.EndDate,
            ResponsiblePerson = center.ResponsiblePerson,
            IsActive = center.IsActive,
        };

        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            context.CostCenters.Add(row);
            AuditCostCenter(scope, row.Id, CostCenterAuditActionCode.Created, null, State(row));
            await LedgerRowMapping.SaveAsync(context, LedgerMasterErrorCodes.CostCenterCodeTaken, "cost center", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        return await GetCostCenterAsync(scope, center.Id, cancellationToken).ConfigureAwait(false);
    }

    public Task<CostCenterDto> UpdateCostCenterAsync(LedgerMasterScope scope, Guid costCenterId, CostCenterInput input, ConcurrencyToken expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ChangeCostCenterAsync(scope, costCenterId, expectedVersion, (row, center, used) =>
        {
            if (input.ParentId.HasValue)
            {
                if (input.ParentId.Value == costCenterId)
                    throw new BusinessRuleException("ledger.cost_center_parent_self", "A cost center cannot be its own parent.");

                var current = input.ParentId.Value;
                var visited = new HashSet<Guid> { costCenterId };
                while (current != Guid.Empty)
                {
                    if (!visited.Add(current))
                        throw new BusinessRuleException("ledger.cost_center_hierarchy_cycle", "A cycle was detected in the cost center hierarchy.");
                    var next = context.CostCenters.AsNoTracking().Where(c => c.CompanyId == scope.CompanyId && c.Id == current).Select(c => c.ParentId).FirstOrDefault();
                    if (!next.HasValue) break;
                    current = next.Value;
                }
            }

            var revised = center.Revise(
                LedgerRowMapping.Code(input.Code),
                LedgerRowMapping.Name(input.NameArabic, input.NameEnglish),
                used,
                input.ParentId,
                input.CenterType,
                input.Description,
                input.StartDate,
                input.EndDate,
                input.ResponsiblePerson);

            row.Code = revised.Code.Value;
            row.NameArabic = revised.Name.Arabic;
            row.NameEnglish = revised.Name.English;
            row.ParentId = revised.ParentId;
            row.CenterType = revised.CenterType;
            row.Description = revised.Description;
            row.StartDate = revised.StartDate;
            row.EndDate = revised.EndDate;
            row.ResponsiblePerson = revised.ResponsiblePerson;
        }, cancellationToken);
    }

    public Task<CostCenterDto> SetCostCenterActiveAsync(LedgerMasterScope scope, Guid costCenterId, bool isActive, ConcurrencyToken expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeCostCenterAsync(scope, costCenterId, expectedVersion, (row, center, _) => row.IsActive = center.WithActivation(isActive).IsActive, cancellationToken);

    public async Task<IReadOnlyList<CostCenterAllocationDto>> GetCostCenterAllocationsAsync(LedgerMasterScope scope, Guid costCenterId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var rows = await context.CostCenterAllocations.AsNoTracking()
            .Include(a => a.TargetCostCenter)
            .Where(a => a.CompanyId == scope.CompanyId && a.SourceCostCenterId == costCenterId)
            .OrderBy(a => a.TargetCostCenter!.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.Select(a => new CostCenterAllocationDto
        {
            Id = a.Id,
            SourceCostCenterId = a.SourceCostCenterId,
            TargetCostCenterId = a.TargetCostCenterId,
            TargetCode = a.TargetCostCenter?.Code ?? string.Empty,
            TargetNameArabic = a.TargetCostCenter?.NameArabic ?? string.Empty,
            Percentage = a.Percentage
        }).ToList().AsReadOnly();
    }

    public async Task SetCostCenterAllocationsAsync(LedgerMasterScope scope, Guid costCenterId, IReadOnlyList<SetCostCenterAllocationInput> allocations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(allocations);
        await ValidateCostCenterWorkingBranchAsync(scope, cancellationToken).ConfigureAwait(false);

        var source = await context.CostCenters.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompanyId == scope.CompanyId && c.Id == costCenterId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("cost center");

        if (allocations.Count > 0)
        {
            var sum = allocations.Sum(a => a.Percentage);
            if (sum != 100.00m && sum != 100.0000m)
            {
                throw new BusinessRuleException("ledger.cost_center_allocation_sum_invalid", "Distribution allocations must sum to exactly 100%.");
            }

            var targetIds = allocations.Select(a => a.TargetCostCenterId).Distinct().ToList();
            if (targetIds.Contains(costCenterId))
            {
                throw new BusinessRuleException("ledger.cost_center_allocation_self", "A cost center cannot distribute to itself.");
            }

            var targetCount = await context.CostCenters.AsNoTracking()
                .CountAsync(c => c.CompanyId == scope.CompanyId && c.IsActive && targetIds.Contains(c.Id), cancellationToken).ConfigureAwait(false);

            if (targetCount != targetIds.Count)
            {
                throw new BusinessRuleException("ledger.cost_center_allocation_target_invalid", "All target cost centers must be active and exist within the same company.");
            }
        }

        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var existing = await context.CostCenterAllocations
                .Where(a => a.CompanyId == scope.CompanyId && a.SourceCostCenterId == costCenterId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            context.CostCenterAllocations.RemoveRange(existing);

            foreach (var input in allocations)
            {
                context.CostCenterAllocations.Add(new CostCenterAllocationRow
                {
                    Id = Guid.NewGuid(),
                    CompanyId = scope.CompanyId,
                    SourceCostCenterId = costCenterId,
                    TargetCostCenterId = input.TargetCostCenterId,
                    Percentage = input.Percentage
                });
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<CostCenterDto> ChangeCostCenterAsync(LedgerMasterScope scope, Guid costCenterId, ConcurrencyToken expectedVersion,
        Action<CostCenterRow, CostCenter, bool> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(expectedVersion);
        await ValidateCostCenterWorkingBranchAsync(scope, cancellationToken).ConfigureAwait(false);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var row = await context.CostCenters.FromSqlInterpolated(
                    $"SELECT * FROM [ledger].[CostCenters] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {costCenterId} AND [CompanyId] = {scope.CompanyId}")
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? throw new ResourceNotFoundException("cost center");
            LedgerRowMapping.VerifyVersion(row.RowVersion, expectedVersion, "cost center");
            var used = await context.JournalLines.AnyAsync(l => l.CostCenterId == row.Id, cancellationToken).ConfigureAwait(false);
            var before = State(row);
            change(row, LedgerRowMapping.ToDomain(row), used);
            var after = State(row);
            if (before != after) AuditCostCenter(scope, row.Id,
                before.IsActive == after.IsActive ? CostCenterAuditActionCode.Updated
                    : after.IsActive ? CostCenterAuditActionCode.Activated : CostCenterAuditActionCode.Deactivated, before, after);
            await LedgerRowMapping.SaveAsync(context, LedgerMasterErrorCodes.CostCenterCodeTaken, "cost center", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetCostCenterAsync(scope, costCenterId, cancellationToken).ConfigureAwait(false);
    }

    private static CostCenterDto Map(CostCenterRow row, bool used, int childrenCount = 0) => new()
    {
        Id = row.Id,
        CompanyId = row.CompanyId,
        ParentId = row.ParentId,
        CenterType = row.CenterType,
        Code = row.Code,
        NameArabic = row.NameArabic,
        NameEnglish = row.NameEnglish,
        Description = row.Description,
        StartDate = row.StartDate,
        EndDate = row.EndDate,
        ResponsiblePerson = row.ResponsiblePerson,
        IsActive = row.IsActive,
        HasJournalLines = used,
        VersionBase64 = Convert.ToBase64String(row.RowVersion),
        ChildrenCount = childrenCount,
    };
}
