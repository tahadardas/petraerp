using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.DocumentProcessing;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Inventory;
using PetraERP.Application.Purchases;
using PetraERP.Application.Sales;
using PetraERP.Application.Treasury;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Purchases;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.DocumentProcessing;

public sealed partial class SqlBulkDocumentPostingService
{
    private sealed record PostingOutcome(bool Success, bool Uncertain, BulkPostingClassification Classification,
        string? NewStatus, Guid? JournalEntryId, string? PostingNumber, string Reason, string? FailureCode);

    private async Task ProcessItemAsync(FinancialDbContext store, PostingUnit unit, Guid itemId, Guid companyId,
        Guid branchId, Guid userId, Guid batchId, CancellationToken cancellationToken)
    {
        store.ChangeTracker.Clear();
        var item = await store.Set<BulkPostingItemRow>().SingleAsync(i => i.Id == itemId, cancellationToken)
            .ConfigureAwait(false);
        item.Status = (byte)BulkPostingItemStatus.Running;
        item.AttemptCount++;
        item.LastAttemptAtUtc = time.GetUtcNow();
        try
        {
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsActiveDocumentConflict(exception))
        {
            // Another batch claimed this document between discovery and this claim. The unique running index is the
            // last line of defence against two operators posting the same document: yield the item instead of posting.
            store.ChangeTracker.Clear();
            var contested = await store.Set<BulkPostingItemRow>().SingleAsync(i => i.Id == itemId, cancellationToken)
                .ConfigureAwait(false);
            contested.Status = (byte)BulkPostingItemStatus.Skipped;
            contested.Classification = (byte)BulkPostingClassification.ConcurrencyConflict;
            contested.Reason = "المستند قيد الترحيل حالياً ضمن دفعة أخرى متزامنة؛ تُرك لتلك الدفعة ولن يُرحّل مرتين. "
                + "/ The document is being posted by another simultaneous batch; it was left to that batch and will not be posted twice.";
            contested.UpdatedAtUtc = time.GetUtcNow();
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            store.ChangeTracker.Clear();
            return;
        }

        store.ChangeTracker.Clear();

        PostingOutcome outcome;
        try
        {
            outcome = await ExecuteAsync(unit, (BulkPostingDocumentType)item.DocumentType, item.DocumentId, companyId,
                branchId, userId, batchId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            outcome = Failure(exception);
        }

        var actualStatus = outcome.Success
            ? outcome.NewStatus
            : await TryReadStatusAsync(store, (BulkPostingDocumentType)item.DocumentType, item.DocumentId,
                companyId, branchId, cancellationToken).ConfigureAwait(false);

        store.ChangeTracker.Clear();
        item = await store.Set<BulkPostingItemRow>().SingleAsync(i => i.Id == itemId, cancellationToken)
            .ConfigureAwait(false);
        if (outcome.Success)
        {
            var alreadyPosted = outcome.Classification == BulkPostingClassification.AlreadyPosted;
            item.Status = (byte)(alreadyPosted ? BulkPostingItemStatus.Skipped : BulkPostingItemStatus.Posted);
            item.NewStatus = actualStatus;
            item.JournalEntryId = outcome.JournalEntryId;
            item.PostingNumber = outcome.PostingNumber;
            item.Classification = alreadyPosted
                ? (byte)BulkPostingClassification.AlreadyPosted
                : item.Classification is (byte)BulkPostingClassification.CanBeApprovedAutomatically
                    ? (byte)BulkPostingClassification.CanBeApprovedAutomatically
                    : (byte)BulkPostingClassification.ReadyForPosting;
            item.Reason = outcome.Reason;
            item.FailureCode = null;
        }
        else
        {
            item.Status = (byte)(outcome.Uncertain ? BulkPostingItemStatus.Unknown : BulkPostingItemStatus.Failed);
            item.NewStatus = actualStatus;
            item.Classification = (byte)outcome.Classification;
            item.Reason = outcome.Reason;
            item.FailureCode = outcome.FailureCode;
        }
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PostingOutcome> ExecuteAsync(PostingUnit unit, BulkPostingDocumentType documentType,
        Guid documentId, Guid companyId, Guid branchId, Guid userId, Guid batchId, CancellationToken cancellationToken)
    {
        var reason = "ترحيل جماعي معتمد من إدارة ترحيل المستندات.";
        switch (documentType)
        {
            case BulkPostingDocumentType.SalesInvoice:
            {
                var invoice = await unit.Sales.GetInvoiceAsync(companyId, branchId, documentId, cancellationToken)
                    .ConfigureAwait(false);
                if (invoice.Status == SalesInvoiceStatus.Posted) return Already(invoice.Status.ToString(), invoice.JournalEntryId, invoice.InvoiceNumber);
                var current = invoice;
                if (current.Status == SalesInvoiceStatus.Draft)
                    current = await unit.Sales.ApproveInvoiceAsync(companyId, branchId, documentId, userId,
                        ConcurrencyToken.FromBytes(current.RowVersion), cancellationToken).ConfigureAwait(false);
                var posted = await unit.Sales.PostInvoiceAsync(companyId, branchId, documentId, userId,
                    new PostSalesInvoiceCommand(PostingKey(batchId, documentId), reason), cancellationToken).ConfigureAwait(false);
                return Success(posted.Status.ToString(), posted.JournalEntryId, posted.InvoiceNumber,
                    "تم اعتماد فاتورة المبيعات وترحيلها. / The sales invoice was approved and posted.");
            }
            case BulkPostingDocumentType.PurchaseInvoice:
            {
                var invoice = await unit.Purchases.GetAsync(companyId, branchId, documentId, cancellationToken)
                    .ConfigureAwait(false);
                if (invoice.Status == PurchaseInvoiceStatus.Posted) return Already(invoice.Status.ToString(), invoice.JournalEntryId, invoice.InvoiceNumber);
                var current = invoice;
                if (current.Status == PurchaseInvoiceStatus.Draft)
                    current = await unit.Purchases.ApproveAsync(companyId, branchId, documentId, userId,
                        new ApprovePurchaseInvoiceCommand(ConcurrencyToken.FromBytes(current.RowVersion), reason),
                        cancellationToken).ConfigureAwait(false);
                var posted = await unit.Purchases.PostAsync(companyId, branchId, documentId, userId,
                    new PostPurchaseInvoiceCommand(ConcurrencyToken.FromBytes(current.RowVersion),
                        PostingKey(batchId, documentId), null), cancellationToken).ConfigureAwait(false);
                return Success(posted.Status.ToString(), posted.JournalEntryId, posted.InvoiceNumber,
                    "تم اعتماد فاتورة الشراء وترحيلها. / The purchase invoice was approved and posted.");
            }
            case BulkPostingDocumentType.Voucher:
            {
                var voucher = await unit.Vouchers.GetAsync(companyId, branchId, documentId, cancellationToken)
                    .ConfigureAwait(false);
                if (voucher.Status == VoucherStatus.Posted) return Already(voucher.Status.ToString(), voucher.JournalEntryId, voucher.VoucherNumber);
                var current = voucher;
                if (current.Status == VoucherStatus.Draft)
                    current = await unit.Vouchers.ApproveAsync(companyId, branchId, documentId, userId,
                        new VoucherActionCommand(ConcurrencyToken.FromBytes(current.RowVersion), reason),
                        cancellationToken).ConfigureAwait(false);
                var posted = await unit.Vouchers.PostAsync(companyId, branchId, documentId, userId,
                    new PostVoucherCommand(ConcurrencyToken.FromBytes(current.RowVersion),
                        PostingKey(batchId, documentId), null), cancellationToken).ConfigureAwait(false);
                return Success(posted.Status.ToString(), posted.JournalEntryId, posted.VoucherNumber,
                    "تم اعتماد السند وترحيله. / The voucher was approved and posted.");
            }
            case BulkPostingDocumentType.Journal:
            {
                var scope = JournalEntryScope.Create(companyId, branchId, userId);
                var journal = await unit.Journals.GetJournalAsync(scope, documentId, cancellationToken)
                    .ConfigureAwait(false);
                if (journal.State == (byte)JournalEntryState.Posted)
                    return Already(Name<JournalEntryState>(journal.State), journal.Id, journal.PostingSequence?.ToString(CultureInfo.InvariantCulture));
                if (journal.State == (byte)JournalEntryState.Draft)
                    throw new BusinessRuleException("ledger.journal_state_invalid",
                        "لا يمكن ترحيل قيد ما زال مسودة غير مُتحقَّق منها. / A draft journal must be validated before posting.");
                if (journal.State == (byte)JournalEntryState.Validated)
                    journal = await unit.Journals.ApproveDraftAsync(scope, documentId, new JournalDraftActionCommand
                    {
                        ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(journal.VersionBase64)),
                        Reason = reason,
                    }, cancellationToken).ConfigureAwait(false);
                if (journal.State != (byte)JournalEntryState.Approved)
                    throw new BusinessRuleException("ledger.journal_state_invalid",
                        "حالة القيد لا تسمح بالترحيل. / The journal state does not allow posting.");
                await unit.Posting.PostAsync(PostingRequest.Post(PostingRequestScope.Create(companyId, branchId, userId),
                    PostingKeyUuid(batchId, documentId), documentId,
                    ExpectedJournalVersion.FromBase64(journal.VersionBase64), AuditReason.Create(reason)),
                    cancellationToken).ConfigureAwait(false);
                var posted = await unit.Journals.GetJournalAsync(scope, documentId, cancellationToken)
                    .ConfigureAwait(false);
                return Success(Name<JournalEntryState>(posted.State), posted.Id,
                    posted.PostingSequence?.ToString(CultureInfo.InvariantCulture),
                    "تم اعتماد القيد وترحيله. / The journal was approved and posted.");
            }
            case BulkPostingDocumentType.InventoryDocument:
            {
                var scope = new InventoryDocumentScope(companyId, branchId, userId);
                var document = await unit.Inventory.GetAsync(scope, documentId, cancellationToken).ConfigureAwait(false);
                if (document.State == InventoryDocumentState.Posted)
                    return Already(document.State.ToString(), document.JournalId, document.Number);
                var posted = await unit.Inventory.PostAsync(scope, documentId,
                    new InventoryDocumentAction(document.Version, PostingKey(batchId, documentId), reason),
                    cancellationToken).ConfigureAwait(false);
                return Success(posted.State.ToString(), posted.JournalId, posted.Number,
                    "تم ترحيل مستند المخزون. / The inventory document was posted.");
            }
            default:
                throw new BusinessRuleException("posting.unsupported_type",
                    "نوع المستند غير مدعوم في الترحيل الجماعي. / The document type is not supported by bulk posting.");
        }
    }

    private static PostingOutcome Success(string? newStatus, Guid? journalId, string? postingNumber, string reason) =>
        new(true, false, BulkPostingClassification.ReadyForPosting, newStatus, journalId, postingNumber, reason, null);

    private static PostingOutcome Already(string? status, Guid? journalId, string? postingNumber) =>
        new(true, false, BulkPostingClassification.AlreadyPosted, status, journalId, postingNumber,
            "المستند مُرحَّل مسبقاً؛ لم تُنشأ حركة مزدوجة. / Already posted; nothing was duplicated.", null);

    private static PostingOutcome Failure(Exception exception)
    {
        if (exception is OperationCanceledException or TimeoutException or DbUpdateException or SqlException)
            return Unknown(exception, "لم تتأكد نتيجة الطلب؛ راجع المستند ثم أعد المحاولة بأمان. / The outcome is uncertain; review the document before retrying.");

        var (code, message) = exception switch
        {
            BusinessRuleException business => (business.Code, business.Message),
            ConcurrencyConflictException conflict => ("posting.concurrency_conflict", conflict.Message),
            PostingAuthorizationException authorization => ("posting.permission_denied", authorization.Message),
            PostingIdempotencyConflictException conflict => ("posting.idempotency_conflict", conflict.Message),
            RequestValidationException validation => (validation.Code, validation.Message),
            ResourceNotFoundException notFound => ("posting.document_missing", notFound.Message),
            InvalidOperationException invalid when invalid.Data[RuleViolation.DataKey] is string rule => (rule, invalid.Message),
            _ => ((string?)null, exception.Message),
        };

        if (code is null)
            return Unknown(exception, "حدث خطأ غير متوقع أثناء الترحيل؛ لم تتأكد النتيجة. / An unexpected error occurred; the outcome is uncertain.");

        var classification = ClassifyFailure(code);
        return new PostingOutcome(false, false, classification, null, null, null,
            $"{ArabicFailure(classification)} ({code})", code);
    }

    private static PostingOutcome Unknown(Exception exception, string reason) =>
        new(false, true, BulkPostingClassification.Unknown, null, null, null, reason, exception.GetType().Name);

    private static BulkPostingClassification ClassifyFailure(string code)
    {
        if (code.Contains("period", StringComparison.OrdinalIgnoreCase) || code.Contains("closed", StringComparison.OrdinalIgnoreCase))
            return BulkPostingClassification.ClosedAccountingPeriod;
        if (code.Contains("stock", StringComparison.OrdinalIgnoreCase) || code.Contains("valuation", StringComparison.OrdinalIgnoreCase)
            || code.Contains("wac", StringComparison.OrdinalIgnoreCase) || code.Contains("conversion", StringComparison.OrdinalIgnoreCase))
            return BulkPostingClassification.InsufficientStockOrValuation;
        if (code.Contains("currency", StringComparison.OrdinalIgnoreCase) || code.Contains("exchange", StringComparison.OrdinalIgnoreCase)
            || code.Contains("rate", StringComparison.OrdinalIgnoreCase) || code.Contains("fx", StringComparison.OrdinalIgnoreCase))
            return BulkPostingClassification.InvalidCurrencyOrExchangeRate;
        if (code.Contains("account", StringComparison.OrdinalIgnoreCase) || code.Contains("mapping", StringComparison.OrdinalIgnoreCase)
            || code.Contains("control", StringComparison.OrdinalIgnoreCase) || code.Contains("unresolved", StringComparison.OrdinalIgnoreCase))
            return BulkPostingClassification.MissingAccountMapping;
        if (code.Contains("permission", StringComparison.OrdinalIgnoreCase) || code.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || code.Contains("grant", StringComparison.OrdinalIgnoreCase) || code.Contains("scope", StringComparison.OrdinalIgnoreCase)
            || code.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
            return BulkPostingClassification.MissingPermission;
        if (code.Contains("concurrency", StringComparison.OrdinalIgnoreCase) || code.Contains("version", StringComparison.OrdinalIgnoreCase)
            || code.Contains("conflict", StringComparison.OrdinalIgnoreCase))
            return BulkPostingClassification.ConcurrencyConflict;
        if (code.Contains("approval", StringComparison.OrdinalIgnoreCase) || code.Contains("state", StringComparison.OrdinalIgnoreCase)
            || code.Contains("draft", StringComparison.OrdinalIgnoreCase) || code.Contains("posted", StringComparison.OrdinalIgnoreCase))
            return BulkPostingClassification.IneligibleDocumentState;
        return BulkPostingClassification.Unknown;
    }

    private static string ArabicFailure(BulkPostingClassification classification) => classification switch
    {
        BulkPostingClassification.ClosedAccountingPeriod => "تعذر الترحيل: الفترة المحاسبية مغلقة أو غير موجودة.",
        BulkPostingClassification.InvalidCurrencyOrExchangeRate => "تعذر الترحيل: العملة أو سعر الصرف غير صالح.",
        BulkPostingClassification.MissingAccountMapping => "تعذر الترحيل: حساب محاسبي مطلوب غير مربوط.",
        BulkPostingClassification.InsufficientStockOrValuation => "تعذر الترحيل: المخزون أو التقييم غير كافٍ أو غير سليم.",
        BulkPostingClassification.ConcurrencyConflict => "تعذر الترحيل: تغيّر المستند أو مفتاح الترحيل مستخدم لطلب مختلف.",
        BulkPostingClassification.MissingPermission => "تعذر الترحيل: لا تملك الصلاحية المطلوبة لهذا المستند.",
        BulkPostingClassification.IneligibleDocumentState => "تعذر الترحيل: حالة المستند لا تسمح بهذه العملية.",
        BulkPostingClassification.UnsupportedSpecialDocumentType => "تعذر الترحيل: نوع مستند خاص يتطلب معالجة فردية.",
        _ => "تعذر الترحيل بسبب خطأ غير متوقع.",
    };

    private static async Task<string?> TryReadStatusAsync(FinancialDbContext db, BulkPostingDocumentType type, Guid id,
        Guid companyId, Guid branchId, CancellationToken cancellationToken)
    {
        try
        {
            return type switch
            {
                BulkPostingDocumentType.SalesInvoice => await db.SalesInvoices.AsNoTracking()
                    .Where(i => i.Id == id && i.CompanyId == companyId && i.BranchId == branchId)
                    .Select(i => (int?)i.Status).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) is { } status
                    ? Name<SalesInvoiceStatus>(status) : null,
                BulkPostingDocumentType.PurchaseInvoice => await db.PurchaseInvoices.AsNoTracking()
                    .Where(i => i.Id == id && i.CompanyId == companyId && i.BranchId == branchId)
                    .Select(i => (int?)i.Status).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) is { } status
                    ? Name<PurchaseInvoiceStatus>(status) : null,
                BulkPostingDocumentType.Voucher => await db.Vouchers.AsNoTracking()
                    .Where(i => i.Id == id && i.CompanyId == companyId && i.BranchId == branchId)
                    .Select(i => (int?)i.Status).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) is { } status
                    ? Name<VoucherStatus>(status) : null,
                BulkPostingDocumentType.Journal => await db.JournalEntries.AsNoTracking()
                    .Where(i => i.Id == id && i.CompanyId == companyId && i.BranchId == branchId)
                    .Select(i => (int?)i.State).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) is { } state
                    ? Name<JournalEntryState>(state) : null,
                BulkPostingDocumentType.InventoryDocument => await db.InventoryDocuments.AsNoTracking()
                    .Where(i => i.Id == id && i.CompanyId == companyId && i.BranchId == branchId)
                    .Select(i => (int?)i.State).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) is { } state
                    ? Name<InventoryDocumentState>(state) : null,
                _ => null,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private static string PostingKey(Guid batchId, Guid documentId) => $"bulk:{batchId:N}:{documentId:N}";

    private static bool IsActiveDocumentConflict(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 } sql
        && sql.Message.Contains("UX_BulkPostingItems_ActiveDocument", StringComparison.Ordinal);

    private static Guid PostingKeyUuid(Guid batchId, Guid documentId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(PostingKey(batchId, documentId)))[..16]);
}
