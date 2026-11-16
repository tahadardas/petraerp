using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

public sealed class SqlWarehouseAccountingService(FinancialDbContext context) : IWarehouseAccountingService
{
    public async Task<WarehouseAccountingResult> GetAsync(Guid companyId, Guid branchId, Guid warehouseId, CancellationToken cancellationToken = default)
    {
        var w = await context.Warehouses.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.BranchId == branchId && x.Id == warehouseId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Warehouse");
        return await ToResultAsync(companyId, w, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WarehouseAccountingResult> SetAsync(Guid companyId, Guid branchId, Guid warehouseId, SetWarehouseAccountingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var w = await context.Warehouses.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.BranchId == branchId && x.Id == warehouseId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Warehouse");
        if (command.ParentWarehouseId is { } parentId)
        {
            if (parentId == warehouseId) throw new BusinessRuleException("warehouse.parent_self", "A warehouse cannot be its own parent.", "parentWarehouseId");
            var parents = await context.Warehouses.AsNoTracking().Where(x => x.CompanyId == companyId).Select(x => new { x.Id, x.BranchId, x.ParentWarehouseId }).ToDictionaryAsync(x => x.Id, cancellationToken).ConfigureAwait(false);
            if (!parents.TryGetValue(parentId, out var parent) || parent.BranchId != branchId)
                throw new BusinessRuleException("warehouse.parent_invalid", "The parent warehouse must exist in the same company and branch.", "parentWarehouseId");
            var visited = new HashSet<Guid>();
            for (Guid? cursor = parent.ParentWarehouseId; cursor is { } c && visited.Add(c); cursor = parents.TryGetValue(c, out var next) ? next.ParentWarehouseId : null)
            {
                if (c == warehouseId) throw new BusinessRuleException("warehouse.parent_cycle", "The parent chain would form a cycle.", "parentWarehouseId");
            }
        }

        await RequirePostableAccountAsync(companyId, command.InventoryAccountId, "inventoryAccountId", cancellationToken).ConfigureAwait(false);
        await RequirePostableAccountAsync(companyId, command.InTransitAccountId, "inTransitAccountId", cancellationToken).ConfigureAwait(false);
        if (command.DefaultCostCenterId is { } cc && !await context.CostCenters.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == cc && x.IsActive, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("warehouse.cost_center_invalid", "The default cost center must be an active cost center of this company.", "defaultCostCenterId");
        w.ParentWarehouseId = command.ParentWarehouseId;
        w.InventoryAccountId = command.InventoryAccountId;
        w.InTransitAccountId = command.InTransitAccountId;
        w.DefaultCostCenterId = command.DefaultCostCenterId;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToResultAsync(companyId, w, cancellationToken).ConfigureAwait(false);
    }

    private async Task RequirePostableAccountAsync(Guid companyId, Guid? accountId, string field, CancellationToken ct)
    {
        if (accountId is not { } id) return;
        var ok = await context.Accounts.AsNoTracking().AnyAsync(a => a.CompanyId == companyId && a.Id == id && a.IsActive && a.PostingAllowed, ct).ConfigureAwait(false);
        if (!ok) throw new BusinessRuleException("warehouse.account_invalid", "The account must be an active, postable account of this company.", field);
    }

    private async Task<WarehouseAccountingResult> ToResultAsync(Guid companyId, WarehouseRow w, CancellationToken ct)
    {
        var ids = new[] { w.InventoryAccountId, w.InTransitAccountId }.OfType<Guid>().ToArray();
        var codes = ids.Length == 0 ? [] : await context.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, ct).ConfigureAwait(false);
        string? cc = w.DefaultCostCenterId is { } c ? await context.CostCenters.AsNoTracking().Where(x => x.CompanyId == companyId && x.Id == c).Select(x => x.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;
        return new WarehouseAccountingResult(w.Id, w.ParentWarehouseId, w.InventoryAccountId, w.InventoryAccountId is { } a ? codes.GetValueOrDefault(a) : null,
            w.InTransitAccountId, w.InTransitAccountId is { } t ? codes.GetValueOrDefault(t) : null, w.DefaultCostCenterId, cc);
    }
}
