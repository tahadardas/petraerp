using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Domain.Inventory;

namespace PetraERP.Infrastructure.Inventory;

/// <summary>
/// FIFO layer maintenance at the mandatory movement write boundary (<see cref="InventoryMovementGuard"/>), so every producer of
/// stock movements - invoices, returns, transfers, adjustments, reversals - keeps the same layers. Consumption order is
/// posting order (recorded sequence), never the document date.
/// </summary>
internal static class FifoLedger
{
    internal static async Task<List<InventoryCostLayerRow>> LoadOpenLayersAsync(DbContext db, Guid company, Guid warehouse, Guid item, CancellationToken token) =>
        await db.Set<InventoryCostLayerRow>().Where(l => l.CompanyId == company && l.WarehouseId == warehouse && l.ItemId == item && l.QuantityRemaining > 0)
            .OrderBy(l => l.SourceSequence).ToListAsync(token).ConfigureAwait(false);

    /// <summary>The layers must describe exactly the stock the movements describe, otherwise FIFO cannot be applied safely.</summary>
    internal static void RequireConsistent(IReadOnlyList<InventoryCostLayerRow> open, decimal quantity, decimal value)
    {
        if (open.Sum(l => l.QuantityRemaining) != quantity || open.Sum(l => l.ValueRemaining) != value)
            throw new BusinessRuleException("inventory.fifo_layers_inconsistent",
                "طبقات FIFO لا تطابق رصيد المخزون؛ لا يمكن تغيير طريقة التكلفة لمادة لها حركات / FIFO layers do not match the stock balance; the cost method cannot change for an item that already has movements.");
    }

    internal static async Task ApplyAsync(DbContext db, StockMovementRow movement, List<InventoryCostLayerRow> open, CancellationToken token)
    {
        if (movement.BaseQuantity > 0)
        {
            if (movement.ReversesMovementId is { } issueId) await RestoreAsync(db, movement, issueId, open, token).ConfigureAwait(false);
            else Receive(db, movement, open);
        }
        else if (movement.ReversesMovementId is { } receiptId) await UndoReceiptAsync(db, movement, receiptId, open, token).ConfigureAwait(false);
        else Issue(db, movement, open);
    }

    private static void Receive(DbContext db, StockMovementRow movement, List<InventoryCostLayerRow> open)
    {
        var layer = new InventoryCostLayerRow
        {
            Id = Guid.NewGuid(), CompanyId = movement.CompanyId, BranchId = movement.BranchId, WarehouseId = movement.WarehouseId, ItemId = movement.ItemId,
            ReceiptMovementId = movement.Id, SourceSequence = movement.RecordedSequence, ReceivedAtUtc = movement.MovementDateUtc,
            QuantityReceived = movement.BaseQuantity, QuantityRemaining = movement.BaseQuantity,
            ValueReceived = movement.TotalCost, ValueRemaining = movement.TotalCost, UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.Add(layer);
        open.Add(layer);
    }

    private static void Issue(DbContext db, StockMovementRow movement, List<InventoryCostLayerRow> open)
    {
        var quantity = -movement.BaseQuantity;
        var balances = open.Select(l => new FifoLayerBalance(l.Id, l.QuantityRemaining, l.ValueRemaining)).ToList();
        if (!FifoCosting.TryIssue(balances, quantity, out var slices, out var total))
            throw new BusinessRuleException("inventory.fifo_insufficient",
                "رصيد طبقات FIFO لا يكفي؛ الإخراج بالسالب غير مدعوم لمواد FIFO / FIFO layers are insufficient; negative issue is not supported for FIFO items.");
        if (total != movement.TotalCost)
            throw new BusinessRuleException("inventory.fifo_cost_mismatch",
                "تكلفة الإخراج لا تطابق تخصيص FIFO / The issue cost does not match the FIFO allocation.");
        foreach (var slice in slices)
        {
            var layer = open.Single(l => l.Id == slice.LayerId);
            layer.QuantityRemaining -= slice.Quantity;
            layer.ValueRemaining -= slice.Cost;
            layer.UpdatedAtUtc = DateTimeOffset.UtcNow;
            db.Add(Allocation(movement, layer.Id, slice.Quantity, slice.Cost));
        }
        open.RemoveAll(l => l.QuantityRemaining == 0m);
    }

    /// <summary>A linked reversal of an issue gives the quantity and value back to the very layers it consumed.</summary>
    private static async Task RestoreAsync(DbContext db, StockMovementRow movement, Guid issueMovementId, List<InventoryCostLayerRow> open, CancellationToken token)
    {
        var allocations = await db.Set<InventoryCostAllocationRow>().AsNoTracking().Where(a => a.MovementId == issueMovementId).ToListAsync(token).ConfigureAwait(false);
        if (allocations.Count == 0 || allocations.Sum(a => a.Quantity) != movement.BaseQuantity || allocations.Sum(a => a.Cost) != movement.TotalCost)
            throw new BusinessRuleException("inventory.fifo_reversal_invalid",
                "لا توجد تخصيصات FIFO مطابقة لحركة الإخراج المعكوسة / No FIFO allocation matches the reversed issue.");
        foreach (var allocation in allocations)
        {
            var layer = open.FirstOrDefault(l => l.Id == allocation.LayerId)
                ?? await db.Set<InventoryCostLayerRow>().SingleAsync(l => l.Id == allocation.LayerId, token).ConfigureAwait(false);
            layer.QuantityRemaining += allocation.Quantity;
            layer.ValueRemaining += allocation.Cost;
            layer.UpdatedAtUtc = DateTimeOffset.UtcNow;
            db.Add(Allocation(movement, layer.Id, -allocation.Quantity, -allocation.Cost));
            if (!open.Contains(layer))
            {
                var index = open.FindIndex(l => l.SourceSequence > layer.SourceSequence);
                if (index < 0) open.Add(layer); else open.Insert(index, layer);
            }
        }
    }

    /// <summary>A linked reversal of a receipt is possible only while nothing was issued from its layer.</summary>
    private static async Task UndoReceiptAsync(DbContext db, StockMovementRow movement, Guid receiptMovementId, List<InventoryCostLayerRow> open, CancellationToken token)
    {
        var layer = open.FirstOrDefault(l => l.ReceiptMovementId == receiptMovementId)
            ?? await db.Set<InventoryCostLayerRow>().SingleOrDefaultAsync(l => l.ReceiptMovementId == receiptMovementId, token).ConfigureAwait(false)
            ?? throw new BusinessRuleException("inventory.fifo_reversal_invalid", "لا توجد طبقة FIFO للاستلام المعكوس / No FIFO layer exists for the reversed receipt.");
        if (layer.QuantityRemaining != layer.QuantityReceived || layer.ValueRemaining != layer.ValueReceived)
            throw new BusinessRuleException("inventory.fifo_layer_consumed",
                "صُرف جزء من هذا الاستلام؛ لا يمكن عكسه قبل عكس الصرف / Part of this receipt was already issued; reverse the issues first.");
        layer.QuantityRemaining = 0m;
        layer.ValueRemaining = 0m;
        layer.UpdatedAtUtc = DateTimeOffset.UtcNow;
        db.Add(Allocation(movement, layer.Id, layer.QuantityReceived, layer.ValueReceived));
        open.Remove(layer);
    }

    private static InventoryCostAllocationRow Allocation(StockMovementRow movement, Guid layerId, decimal quantity, decimal cost) => new()
    {
        Id = Guid.NewGuid(), CompanyId = movement.CompanyId, LayerId = layerId, MovementId = movement.Id, Quantity = quantity, Cost = cost,
        RecordedSequence = movement.RecordedSequence, CreatedAtUtc = DateTimeOffset.UtcNow,
    };
}

/// <summary>
/// Stateful issue-cost quote for a document that has not yet been saved: lines of the same item see what earlier lines of
/// the same document would consume. Create a new quoter after every SaveChanges.
/// </summary>
internal sealed class FifoQuoter(DbContext db)
{
    private readonly Dictionary<(Guid Company, Guid Warehouse, Guid Item), List<FifoLayerBalance>> state = [];

    private async Task<List<FifoLayerBalance>> LayersAsync(Guid company, Guid warehouse, Guid item, CancellationToken token)
    {
        var key = (company, warehouse, item);
        if (state.TryGetValue(key, out var found)) return found;
        var rows = await db.Set<InventoryCostLayerRow>().AsNoTracking().Where(l => l.CompanyId == company && l.WarehouseId == warehouse && l.ItemId == item && l.QuantityRemaining > 0)
            .OrderBy(l => l.SourceSequence).Select(l => new FifoLayerBalance(l.Id, l.QuantityRemaining, l.ValueRemaining)).ToListAsync(token).ConfigureAwait(false);
        state[key] = rows;
        return rows;
    }

    internal async Task<decimal> AvailableAsync(Guid company, Guid warehouse, Guid item, CancellationToken token) =>
        (await LayersAsync(company, warehouse, item, token).ConfigureAwait(false)).Sum(l => l.QuantityRemaining);

    /// <summary>Costs an issue of <paramref name="baseQuantity"/> and consumes it virtually for later quotes of this quoter.</summary>
    internal async Task<decimal> IssueAsync(Guid company, Guid warehouse, Guid item, decimal baseQuantity, CancellationToken token)
    {
        var layers = await LayersAsync(company, warehouse, item, token).ConfigureAwait(false);
        if (!FifoCosting.TryIssue(layers, baseQuantity, out var slices, out var total))
            throw new BusinessRuleException("inventory.fifo_insufficient",
                "رصيد طبقات FIFO لا يكفي / FIFO layers are insufficient for this issue.");
        foreach (var slice in slices)
        {
            var index = layers.FindIndex(l => l.LayerId == slice.LayerId);
            var layer = layers[index];
            var left = layer.QuantityRemaining - slice.Quantity;
            if (left == 0m) layers.RemoveAt(index);
            else layers[index] = layer with { QuantityRemaining = left, ValueRemaining = layer.ValueRemaining - slice.Cost };
        }
        return total;
    }

    /// <summary>Unit cost of the most recent open layer, used to value a count increase of a FIFO item; null when none.</summary>
    internal async Task<decimal?> LatestUnitCostAsync(Guid company, Guid warehouse, Guid item, CancellationToken token)
    {
        var last = await db.Set<InventoryCostLayerRow>().AsNoTracking().Where(l => l.CompanyId == company && l.WarehouseId == warehouse && l.ItemId == item && l.QuantityReceived > 0)
            .OrderByDescending(l => l.SourceSequence).Select(l => new { l.QuantityReceived, l.ValueReceived }).FirstOrDefaultAsync(token).ConfigureAwait(false);
        return last is null ? null : decimal.Round(last.ValueReceived / last.QuantityReceived, 4, MidpointRounding.ToEven);
    }
}
