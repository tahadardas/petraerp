using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory;
using PetraERP.Domain.Inventory;

namespace PetraERP.Infrastructure.Inventory;

public sealed partial class SqlInventoryDocumentService
{
    /// <summary>Historical documents remain read-only. No missing journal, audit balance or snapshot is invented.</summary>
    private async Task<InventoryDocumentResult> LegacyAsync(InventoryDocumentScope s, Guid id, CancellationToken token)
    {
        var codes = await db.Items.AsNoTracking().Where(i => i.CompanyId == s.CompanyId).ToDictionaryAsync(i => i.Id, i => new { i.Code, i.BaseUnitId }, token).ConfigureAwait(false);
        var transfer = await db.StockTransfers.AsNoTracking().Include(d => d.Lines)
            .SingleOrDefaultAsync(d => d.Id == id && d.CompanyId == s.CompanyId && d.SourceBranchId == s.BranchId, token).ConfigureAwait(false);
        if (transfer is not null)
            return new(transfer.Id, transfer.TransferNumber, InventoryDocumentKind.Transfer,
                transfer.Status == (int)StockTransferStatus.Completed ? InventoryDocumentState.Posted : InventoryDocumentState.Draft,
                transfer.SourceWarehouseId, transfer.TargetWarehouseId, transfer.TransferDateUtc, transfer.Notes ?? string.Empty, null, null, null, null, null,
                transfer.CreatedByUserId, transfer.CreatedAtUtc, null, null, Convert.ToBase64String(transfer.RowVersion), transfer.Lines.Select(l =>
                    new InventoryDocumentLineResult(l.Id, l.ItemId, codes.TryGetValue(l.ItemId, out var item) ? item.Code : string.Empty,
                        l.UnitId, l.Quantity, l.BaseQuantity, null, null, l.TotalCost)).ToList()) { IsLegacy = true };
        var count = await db.StockAdjustments.AsNoTracking().Include(d => d.Lines)
            .SingleOrDefaultAsync(d => d.Id == id && d.CompanyId == s.CompanyId && d.BranchId == s.BranchId, token).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("InventoryDocument");
        return new(count.Id, count.AdjustmentNumber, InventoryDocumentKind.Count,
            count.Status == (int)StockAdjustmentStatus.Approved ? InventoryDocumentState.Posted : InventoryDocumentState.Draft,
            count.WarehouseId, null, count.AdjustmentDateUtc, count.Reason, null, null, null, null, null, count.CreatedByUserId,
            count.CreatedAtUtc, count.ApprovedByUserId, count.ApprovedAtUtc, Convert.ToBase64String(count.RowVersion), count.Lines.Select(l =>
                new InventoryDocumentLineResult(l.Id, l.ItemId, codes.TryGetValue(l.ItemId, out var item) ? item.Code : string.Empty,
                    codes.TryGetValue(l.ItemId, out var unit) ? unit.BaseUnitId : l.UnitId, l.CountedQuantity, l.CountedQuantity,
                    l.SystemQuantity, null, null)).ToList()) { IsLegacy = true };
    }
}
