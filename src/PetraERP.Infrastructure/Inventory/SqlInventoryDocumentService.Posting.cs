using System.Data;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Inventory;
using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Inventory;

public sealed partial class SqlInventoryDocumentService
{
    public async Task<InventoryDocumentResult> PostAsync(InventoryDocumentScope scope, Guid id, InventoryDocumentAction command, CancellationToken token)
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
            if (row.PostKey == command.IdempotencyKey)
            {
                if (row.PostVersion != command.ExpectedVersion || row.PostedBy != scope.UserId || row.PostReason != c.Reason)
                    throw Error("idempotency_conflict", "مفتاح الترحيل مرتبط بطلب مختلف", "Posting key is bound to a different request.");
                await tx.CommitAsync(token).ConfigureAwait(false);
                return await GetAsync(s, id, token).ConfigureAwait(false);
            }
            if (row.State != (int)InventoryDocumentState.Draft) throw Error("not_draft", "المستند مرحل؛ أعد تحميله", "Document is already posted; reload it.");
            RequireVersion(row, command.ExpectedVersion);
            await RequirePeriodAsync(scope.CompanyId, row.DateUtc, token).ConfigureAwait(false);
            await InventoryWriteScope.ValidateWarehouseAsync(db, scope.CompanyId, scope.BranchId, row.WarehouseId, token).ConfigureAwait(false);
            if (row.TargetWarehouseId is { } target) await InventoryWriteScope.ValidateWarehouseAsync(db, scope.CompanyId, scope.BranchId, target, token).ConfigureAwait(false);
            row.Number = await NextNumberAsync(s, (InventoryDocumentKind)row.Kind, row.DateUtc.Year, token).ConfigureAwait(false);
            row.PostReason = c.Reason;
            decimal signedValue = 0;
            var fifoQuoter = new FifoQuoter(db);
            foreach (var line in row.Lines.OrderBy(l => l.ItemId))
            {
                var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == line.ItemId && i.CompanyId == scope.CompanyId, token).ConfigureAwait(false);
                if (!item.IsActive || item.CostMethod is not ((int)CostMethod.WeightedAverage or (int)CostMethod.Fifo)) throw Error("cost_method", "المادة غير فعالة أو طريقة تكلفتها غير مدعومة", "Item must be active with a supported cost method.");
                var converted = await InventoryUnits.BaseQuantityAsync(db, scope.CompanyId, line.ItemId, line.UnitId, item.BaseUnitId,
                    line.Quantity, row.Kind == (int)InventoryDocumentKind.Count, token).ConfigureAwait(false);
                if (converted != line.BaseQuantity) throw Error("conversion_changed", "تغير تحويل الوحدة؛ أعد حفظ المسودة", "Unit conversion changed; save the draft again.");
                var balance = await InventoryMovementGuard.BalanceAsync(db, scope.CompanyId, row.WarehouseId, line.ItemId, token).ConfigureAwait(false);
                var delta = row.Kind == (int)InventoryDocumentKind.Count ? line.BaseQuantity - balance.Quantity : -line.BaseQuantity;
                if (row.Kind == (int)InventoryDocumentKind.Count)
                {
                    var nextAccountingDay = new DateTimeOffset(row.DateUtc.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
                    if (line.SnapshotSequence != balance.LastSequence || line.SnapshotQuantity != balance.Quantity
                        || await db.StockMovements.AnyAsync(m => m.CompanyId == scope.CompanyId && m.WarehouseId == row.WarehouseId && m.ItemId == line.ItemId && m.MovementDateUtc >= nextAccountingDay, token).ConfigureAwait(false))
                        throw Error("count_stale", "تغير المخزون أو تاريخ الجرد قديم؛ أعد العد واحفظ نسخة جديدة", "Stock changed or count date is stale; recount and save a new snapshot.");
                }
                if (line.OpeningUnitCost is not null && (row.Kind != (int)InventoryDocumentKind.Count || balance.Quantity != 0 || balance.Value != 0 || delta <= 0))
                    throw Error("opening_cost", "لا تستخدم تكلفة افتتاحية عند وجود WAC أو دون زيادة جرد", "Opening cost is not allowed when WAC exists or without a positive count adjustment.");
                if (delta == 0) continue;
                if (delta < 0 && balance.Quantity < -delta) throw Error("negative_stock_blocked", "الرصيد لا يكفي للصرف", "Insufficient stock for this issue.");
                decimal cost;
                if (balance.Quantity == 0 && balance.Value == 0 && delta > 0 && row.Kind == (int)InventoryDocumentKind.Count)
                {
                    if (line.OpeningUnitCost is not { } opening) throw Error("valuation_required", "أدخل تكلفة افتتاحية صريحة لوحدة الأساس لزيادة الجرد دون رصيد", "Supply an explicit opening base-unit cost for a positive count with no stock.");
                    try { cost = decimal.Round(checked(opening * delta), 4, MidpointRounding.ToEven); }
                    catch (OverflowException) { throw Error("valuation", "قيمة الجرد تتجاوز حدود الدقة", "Count valuation exceeds storage precision."); }
                }
                else if (item.CostMethod == (int)CostMethod.Fifo)
                {
                    if (delta < 0)
                        cost = await fifoQuoter.IssueAsync(scope.CompanyId, row.WarehouseId, line.ItemId, -delta, token).ConfigureAwait(false);
                    else
                    {
                        var latest = await fifoQuoter.LatestUnitCostAsync(scope.CompanyId, row.WarehouseId, line.ItemId, token).ConfigureAwait(false)
                            ?? throw Error("valuation_required", "لا توجد طبقة FIFO مرجعية لتقييم الزيادة", "No reference FIFO layer exists to value the increase.");
                        cost = decimal.Round(latest * delta, 4, MidpointRounding.ToEven);
                    }
                }
                else
                {
                    if (balance.Quantity <= 0 || balance.Value < 0) throw Error("valuation_required", "لا توجد تكلفة WAC سليمة", "No valid authoritative WAC is available.");
                    cost = balance.Cost(Math.Abs(delta));
                }
                if (cost < 0) throw Error("valuation", "قيمة المخزون غير سليمة", "Invalid stock valuation.");
                line.Cost = cost;
                var kind = row.Kind == (int)InventoryDocumentKind.Transfer ? StockMovementKind.TransferOut
                    : row.Kind == (int)InventoryDocumentKind.InternalIssue ? StockMovementKind.InternalIssue
                    : delta < 0 ? StockMovementKind.AdjustmentDecrease : StockMovementKind.AdjustmentIncrease;
                db.StockMovements.Add(Movement(s, row, line, item.BaseUnitId, row.WarehouseId, delta, cost, kind, null));
                if (row.Kind == (int)InventoryDocumentKind.Transfer)
                    db.StockMovements.Add(Movement(s, row, line, item.BaseUnitId, row.TargetWarehouseId!.Value, -delta, cost, StockMovementKind.TransferIn, null));
                else signedValue += delta < 0 ? -cost : cost;
            }
            row.PostKey = command.IdempotencyKey; row.PostVersion = command.ExpectedVersion; row.PostReason = c.Reason;
            row.PostedBy = scope.UserId; row.PostedAtUtc = time.GetUtcNow(); row.State = (int)InventoryDocumentState.Posted;
            if (signedValue != 0)
            {
                if (row.InventoryAccountId is null || row.OffsetAccountId is null || row.InventoryAccountId == row.OffsetAccountId)
                    throw Error("accounts", "حسابات أثر المخزون مطلوبة", "Inventory and offset account mappings are required.");
                var journal = await CreateJournalAsync(s, row, signedValue, token).ConfigureAwait(false);
                row.JournalId = journal.Id;
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                var request = PostingRequest.Post(PostingRequestScope.Create(scope.CompanyId, scope.BranchId, scope.UserId), id, journal.Id,
                    ExpectedJournalVersion.FromBase64(journal.VersionBase64), AuditReason.Create(c.Reason));
                await new SqlPostingService(db).PostInventoryAsync(id, request, token).ConfigureAwait(false);
            }
            else await db.SaveChangesAsync(token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
        }
        catch { db.ChangeTracker.Clear(); throw; }
        return await GetAsync(s, id, token).ConfigureAwait(false);
    }

    private async Task<JournalDetailsDto> CreateJournalAsync(InventoryDocumentScope s, InventoryDocumentRow row, decimal value, CancellationToken token)
    {
        var currency = await db.Companies.Where(c => c.Id == s.CompanyId).Select(c => c.BaseCurrencyCode).SingleAsync(token).ConfigureAwait(false);
        var amount = Math.Abs(value);
        JournalDraftLineInput Line(Guid account, bool debit) => new()
        {
            AccountId = account, CostCenterId = row.CostCenterId, TransactionCurrencyCode = currency,
            TransactionDebit = debit ? amount : 0, TransactionCredit = debit ? 0 : amount,
            RateSourceCurrencyCode = currency, RateTargetCurrencyCode = currency, RateValue = 1m, Description = row.Reason
        };
        var service = new SqlJournalEntryService(db, time);
        var journalScope = JournalEntryScope.Create(s.CompanyId, s.BranchId, s.UserId);
        var journal = await service.CreateDraftAsync(journalScope, new CreateJournalDraftCommand
        {
            AccountingDate = DateOnly.FromDateTime(row.DateUtc.UtcDateTime), Description = $"مخزون / Inventory {row.Number}",
            Lines = [Line(row.InventoryAccountId!.Value, value > 0), Line(row.OffsetAccountId!.Value, value < 0)]
        }, token).ConfigureAwait(false);
        return await ApproveAsync(s, journal, row.Reason, token).ConfigureAwait(false);
    }

    private async Task<JournalDetailsDto> ApproveAsync(InventoryDocumentScope s, JournalDetailsDto journal, string reason, CancellationToken token)
    {
        var service = new SqlJournalEntryService(db, time); var journalScope = JournalEntryScope.Create(s.CompanyId, s.BranchId, s.UserId);
        journal = await service.ValidateDraftAsync(journalScope, journal.Id, new JournalDraftActionCommand { ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(journal.VersionBase64)), Reason = reason }, token).ConfigureAwait(false);
        return await service.ApproveDraftAsync(journalScope, journal.Id, new JournalDraftActionCommand { ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(journal.VersionBase64)), Reason = reason }, token).ConfigureAwait(false);
    }

    private StockMovementRow Movement(InventoryDocumentScope s, InventoryDocumentRow row, InventoryDocumentLineRow line, Guid baseUnitId, Guid warehouse,
        decimal delta, decimal cost, StockMovementKind kind, Guid? reverses) => new()
    {
        Id = Guid.NewGuid(), CompanyId = s.CompanyId, BranchId = s.BranchId, WarehouseId = warehouse, ItemId = line.ItemId,
        UnitId = row.Kind == (int)InventoryDocumentKind.Count ? baseUnitId : line.UnitId, Quantity = row.Kind == (int)InventoryDocumentKind.Count ? Math.Abs(delta) : line.Quantity,
        BaseQuantity = delta, UnitCost = decimal.Round(cost / Math.Abs(delta), 4, MidpointRounding.ToEven), TotalCost = cost,
        MovementKind = (int)kind, MovementDateUtc = row.DateUtc, DocumentType = delta < 0 ? "InventoryDocumentOut" : "InventoryDocumentIn",
        DocumentId = row.Id, DocumentLineId = line.Id, DocumentNumber = row.Number, CostCenterId = row.CostCenterId,
        Notes = row.PostReason ?? row.Reason, CreatedByUserId = s.UserId, CreatedAtUtc = time.GetUtcNow(), ReversesMovementId = reverses
    };

    private static void RequireAction(InventoryDocumentAction c)
    {
        if (string.IsNullOrWhiteSpace(c.IdempotencyKey) || c.IdempotencyKey.Length > 128 || string.IsNullOrWhiteSpace(c.Reason) || c.Reason.Length > 500)
            throw Error("request", "مفتاح الطلب والسبب مطلوبان", "A request key and reason are required.");
    }

    private async Task RequirePeriodAsync(Guid company, DateTimeOffset date, CancellationToken token)
    {
        var day = DateOnly.FromDateTime(date.UtcDateTime);
        if (date > time.GetUtcNow().AddMinutes(5) || !await db.AccountingPeriods.AnyAsync(p => p.CompanyId == company && p.RangeStart <= day && p.RangeEnd >= day && p.State == (byte)AccountingPeriodState.Open, token).ConfigureAwait(false))
            throw Error("period_closed", "التاريخ غير صالح أو الفترة مقفلة", "Invalid date or closed accounting period.");
    }

    private async Task<string> NextNumberAsync(InventoryDocumentScope s, InventoryDocumentKind kind, int year, CancellationToken token)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"IF NOT EXISTS(SELECT 1 FROM [inventory].[DocumentSequences] WITH(UPDLOCK,HOLDLOCK) WHERE [CompanyId]={s.CompanyId} AND [BranchId]={s.BranchId} AND [Year]={year} AND [Kind]={(int)kind}) INSERT INTO [inventory].[DocumentSequences]([CompanyId],[BranchId],[Year],[Kind],[NextValue]) VALUES({s.CompanyId},{s.BranchId},{year},{(int)kind},0); UPDATE [inventory].[DocumentSequences] WITH(UPDLOCK,HOLDLOCK) SET [NextValue]=[NextValue]+1 WHERE [CompanyId]={s.CompanyId} AND [BranchId]={s.BranchId} AND [Year]={year} AND [Kind]={(int)kind};", token).ConfigureAwait(false);
        var sequence = await db.Database.SqlQuery<long>($"SELECT [NextValue] AS [Value] FROM [inventory].[DocumentSequences] WHERE [CompanyId]={s.CompanyId} AND [BranchId]={s.BranchId} AND [Year]={year} AND [Kind]={(int)kind}").SingleAsync(token).ConfigureAwait(false);
        return FormattableString.Invariant($"INV-{(int)kind}-{year}-{sequence:D8}");
    }
}
