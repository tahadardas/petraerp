using System.Data;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Inventory;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Inventory;

public sealed partial class SqlInventoryDocumentService
{
    public async Task<InventoryDocumentResult> ReverseAsync(InventoryDocumentScope scope, Guid id, InventoryDocumentAction command, CancellationToken token)
    {
        await BindScopeAsync(scope, null, id, token).ConfigureAwait(false);
        var s = scope; var c = command;
        RequireAction(c);
        var tx = await PetraERP.Infrastructure.Persistence.OwnedDatabaseTransaction.BeginAsync(db, IsolationLevel.Serializable, token).ConfigureAwait(false);
        await using var transactionLifetime = tx.ConfigureAwait(false);
        try
        {
            await InventoryWriteScope.LockAsync(db, scope.CompanyId, scope.BranchId, token).ConfigureAwait(false);
            var row = await Documents(s).Include(d => d.Lines).SingleOrDefaultAsync(d => d.Id == id, token).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("InventoryDocument");
            await AuthorizeAsync(s, (InventoryDocumentKind)row.Kind, token).ConfigureAwait(false);
            if (row.ReverseKey == command.IdempotencyKey)
            {
                if (row.ReverseVersion != command.ExpectedVersion || row.ReversedBy != scope.UserId || row.ReverseReason != command.Reason)
                    throw Error("idempotency_conflict", "مفتاح العكس مرتبط بطلب مختلف", "Reversal key is bound to another request.");
                await tx.CommitAsync(token).ConfigureAwait(false);
                return await GetAsync(s, id, token).ConfigureAwait(false);
            }
            RequireVersion(row, command.ExpectedVersion);
            if (row.State != (int)InventoryDocumentState.Posted) throw Error("not_posted", "يمكن عكس مستند مرحل فقط", "Only a posted document can be reversed.");
            var now = time.GetUtcNow(); await RequirePeriodAsync(scope.CompanyId, now, token).ConfigureAwait(false);
            var movements = await db.StockMovements.AsNoTracking().Where(m => m.CompanyId == scope.CompanyId && m.BranchId == scope.BranchId && m.DocumentId == id
                && (m.DocumentType == "InventoryDocumentIn" || m.DocumentType == "InventoryDocumentOut")).OrderByDescending(m => m.RecordedSequence).ToListAsync(token).ConfigureAwait(false);
            foreach (var original in movements)
            {
                var balance = await InventoryMovementGuard.BalanceAsync(db, scope.CompanyId, original.WarehouseId, original.ItemId, token).ConfigureAwait(false);
                if (balance.LastSequence != original.RecordedSequence)
                    throw Error("reversal_dependency", "توجد حركات لاحقة؛ استخدم تسوية مراجعة بدلاً من عكس التاريخ", "Later movements exist; use a reviewed adjustment rather than reversing history.");
                db.StockMovements.Add(new StockMovementRow
                {
                    Id = Guid.NewGuid(), CompanyId = scope.CompanyId, BranchId = scope.BranchId, WarehouseId = original.WarehouseId,
                    ItemId = original.ItemId, UnitId = original.UnitId, MovementKind = (int)(original.BaseQuantity < 0 ? StockMovementKind.AdjustmentIncrease : StockMovementKind.AdjustmentDecrease),
                    Quantity = original.Quantity, BaseQuantity = -original.BaseQuantity, UnitCost = original.UnitCost, TotalCost = original.TotalCost,
                    MovementDateUtc = now, DocumentType = "InventoryReversal", DocumentId = id, DocumentLineId = original.Id,
                    DocumentNumber = row.Number, CostCenterId = original.CostCenterId, Notes = command.Reason, CreatedByUserId = scope.UserId,
                    CreatedAtUtc = now, ReversesMovementId = original.Id
                });
            }
            row.State = (int)InventoryDocumentState.Reversed; row.ReverseKey = command.IdempotencyKey; row.ReverseVersion = command.ExpectedVersion;
            row.ReverseReason = command.Reason; row.ReversedBy = scope.UserId; row.ReversedAtUtc = now;
            if (row.JournalId is { } originalId)
            {
                var journalService = new SqlJournalEntryService(db, time); var journalScope = JournalEntryScope.Create(scope.CompanyId, scope.BranchId, scope.UserId);
                var original = await journalService.GetJournalAsync(journalScope, originalId, token).ConfigureAwait(false);
                var journal = await journalService.CreateReversalDraftAsync(journalScope, new CreateReversalDraftCommand
                {
                    OriginalJournalId = originalId, OriginalExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(original.VersionBase64)),
                    AccountingDate = DateOnly.FromDateTime(now.UtcDateTime), Reason = command.Reason
                }, token).ConfigureAwait(false);
                journal = await ApproveAsync(s, journal, command.Reason, token).ConfigureAwait(false);
                row.ReversalJournalId = journal.Id;
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                await new SqlPostingService(db).PostInventoryAsync(id, PostingRequest.Reverse(PostingRequestScope.Create(scope.CompanyId, scope.BranchId, scope.UserId),
                    Guid.NewGuid(), journal.Id, ExpectedJournalVersion.FromBase64(journal.VersionBase64), originalId,
                    ExpectedJournalVersion.FromBase64(original.VersionBase64), AuditReason.Create(command.Reason)), token).ConfigureAwait(false);
            }
            else await db.SaveChangesAsync(token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
        }
        catch { db.ChangeTracker.Clear(); throw; }
        return await GetAsync(s, id, token).ConfigureAwait(false);
    }
}
