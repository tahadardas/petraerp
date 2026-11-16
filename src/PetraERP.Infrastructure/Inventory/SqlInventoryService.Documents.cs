using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Inventory;

/// <summary>Compatibility routes use the same engine. Caller UnitCost/official document numbers are never authoritative.</summary>
public sealed partial class SqlInventoryService
{
    public async Task<StockTransferResult> CreateStockTransferAsync(Guid companyId, Guid branchId, Guid userId,
        CreateStockTransferCommand command, CancellationToken cancellationToken = default)
    {
        if (command.TargetWarehouseId == command.SourceWarehouseId)
            throw new BusinessRuleException("inventory.transfer_same_warehouse", "المستودع المصدر ومستودع الوجهة متطابقان / Source and target warehouses cannot be the same.", "targetWarehouseId");
        if (command.TargetBranchId != branchId) throw new BusinessRuleException("inventory.cross_branch_policy",
            "المناقلة الحالية بين مستودعات الفرع نفسه؛ المناقلة بين الفروع تتطلب حسابات تسوية مستقلة / This transfer is intra-branch; interbranch transfers require a separate clearing-account policy.");
        var saved = await PostLegacyAsync(new(companyId, branchId, userId), new(InventoryDocumentKind.Transfer, command.SourceWarehouseId,
            command.TargetWarehouseId, command.TransferDateUtc, command.Notes ?? "مناقلة / Transfer", null, null, null,
            command.Lines.Select(l => new InventoryDocumentLineInput(l.ItemId, l.UnitId, l.Quantity)).ToList(), null),
            command.IdempotencyKey ?? command.TransferNumber, false, cancellationToken).ConfigureAwait(false);
        return new(saved.Id, saved.Number!, branchId, saved.WarehouseId, branchId, saved.TargetWarehouseId!.Value, saved.DateUtc,
            StockTransferStatus.Completed, saved.Reason, saved.Lines.Select(l => new StockTransferLineResult(l.Id, l.ItemId, l.ItemCode, l.ItemCode,
                l.UnitId, string.Empty, l.Quantity, l.BaseQuantity, l.BaseQuantity == 0 ? 0 : decimal.Round(KnownCost(l) / l.BaseQuantity, 4, MidpointRounding.ToEven), KnownCost(l))).ToList());
    }


    private async Task<(Guid inv, Guid off)> EnsureDefaultAccountsAsync(Guid companyId, CancellationToken token)
    {
        var inv = await context.Accounts.FirstOrDefaultAsync(a => a.CompanyId == companyId && a.Code == "1210", token).ConfigureAwait(false);
        var off = await context.Accounts.FirstOrDefaultAsync(a => a.CompanyId == companyId && a.Code == "5110", token).ConfigureAwait(false);
        if (inv is null)
        {
            inv = new AccountRow
            {
                Id = Guid.CreateVersion7(),
                CompanyId = companyId,
                Code = "1210",
                NameArabic = "بضاعة المخزون",
                AccountType = 1,
                PostingAllowed = true,
                IsActive = true,
                CurrencyMode = 2,
                CostCenterPolicy = 2,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            context.Accounts.Add(inv);
        }
        if (off is null)
        {
            off = new AccountRow
            {
                Id = Guid.CreateVersion7(),
                CompanyId = companyId,
                Code = "5110",
                NameArabic = "فروق ومصاريف المخزون",
                AccountType = 5,
                PostingAllowed = true,
                IsActive = true,
                CurrencyMode = 2,
                CostCenterPolicy = 2,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            context.Accounts.Add(off);
        }
        if (context.ChangeTracker.HasChanges())
            await context.SaveChangesAsync(token).ConfigureAwait(false);
        return (inv.Id, off.Id);
    }

    public async Task<StockAdjustmentResult> CreateStockAdjustmentAsync(Guid companyId, Guid branchId, Guid userId,
        CreateStockAdjustmentCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new BusinessRuleException("inventory.adjustment_reason_required", "سبب الجرد مطلوب / Reason is required for stock adjustment.", "reason");

        var invId = command.InventoryAccountId;
        var offId = command.OffsetAccountId;
        if (invId is null || offId is null)
        {
            var defaults = await EnsureDefaultAccountsAsync(companyId, cancellationToken).ConfigureAwait(false);
            invId ??= defaults.inv;
            offId ??= defaults.off;
        }

        var result = await new SqlInventoryDocumentService(context, Clock).SaveAsync(new(companyId, branchId, userId), null,
            new(InventoryDocumentKind.Count, command.WarehouseId, null, command.AdjustmentDateUtc, command.Reason, null,
                invId, offId, command.Lines.Select(l => new InventoryDocumentLineInput(l.ItemId, l.UnitId, l.CountedQuantity)).ToList(), null), cancellationToken).ConfigureAwait(false);
        return Adjustment(result, branchId);
    }

    public async Task<StockAdjustmentResult> ApproveStockAdjustmentAsync(Guid companyId, Guid branchId, Guid adjustmentId, Guid userId,
        CancellationToken cancellationToken = default)
    {
        var doc = await context.InventoryDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == adjustmentId && d.CompanyId == companyId && d.BranchId == branchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("InventoryDocument");
        return await ApproveStockAdjustmentAsync(companyId, branchId, adjustmentId, userId,
            new(Convert.ToBase64String(doc.RowVersion), Guid.NewGuid().ToString("N"), doc.Reason), cancellationToken).ConfigureAwait(false);
    }

    public async Task<StockAdjustmentResult> ApproveStockAdjustmentAsync(Guid companyId, Guid branchId, Guid adjustmentId, Guid userId,
        InventoryDocumentAction command, CancellationToken cancellationToken = default) => Adjustment(await new SqlInventoryDocumentService(context, Clock)
            .PostAsync(new(companyId, branchId, userId), adjustmentId, command, cancellationToken).ConfigureAwait(false), branchId);

    private static StockAdjustmentResult Adjustment(InventoryDocumentResult r, Guid branch) => new(r.Id, r.Number ?? string.Empty, branch,
        r.WarehouseId, r.DateUtc, r.Reason, r.State == InventoryDocumentState.Draft ? StockAdjustmentStatus.Draft : StockAdjustmentStatus.Approved,
        r.PostedBy, r.PostedAtUtc, r.Reason, r.Lines.Select(l => new StockAdjustmentLineResult(l.Id, l.ItemId, l.ItemCode, l.ItemCode, l.UnitId,
            string.Empty, l.SnapshotQuantity ?? 0, l.BaseQuantity, l.BaseQuantity - (l.SnapshotQuantity ?? 0),
            l.BaseQuantity == l.SnapshotQuantity ? 0 : decimal.Round(KnownCost(l) / Math.Abs(l.BaseQuantity - (l.SnapshotQuantity ?? 0)), 4, MidpointRounding.ToEven))).ToList()) { Version = r.Version };

    private static decimal KnownCost(InventoryDocumentLineResult line) => line.Cost
        ?? throw new BusinessRuleException("inventory.valuation_missing", "التكلفة التاريخية غير متاحة / Historical valuation is unavailable.");

    public async Task<StockMovementResult> PostInternalIssueAsync(Guid companyId, Guid branchId, Guid userId, PostInternalIssueCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.CostCenterId == Guid.Empty)
            throw new BusinessRuleException("inventory.cost_center_required", "مركز التكلفة مطلوب للصرف الداخلي / Cost center is required for internal issue.", "costCenterId");

        var invId = command.InventoryAccountId;
        var offId = command.OffsetAccountId;
        if (invId is null || offId is null)
        {
            var defaults = await EnsureDefaultAccountsAsync(companyId, cancellationToken).ConfigureAwait(false);
            invId ??= defaults.inv;
            offId ??= defaults.off;
        }

        var doc = await PostLegacyAsync(new(companyId, branchId, userId), new(InventoryDocumentKind.InternalIssue, command.WarehouseId, null,
            Clock.GetUtcNow(), command.Notes ?? "صرف داخلي / Internal issue", command.CostCenterId, invId, offId,
            [new(command.ItemId, command.UnitId, command.Quantity)], null), command.IdempotencyKey ?? command.DocumentNumber, true, cancellationToken).ConfigureAwait(false);
        var movement = await context.StockMovements.AsNoTracking().SingleAsync(m => m.CompanyId == companyId && m.BranchId == branchId && m.DocumentId == doc.Id
            && m.DocumentType == "InventoryDocumentOut", cancellationToken).ConfigureAwait(false);
        var ccCode = command.CostCenterId != Guid.Empty
            ? await context.CostCenters.Where(c => c.Id == command.CostCenterId).Select(c => c.Code).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        return new(movement.Id, companyId, branchId, movement.WarehouseId, string.Empty, movement.ItemId, doc.Lines[0].ItemCode,
            doc.Lines[0].ItemCode, movement.UnitId, string.Empty, StockMovementKind.InternalIssue, movement.Quantity, movement.BaseQuantity,
            movement.UnitCost, movement.TotalCost, movement.MovementDateUtc, movement.DocumentType, movement.DocumentNumber, movement.CostCenterId, ccCode, movement.Notes);
    }

    private async Task<InventoryDocumentResult> PostLegacyAsync(InventoryDocumentScope scope, SaveInventoryDocumentCommand command, string? key,
        bool ignoreDate, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new BusinessRuleException("inventory.key_required", "مفتاح طلب ثابت مطلوب / A stable request key is required.");
        var canonical = command with { DateUtc = ignoreDate ? DateTimeOffset.UnixEpoch : command.DateUtc.ToUniversalTime() };
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical)));
        var transaction = await PetraERP.Infrastructure.Persistence.OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, token).ConfigureAwait(false);
        await using var transactionLifetime = transaction.ConfigureAwait(false);
        try
        {
            await InventoryWriteScope.LockAsync(context, scope.CompanyId, scope.BranchId, token).ConfigureAwait(false);
            await new GeneralLedger.SqlPostingService(context).VerifyInventoryActorAsync(scope, SqlInventoryDocumentService.Permission(command.Kind), token).ConfigureAwait(false);
            var service = new SqlInventoryDocumentService(context, Clock);
            var existing = await context.InventoryDocuments.SingleOrDefaultAsync(d => d.CompanyId == scope.CompanyId && d.BranchId == scope.BranchId && d.PostKey == key, token).ConfigureAwait(false);
            InventoryDocumentResult result;
            if (existing is not null)
            {
                if (existing.CreationFingerprint != fingerprint || existing.PostedBy != scope.UserId)
                    throw new BusinessRuleException("inventory.idempotency_conflict", "مفتاح الطلب مستخدم لبيانات أو مستخدم مختلف / Request key belongs to a different payload or actor.");
                result = await service.GetAsync(scope, existing.Id, token).ConfigureAwait(false);
            }
            else
            {
                var draft = await service.SaveAsync(scope, null, command, token).ConfigureAwait(false);
                var row = await context.InventoryDocuments.SingleAsync(d => d.Id == draft.Id, token).ConfigureAwait(false);
                row.CreationFingerprint = fingerprint;
                result = await service.PostAsync(scope, draft.Id, new(draft.Version, key, command.Reason), token).ConfigureAwait(false);
            }
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }
        catch { context.ChangeTracker.Clear(); throw; }
    }
}
