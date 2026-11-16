using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.Inventory;

namespace PetraERP.Infrastructure.Reports;

public sealed partial class SqlCommercialReportService
{
    public async Task<InventoryValuationResult> GetInventoryValuationAsync(LedgerReportScope scope, DateOnly asOf, Guid? warehouseId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (warehouseId is { } requested && !await WarehousesInScope(scope).AnyAsync(w => w.Id == requested, cancellationToken).ConfigureAwait(false))
            throw new ResourceNotFoundException("warehouse");
        var end = EndUtc(asOf);
        var movements = MovementsInScope(scope).Where(m => m.MovementDateUtc < end);
        if (warehouseId is { } warehouse) movements = movements.Where(m => m.WarehouseId == warehouse);
        var balances = await movements.GroupBy(m => new { m.WarehouseId, m.ItemId })
            .Select(g => new { g.Key.WarehouseId, g.Key.ItemId, Quantity = g.Sum(m => m.BaseQuantity), Value = g.Sum(m => m.BaseQuantity < 0 ? -m.TotalCost : m.TotalCost) })
            .Where(b => b.Quantity != 0m || b.Value != 0m)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var itemIds = balances.Select(b => b.ItemId).Distinct().ToList();
        var warehouseIds = balances.Select(b => b.WarehouseId).Distinct().ToList();
        var items = await context.Items.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId && itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Code, i.NameArabic, BaseUnitCode = i.BaseUnit!.Code }).ToDictionaryAsync(i => i.Id, cancellationToken).ConfigureAwait(false);
        var warehouses = await context.Warehouses.AsNoTracking().Where(w => w.CompanyId == scope.CompanyId && warehouseIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.Code, cancellationToken).ConfigureAwait(false);
        var rows = balances.Where(b => items.ContainsKey(b.ItemId))
            .Select(b => new InventoryValuationRow(b.WarehouseId, warehouses.GetValueOrDefault(b.WarehouseId) ?? string.Empty, b.ItemId, items[b.ItemId].Code,
                items[b.ItemId].NameArabic, items[b.ItemId].BaseUnitCode, b.Quantity, b.Value,
                b.Quantity > 0m ? decimal.Round(b.Value / b.Quantity, 4, MidpointRounding.ToEven) : null))
            .OrderBy(r => r.WarehouseCode, StringComparer.Ordinal).ThenBy(r => r.ItemCode, StringComparer.Ordinal).ToList();
        var total = rows.Sum(r => r.Value);

        // Inventory accounts: every pattern-mapped Inventory role plus explicit accounts used by inventory documents.
        var accounts = (await MappedAccountsAsync(scope.CompanyId, ["Inventory"], cancellationToken).ConfigureAwait(false)).Keys.ToHashSet();
        foreach (var id in await context.InventoryDocuments.AsNoTracking().Where(d => d.CompanyId == scope.CompanyId && d.InventoryAccountId != null)
                     .Select(d => d.InventoryAccountId!.Value).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false))
            accounts.Add(id);
        var accountIds = accounts.ToList();
        // The GL is per branch, not per warehouse: a warehouse filter makes the GL comparison not applicable (difference 0 is not claimed).
        var gl = await PostedLines(scope).Where(l => accountIds.Contains(l.AccountId) && l.Journal!.AccountingDate <= asOf)
            .SumAsync(l => l.BaseDebit - l.BaseCredit, cancellationToken).ConfigureAwait(false);
        return new(asOf, await BaseCurrencyAsync(scope.CompanyId, cancellationToken).ConfigureAwait(false), rows, total, accountIds, gl,
            warehouseId is null ? total - gl : 0m);
    }

    public async Task<InventoryMovementReportResult> GetInventoryMovementsAsync(LedgerReportScope scope, Guid itemId, Guid? warehouseId, DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        EnsureRange(fromDate, toDate);
        var item = await context.Items.AsNoTracking().Where(i => i.Id == itemId && i.CompanyId == scope.CompanyId)
            .Select(i => new { i.Code, i.NameArabic, BaseUnitCode = i.BaseUnit!.Code }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("item");
        if (warehouseId is { } requested && !await WarehousesInScope(scope).AnyAsync(w => w.Id == requested, cancellationToken).ConfigureAwait(false))
            throw new ResourceNotFoundException("warehouse");
        var start = StartUtc(fromDate); var end = EndUtc(toDate);
        var movements = MovementsInScope(scope).Where(m => m.ItemId == itemId);
        if (warehouseId is { } warehouse) movements = movements.Where(m => m.WarehouseId == warehouse);
        var opening = await movements.Where(m => m.MovementDateUtc < start).GroupBy(_ => 1)
            .Select(g => new { Quantity = g.Sum(m => m.BaseQuantity), Value = g.Sum(m => m.BaseQuantity < 0 ? -m.TotalCost : m.TotalCost) })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var period = movements.Where(m => m.MovementDateUtc >= start && m.MovementDateUtc < end);
        var totals = await period.GroupBy(_ => 1).Select(g => new
        {
            In = g.Sum(m => m.BaseQuantity > 0 ? m.BaseQuantity : 0m), Out = g.Sum(m => m.BaseQuantity < 0 ? -m.BaseQuantity : 0m),
            Value = g.Sum(m => m.BaseQuantity < 0 ? -m.TotalCost : m.TotalCost), Count = g.Count(),
        }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var rows = await period.OrderBy(m => m.MovementDateUtc).ThenBy(m => m.RecordedSequence).ThenBy(m => m.Id).Take(MaxLines)
            .Select(m => new { m.MovementDateUtc, m.MovementKind, m.DocumentType, m.DocumentId, m.DocumentNumber, m.WarehouseId, m.BaseQuantity, m.TotalCost })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var warehouseIds = rows.Select(r => r.WarehouseId).Distinct().ToList();
        var codes = await context.Warehouses.AsNoTracking().Where(w => w.CompanyId == scope.CompanyId && warehouseIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.Code, cancellationToken).ConfigureAwait(false);
        decimal quantity = opening?.Quantity ?? 0m, value = opening?.Value ?? 0m;
        var lines = new List<InventoryMovementLine>(rows.Count);
        foreach (var r in rows)
        {
            var signedValue = r.BaseQuantity < 0 ? -r.TotalCost : r.TotalCost;
            quantity += r.BaseQuantity; value += signedValue;
            lines.Add(new(r.MovementDateUtc, (StockMovementKind)r.MovementKind, r.DocumentType, r.DocumentId, r.DocumentNumber, r.WarehouseId,
                codes.GetValueOrDefault(r.WarehouseId) ?? string.Empty, r.BaseQuantity, signedValue, quantity, value));
        }
        var openingQuantity = opening?.Quantity ?? 0m; var openingValue = opening?.Value ?? 0m;
        var inQuantity = totals?.In ?? 0m; var outQuantity = totals?.Out ?? 0m;
        return new(itemId, item.Code, item.NameArabic, item.BaseUnitCode, warehouseId, fromDate, toDate, openingQuantity, openingValue, lines, inQuantity, outQuantity,
            openingQuantity + inQuantity - outQuantity, openingValue + (totals?.Value ?? 0m), (totals?.Count ?? 0) > MaxLines);
    }

    private IQueryable<StockMovementRow> MovementsInScope(LedgerReportScope scope)
    {
        var rows = context.StockMovements.AsNoTracking().Where(m => m.CompanyId == scope.CompanyId);
        return scope.Consolidated ? rows : rows.Where(m => m.BranchId == scope.BranchId);
    }

    private IQueryable<WarehouseRow> WarehousesInScope(LedgerReportScope scope)
    {
        var rows = context.Warehouses.AsNoTracking().Where(w => w.CompanyId == scope.CompanyId);
        return scope.Consolidated ? rows : rows.Where(w => w.BranchId == scope.BranchId);
    }
}
