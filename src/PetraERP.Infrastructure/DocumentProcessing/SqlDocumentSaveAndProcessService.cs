#pragma warning disable CA2007 // await using over a pure PhaseScope; no sync context in server/bulk paths
using PetraERP.Application.Common;
using PetraERP.Application.CompanyOptions;
using PetraERP.Application.DocumentProcessing;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Purchases;
using PetraERP.Application.Sales;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Purchases;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Primitives;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using PetraERP.Application.Treasury;
using VoucherKind = PetraERP.Domain.Treasury.VoucherKind;
using VoucherStatus = PetraERP.Domain.Treasury.VoucherStatus;
using SqlTreasuryVoucherService = PetraERP.Infrastructure.Treasury.SqlTreasuryVoucherService;
using System.Text;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Workspaces;
using SqlPatternService = PetraERP.Infrastructure.Patterns.SqlPatternService;
using SqlSalesInvoiceService = PetraERP.Infrastructure.Sales.SqlSalesInvoiceService;
using SqlPurchaseInvoiceService = PetraERP.Infrastructure.Purchases.SqlPurchaseInvoiceService;

namespace PetraERP.Infrastructure.DocumentProcessing;

/// <summary>
/// Save → approve → post through the existing document services. Each step keeps its own authoritative
/// transaction, so a failure never reports a step that did not commit; the document row is the durable state.
/// Each phase (Manage → Approve → Post) runs on its own annual context bound to that phase's accounting operation,
/// so <c>AccountingMutationPolicy</c> and the central actor authority validate every transition independently.
/// The request context (bound to Manage) is never rebound.
/// </summary>
public sealed class SqlDocumentSaveAndProcessService(
    ISalesInvoiceService sales,
    IPurchaseInvoiceService purchases,
    ICompanyOptionsService options,
    FinancialDbContext? sharedContext = null,
    IAccountingDbContextFactory? contextFactory = null,
    TimeProvider? time = null,
    ITreasuryVoucherService? vouchers = null) : IDocumentSaveAndProcessService
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    private sealed class PhaseScope(ISalesInvoiceService sales, IPurchaseInvoiceService purchases, ITreasuryVoucherService? vouchers, AccountingDbContext? owned) : IAsyncDisposable
    {
        internal AccountingDbContext? Db => owned;
        internal ISalesInvoiceService Sales => sales;
        internal IPurchaseInvoiceService Purchases => purchases;
        internal ITreasuryVoucherService Vouchers => vouchers ?? throw new InvalidOperationException("Voucher service is not available in this scope.");
        public ValueTask DisposeAsync() => owned is null ? ValueTask.CompletedTask : owned.DisposeAsync();
    }

    /// <summary>A fresh annual context for one phase, bound to exactly that phase's operation, company, year, branch and actor.</summary>
    private PhaseScope OpenPhase(AccountingOperation operation, Guid companyId, Guid branchId, Guid userId)
    {
        if (contextFactory is null || sharedContext is not AccountingDbContext annual
            || annual.BoundFiscalYearId is not { } fiscalYearId
            || annual.Database.GetConnectionString() is not { Length: > 0 } connection)
            return new PhaseScope(sales, purchases, vouchers, null);
        var fresh = contextFactory.CreateDbContext(connection);
        try
        {
            fresh.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, userId, operation, branchId));
            var patterns = new SqlPatternService(fresh);
            return new PhaseScope(new SqlSalesInvoiceService(fresh, patterns, clock), new SqlPurchaseInvoiceService(fresh, patterns, clock),
                new SqlTreasuryVoucherService(fresh, patterns, clock), fresh);
        }
        catch
        {
            fresh.Dispose();
            throw;
        }
    }

    public async Task<DocumentProcessingResult> CreateSalesInvoiceAsync(Guid companyId, Guid branchId, Guid userId,
        DocumentProcessingPermissions permissions, CreateSalesInvoiceCommand command, bool saveAsDraft,
        DocumentPostingIntent? posting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(permissions);
        var policy = await EffectivePolicyAsync(companyId, cancellationToken).ConfigureAwait(false);
        RequireDraftAllowed(policy, saveAsDraft);
        var documentId = DeriveDocumentId("SalesInvoice", companyId, branchId, posting?.IdempotencyKey);
        var bound = await BindAsync(AccountingOperation.ManageSalesInvoices, "SalesInvoice", companyId, branchId, userId, posting?.IdempotencyKey, documentId,
            () => DocumentRequestHash.Of("SalesInvoice", command), cancellationToken).ConfigureAwait(false);
        var created = await TrySalesAsync(companyId, branchId, userId, documentId, cancellationToken).ConfigureAwait(false);
        if (created is not null) { if (bound != RequestBinding.Verified) EnsureSameSales(created, command); }
        else
        {
            try
            {
                // The next step reads only status and version; the enriched detail is built once, for what the client receives.
                // A draft is itself the answer (its credit snapshot warns the user), so only a request that goes on to approve/post skips it.
                using (saveAsDraft ? null : PetraERP.Infrastructure.Sales.SalesDetailEnrichment.Suppress())
                    created = await sales.CreateInvoiceAsync(companyId, branchId, userId, command with { DocumentId = documentId }, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException) when (documentId is not null)
            {
                // A concurrent retry with the same key committed first: resolve to that document instead of creating another.
                var found = await TrySalesAsync(companyId, branchId, userId, documentId, cancellationToken).ConfigureAwait(false);
                found ??= await AwaitCommittedAsync(() => TrySalesAsync(companyId, branchId, userId, documentId, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (found is null) throw;
                created = found;
                if (bound != RequestBinding.Verified) EnsureSameSales(created, command);
            }
        }
        if (saveAsDraft)
            return created.Status == SalesInvoiceStatus.Draft ? Draft(created, policy) : Sales(created,
                created.Status == SalesInvoiceStatus.Posted ? DocumentProcessingOutcome.AlreadyPosted : DocumentProcessingOutcome.Approved,
                true, created.Status == SalesInvoiceStatus.Posted, policy.DocumentPostingMode.ToString(),
                "تم العثور على المستند المحفوظ مسبقاً بنفس مفتاح الطلب؛ لم يُنشأ مستند جديد.");
        return await AdvanceSalesAsync(companyId, branchId, userId, permissions, created, policy, posting, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentProcessingResult> UpdateSalesInvoiceAsync(Guid companyId, Guid branchId, Guid userId,
        DocumentProcessingPermissions permissions, Guid invoiceId, UpdateSalesInvoiceCommand command, bool saveAsDraft,
        DocumentPostingIntent? posting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(permissions);
        var policy = await EffectivePolicyAsync(companyId, cancellationToken).ConfigureAwait(false);
        RequireDraftAllowed(policy, saveAsDraft);
        var existing = await sales.GetInvoiceAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
        if (existing.Status == SalesInvoiceStatus.Posted)
        {
            var asCreate = new CreateSalesInvoiceCommand(existing.PatternRevisionId, command.CustomerId, command.WarehouseId, command.InvoiceDateUtc,
                command.PaymentMethod, command.CurrencyCode, command.ExchangeRate, command.CostCenterId, command.Notes, command.Lines) { Adjustments = command.Adjustments };
            var applied = await IsAppliedRetryAsync(AccountingOperation.ManageSalesInvoices, companyId, branchId, userId, posting?.IdempotencyKey, invoiceId,
                ("SalesInvoice", DocumentRequestHash.Of("SalesInvoice", asCreate)), ("SalesInvoice.Update", DocumentRequestHash.Of("SalesInvoice.Update", command))).ConfigureAwait(false)
                ?? SameAsSaved(() => DocumentRequestFingerprint.EnsureSame(existing, asCreate));
            if (!applied) throw PostedImmutable();
            return Sales(existing, DocumentProcessingOutcome.AlreadyPosted, true, true, policy.DocumentPostingMode.ToString(),
                "المستند مُرحَّل مسبقاً وهذا تكرار مطابق للطلب الأصلي؛ لم تُنشأ حركة مالية جديدة.");
        }
        await BindAsync(AccountingOperation.ManageSalesInvoices, "SalesInvoice.Update", companyId, branchId, userId, posting?.IdempotencyKey, invoiceId,
            () => DocumentRequestHash.Of("SalesInvoice.Update", command), cancellationToken).ConfigureAwait(false);
        SalesInvoiceDetailResult updated;
        using (saveAsDraft ? null : PetraERP.Infrastructure.Sales.SalesDetailEnrichment.Suppress())
            updated = await sales.UpdateInvoiceAsync(companyId, branchId, invoiceId, command, cancellationToken).ConfigureAwait(false);
        return saveAsDraft
            ? Draft(updated, policy)
            : await AdvanceSalesAsync(companyId, branchId, userId, permissions, updated, policy, posting, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentProcessingResult> CreatePurchaseInvoiceAsync(Guid companyId, Guid branchId, Guid userId,
        DocumentProcessingPermissions permissions, SavePurchaseInvoiceCommand command, bool saveAsDraft,
        DocumentPostingIntent? posting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(permissions);
        var policy = await EffectivePolicyAsync(companyId, cancellationToken).ConfigureAwait(false);
        RequireDraftAllowed(policy, saveAsDraft);
        var documentId = DeriveDocumentId("PurchaseInvoice", companyId, branchId, posting?.IdempotencyKey);
        var bound = await BindAsync(AccountingOperation.ManagePurchaseInvoices, "PurchaseInvoice", companyId, branchId, userId, posting?.IdempotencyKey, documentId,
            () => DocumentRequestHash.Of("PurchaseInvoice", command), cancellationToken).ConfigureAwait(false);
        var created = await TryPurchaseAsync(companyId, branchId, userId, documentId, cancellationToken).ConfigureAwait(false);
        if (created is not null) { if (bound != RequestBinding.Verified) EnsureSamePurchase(created, command); }
        else
        {
            try
            {
                created = await purchases.CreateDraftAsync(companyId, branchId, userId, command with { DocumentId = documentId }, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException) when (documentId is not null)
            {
                var found = await TryPurchaseAsync(companyId, branchId, userId, documentId, cancellationToken).ConfigureAwait(false);
                found ??= await AwaitCommittedAsync(() => TryPurchaseAsync(companyId, branchId, userId, documentId, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (found is null) throw;
                created = found;
                if (bound != RequestBinding.Verified) EnsureSamePurchase(created, command);
            }
        }
        if (saveAsDraft)
            return created.Status == PurchaseInvoiceStatus.Draft ? Draft(created, policy) : Purchase(created,
                created.Status == PurchaseInvoiceStatus.Posted ? DocumentProcessingOutcome.AlreadyPosted : DocumentProcessingOutcome.Approved,
                true, created.Status == PurchaseInvoiceStatus.Posted, policy.DocumentPostingMode.ToString(),
                "تم العثور على المستند المحفوظ مسبقاً بنفس مفتاح الطلب؛ لم يُنشأ مستند جديد.");
        return await AdvancePurchaseAsync(companyId, branchId, userId, permissions, created, policy, posting, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentProcessingResult> UpdatePurchaseInvoiceAsync(Guid companyId, Guid branchId, Guid userId,
        DocumentProcessingPermissions permissions, Guid invoiceId, SavePurchaseInvoiceCommand command, bool saveAsDraft,
        DocumentPostingIntent? posting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(permissions);
        var policy = await EffectivePolicyAsync(companyId, cancellationToken).ConfigureAwait(false);
        RequireDraftAllowed(policy, saveAsDraft);
        var existing = await purchases.GetAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
        if (existing.Status == PurchaseInvoiceStatus.Posted)
        {
            var applied = await IsAppliedRetryAsync(AccountingOperation.ManagePurchaseInvoices, companyId, branchId, userId, posting?.IdempotencyKey, invoiceId,
                ("PurchaseInvoice", DocumentRequestHash.Of("PurchaseInvoice", command)), ("PurchaseInvoice.Update", DocumentRequestHash.Of("PurchaseInvoice.Update", command))).ConfigureAwait(false)
                ?? SameAsSaved(() => DocumentRequestFingerprint.EnsureSame(existing, command));
            if (!applied) throw PostedImmutable();
            return Purchase(existing, DocumentProcessingOutcome.AlreadyPosted, true, true, policy.DocumentPostingMode.ToString(),
                "المستند مُرحَّل مسبقاً وهذا تكرار مطابق للطلب الأصلي؛ لم تُنشأ حركة مالية جديدة.");
        }
        await BindAsync(AccountingOperation.ManagePurchaseInvoices, "PurchaseInvoice.Update", companyId, branchId, userId, posting?.IdempotencyKey, invoiceId,
            () => DocumentRequestHash.Of("PurchaseInvoice.Update", command), cancellationToken).ConfigureAwait(false);
        var updated = await purchases.UpdateDraftAsync(companyId, branchId, invoiceId, command, cancellationToken).ConfigureAwait(false);
        return saveAsDraft
            ? Draft(updated, policy)
            : await AdvancePurchaseAsync(companyId, branchId, userId, permissions, updated, policy, posting, cancellationToken).ConfigureAwait(false);
    }


    public async Task<DocumentProcessingResult> CreateVoucherAsync(Guid companyId, Guid branchId, Guid userId,
        DocumentProcessingPermissions permissions, SaveVoucherCommand command, bool saveAsDraft,
        DocumentPostingIntent? posting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(permissions);
        var writer = vouchers ?? throw new InvalidOperationException("Voucher service is not available.");
        var policy = await EffectivePolicyAsync(companyId, cancellationToken).ConfigureAwait(false);
        RequireDraftAllowed(policy, saveAsDraft);
        var documentId = DeriveDocumentId("Voucher", companyId, branchId, posting?.IdempotencyKey);
        var bound = await BindAsync(AccountingOperation.ManageTreasuryVouchers, "Voucher", companyId, branchId, userId, posting?.IdempotencyKey, documentId,
            () => DocumentRequestHash.Of("Voucher", command), cancellationToken).ConfigureAwait(false);
        var created = await TryVoucherAsync(companyId, branchId, userId, documentId, cancellationToken).ConfigureAwait(false);
        if (created is not null) { if (bound != RequestBinding.Verified) EnsureSameVoucher(created, command); }
        else
        {
            try
            {
                created = await writer.CreateDraftAsync(companyId, branchId, userId, command with { DocumentId = documentId, ActorUserId = userId }, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException) when (documentId is not null)
            {
                var found = await TryVoucherAsync(companyId, branchId, userId, documentId, cancellationToken).ConfigureAwait(false);
                found ??= await AwaitCommittedAsync(() => TryVoucherAsync(companyId, branchId, userId, documentId, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (found is null) throw;
                created = found;
                if (bound != RequestBinding.Verified) EnsureSameVoucher(created, command);
            }
        }
        if (saveAsDraft)
            return created.Status == VoucherStatus.Draft ? Draft(created, policy) : Voucher(created,
                created.Status == VoucherStatus.Posted ? DocumentProcessingOutcome.AlreadyPosted : DocumentProcessingOutcome.Approved,
                created.Status != VoucherStatus.Draft, created.Status == VoucherStatus.Posted, policy.DocumentPostingMode.ToString(),
                "تم العثور على السند المحفوظ مسبقاً بنفس مفتاح الطلب؛ لم يُنشأ سند جديد.");
        return await AdvanceVoucherAsync(companyId, branchId, userId, permissions, created, policy, posting, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentProcessingResult> UpdateVoucherAsync(Guid companyId, Guid branchId, Guid userId,
        DocumentProcessingPermissions permissions, Guid voucherId, SaveVoucherCommand command, bool saveAsDraft,
        DocumentPostingIntent? posting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(permissions);
        var writer = vouchers ?? throw new InvalidOperationException("Voucher service is not available.");
        var policy = await EffectivePolicyAsync(companyId, cancellationToken).ConfigureAwait(false);
        RequireDraftAllowed(policy, saveAsDraft);
        var existing = await writer.GetAsync(companyId, branchId, voucherId, cancellationToken).ConfigureAwait(false);
        if (existing.Status == VoucherStatus.Posted)
        {
            var applied = await IsAppliedRetryAsync(AccountingOperation.ManageTreasuryVouchers, companyId, branchId, userId, posting?.IdempotencyKey, voucherId,
                ("Voucher", DocumentRequestHash.Of("Voucher", command)), ("Voucher.Update", DocumentRequestHash.Of("Voucher.Update", command))).ConfigureAwait(false)
                ?? SameAsSaved(() => DocumentRequestFingerprint.EnsureSame(existing, command));
            if (!applied) throw PostedImmutable();
            return Voucher(existing, DocumentProcessingOutcome.AlreadyPosted, true, true, policy.DocumentPostingMode.ToString(),
                "السند مُرحَّل مسبقاً وهذا تكرار مطابق للطلب الأصلي؛ لم تُنشأ حركة مالية جديدة.");
        }
        await BindAsync(AccountingOperation.ManageTreasuryVouchers, "Voucher.Update", companyId, branchId, userId, posting?.IdempotencyKey, voucherId,
            () => DocumentRequestHash.Of("Voucher.Update", command), cancellationToken).ConfigureAwait(false);
        var updated = await writer.UpdateDraftAsync(companyId, branchId, voucherId, command with { ActorUserId = userId }, cancellationToken).ConfigureAwait(false);
        return saveAsDraft
            ? Draft(updated, policy)
            : await AdvanceVoucherAsync(companyId, branchId, userId, permissions, updated, policy, posting, cancellationToken).ConfigureAwait(false);
    }

    private async Task<VoucherDetailResult?> TryVoucherAsync(Guid companyId, Guid branchId, Guid userId, Guid? id, CancellationToken cancellationToken)
    {
        if (id is not { } documentId) return null;
        await using var read = OpenPhase(AccountingOperation.ManageTreasuryVouchers, companyId, branchId, userId);
        try { return await read.Vouchers.GetAsync(companyId, branchId, documentId, cancellationToken).ConfigureAwait(false); }
        catch (ResourceNotFoundException) { return null; }
    }

    private static void EnsureSameVoucher(VoucherDetailResult existing, SaveVoucherCommand command) =>
        DocumentRequestFingerprint.EnsureSame(existing, command);

    private static DocumentProcessingResult Draft(VoucherDetailResult document, CompanyFinancialPolicy policy) =>
        new(VoucherType(document), document.Id, document.VoucherNumber, document.Status.ToString(), DocumentProcessingOutcome.SavedDraft,
            false, false, document.JournalEntryId, policy.DocumentPostingMode.ToString(), Version(document.RowVersion))
        {
            Message = "تم حفظ السند كمسودة. المسودة لا تُرحَّل في أي وضع."
        };

    private async Task<DocumentProcessingResult> AdvanceVoucherAsync(Guid companyId, Guid branchId, Guid userId,
        DocumentProcessingPermissions permissions, VoucherDetailResult document, CompanyFinancialPolicy policy,
        DocumentPostingIntent? posting, CancellationToken cancellationToken)
    {
        var mode = policy.DocumentPostingMode.ToString();
        if (document.Status == VoucherStatus.Posted)
            return Voucher(document, DocumentProcessingOutcome.AlreadyPosted, true, true, mode, "السند مُرحَّل مسبقاً؛ لم تُنشأ حركة مالية جديدة.");
        if (document.Status == VoucherStatus.Reversed)
            throw new BusinessRuleException("voucher.reversed", "The voucher is already reversed and cannot be processed again.");
        if (policy.DocumentPostingMode == DocumentPostingMode.ApproveOnly)
            return Voucher(document, document.Status == VoucherStatus.Draft ? DocumentProcessingOutcome.SavedDraft : DocumentProcessingOutcome.Approved,
                false, false, mode, "وضع الشركة: اعتماد فقط. السندات لا تملك خطوة اعتماد، لذلك بقي السند محفوظاً بانتظار الترحيل اليدوي.");
        if (!permissions.CanPost)
            return Voucher(document, DocumentProcessingOutcome.PostingPermissionMissing, false, false, mode,
                "تم حفظ السند ولم يُرحَّل: لا تملك صلاحية ترحيل السندات، ولم تُرفع صلاحياتك تلقائياً.");
        // The pattern can only be stricter than the company: ManualOnly holds the voucher for a deliberate post.
        VoucherPostingMode patternPosting;
        await using (var policyRead = OpenPhase(AccountingOperation.ManageTreasuryVouchers, companyId, branchId, userId))
            patternPosting = (await policyRead.Vouchers.GetVoucherPatternPolicyAsync(companyId, document.PatternRevisionId, cancellationToken).ConfigureAwait(false)).PostingMode;
        if (patternPosting == VoucherPostingMode.ManualOnly)
            return Voucher(document, document.Status == VoucherStatus.Draft ? DocumentProcessingOutcome.SavedDraft : DocumentProcessingOutcome.Approved,
                false, false, mode, "نمط السند يشترط الترحيل اليدوي: بقي السند محفوظاً بانتظار ترحيله من الإجراء المخصص.");
        if (string.IsNullOrWhiteSpace(posting?.IdempotencyKey))
            throw new BusinessRuleException("document.posting_key_required", "The server needs the posting idempotency key before it can post this document.");
        try
        {
            await using var postPhase = OpenPhase(AccountingOperation.PostTreasuryVoucher, companyId, branchId, userId);
            var posted = await postPhase.Vouchers.PostAsync(companyId, branchId, document.Id, userId,
                new PostVoucherCommand(ConcurrencyToken.FromBytes(document.RowVersion), posting.IdempotencyKey, posting.ManualNumber), cancellationToken).ConfigureAwait(false);
            return Voucher(posted, DocumentProcessingOutcome.Posted, false, true, mode, "تم حفظ السند وترحيله تلقائياً بعد نجاح جميع التحققات المحاسبية.");
        }
        catch (BusinessRuleException exception)
        {
            return await VoucherPostingFailedAsync(companyId, branchId, userId, document.Id, mode, exception.Code, exception.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (PostingIdempotencyConflictException exception)
        {
            return await VoucherPostingFailedAsync(companyId, branchId, userId, document.Id, mode, "posting.idempotency_conflict", exception.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (ConcurrencyConflictException exception)
        {
            return await VoucherPostingFailedAsync(companyId, branchId, userId, document.Id, mode, "posting.concurrency_conflict", exception.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<DocumentProcessingResult> VoucherPostingFailedAsync(Guid companyId, Guid branchId, Guid userId, Guid voucherId,
        string mode, string code, string message, CancellationToken cancellationToken)
    {
        await using var read = OpenPhase(AccountingOperation.ManageTreasuryVouchers, companyId, branchId, userId);
        var reloaded = await read.Vouchers.GetAsync(companyId, branchId, voucherId, cancellationToken).ConfigureAwait(false);
        return Voucher(reloaded, DocumentProcessingOutcome.PostingFailed, false, reloaded.Status == VoucherStatus.Posted, mode,
            $"تم حفظ السند ولم يُرحَّل: {message}", code);
    }

    private static string VoucherType(VoucherDetailResult document) =>
        document.Kind == VoucherKind.Receipt ? "CashReceiptVoucher" : "CashPaymentVoucher";

    private static DocumentProcessingResult Voucher(VoucherDetailResult document, DocumentProcessingOutcome outcome,
        bool approvalApplied, bool postingApplied, string mode, string message, string? failureCode = null) =>
        new(VoucherType(document), document.Id, document.VoucherNumber, document.Status.ToString(), outcome, approvalApplied,
            postingApplied, document.JournalEntryId, mode, Version(document.RowVersion))
        { Message = message, FailureCode = failureCode };

    /// <summary>Refuses Save as Draft before any CREATE/UPDATE side effect when company policy forbids drafts.</summary>
    private static void RequireDraftAllowed(CompanyFinancialPolicy policy, bool saveAsDraft)
    {
        if (saveAsDraft && !policy.AllowSaveAsDraft)
            throw new BusinessRuleException("company.draft_saving_disabled", "Company policy does not allow saving drafts; complete the document or enable draft saving in the company policy.");
    }

    private enum RequestBinding { NoKey, NoStore, Registered, Verified }

    private const int BindingKeyMax = 128;

    private static string BindingKey(string requestKey)
    {
        var key = requestKey.Trim();
        return key.Length <= BindingKeyMax ? key : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    private static BusinessRuleException KeyConflict(string detail) => new("document.request_key_conflict",
        $"This request key was already used for a different request ({detail}). Use a new key for a new document or change.");

    private static void VerifyBinding(DocumentRequestBindingRow bound, Guid documentId, string requestHash)
    {
        if (bound.DocumentId != documentId) throw KeyConflict("different document");
        if (!string.Equals(bound.RequestHashHex, requestHash, StringComparison.Ordinal)) throw KeyConflict("financial input differs");
    }

    private static Task<DocumentRequestBindingRow?> FindBindingAsync(AccountingDbContext db, Guid companyId, Guid branchId, string kind, string key,
        CancellationToken cancellationToken) =>
        db.Set<DocumentRequestBindingRow>().AsNoTracking().SingleOrDefaultAsync(r => r.CompanyId == companyId && r.BranchId == branchId
            && r.DocumentKind == kind && r.IdempotencyKey == key, cancellationToken);

    /// <summary>
    /// Durable key → canonical-request binding. The first request registers (key, document, hash of the full normalized input);
    /// any later request with the same key must carry the identical hash, whatever the document's current state. The unique key
    /// arbitrates simultaneous identical requests. Without a durable store (no annual context factory) callers keep the strict
    /// structural comparison against the stored document.
    /// </summary>
    private async Task<RequestBinding> BindAsync(AccountingOperation manage, string kind, Guid companyId, Guid branchId, Guid userId,
        string? requestKey, Guid? documentId, Func<string> hash, CancellationToken cancellationToken)
    {
        if (documentId is not { } id || string.IsNullOrWhiteSpace(requestKey)) return RequestBinding.NoKey;
        var key = BindingKey(requestKey);
        var requestHash = hash();
        await using (var read = OpenPhase(manage, companyId, branchId, userId))
        {
            if (read.Db is null) return RequestBinding.NoStore;
            var found = await FindBindingAsync(read.Db, companyId, branchId, kind, key, cancellationToken).ConfigureAwait(false);
            if (found is not null) { VerifyBinding(found, id, requestHash); return RequestBinding.Verified; }
        }
        try
        {
            await using var write = OpenPhase(manage, companyId, branchId, userId);
            write.Db!.Add(new DocumentRequestBindingRow
            {
                CompanyId = companyId, BranchId = branchId, DocumentKind = kind, IdempotencyKey = key, DocumentId = id,
                ActorUserId = userId, RequestHashHex = requestHash, RegisteredAtUtc = clock.GetUtcNow(),
            });
            await write.Db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return RequestBinding.Registered;
        }
        catch (DbUpdateException)
        {
            // A simultaneous request registered the key first: it must be the identical request.
            await using var again = OpenPhase(manage, companyId, branchId, userId);
            var raced = await FindBindingAsync(again.Db!, companyId, branchId, kind, key, cancellationToken).ConfigureAwait(false);
            if (raced is null) throw;
            VerifyBinding(raced, id, requestHash);
            return RequestBinding.Verified;
        }
    }

    /// <summary>True only when the key is already bound to exactly this document and one of these canonical hashes.</summary>
    private async Task<bool?> IsAppliedRetryAsync(AccountingOperation manage, Guid companyId, Guid branchId, Guid userId, string? requestKey,
        Guid documentId, params (string Kind, string Hash)[] candidates)
    {
        if (string.IsNullOrWhiteSpace(requestKey)) return false;
        await using var read = OpenPhase(manage, companyId, branchId, userId);
        if (read.Db is null) return null;
        var key = BindingKey(requestKey);
        var boundElsewhere = false;
        foreach (var (kind, requestHash) in candidates)
        {
            var found = await FindBindingAsync(read.Db, companyId, branchId, kind, key, CancellationToken.None).ConfigureAwait(false);
            if (found is null) continue;
            if (found.DocumentId == documentId && string.Equals(found.RequestHashHex, requestHash, StringComparison.Ordinal)) return true;
            boundElsewhere = true;
        }
        if (boundElsewhere) throw KeyConflict("financial input differs");
        return false;
    }

    private static BusinessRuleException PostedImmutable() => new("document.posted_immutable",
        "The document is already posted; a request that differs from the one that created it cannot change it. Use a reversal or a new document.");

    /// <summary>No durable store: the strict structural comparison against the saved document decides whether this is the same request.</summary>
    private static bool SameAsSaved(Action compare)
    {
        try { compare(); return true; }
        catch (BusinessRuleException exception) when (exception.Code == "document.request_key_conflict") { return false; }
    }

    /// <summary>Bounded authoritative re-reads while a simultaneous identical request is still committing; never a write retry.</summary>
    private static async Task<T?> AwaitCommittedAsync<T>(Func<Task<T?>> read, CancellationToken cancellationToken) where T : class
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken).ConfigureAwait(false);
            if (await read().ConfigureAwait(false) is { } found) return found;
        }
        return null;
    }

    /// <summary>Stable identity from company + branch + document type + request key; null when the caller supplied no key.</summary>
    private static Guid? DeriveDocumentId(string documentType, Guid companyId, Guid branchId, string? requestKey)
    {
        if (string.IsNullOrWhiteSpace(requestKey)) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"PetraERP.SaveAndProcess.create:{documentType}:{companyId:N}:{branchId:N}:{requestKey.Trim()}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private async Task<SalesInvoiceDetailResult?> TrySalesAsync(Guid companyId, Guid branchId, Guid userId, Guid? id, CancellationToken cancellationToken)
    {
        if (id is not { } documentId) return null;
        await using var read = OpenPhase(AccountingOperation.ManageSalesInvoices, companyId, branchId, userId);
        try { return await read.Sales.GetInvoiceAsync(companyId, branchId, documentId, cancellationToken).ConfigureAwait(false); }
        catch (ResourceNotFoundException) { return null; }
    }

    private async Task<PurchaseDetailResult?> TryPurchaseAsync(Guid companyId, Guid branchId, Guid userId, Guid? id, CancellationToken cancellationToken)
    {
        if (id is not { } documentId) return null;
        await using var read = OpenPhase(AccountingOperation.ManagePurchaseInvoices, companyId, branchId, userId);
        try { return await read.Purchases.GetAsync(companyId, branchId, documentId, cancellationToken).ConfigureAwait(false); }
        catch (ResourceNotFoundException) { return null; }
    }

    /// <summary>A reused request key must carry the same financial payload; otherwise it is a client bug, never a silent merge.</summary>
    private static void EnsureSameSales(SalesInvoiceDetailResult existing, CreateSalesInvoiceCommand command) =>
        DocumentRequestFingerprint.EnsureSame(existing, command);

    private static void EnsureSamePurchase(PurchaseDetailResult existing, SavePurchaseInvoiceCommand command) =>
        DocumentRequestFingerprint.EnsureSame(existing, command);

    private async Task<CompanyFinancialPolicy> EffectivePolicyAsync(Guid companyId, CancellationToken cancellationToken) =>
        (await options.GetAsync(companyId, cancellationToken).ConfigureAwait(false)).Policy;

    private static DocumentProcessingResult Draft(SalesInvoiceDetailResult document, CompanyFinancialPolicy policy)
    {
        if (!policy.AllowSaveAsDraft)
            throw new BusinessRuleException("company.draft_saving_disabled", "Company policy does not allow saving drafts; complete the document or enable draft saving in the company policy.");
        return new DocumentProcessingResult("SalesInvoice", document.Id, document.InvoiceNumber, document.Status.ToString(),
            DocumentProcessingOutcome.SavedDraft, false, false, document.JournalEntryId, policy.DocumentPostingMode.ToString(),
            Version(document.RowVersion))
        {
            Message = "تم حفظ المستند كمسودة. المسودة لا تُعتمد ولا تُرحَّل في أي وضع."
        };
    }

    private static DocumentProcessingResult Draft(PurchaseDetailResult document, CompanyFinancialPolicy policy)
    {
        if (!policy.AllowSaveAsDraft)
            throw new BusinessRuleException("company.draft_saving_disabled", "Company policy does not allow saving drafts; complete the document or enable draft saving in the company policy.");
        return new DocumentProcessingResult("PurchaseInvoice", document.Id, document.InvoiceNumber, document.Status.ToString(),
            DocumentProcessingOutcome.SavedDraft, false, false, document.JournalEntryId, policy.DocumentPostingMode.ToString(),
            Version(document.RowVersion))
        {
            Message = "تم حفظ المستند كمسودة. المسودة لا تُعتمد ولا تُرحَّل في أي وضع."
        };
    }

    private async Task<DocumentProcessingResult> AdvanceSalesAsync(Guid companyId, Guid branchId, Guid userId,
        DocumentProcessingPermissions permissions, SalesInvoiceDetailResult document, CompanyFinancialPolicy policy,
        DocumentPostingIntent? posting, CancellationToken cancellationToken)
    {
        var mode = policy.DocumentPostingMode.ToString();
        if (document.Status == SalesInvoiceStatus.Posted)
            return Sales(document, DocumentProcessingOutcome.AlreadyPosted, true, true, mode,
                "المستند مُرحَّل مسبقاً؛ لم تُنشأ حركة مالية جديدة.");

        var current = document;
        var approvalApplied = false;
        if (current.Status == SalesInvoiceStatus.Draft)
        {
            if (!permissions.CanApprove)
                return Sales(await EnrichedSalesAsync(companyId, branchId, userId, current.Id, cancellationToken).ConfigureAwait(false), DocumentProcessingOutcome.ApprovalPermissionMissing, false, false, mode,
                    "تم حفظ المستند كمسودة. لا تملك صلاحية اعتماد فواتير المبيعات، لذلك لم يُعتمد ولم يُرحَّل، ولم تُرفع صلاحياتك تلقائياً.");
            try
            {
                await using var approvePhase = OpenPhase(AccountingOperation.ApproveSalesInvoice, companyId, branchId, userId);
                using (PetraERP.Infrastructure.Sales.SalesDetailEnrichment.Suppress())
                    current = await approvePhase.Sales.ApproveInvoiceAsync(companyId, branchId, current.Id, userId,
                        ConcurrencyToken.FromBytes(current.RowVersion), cancellationToken).ConfigureAwait(false);
            }
            catch (BusinessRuleException exception)
            {
                return await SalesStageFailedAsync(companyId, branchId, userId, current.Id, DocumentProcessingOutcome.ApprovalFailed, mode, exception.Code, exception.Message, cancellationToken).ConfigureAwait(false);
            }
            catch (ConcurrencyConflictException exception)
            {
                return await SalesStageFailedAsync(companyId, branchId, userId, current.Id, DocumentProcessingOutcome.ApprovalFailed, mode, "approval.concurrency_conflict", exception.Message, cancellationToken).ConfigureAwait(false);
            }
            approvalApplied = true;
        }

        if (policy.DocumentPostingMode == DocumentPostingMode.ApproveOnly)
            return Sales(await EnrichedSalesAsync(companyId, branchId, userId, current.Id, cancellationToken).ConfigureAwait(false), DocumentProcessingOutcome.Approved, approvalApplied, false, mode,
                "تم اعتماد المستند بانتظار الترحيل وفق سياسة الشركة: الترحيل اليدوي بعد الحفظ.");

        if (!permissions.CanPost)
            return Sales(await EnrichedSalesAsync(companyId, branchId, userId, current.Id, cancellationToken).ConfigureAwait(false), DocumentProcessingOutcome.PostingPermissionMissing, approvalApplied, false, mode,
                "تم اعتماد المستند ولم يُرحَّل: لا تملك صلاحية ترحيل فواتير المبيعات.");

        if (string.IsNullOrWhiteSpace(posting?.IdempotencyKey))
            throw new BusinessRuleException("document.posting_key_required", "The server needs the posting idempotency key before it can post this document.");

        try
        {
            await using var postPhase = OpenPhase(AccountingOperation.PostSalesInvoice, companyId, branchId, userId);
            var posted = await postPhase.Sales.PostInvoiceAsync(companyId, branchId, current.Id, userId,
                new PostSalesInvoiceCommand(posting.IdempotencyKey, posting.Notes)
                {
                    ManualNumber = posting.ManualNumber,
                    ConfirmCreditWarning = posting.ConfirmCreditWarning,
                    OverrideCreditPolicy = posting.OverrideCreditPolicy,
                    CreditOverrideReason = posting.CreditOverrideReason,
                    NegativeStockReason = posting.NegativeStockReason,
                },
                cancellationToken).ConfigureAwait(false);
            return Sales(posted, DocumentProcessingOutcome.Posted, approvalApplied, true, mode,
                "تم اعتماد المستند وترحيله تلقائياً بعد نجاح جميع التحققات المحاسبية.");
        }
        catch (BusinessRuleException exception)
        {
            return await SalesStageFailedAsync(companyId, branchId, userId, current.Id, DocumentProcessingOutcome.PostingFailed, mode, exception.Code, exception.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (PostingIdempotencyConflictException exception)
        {
            return await SalesStageFailedAsync(companyId, branchId, userId, current.Id, DocumentProcessingOutcome.PostingFailed, mode, "posting.idempotency_conflict", exception.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (ConcurrencyConflictException exception)
        {
            return await SalesStageFailedAsync(companyId, branchId, userId, current.Id, DocumentProcessingOutcome.PostingFailed, mode, "posting.concurrency_conflict", exception.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The full detail (settlement and credit enrichment included) of a document an earlier step returned without it.</summary>
    private async Task<SalesInvoiceDetailResult> EnrichedSalesAsync(Guid companyId, Guid branchId, Guid userId, Guid invoiceId, CancellationToken cancellationToken)
    {
        await using var read = OpenPhase(AccountingOperation.ManageSalesInvoices, companyId, branchId, userId);
        return await read.Sales.GetInvoiceAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reports the real persisted document (identity, status, version) after a failed approve/post phase.</summary>
    private async Task<DocumentProcessingResult> SalesStageFailedAsync(Guid companyId, Guid branchId, Guid userId, Guid invoiceId,
        DocumentProcessingOutcome outcome, string mode, string code, string message, CancellationToken cancellationToken)
    {
        await using var read = OpenPhase(AccountingOperation.ManageSalesInvoices, companyId, branchId, userId);
        var reloaded = await read.Sales.GetInvoiceAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
        var approved = reloaded.Status is SalesInvoiceStatus.Approved or SalesInvoiceStatus.Posted;
        var posted = reloaded.Status == SalesInvoiceStatus.Posted;
        var prefix = outcome == DocumentProcessingOutcome.ApprovalFailed
            ? "تم حفظ المستند كمسودة ولم يُعتمد"
            : "تم اعتماد المستند ولم يُرحَّل";
        return Sales(reloaded, outcome, approved, posted, mode, $"{prefix}: {message}", code);
    }

    private async Task<DocumentProcessingResult> AdvancePurchaseAsync(Guid companyId, Guid branchId, Guid userId,
        DocumentProcessingPermissions permissions, PurchaseDetailResult document, CompanyFinancialPolicy policy,
        DocumentPostingIntent? posting, CancellationToken cancellationToken)
    {
        var mode = policy.DocumentPostingMode.ToString();
        if (document.Status == PurchaseInvoiceStatus.Posted)
            return Purchase(document, DocumentProcessingOutcome.AlreadyPosted, true, true, mode,
                "المستند مُرحَّل مسبقاً؛ لم تُنشأ حركة مالية جديدة.");

        var current = document;
        var approvalApplied = false;
        if (current.Status == PurchaseInvoiceStatus.Draft)
        {
            if (!permissions.CanApprove)
                return Purchase(current, DocumentProcessingOutcome.ApprovalPermissionMissing, false, false, mode,
                    "تم حفظ المستند كمسودة. لا تملك صلاحية اعتماد فواتير المشتريات، لذلك لم يُعتمد ولم يُرحَّل، ولم تُرفع صلاحياتك تلقائياً.");
            try
            {
                await using var approvePhase = OpenPhase(AccountingOperation.ApprovePurchaseInvoice, companyId, branchId, userId);
                current = await approvePhase.Purchases.ApproveAsync(companyId, branchId, current.Id, userId,
                    new ApprovePurchaseInvoiceCommand(ConcurrencyToken.FromBytes(current.RowVersion)), cancellationToken).ConfigureAwait(false);
            }
            catch (BusinessRuleException exception)
            {
                return await PurchaseStageFailedAsync(companyId, branchId, userId, current.Id, DocumentProcessingOutcome.ApprovalFailed, mode, exception.Code, exception.Message, cancellationToken).ConfigureAwait(false);
            }
            catch (ConcurrencyConflictException exception)
            {
                return await PurchaseStageFailedAsync(companyId, branchId, userId, current.Id, DocumentProcessingOutcome.ApprovalFailed, mode, "approval.concurrency_conflict", exception.Message, cancellationToken).ConfigureAwait(false);
            }
            approvalApplied = true;
        }

        if (policy.DocumentPostingMode == DocumentPostingMode.ApproveOnly)
            return Purchase(current, DocumentProcessingOutcome.Approved, approvalApplied, false, mode,
                "تم اعتماد المستند بانتظار الترحيل وفق سياسة الشركة: الترحيل اليدوي بعد الحفظ.");

        if (!permissions.CanPost)
            return Purchase(current, DocumentProcessingOutcome.PostingPermissionMissing, approvalApplied, false, mode,
                "تم اعتماد المستند ولم يُرحَّل: لا تملك صلاحية ترحيل فواتير المشتريات.");

        if (string.IsNullOrWhiteSpace(posting?.IdempotencyKey))
            throw new BusinessRuleException("document.posting_key_required", "The server needs the posting idempotency key before it can post this document.");

        try
        {
            await using var postPhase = OpenPhase(AccountingOperation.PostPurchaseInvoice, companyId, branchId, userId);
            var posted = await postPhase.Purchases.PostAsync(companyId, branchId, current.Id, userId,
                new PostPurchaseInvoiceCommand(ConcurrencyToken.FromBytes(current.RowVersion), posting.IdempotencyKey, posting.ManualNumber),
                cancellationToken).ConfigureAwait(false);
            return Purchase(posted, DocumentProcessingOutcome.Posted, approvalApplied, true, mode,
                "تم اعتماد المستند وترحيله تلقائياً بعد نجاح جميع التحققات المحاسبية.");
        }
        catch (BusinessRuleException exception)
        {
            return await PurchaseStageFailedAsync(companyId, branchId, userId, current.Id, DocumentProcessingOutcome.PostingFailed, mode, exception.Code, exception.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (PostingIdempotencyConflictException exception)
        {
            return await PurchaseStageFailedAsync(companyId, branchId, userId, current.Id, DocumentProcessingOutcome.PostingFailed, mode, "posting.idempotency_conflict", exception.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (ConcurrencyConflictException exception)
        {
            return await PurchaseStageFailedAsync(companyId, branchId, userId, current.Id, DocumentProcessingOutcome.PostingFailed, mode, "posting.concurrency_conflict", exception.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<DocumentProcessingResult> PurchaseStageFailedAsync(Guid companyId, Guid branchId, Guid userId, Guid invoiceId,
        DocumentProcessingOutcome outcome, string mode, string code, string message, CancellationToken cancellationToken)
    {
        await using var read = OpenPhase(AccountingOperation.ManagePurchaseInvoices, companyId, branchId, userId);
        var reloaded = await read.Purchases.GetAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
        var approved = reloaded.Status is PurchaseInvoiceStatus.Approved or PurchaseInvoiceStatus.Posted;
        var posted = reloaded.Status == PurchaseInvoiceStatus.Posted;
        var prefix = outcome == DocumentProcessingOutcome.ApprovalFailed
            ? "تم حفظ المستند كمسودة ولم يُعتمد"
            : "تم اعتماد المستند ولم يُرحَّل";
        return Purchase(reloaded, outcome, approved, posted, mode, $"{prefix}: {message}", code);
    }

    private static DocumentProcessingResult Sales(SalesInvoiceDetailResult document, DocumentProcessingOutcome outcome,
        bool approvalApplied, bool postingApplied, string mode, string message, string? failureCode = null) =>
        new("SalesInvoice", document.Id, document.InvoiceNumber, document.Status.ToString(), outcome, approvalApplied,
            postingApplied, document.JournalEntryId, mode, Version(document.RowVersion))
        { Message = message, FailureCode = failureCode };

    private static DocumentProcessingResult Purchase(PurchaseDetailResult document, DocumentProcessingOutcome outcome,
        bool approvalApplied, bool postingApplied, string mode, string message, string? failureCode = null) =>
        new("PurchaseInvoice", document.Id, document.InvoiceNumber, document.Status.ToString(), outcome, approvalApplied,
            postingApplied, document.JournalEntryId, mode, Version(document.RowVersion))
        { Message = message, FailureCode = failureCode };

    private static string Version(byte[] rowVersion) => ConcurrencyToken.FromBytes(rowVersion).ToBase64();
}
