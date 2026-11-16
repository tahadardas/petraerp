using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory;
using PetraERP.Application.Security;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Inventory;

public sealed partial class SqlInventoryDocumentService
{
    private Task ViewAsync(InventoryDocumentScope s, CancellationToken token) => new SqlPostingService(db)
        .VerifyInventoryActorAsync(s, PermissionCatalog.InventoryMovementsView, token);

    public async Task<InventoryDocumentPage> ListAsync(InventoryDocumentScope scope, string? search, int page, int pageSize, CancellationToken token)
    {
        await BindScopeAsync(scope, null, null, token).ConfigureAwait(false);
        var s = scope;
        await ViewAsync(scope, token).ConfigureAwait(false);
        var paging = PageRequest.Create(page, pageSize); page = paging.Page; pageSize = paging.PageSize;
        if (search?.Length > 200) throw Error("search", "البحث بحد أقصى 200 حرف", "Search is limited to 200 characters.");
        var query = Documents(s).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(d => d.Reason.Contains(search) || (d.Number != null && d.Number.Contains(search)));
        var total = await query.CountAsync(token).ConfigureAwait(false);
        var rows = await query.OrderByDescending(d => d.CreatedAtUtc).ThenBy(d => d.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(token).ConfigureAwait(false);
        return new(page, pageSize, total, rows.Select(r => Map(r, new Dictionary<Guid, string>())).ToList());
    }

    public async Task<InventoryDocumentResult> GetAsync(InventoryDocumentScope scope, Guid id, CancellationToken token)
    {
        await BindScopeAsync(scope, null, null, token).ConfigureAwait(false);
        var s = scope;
        await ViewAsync(scope, token).ConfigureAwait(false);
        var row = await Documents(s).AsNoTracking().Include(d => d.Lines).SingleOrDefaultAsync(d => d.Id == id, token).ConfigureAwait(false);
        if (row is null) return await LegacyAsync(s, id, token).ConfigureAwait(false);
        var ids = row.Lines.Select(l => l.ItemId).ToArray();
        var codes = await db.Items.AsNoTracking().Where(i => i.CompanyId == s.CompanyId && ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, token).ConfigureAwait(false);
        return Map(row, codes);
    }

    private static InventoryDocumentResult Map(InventoryDocumentRow d, IReadOnlyDictionary<Guid, string> codes) => new(d.Id, d.Number,
        (InventoryDocumentKind)d.Kind, (InventoryDocumentState)d.State, d.WarehouseId, d.TargetWarehouseId, d.DateUtc, d.Reason, d.CostCenterId,
        d.InventoryAccountId, d.OffsetAccountId, d.JournalId, d.ReversalJournalId, d.CreatedBy, d.CreatedAtUtc, d.PostedBy, d.PostedAtUtc,
        Convert.ToBase64String(d.RowVersion), d.Lines.OrderBy(l => l.Position).Select(l => new InventoryDocumentLineResult(l.Id, l.ItemId,
            codes.GetValueOrDefault(l.ItemId, string.Empty), l.UnitId, l.Quantity, l.BaseQuantity, l.SnapshotQuantity, l.SnapshotSequence, l.Cost) { OpeningUnitCost = l.OpeningUnitCost }).ToList())
        { PostReason = d.PostReason, ReverseReason = d.ReverseReason, ReversedBy = d.ReversedBy, ReversedAtUtc = d.ReversedAtUtc };

    public async Task<IReadOnlyList<InventoryBalanceResult>> BalancesAsync(InventoryDocumentScope scope, Guid? warehouseId, Guid? itemId, CancellationToken token)
    {
        await BindScopeAsync(scope, null, null, token).ConfigureAwait(false);
        var s = scope;
        await ViewAsync(scope, token).ConfigureAwait(false);
        var transaction = await PetraERP.Infrastructure.Persistence.OwnedDatabaseTransaction.BeginAsync(db, System.Data.IsolationLevel.Serializable, token).ConfigureAwait(false);
        await using var transactionLifetime = transaction.ConfigureAwait(false);
        await InventoryWriteScope.LockAsync(db, s.CompanyId, s.BranchId, token).ConfigureAwait(false);
        var rows = await db.StockMovements.AsNoTracking().Where(m => m.CompanyId == scope.CompanyId && m.BranchId == scope.BranchId
            && (!warehouseId.HasValue || m.WarehouseId == warehouseId) && (!itemId.HasValue || m.ItemId == itemId))
            .GroupBy(m => new { m.WarehouseId, m.ItemId }).OrderBy(g => g.Key.WarehouseId).ThenBy(g => g.Key.ItemId)
            .Select(g => new InventoryBalanceResult(g.Key.WarehouseId, g.Key.ItemId,
                g.Sum(m => m.BaseQuantity), g.Sum(m => m.BaseQuantity < 0 ? -m.TotalCost : m.TotalCost), g.Max(m => m.RecordedSequence))
                {
                    WarehouseCode = db.Warehouses.Where(w => w.Id == g.Key.WarehouseId).Select(w => w.Code).FirstOrDefault() ?? string.Empty,
                    ItemCode = db.Items.Where(i => i.Id == g.Key.ItemId).Select(i => i.Code).FirstOrDefault() ?? string.Empty
                })
            .ToListAsync(token).ConfigureAwait(false);
        var sequences = rows.Select(r => r.LastSequence).ToArray();
        var audit = await db.StockMovements.AsNoTracking().Where(m => m.CompanyId == scope.CompanyId && m.BranchId == scope.BranchId && sequences.Contains(m.RecordedSequence))
            .Select(m => new { m.RecordedSequence, m.QuantityAfter, m.ValueAfter }).ToDictionaryAsync(m => m.RecordedSequence, token).ConfigureAwait(false);
        var result = rows.Select(r => audit.TryGetValue(r.LastSequence, out var last)
            ? r with { AuditQuantity = last.QuantityAfter, AuditValue = last.ValueAfter,
                MatchesAudit = last.QuantityAfter.HasValue && last.ValueAfter.HasValue ? r.Quantity == last.QuantityAfter && r.Value == last.ValueAfter : null }
            : r).ToList();
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return result;
    }

    public async Task<InventoryMovementAuditPage> MovementsAsync(InventoryDocumentScope scope, Guid? warehouseId, Guid? itemId,
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int page, int pageSize, CancellationToken token)
    {
        await BindScopeAsync(scope, null, null, token).ConfigureAwait(false);
        var s = scope;
        await ViewAsync(scope, token).ConfigureAwait(false);
        var paging = PageRequest.Create(page, pageSize); page = paging.Page; pageSize = paging.PageSize;
        if (fromUtc > toUtc) throw Error("range", "بداية الفترة بعد نهايتها", "Start date is after end date.");
        var query = db.StockMovements.AsNoTracking().Where(m => m.CompanyId == scope.CompanyId && m.BranchId == scope.BranchId
            && (!warehouseId.HasValue || m.WarehouseId == warehouseId) && (!itemId.HasValue || m.ItemId == itemId)
            && (!fromUtc.HasValue || m.MovementDateUtc >= fromUtc) && (!toUtc.HasValue || m.MovementDateUtc <= toUtc));
        var total = await query.CountAsync(token).ConfigureAwait(false);
        var rows = await query.OrderByDescending(m => m.RecordedSequence).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new InventoryMovementAuditResult(m.Id, m.RecordedSequence, m.WarehouseId, m.ItemId, (StockMovementKind)m.MovementKind, m.BaseQuantity,
                m.BaseQuantity < 0 ? -m.TotalCost : m.TotalCost, m.QuantityBefore, m.QuantityAfter, m.ValueBefore, m.ValueAfter,
                m.DocumentType, m.DocumentId, m.DocumentNumber, m.CreatedByUserId, m.MovementDateUtc, m.RecordedAtUtc, m.Notes, m.ReversesMovementId)
                {
                    WarehouseCode = db.Warehouses.Where(w => w.Id == m.WarehouseId).Select(w => w.Code).FirstOrDefault() ?? string.Empty,
                    ItemCode = db.Items.Where(i => i.Id == m.ItemId).Select(i => i.Code).FirstOrDefault() ?? string.Empty
                })
            .ToListAsync(token).ConfigureAwait(false);
        return new(page, pageSize, total, rows);
    }
}
