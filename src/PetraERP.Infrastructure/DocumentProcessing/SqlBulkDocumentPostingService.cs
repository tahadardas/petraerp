using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.DocumentProcessing;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.DocumentProcessing;

/// <summary>
/// Server-authoritative bulk posting. Discovery and classification read the annual database; every eligible item is
/// handed to its own document service so financial effects, numbering, idempotency and authorization stay in the
/// existing posting engine. Batch progress is persisted after every item, and uncertain outcomes are marked Unknown
/// so a resume can safely replay with the same per-document key.
/// </summary>
public sealed partial class SqlBulkDocumentPostingService : IBulkDocumentPostingService
{
    private const string DefaultReason = "ترحيل جماعي للمستندات";
    private const int MaxPageSize = 200;
    private const int MaxIdempotencyKeyLength = 128;
    private const int MaxReasonLength = 500;

    private static readonly JsonSerializerOptions FingerprintJson = new() { WriteIndented = false };

    private readonly FinancialDbContext context;
    private readonly IAccessEvaluator access;
    private readonly TimeProvider time;
    private readonly IAccountingDbContextFactory? contextFactory;

    public SqlBulkDocumentPostingService(FinancialDbContext context, IAccessEvaluator access, TimeProvider time,
        IAccountingDbContextFactory? contextFactory = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(time);
        this.context = context;
        this.access = access;
        this.time = time;
        this.contextFactory = contextFactory;
    }

    public async Task<BulkPostingBatchResult> StartAsync(Guid companyId, Guid branchId, Guid userId,
        StartBulkPostingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateScope(companyId, branchId, userId);
        if (!command.Confirmed)
            throw new BusinessRuleException("posting.batch_confirmation_required",
                "يجب تأكيد الترحيل الجماعي صراحةً قبل التنفيذ. / An explicit confirmation is required before a bulk posting runs.");
        var key = NormalizeKey(command.IdempotencyKey);
        var reason = NormalizeReason(command.Reason);
        var fingerprint = Fingerprint(command.Filter, command.Documents);

        var existing = await context.Set<BulkPostingBatchRow>().AsNoTracking()
            .SingleOrDefaultAsync(b => b.CompanyId == companyId && b.IdempotencyKey == key, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.Equals(existing.FilterJson, fingerprint, StringComparison.Ordinal)
                || existing.BranchId != branchId || existing.RequestedByUserId != userId)
                throw new BusinessRuleException("posting.batch_key_conflict",
                    "مفتاح الترحيل الجماعي مستخدم لطلب مختلف. استخدم مفتاحاً جديداً لطلب جديد. / The bulk key was already used for a different request.");
            // An interrupted batch is resumed with the same per-document keys; a finished batch is only reported.
            return existing.Status is (byte)BulkPostingBatchStatus.Pending or (byte)BulkPostingBatchStatus.Running
                ? await ProcessBatchAsync(companyId, branchId, userId, existing.Id, existing.FiscalYearId, cancellationToken).ConfigureAwait(false)
                : await BuildBatchResultAsync(context, existing.Id, cancellationToken).ConfigureAwait(false);
        }

        var candidates = command.Documents is { Count: > 0 }
            ? await ReadRequestedAsync(companyId, branchId, command.Documents, cancellationToken).ConfigureAwait(false)
            : await DiscoverAsync(companyId, branchId, command.Filter, cancellationToken).ConfigureAwait(false);
        var classified = await ClassifyAsync(companyId, branchId, userId, candidates, cancellationToken).ConfigureAwait(false);
        if (classified.Count == 0)
            throw new BusinessRuleException("posting.no_documents",
                "لا توجد مستندات مطابقة للتصفية المحددة. / No documents match the selected filter.");

        var fiscalYearId = await ResolveFiscalYearAsync(companyId,
            classified.Min(c => c.Candidate.DocumentDateUtc), cancellationToken).ConfigureAwait(false);
        var batchId = Guid.NewGuid();
        var now = time.GetUtcNow();
        var order = 0;
        var items = classified
            .OrderBy(c => c.Candidate.DocumentDateUtc)
            .ThenBy(c => (int)c.Candidate.DocumentType)
            .ThenBy(c => c.Candidate.DocumentNumber, StringComparer.Ordinal)
            .ThenBy(c => c.Candidate.DocumentId)
            .Select(c => new BulkPostingItemRow
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                CompanyId = companyId,
                BranchId = branchId,
                DocumentType = (byte)c.Candidate.DocumentType,
                DocumentId = c.Candidate.DocumentId,
                DocumentNumber = c.Candidate.DocumentNumber,
                DocumentDateUtc = c.Candidate.DocumentDateUtc,
                PartyName = c.Candidate.PartyName,
                Amount = c.Candidate.Amount,
                CurrencyCode = c.Candidate.CurrencyCode,
                PreviousStatus = c.Candidate.StatusName,
                Status = (byte)(c.Postable ? BulkPostingItemStatus.Pending : BulkPostingItemStatus.Skipped),
                Classification = (byte)c.Classification,
                Reason = c.Reason,
                ProcessingOrder = order++,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }).ToList();

        var store = CreateStore(companyId, branchId, userId, fiscalYearId);
        await using var storeLifetime = store.ConfigureAwait(false);
        store.Db.Add(new BulkPostingBatchRow
        {
            Id = batchId,
            CompanyId = companyId,
            BranchId = branchId,
            FiscalYearId = fiscalYearId,
            RequestedByUserId = userId,
            IdempotencyKey = key,
            FilterJson = fingerprint,
            Reason = reason,
            Status = (byte)BulkPostingBatchStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        store.Db.AddRange(items);
        await store.Db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        store.Db.ChangeTracker.Clear();

        return await ProcessBatchAsync(companyId, branchId, userId, batchId, fiscalYearId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<BulkPostingBatchResult> ResumeAsync(Guid companyId, Guid branchId, Guid userId, Guid batchId,
        bool retryFailed, CancellationToken cancellationToken = default)
    {
        ValidateScope(companyId, branchId, userId);
        var batch = await context.Set<BulkPostingBatchRow>().AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId && b.BranchId == branchId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("PostingBatch");
        if (batch.Status is (byte)BulkPostingBatchStatus.Completed or (byte)BulkPostingBatchStatus.Cancelled)
            return await BuildBatchResultAsync(context, batch.Id, cancellationToken).ConfigureAwait(false);

        var store = CreateStore(companyId, branchId, userId, batch.FiscalYearId);
        await using var storeLifetime = store.ConfigureAwait(false);
        var rows = await store.Db.Set<BulkPostingItemRow>()
            .Where(i => i.BatchId == batchId
                && (i.Status == (byte)BulkPostingItemStatus.Running || i.Status == (byte)BulkPostingItemStatus.Unknown
                    || (retryFailed && i.Status == (byte)BulkPostingItemStatus.Failed)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in rows)
        {
            row.Status = (byte)BulkPostingItemStatus.Pending;
            row.FailureCode = null;
            row.Reason = "إعادة محاولة آمنة ضمن نفس الدفعة. / Safe retry inside the same batch.";
        }
        var batchRow = await store.Db.Set<BulkPostingBatchRow>().SingleAsync(b => b.Id == batchId, cancellationToken)
            .ConfigureAwait(false);
        batchRow.Status = (byte)BulkPostingBatchStatus.Running;
        batchRow.CompletedAtUtc = null;
        await store.Db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        store.Db.ChangeTracker.Clear();

        return await ProcessBatchAsync(companyId, branchId, userId, batchId, batch.FiscalYearId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<BulkPostingBatchResult> GetAsync(Guid companyId, Guid branchId, Guid batchId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(companyId, branchId, Guid.Empty, allowEmptyUser: true);
        var exists = await context.Set<BulkPostingBatchRow>().AsNoTracking()
            .AnyAsync(b => b.Id == batchId && b.CompanyId == companyId && b.BranchId == branchId, cancellationToken)
            .ConfigureAwait(false);
        if (!exists) throw new ResourceNotFoundException("PostingBatch");
        return await BuildBatchResultAsync(context, batchId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BulkPostingResultsPage> GetResultsAsync(Guid companyId, Guid branchId, Guid batchId,
        BulkPostingItemStatus? status, BulkPostingDocumentType? documentType, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(companyId, branchId, Guid.Empty, allowEmptyUser: true);
        if (page < 1 || pageSize is < 1 or > MaxPageSize || (long)(page - 1) * pageSize > int.MaxValue)
            throw new RequestValidationException("posting.results_page",
                "Page must be positive and pageSize between 1 and 200.", "page");
        var batch = await context.Set<BulkPostingBatchRow>().AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId && b.BranchId == branchId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("PostingBatch");
        var query = context.Set<BulkPostingItemRow>().AsNoTracking().Where(i => i.BatchId == batchId);
        if (status is { } itemStatus) query = query.Where(i => i.Status == (byte)itemStatus);
        if (documentType is { } type) query = query.Where(i => i.DocumentType == (byte)type);
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.OrderBy(i => i.ProcessingOrder).ThenBy(i => i.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new BulkPostingResultsPage(page, pageSize, total,
            await BuildBatchResultAsync(context, batch.Id, cancellationToken).ConfigureAwait(false),
            rows.Select(MapItem).ToArray());
    }

    private async Task<BulkPostingBatchResult> ProcessBatchAsync(Guid companyId, Guid branchId, Guid userId,
        Guid batchId, Guid fiscalYearId, CancellationToken cancellationToken)
    {
        var store = CreateStore(companyId, branchId, userId, fiscalYearId);
        await using var storeLifetime = store.ConfigureAwait(false);
        var batch = await store.Db.Set<BulkPostingBatchRow>()
            .SingleAsync(b => b.Id == batchId, cancellationToken).ConfigureAwait(false);
        batch.Status = (byte)BulkPostingBatchStatus.Running;
        batch.StartedAtUtc ??= time.GetUtcNow();
        batch.CompletedAtUtc = null;
        // A previous attempt that died mid-document leaves a Running item; replaying it is safe because the
        // per-document idempotency key is derived from this same batch and document.
        var stale = await store.Db.Set<BulkPostingItemRow>()
            .Where(i => i.BatchId == batchId && i.Status == (byte)BulkPostingItemStatus.Running)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in stale) item.Status = (byte)BulkPostingItemStatus.Pending;
        await store.Db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        store.Db.ChangeTracker.Clear();

        var pending = await store.Db.Set<BulkPostingItemRow>().AsNoTracking()
            .Where(i => i.BatchId == batchId && i.Status == (byte)BulkPostingItemStatus.Pending)
            .OrderBy(i => i.ProcessingOrder).ThenBy(i => i.Id)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var itemId in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var unit = CreateUnit(companyId, branchId, userId, fiscalYearId);
                await using var unitLifetime = unit.ConfigureAwait(false);
                await ProcessItemAsync(store.Db, unit, itemId, companyId, branchId, userId, batchId, cancellationToken)
                    .ConfigureAwait(false);
                store.Db.ChangeTracker.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnect / timeout: never leave an item Running (it would hold the active-document index and
            // block every other batch) or the batch Running forever. Persist a resumable state, then propagate.
            await MarkInterruptedAsync(companyId, branchId, userId, batchId, fiscalYearId).ConfigureAwait(false);
            throw;
        }

        batch = await store.Db.Set<BulkPostingBatchRow>().SingleAsync(b => b.Id == batchId, cancellationToken)
            .ConfigureAwait(false);
        var failures = await store.Db.Set<BulkPostingItemRow>().AsNoTracking()
            .CountAsync(i => i.BatchId == batchId
                && (i.Status == (byte)BulkPostingItemStatus.Failed || i.Status == (byte)BulkPostingItemStatus.Unknown),
                cancellationToken).ConfigureAwait(false);
        batch.Status = (byte)(failures == 0 ? BulkPostingBatchStatus.Completed : BulkPostingBatchStatus.CompletedWithFailures);
        batch.CompletedAtUtc = time.GetUtcNow();
        await store.Db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        store.Db.ChangeTracker.Clear();

        return await BuildBatchResultAsync(store.Db, batchId, cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkInterruptedAsync(Guid companyId, Guid branchId, Guid userId, Guid batchId, Guid fiscalYearId)
    {
        var store = CreateStore(companyId, branchId, userId, fiscalYearId);
        await using var storeLifetime = store.ConfigureAwait(false);
        var running = await store.Db.Set<BulkPostingItemRow>()
            .Where(i => i.BatchId == batchId && i.Status == (byte)BulkPostingItemStatus.Running)
            .ToListAsync(CancellationToken.None).ConfigureAwait(false);
        var now = time.GetUtcNow();
        foreach (var row in running)
        {
            // The in-flight document may or may not have committed: Unknown forces a status read + safe replay with the same key.
            row.Status = (byte)BulkPostingItemStatus.Unknown;
            row.Classification = (byte)BulkPostingClassification.Unknown;
            row.FailureCode = "posting.interrupted";
            row.Reason = "انقطع الطلب أثناء معالجة هذا المستند؛ النتيجة غير مؤكدة. استأنف الدفعة لإعادة المحاولة الآمنة بنفس المفتاح. "
                + "/ The request was interrupted while this document was processing; resume the batch to retry safely with the same key.";
            row.UpdatedAtUtc = now;
        }
        var batch = await store.Db.Set<BulkPostingBatchRow>().SingleAsync(b => b.Id == batchId, CancellationToken.None).ConfigureAwait(false);
        batch.Status = (byte)BulkPostingBatchStatus.Pending;
        batch.UpdatedAtUtc = now;
        await store.Db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        store.Db.ChangeTracker.Clear();
    }

    private static async Task<BulkPostingBatchResult> BuildBatchResultAsync(FinancialDbContext db, Guid batchId,
        CancellationToken cancellationToken)
    {
        var batch = await db.Set<BulkPostingBatchRow>().AsNoTracking()
            .SingleAsync(b => b.Id == batchId, cancellationToken).ConfigureAwait(false);
        var groups = await db.Set<BulkPostingItemRow>().AsNoTracking()
            .Where(i => i.BatchId == batchId)
            .GroupBy(i => new { i.Status, i.Classification })
            .Select(g => new { g.Key.Status, g.Key.Classification, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var total = groups.Sum(g => g.Count);
        var alreadyPosted = groups.Where(g => g.Classification == (byte)BulkPostingClassification.AlreadyPosted).Sum(g => g.Count);
        var eligible = groups.Where(g => g.Classification is (byte)BulkPostingClassification.ReadyForPosting
            or (byte)BulkPostingClassification.CanBeApprovedAutomatically).Sum(g => g.Count);
        var posted = groups.Where(g => g.Status == (byte)BulkPostingItemStatus.Posted).Sum(g => g.Count);
        var skipped = groups.Where(g => g.Status == (byte)BulkPostingItemStatus.Skipped).Sum(g => g.Count);
        var failed = groups.Where(g => g.Status == (byte)BulkPostingItemStatus.Failed).Sum(g => g.Count);
        var unknown = groups.Where(g => g.Status == (byte)BulkPostingItemStatus.Unknown).Sum(g => g.Count);
        var status = (BulkPostingBatchStatus)batch.Status;
        return new BulkPostingBatchResult(batch.Id, status, batch.CreatedAtUtc, batch.StartedAtUtc, batch.CompletedAtUtc,
            total, alreadyPosted, eligible, posted, skipped, failed, unknown,
            Message(total, posted, skipped, failed, unknown));
    }

    private static string Message(int total, int posted, int skipped, int failed, int unknown) => total == 0
        ? "لا توجد مستندات في هذه الدفعة. / The batch contains no documents."
        : $"تم فحص {total} مستنداً؛ رُحّل {posted}، وتُخطّي {skipped}، وفشل {failed}، وغير مؤكد {unknown}. "
          + $"/ Examined {total}; posted {posted}; skipped {skipped}; failed {failed}; unknown {unknown}.";

    private static BulkPostingItemResult MapItem(BulkPostingItemRow row) => new(
        row.Id,
        (BulkPostingDocumentType)row.DocumentType,
        row.DocumentId,
        row.DocumentNumber,
        row.DocumentDateUtc,
        row.PartyName,
        row.Amount,
        row.CurrencyCode,
        row.PreviousStatus,
        row.NewStatus,
        row.JournalEntryId,
        row.PostingNumber,
        (BulkPostingItemStatus)row.Status,
        (BulkPostingClassification)row.Classification,
        row.Reason,
        row.FailureCode,
        row.AttemptCount,
        row.LastAttemptAtUtc);

    private ScopedAccountingContext CreateStore(Guid companyId, Guid branchId, Guid userId, Guid fiscalYearId) =>
        ScopedAccountingContext.Create(context, contextFactory, companyId, branchId, userId, fiscalYearId);

    private PostingUnit CreateUnit(Guid companyId, Guid branchId, Guid userId, Guid fiscalYearId) =>
        new(ScopedAccountingContext.Create(context, contextFactory, companyId, branchId, userId, fiscalYearId), time);

    private async Task<Guid> ResolveFiscalYearAsync(Guid companyId, DateTimeOffset date, CancellationToken cancellationToken)
    {
        if (context is AccountingDbContext { BoundFiscalYearId: { } bound }) return bound;
        var day = DateOnly.FromDateTime(date.UtcDateTime);
        var id = await context.FiscalYears.AsNoTracking()
            .Where(y => y.CompanyId == companyId && y.RangeStart <= day && y.RangeEnd >= day)
            .Select(y => (Guid?)y.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return id ?? throw new BusinessRuleException("posting.fiscal_year_missing",
            "لا توجد سنة مالية تغطي المستندات المحددة. / No fiscal year covers the selected documents.");
    }

    private static void ValidateScope(Guid companyId, Guid branchId, Guid userId, bool allowEmptyUser = false)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || (!allowEmptyUser && userId == Guid.Empty))
            throw new RequestValidationException("posting.scope", "Company, branch and user are required.", "companyId");
    }

    private static string NormalizeKey(string? key)
    {
        var value = key?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > MaxIdempotencyKeyLength)
            throw new RequestValidationException("posting.batch_key",
                "Supply an idempotency key of at most 128 characters.", "idempotencyKey");
        return value;
    }

    private static string NormalizeReason(string? reason)
    {
        var value = string.IsNullOrWhiteSpace(reason) ? DefaultReason : reason.Trim();
        if (value.Length > MaxReasonLength)
            throw new RequestValidationException("posting.batch_reason",
                "The reason must be at most 500 characters.", "reason");
        return value;
    }

    private static string Fingerprint(BulkPostingFilter filter, IReadOnlyList<BulkPostingDocumentReference>? documents)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var normalized = new
        {
            Types = (filter.DocumentTypes ?? []).Distinct().OrderBy(static t => (int)t).ToArray(),
            From = filter.FromDateUtc,
            To = filter.ToDateUtc,
            Search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim(),
            Documents = (documents ?? []).Distinct().OrderBy(static d => (int)d.DocumentType).ThenBy(static d => d.DocumentId).ToArray(),
        };
        return JsonSerializer.Serialize(normalized, FingerprintJson);
    }
}

/// <summary>One accounting context for batch rows and, separately, one per document attempt.</summary>
internal sealed class ScopedAccountingContext : IAsyncDisposable
{
    private readonly bool owned;

    private ScopedAccountingContext(FinancialDbContext db, bool owned)
    {
        Db = db;
        this.owned = owned;
    }

    internal FinancialDbContext Db { get; }

    internal static ScopedAccountingContext Create(FinancialDbContext shared, IAccountingDbContextFactory? factory,
        Guid companyId, Guid branchId, Guid userId, Guid fiscalYearId)
    {
        if (factory is not null && shared is AccountingDbContext annual
            && annual.Database.GetConnectionString() is { Length: > 0 } connection)
        {
            var fresh = factory.CreateDbContext(connection);
            fresh.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, userId,
                AccountingOperation.ManagePostingBatches, branchId));
            return new ScopedAccountingContext(fresh, true);
        }
        return new ScopedAccountingContext(shared, false);
    }

    public async ValueTask DisposeAsync()
    {
        if (owned) await Db.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Document services bound to one context, so a failed attempt cannot poison the next document.</summary>
internal sealed class PostingUnit : IAsyncDisposable
{
    private readonly ScopedAccountingContext owner;

    internal PostingUnit(ScopedAccountingContext owner, TimeProvider time)
    {
        this.owner = owner;
        var patterns = new Patterns.SqlPatternService(owner.Db);
        Sales = new Sales.SqlSalesInvoiceService(owner.Db, patterns, time);
        Purchases = new Purchases.SqlPurchaseInvoiceService(owner.Db, patterns, time);
        Vouchers = new Treasury.SqlTreasuryVoucherService(owner.Db, patterns, time);
        Journals = new GeneralLedger.SqlJournalEntryService(owner.Db, time);
        Posting = new GeneralLedger.SqlPostingService(owner.Db);
        Inventory = new Inventory.SqlInventoryDocumentService(owner.Db, time);
    }

    internal FinancialDbContext Db => owner.Db;
    internal PetraERP.Application.Sales.ISalesInvoiceService Sales { get; }
    internal PetraERP.Application.Purchases.IPurchaseInvoiceService Purchases { get; }
    internal Treasury.SqlTreasuryVoucherService Vouchers { get; }
    internal PetraERP.Application.GeneralLedger.IJournalEntryService Journals { get; }
    internal PetraERP.Application.GeneralLedger.Posting.IPostingService Posting { get; }
    internal PetraERP.Application.Inventory.IInventoryDocumentService Inventory { get; }

    public ValueTask DisposeAsync() => owner.DisposeAsync();
}
