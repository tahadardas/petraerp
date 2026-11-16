using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

/// <summary>Mandatory write boundary, including sales/purchases/returns. Balances are always rebuilt from movements.</summary>
internal static class InventoryMovementGuard
{
    internal sealed record Balance(decimal Quantity, decimal Value, long LastSequence)
    {
        internal decimal Cost(decimal quantity) => WeightedAverageCosting.TryValue(Quantity, Value, quantity, out var cost) ? cost
            : throw new BusinessRuleException("inventory.valuation_precision", "تعذر حساب WAC ضمن حدود الدقة / WAC cannot be represented at storage precision.");
    }

    internal static async Task<Balance> BalanceAsync(DbContext db, Guid company, Guid warehouse, Guid item, CancellationToken token)
    {
        var value = await db.Set<StockMovementRow>().AsNoTracking().Where(m => m.CompanyId == company && m.WarehouseId == warehouse && m.ItemId == item)
            .GroupBy(m => m.ItemId).Select(g => new { Quantity = g.Sum(m => m.BaseQuantity), Value = g.Sum(m => m.BaseQuantity < 0 ? -m.TotalCost : m.TotalCost), Last = g.Max(m => m.RecordedSequence) })
            .SingleOrDefaultAsync(token).ConfigureAwait(false);
        return value is null ? new(0m, 0m, 0) : new(value.Quantity, value.Value, value.Last);
    }

    internal static async Task ApplyAsync(DbContext db, CancellationToken token)
    {
        if (db.ChangeTracker.Entries<StockMovementRow>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new BusinessRuleException("inventory.immutable", "حركات المخزون لا تعدّل؛ استخدم العكس / Stock movements are immutable; use a linked reversal.");
        var added = db.ChangeTracker.Entries<StockMovementRow>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToArray();
        var authorization = (db as FinancialDbContext)?.NegativeStockAuthorization;
        foreach (var scope in added.Select(m => (m.CompanyId, m.BranchId)).Distinct().OrderBy(s => s.CompanyId).ThenBy(s => s.BranchId))
            await InventoryWriteScope.LockAsync(db, scope.CompanyId, scope.BranchId, token).ConfigureAwait(false);
        foreach (var group in added.GroupBy(m => (m.CompanyId, m.BranchId, m.WarehouseId, m.ItemId)).OrderBy(g => g.Key.CompanyId).ThenBy(g => g.Key.WarehouseId).ThenBy(g => g.Key.ItemId))
        {
            var key = group.Key;
            await InventoryWriteScope.ValidateWarehouseAsync(db, key.CompanyId, key.BranchId, key.WarehouseId, token).ConfigureAwait(false);
            var item = await db.Set<ItemRow>().AsNoTracking().SingleOrDefaultAsync(i => i.Id == key.ItemId && i.CompanyId == key.CompanyId, token).ConfigureAwait(false)
                ?? throw new BusinessRuleException("inventory.item_scope", "المادة خارج الشركة / Item is outside company scope.");
            if (!item.IsActive || item.CostMethod is not ((int)PetraERP.Domain.Inventory.CostMethod.WeightedAverage or (int)PetraERP.Domain.Inventory.CostMethod.Fifo))
                throw new BusinessRuleException("inventory.cost_method_invalid", "المادة يجب أن تكون فعالة بطريقة تكلفة مدعومة (متوسط مرجح أو FIFO) / The item must be active with a supported cost method (weighted average or FIFO).");
            var fifo = item.CostMethod == (int)PetraERP.Domain.Inventory.CostMethod.Fifo;
            var balance = await BalanceAsync(db, key.CompanyId, key.WarehouseId, key.ItemId, token).ConfigureAwait(false);
            if (!IsCoherent(balance.Quantity, balance.Value))
                throw new BusinessRuleException("inventory.history_inconsistent", "الرصيد التاريخي غير سليم؛ يلزم تصحيح محاسبي معتمد قبل حركات جديدة / Historical stock is inconsistent; an approved accounting remediation is required before new movements.");
            List<InventoryCostLayerRow>? openLayers = null;
            if (fifo)
            {
                openLayers = await FifoLedger.LoadOpenLayersAsync(db, key.CompanyId, key.WarehouseId, key.ItemId, token).ConfigureAwait(false);
                FifoLedger.RequireConsistent(openLayers, balance.Quantity, balance.Value);
            }
            foreach (var movement in group)
            {
                if (movement.BaseQuantity == 0 || movement.Quantity <= 0 || movement.TotalCost < 0 || movement.UnitCost < 0
                    || movement.DocumentId is null || movement.DocumentId == Guid.Empty || movement.CreatedByUserId == Guid.Empty)
                    throw new BusinessRuleException("inventory.movement_invalid", "كمية أو تكلفة أو مصدر الحركة غير صالح / Invalid movement quantity, cost or source.");
                if (((StockMovementKind)movement.MovementKind).DirectionSign() != Math.Sign(movement.BaseQuantity))
                    throw new BusinessRuleException("inventory.direction_invalid", "اتجاه الحركة لا يطابق نوعها / Movement direction does not match its kind.");
                if (movement.ReversesMovementId is { } originalId && !await db.Set<StockMovementRow>().AsNoTracking().AnyAsync(m => m.Id == originalId
                    && m.CompanyId == key.CompanyId && m.BranchId == key.BranchId && m.WarehouseId == key.WarehouseId && m.ItemId == key.ItemId
                    && m.BaseQuantity == -movement.BaseQuantity && m.TotalCost == movement.TotalCost, token).ConfigureAwait(false))
                    throw new BusinessRuleException("inventory.reversal_invalid", "حركة العكس لا تطابق أصلها ونطاقها / Reversal does not match its original movement and scope.");
                if (!await db.Set<UnitOfMeasureRow>().AsNoTracking().AnyAsync(u => u.Id == movement.UnitId && u.CompanyId == key.CompanyId, token).ConfigureAwait(false))
                    throw new BusinessRuleException("inventory.unit_scope", "الوحدة خارج الشركة / Unit is outside company scope.");
                var after = balance.Quantity + movement.BaseQuantity;
                var value = balance.Value + (movement.BaseQuantity < 0 ? -movement.TotalCost : movement.TotalCost);
                const decimal maximum = WeightedAverageCosting.MaximumStoredValue;
                if (balance.Quantity > maximum || balance.Value > maximum || after > maximum || value > maximum
                    || movement.UnitCost > maximum || decimal.Round(movement.BaseQuantity, 4) != movement.BaseQuantity || decimal.Round(movement.TotalCost, 4) != movement.TotalCost)
                    throw new BusinessRuleException("inventory.precision", "تجاوزت الكمية أو القيمة حدود الدقة / Stock quantity or value exceeds storage precision.");
                if (after < 0 && value > 0 || after >= 0 && value < 0 || after == 0 && value != 0)
                    throw new BusinessRuleException("inventory.valuation_mismatch", "قيمة المخزون لا تطابق كميته بعد الحركة؛ صحح تكلفة الاستلام أو نفّذ تسوية معتمدة قبل المتابعة / Stock value would not match its quantity after this movement; correct the receipt cost or post an approved adjustment before continuing.");
                // A coherent negative balance is only reachable through an issue that the posting service explicitly
                // authorized (company policy + approval grant + reason). Receipts may reduce an existing negative
                // balance back towards zero without a new exception.
                if (after < 0 && movement.BaseQuantity < 0
                    && (authorization is null || !authorization.Covers(key.CompanyId, key.BranchId, key.WarehouseId, key.ItemId)))
                    throw new BusinessRuleException("inventory.negative_stock_blocked", "الإخراج بالسالب غير مصرح لهذه الحركة؛ فعّل سياسة الاستثناء المفوّض وامنح صلاحية اعتماد الإخراج بالسالب مع سبب موثق / A negative issue is not authorized; enable the authorized-exception policy, grant the negative-issue approval permission and record a reason.");
                movement.QuantityBefore = balance.Quantity; movement.QuantityAfter = after;
                movement.ValueBefore = balance.Value; movement.ValueAfter = value;
                // Assign order before EF batching: SQL identity/insert order is not a guaranteed audit order.
                // Sequence gaps after rollback are harmless; official document counters remain transactional.
                var sequence = await db.Database.SqlQueryRaw<long>("SELECT NEXT VALUE FOR [inventory].[MovementOrder] AS [Value]")
                    .ToListAsync(token).ConfigureAwait(false);
                movement.RecordedSequence = sequence.Single();
                movement.RecordedAtUtc = DateTimeOffset.UtcNow;
                if (openLayers is not null) await FifoLedger.ApplyAsync(db, movement, openLayers, token).ConfigureAwait(false);
                balance = new(after, value, balance.LastSequence);
            }
        }
    }

    private static bool IsCoherent(decimal quantity, decimal value) =>
        quantity > 0 ? value >= 0 : quantity < 0 ? value <= 0 : value == 0;
}
