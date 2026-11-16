using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory.Opening;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

public sealed class SqlInventoryOpeningService(FinancialDbContext context, TimeProvider timeProvider) : IInventoryOpeningService
{
    public async Task<OpeningStockBatchDto> PostOpeningStockBatchAsync(Guid companyId, Guid actorId, CreateOpeningStockBatchCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Lines.Count == 0)
            throw new RequestValidationException("inventory_opening.empty_lines", "At least one line is required.", nameof(command.Lines));

        var branch = await context.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == command.BranchId && b.CompanyId == companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Branch");

        var warehouseIds = command.Lines.Select(l => l.WarehouseId).Distinct().ToArray();
        var itemIds = command.Lines.Select(l => l.ItemId).Distinct().ToArray();
        var unitIds = command.Lines.Select(l => l.UnitId).Distinct().ToArray();

        var warehouses = await context.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id) && w.CompanyId == companyId && w.BranchId == command.BranchId).ToDictionaryAsync(w => w.Id, cancellationToken).ConfigureAwait(false);
        foreach (var wid in warehouseIds)
        {
            if (!warehouses.ContainsKey(wid))
                throw new BusinessRuleException("inventory_opening.warehouse_invalid", $"Warehouse {wid} does not belong to company/branch.");
        }

        var items = await context.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id) && i.CompanyId == companyId).ToDictionaryAsync(i => i.Id, cancellationToken).ConfigureAwait(false);
        foreach (var iid in itemIds)
        {
            if (!items.TryGetValue(iid, out var item) || !item.IsActive)
                throw new BusinessRuleException("inventory_opening.item_invalid", $"Item {iid} is not an active company item.");
        }

        var units = await context.UnitsOfMeasure.AsNoTracking().Where(u => unitIds.Contains(u.Id) && u.CompanyId == companyId).ToDictionaryAsync(u => u.Id, cancellationToken).ConfigureAwait(false);
        foreach (var uid in unitIds)
        {
            if (!units.ContainsKey(uid))
                throw new BusinessRuleException("inventory_opening.unit_invalid", $"Unit {uid} does not belong to company.");
        }

        var batchId = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        var movements = new List<StockMovementRow>(command.Lines.Count);

        foreach (var l in command.Lines)
        {
            if (l.Quantity <= 0)
                throw new BusinessRuleException("inventory_opening.quantity_invalid", "Quantity must be positive.");
            if (l.TotalValue < 0)
                throw new BusinessRuleException("inventory_opening.value_invalid", "Total value cannot be negative.");

            var unitCost = l.Quantity > 0 ? decimal.Round(l.TotalValue / l.Quantity, 4, MidpointRounding.ToEven) : 0m;

            var movement = new StockMovementRow
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                BranchId = command.BranchId,
                WarehouseId = l.WarehouseId,
                ItemId = l.ItemId,
                UnitId = l.UnitId,
                MovementKind = (int)StockMovementKind.OpeningBalance,
                Quantity = l.Quantity,
                BaseQuantity = l.Quantity,
                UnitCost = unitCost,
                TotalCost = l.TotalValue,
                MovementDateUtc = command.OpeningDateUtc,
                DocumentType = "OpeningBalance",
                DocumentId = batchId,
                Notes = l.Notes ?? command.SourceReference ?? "رصيد مخزني افتتاحي / Opening stock balance",
                CreatedByUserId = actorId,
                CreatedAtUtc = now,
            };

            movements.Add(movement);
            context.StockMovements.Add(movement);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var lineDtos = movements.Select(m => new OpeningStockLineDto(
            m.Id,
            m.WarehouseId,
            warehouses[m.WarehouseId].NameArabic,
            m.ItemId,
            items[m.ItemId].Code,
            items[m.ItemId].NameArabic,
            m.UnitId,
            units[m.UnitId].Code,
            m.Quantity,
            m.UnitCost,
            m.TotalCost,
            m.Notes)).ToList();

        return new OpeningStockBatchDto(
            batchId,
            companyId,
            command.BranchId,
            command.OpeningDateUtc,
            command.SourceReference,
            movements.Sum(m => m.Quantity),
            movements.Sum(m => m.TotalCost),
            lineDtos);
    }

    public async Task<IReadOnlyList<OpeningStockLineDto>> GetOpeningStockAsync(Guid companyId, Guid? warehouseId, CancellationToken cancellationToken = default)
    {
        var query = context.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && m.MovementKind == (int)StockMovementKind.OpeningBalance);
        if (warehouseId.HasValue) query = query.Where(m => m.WarehouseId == warehouseId.Value);

        var movements = await query.OrderBy(m => m.MovementDateUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
        var warehouseIds = movements.Select(m => m.WarehouseId).Distinct().ToArray();
        var itemIds = movements.Select(m => m.ItemId).Distinct().ToArray();
        var unitIds = movements.Select(m => m.UnitId).Distinct().ToArray();

        var warehouses = await context.Warehouses.Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.NameArabic, cancellationToken).ConfigureAwait(false);
        var items = await context.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken).ConfigureAwait(false);
        var units = await context.UnitsOfMeasure.Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Code, cancellationToken).ConfigureAwait(false);

        return movements.Select(m => new OpeningStockLineDto(
            m.Id,
            m.WarehouseId,
            warehouses.GetValueOrDefault(m.WarehouseId, string.Empty),
            m.ItemId,
            items.TryGetValue(m.ItemId, out var item) ? item.Code : string.Empty,
            items.TryGetValue(m.ItemId, out var item2) ? item2.NameArabic : string.Empty,
            m.UnitId,
            units.GetValueOrDefault(m.UnitId, string.Empty),
            m.Quantity,
            m.UnitCost,
            m.TotalCost,
            m.Notes)).ToList();
    }
}
