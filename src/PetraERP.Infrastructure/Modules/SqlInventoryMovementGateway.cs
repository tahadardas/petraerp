using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory;
using PetraERP.Application.Security;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Modules.Abstractions;
using BalanceResult = PetraERP.Modules.Abstractions.InventoryBalanceResult;

namespace PetraERP.Infrastructure.Modules;

/// <summary>
/// Stock changes by a module are ordinary warehouse transfer documents: they take the same locks, cost-layer rules,
/// negative-stock guard and audit as the built-in screen, and a module can never insert movement rows or restate a cost.
/// The caller's idempotency key becomes both the document creation key and the posting key, so a replay returns the
/// original movement instead of moving stock twice.
/// </summary>
public sealed class SqlInventoryMovementGateway(IModuleLedgerContexts contexts, TimeProvider time) : IInventoryMovementGateway
{
    public async Task<BalanceResult> QueryBalanceAsync(ModuleRequestContext context, Guid itemId, Guid warehouseId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        RequireScope(context);
        if (itemId == Guid.Empty || warehouseId == Guid.Empty) throw new BusinessRuleException("module.inventory_invalid", "item and warehouse are required.");
        // Reads use the same operation and never save.
        var read = await contexts.OpenAsync(context, AccountingOperation.ManageTransfers, cancellationToken).ConfigureAwait(false);
        await using var readLifetime = read.ConfigureAwait(false);
        var baseUnit = await read.Db.Items.AsNoTracking().Where(i => i.Id == itemId && i.CompanyId == context.CompanyId)
            .Select(i => (Guid?)i.BaseUnitId).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("module.inventory_invalid", "the item does not belong to the company.");
        var rows = await new SqlInventoryDocumentService(read.Db, time).BalancesAsync(Scope(context), warehouseId, itemId, cancellationToken).ConfigureAwait(false);
        return new BalanceResult(rows.Select(r => new InventoryBalanceLine(
            r.WarehouseId, baseUnit, r.Quantity, r.Value, r.Quantity > 0 ? decimal.Round(r.Value / r.Quantity, 4, MidpointRounding.ToEven) : null)).ToList());
    }

    public async Task<InventoryMovementReceipt> MoveAsync(ModuleRequestContext context, InventoryMovementRequest movement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(movement);
        RequireScope(context);
        if (!context.Has(PermissionCatalog.InventoryTransfersManage))
            throw new PetraERP.Application.GeneralLedger.Posting.PostingAuthorizationException($"The module was not granted '{PermissionCatalog.InventoryTransfersManage}'.");
        if (movement.IdempotencyKey == Guid.Empty) throw new BusinessRuleException("module.inventory_invalid", "an idempotency key is required.");
        if (movement.ItemId == Guid.Empty || movement.UnitId == Guid.Empty) throw new BusinessRuleException("module.inventory_invalid", "item and unit are required.");
        if (movement.FromWarehouseId == Guid.Empty || movement.ToWarehouseId == Guid.Empty || movement.FromWarehouseId == movement.ToWarehouseId)
            throw new BusinessRuleException("module.inventory_invalid", "two different warehouses are required.");
        if (movement.Quantity <= 0) throw new BusinessRuleException("module.inventory_invalid", "the quantity must be positive.");
        if (string.IsNullOrWhiteSpace(movement.Reason)) throw new BusinessRuleException("module.inventory_invalid", "a reason is required.");

        var scope = Scope(context);
        var key = movement.IdempotencyKey.ToString("N");
        var reason = movement.Reason.Trim();
        var date = new DateTimeOffset(movement.MovementDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        // A transfer document is written under the "manage transfers" operation, which the engine composes with the stock-movement
        // and cost-layer writes; the database layer checks the real user for exactly that permission.
        var write = await contexts.OpenAsync(context, AccountingOperation.ManageTransfers, cancellationToken).ConfigureAwait(false);
        await using var writeLifetime = write.ConfigureAwait(false);
        var documents = new SqlInventoryDocumentService(write.Db, time);
        var draft = await documents.SaveAsync(scope, null, new SaveInventoryDocumentCommand(InventoryDocumentKind.Transfer, movement.FromWarehouseId,
            movement.ToWarehouseId, date, reason, null, null, null,
            [new InventoryDocumentLineInput(movement.ItemId, movement.UnitId, movement.Quantity)], null) { CreationKey = key }, cancellationToken).ConfigureAwait(false);

        var alreadyRecorded = draft.State != InventoryDocumentState.Draft;
        var posted = alreadyRecorded
            ? draft
            : await documents.PostAsync(scope, draft.Id, new InventoryDocumentAction(draft.Version, key, reason), cancellationToken).ConfigureAwait(false);
        if (posted.State == InventoryDocumentState.Draft)
            throw new BusinessRuleException("module.inventory_invalid", "the movement document was not posted.");
        return new InventoryMovementReceipt(posted.Id, posted.PostedAtUtc ?? posted.CreatedAtUtc, alreadyRecorded);
    }

    private static InventoryDocumentScope Scope(ModuleRequestContext context) => new(context.CompanyId, context.BranchId, context.UserId);

    private static void RequireScope(ModuleRequestContext context)
    {
        if (context.CompanyId == Guid.Empty || context.BranchId == Guid.Empty || context.UserId == Guid.Empty)
            throw new BusinessRuleException("module.inventory_invalid", "company, branch and user are required.");
    }
}
