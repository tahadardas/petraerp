using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory;
using PetraERP.Application.Security;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

public sealed partial class SqlInventoryDocumentService(FinancialDbContext db, TimeProvider time) : IInventoryDocumentService
{
    private IQueryable<InventoryDocumentRow> Documents(InventoryDocumentScope s) => db.InventoryDocuments
        .Where(d => d.CompanyId == s.CompanyId && d.BranchId == s.BranchId);

    internal static string Permission(InventoryDocumentKind kind) => kind == InventoryDocumentKind.Transfer
        ? PermissionCatalog.InventoryTransfersManage : PermissionCatalog.InventoryAdjustmentsManage;

    private async Task AuthorizeAsync(InventoryDocumentScope s, InventoryDocumentKind kind, CancellationToken token)
    {
        await ViewAsync(s, token).ConfigureAwait(false);
        await new SqlPostingService(db).VerifyInventoryActorAsync(s, Permission(kind), token).ConfigureAwait(false);
    }

    public async Task<InventoryDocumentResult> SaveAsync(InventoryDocumentScope scope, Guid? id, SaveInventoryDocumentCommand command, CancellationToken token)
    {
        await BindScopeAsync(scope, command.Kind, id, token).ConfigureAwait(false);
        var s = scope; var c = command;
        ArgumentNullException.ThrowIfNull(command);
        if (c.CreationKey is { Length: > 128 } || c.CreationKey is not null && string.IsNullOrWhiteSpace(c.CreationKey))
            throw Error("key", "مفتاح إنشاء المسودة غير صالح", "Invalid draft creation key.");
        if (!Enum.IsDefined(c.Kind) || string.IsNullOrWhiteSpace(c.Reason) || c.Reason.Length > 500 || c.Lines is null || c.Lines.Count is 0 or > 200
            || c.Lines.Any(l => l is null) || c.Lines.Select(l => l.ItemId).Distinct().Count() != c.Lines.Count || c.DateUtc > time.GetUtcNow().AddMinutes(5))
            throw Error("input", "أدخل التاريخ والسبب وأسطر مواد مميزة (1–200)", "Supply a valid date, reason and 1–200 distinct item lines.");
        Guid documentId;
        var tx = await PetraERP.Infrastructure.Persistence.OwnedDatabaseTransaction.BeginAsync(db, IsolationLevel.Serializable, token).ConfigureAwait(false);
        await using var transactionLifetime = tx.ConfigureAwait(false);
        try
        {
            await InventoryWriteScope.LockAsync(db, scope.CompanyId, scope.BranchId, token).ConfigureAwait(false);
            await AuthorizeAsync(s, c.Kind, token).ConfigureAwait(false);
            var creationFingerprint = c.CreationKey is null ? null : Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(c with
                { ExpectedVersion = null, CreationKey = null, DateUtc = c.DateUtc.ToUniversalTime() })));
            if (id is null && c.CreationKey is not null)
            {
                var existing = await Documents(s).AsNoTracking().SingleOrDefaultAsync(d => d.CreationKey == c.CreationKey, token).ConfigureAwait(false);
                if (existing is not null)
                {
                    if (existing.CreationFingerprint != creationFingerprint || existing.CreatedBy != scope.UserId)
                        throw Error("creation_conflict", "مفتاح إنشاء المسودة مرتبط بطلب آخر؛ أعد تحميل النتيجة الأصلية", "Draft creation key belongs to another request; reload the original result.");
                    await tx.CommitAsync(token).ConfigureAwait(false);
                    return await GetAsync(s, existing.Id, token).ConfigureAwait(false);
                }
            }
            await InventoryWriteScope.ValidateWarehouseAsync(db, scope.CompanyId, scope.BranchId, c.WarehouseId, token).ConfigureAwait(false);
            if (c.Kind == InventoryDocumentKind.Transfer)
            {
                if (c.InventoryAccountId is not null || c.OffsetAccountId is not null)
                    throw Error("accounts", "المناقلة ضمن الفرع تنقل القيمة نفسها ولا تقبل حسابات تسوية", "An intra-branch transfer preserves value and does not accept offset accounts.");
                if (command.TargetWarehouseId is not { } target || target == c.WarehouseId)
                    throw Error("target", "اختر مستودع وجهة مختلفاً", "Choose a different target warehouse.");
                // This contract is explicitly intra-branch. Cross-branch must never be inferred from an arbitrary warehouse id.
                await InventoryWriteScope.ValidateWarehouseAsync(db, scope.CompanyId, scope.BranchId, target, token).ConfigureAwait(false);
            }
            else if (command.TargetWarehouseId is not null) throw Error("target", "الوجهة للمناقلة فقط", "Target warehouse is only valid for transfers.");
            if (c.Kind == InventoryDocumentKind.InternalIssue && c.CostCenterId is null)
                throw Error("center", "مركز التكلفة مطلوب للصرف الداخلي", "Internal issue requires a cost center.");
            if (c.CostCenterId is { } center && !await db.CostCenters.AnyAsync(x => x.Id == center && x.CompanyId == scope.CompanyId && x.IsActive, token).ConfigureAwait(false))
                throw Error("center", "مركز التكلفة غير صالح", "Invalid cost center.");
            if (c.Kind != InventoryDocumentKind.Transfer)
            {
                if (c.InventoryAccountId is null || c.OffsetAccountId is null || c.InventoryAccountId == c.OffsetAccountId)
                    throw Error("accounts", "اختر حساب المخزون وحساب المصروف أو فروق الجرد المختلفين", "Select distinct inventory and expense/count variance accounts.");
                var ids = new[] { c.InventoryAccountId.Value, c.OffsetAccountId.Value };
                if (await db.Accounts.CountAsync(a => ids.Contains(a.Id) && a.CompanyId == scope.CompanyId && a.IsActive && a.PostingAllowed, token).ConfigureAwait(false) != 2)
                    throw Error("accounts", "الحسابات غير فعالة أو خارج الشركة", "Accounts must be active posting accounts in this company.");
            }
            var row = id.HasValue ? await Documents(s).Include(d => d.Lines).SingleOrDefaultAsync(d => d.Id == id.Value, token).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("InventoryDocument") : new InventoryDocumentRow { Id = Guid.NewGuid(), CompanyId = scope.CompanyId, BranchId = scope.BranchId, CreatedBy = scope.UserId };
            if (id.HasValue)
            {
                RequireVersion(row, command.ExpectedVersion);
                if (row.State != (int)InventoryDocumentState.Draft || row.Kind != (int)c.Kind) throw Error("immutable", "يمكن تعديل مسودة من النوع نفسه فقط", "Only a draft of the same kind can be edited.");
                db.InventoryDocumentLines.RemoveRange(row.Lines); row.Lines.Clear();
            }
            else { row.CreationKey = c.CreationKey; row.CreationFingerprint = creationFingerprint; db.InventoryDocuments.Add(row); }
            documentId = row.Id;
            row.Kind = (int)c.Kind; row.WarehouseId = c.WarehouseId; row.TargetWarehouseId = command.TargetWarehouseId;
            row.DateUtc = c.DateUtc.ToUniversalTime(); row.Reason = c.Reason.Trim(); row.CostCenterId = c.CostCenterId;
            row.InventoryAccountId = c.InventoryAccountId; row.OffsetAccountId = c.OffsetAccountId;
            var position = 0;
            foreach (var line in c.Lines)
            {
                if (line.Quantity < 0 || decimal.Round(line.Quantity, 4) != line.Quantity || (line.Quantity == 0 && c.Kind != InventoryDocumentKind.Count))
                    throw Error("quantity", "الكمية غير صالحة؛ الصفر للجرد فقط", "Invalid quantity; zero is allowed only for counts.");
                var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == line.ItemId && i.CompanyId == scope.CompanyId && i.IsActive, token).ConfigureAwait(false)
                    ?? throw Error("item", "المادة غير فعالة أو خارج الشركة", "Item must be active in this company.");
                if (item.CostMethod is not ((int)CostMethod.WeightedAverage or (int)CostMethod.Fifo)) throw Error("cost_method", "طريقة التكلفة غير مدعومة", "Unsupported cost method.");
                if (line.OpeningUnitCost is { } opening && (c.Kind != InventoryDocumentKind.Count || opening < 0 || opening > 999999999999999.9999m || decimal.Round(opening, 4) != opening))
                    throw Error("opening_cost", "تكلفة الأساس الافتتاحية صالحة للجرد فقط وبأربع منازل", "Opening base-unit cost is only valid for counts and must be nonnegative with four decimals.");
                // Validate the units even for a zero count.
                var factorQuantity = await InventoryUnits.BaseQuantityAsync(db, scope.CompanyId, item.Id, line.UnitId, item.BaseUnitId,
                    line.Quantity, c.Kind == InventoryDocumentKind.Count, token).ConfigureAwait(false);
                var balance = await InventoryMovementGuard.BalanceAsync(db, scope.CompanyId, row.WarehouseId, item.Id, token).ConfigureAwait(false);
                if (line.OpeningUnitCost is not null && (balance.Quantity != 0 || balance.Value != 0 || factorQuantity <= 0))
                    throw Error("opening_cost", "التكلفة الافتتاحية لزيادة جرد دون رصيد فقط", "Opening cost is only valid for a positive count with no stock.");
                row.Lines.Add(new InventoryDocumentLineRow { Id = Guid.NewGuid(), DocumentId = row.Id, ItemId = item.Id, UnitId = line.UnitId,
                    Quantity = line.Quantity, BaseQuantity = line.Quantity == 0 ? 0 : factorQuantity, SnapshotQuantity = balance.Quantity,
                    SnapshotSequence = balance.LastSequence, Position = position++, OpeningUnitCost = line.OpeningUnitCost });
            }
            await db.SaveChangesAsync(token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
        }
        catch { db.ChangeTracker.Clear(); throw; }
        return await GetAsync(s, documentId, token).ConfigureAwait(false);
    }

    private static void RequireVersion(InventoryDocumentRow row, string? expected)
    {
        if (!ConcurrencyToken.TryParse(expected, out var version) || !version.Matches(row.RowVersion))
            throw Error("version", "تغيرت نسخة المستند؛ أعد التحميل", "The document version changed; reload it.");
    }

    private static BusinessRuleException Error(string code, string arabic, string english) => new("inventory." + code, arabic + " / " + english);
}
