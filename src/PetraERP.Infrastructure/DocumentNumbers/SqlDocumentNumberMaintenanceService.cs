using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.DocumentNumbers;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.DocumentNumbers;

/// <summary>
/// Document-number maintenance of one company-year database. A request names one document and a proposed number; a different
/// person approves it; approval rewrites only the number text and appends a hash-chained audit entry in the same transaction.
/// The document's id, type, status, amounts and journal link are never touched.
/// </summary>
public sealed class SqlDocumentNumberMaintenanceService(FinancialDbContext context, TimeProvider? clock = null) : IDocumentNumberMaintenanceService
{
    internal const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private const int MaxLength = 64;
    private TimeProvider Clock => clock ?? TimeProvider.System;

    public async Task<DocumentNumberPreviewResult> PreviewAsync(Guid companyId, Guid branchId, DocumentNumberFamily family, Guid documentId, string proposedNumber,
        CancellationToken cancellationToken = default)
    {
        var number = NormalizeNumber(proposedNumber);
        var doc = await LoadAsync(companyId, branchId, family, documentId, track: false, cancellationToken).ConfigureAwait(false);
        var collisions = await CollisionsAsync(companyId, branchId, family, documentId, number, cancellationToken).ConfigureAwait(false);
        string? blocking = null;
        if (string.Equals(doc.Number, number, StringComparison.Ordinal)) blocking = "docnum.same_number";
        else if (collisions.Count > 0) blocking = "docnum.number_in_use";
        else if (await context.DocumentNumberChanges.AsNoTracking().AnyAsync(r => r.CompanyId == companyId && r.Family == (byte)family
            && r.DocumentId == documentId && r.State == (byte)DocumentNumberChangeState.Pending, cancellationToken).ConfigureAwait(false))
            blocking = "docnum.request_pending";
        return new(family, documentId, doc.Number, number, blocking is null, blocking, collisions);
    }

    public async Task<DocumentNumberChangeResult> RequestAsync(Guid companyId, Guid branchId, Guid userId, RequestDocumentNumberChangeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var number = NormalizeNumber(command.NewNumber);
        var reason = Reason(command.Reason, "docnum.reason_required");
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        var doc = await LoadAsync(companyId, branchId, command.Family, command.DocumentId, track: false, cancellationToken).ConfigureAwait(false);
        if (string.Equals(doc.Number, number, StringComparison.Ordinal))
            throw new BusinessRuleException("docnum.same_number", "الرقم الجديد مطابق للحالي / The new number equals the current one.", "newNumber");

        var open = await context.DocumentNumberChanges.AsNoTracking().SingleOrDefaultAsync(r => r.CompanyId == companyId && r.Family == (byte)command.Family
            && r.DocumentId == command.DocumentId && r.State == (byte)DocumentNumberChangeState.Pending, cancellationToken).ConfigureAwait(false);
        if (open is not null)
        {
            // Retrying the identical request is idempotent; a different one must wait for the first to be decided.
            if (open.RequestedByUserId == userId && open.NewNumber == number) { await transaction.CommitAsync(cancellationToken).ConfigureAwait(false); return Map(open); }
            throw new ResourceConflictException("docnum.request_pending", "يوجد طلب معلق لهذا المستند / A change request is already pending for this document.");
        }
        var collisions = await CollisionsAsync(companyId, branchId, command.Family, command.DocumentId, number, cancellationToken).ConfigureAwait(false);
        if (collisions.Count > 0)
            throw new BusinessRuleException("docnum.number_in_use", "الرقم مستخدم أو محجوز لمستند آخر / The number is used or claimed by another document.", "newNumber");

        var now = Clock.GetUtcNow();
        var row = new DocumentNumberChangeRow
        {
            Id = Guid.CreateVersion7(now), CompanyId = companyId, BranchId = branchId, Family = (byte)command.Family, DocumentId = command.DocumentId,
            OldNumber = doc.Number, NewNumber = number, Reason = reason, State = (byte)DocumentNumberChangeState.Pending,
            RequestedByUserId = userId, RequestedAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        context.DocumentNumberChanges.Add(row);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Map(row);
    }

    public async Task<DocumentNumberChangeResult> ApproveAsync(Guid companyId, Guid branchId, Guid userId, Guid requestId, DocumentNumberDecisionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = Reason(command.Reason, "docnum.reason_required");
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        var request = await LoadRequestAsync(companyId, branchId, requestId, cancellationToken).ConfigureAwait(false);
        if (request.State != (byte)DocumentNumberChangeState.Pending)
            throw new ResourceConflictException("docnum.not_pending", "الطلب لم يعد معلقاً / The request is no longer pending.");
        if (request.RequestedByUserId == userId)
            throw new BusinessRuleException("docnum.two_person_required", "يجب أن يعتمد التغيير شخص غير مقدّم الطلب / A different person must approve the change.");

        var family = (DocumentNumberFamily)request.Family;
        var doc = await LoadAsync(companyId, branchId, family, request.DocumentId, track: true, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(doc.Number, request.OldNumber, StringComparison.Ordinal))
            throw new ResourceConflictException("docnum.stale_request", "تغيّر رقم المستند بعد الطلب؛ ارفض الطلب وقدّم غيره / The document number changed after the request.");
        var collisions = await CollisionsAsync(companyId, branchId, family, request.DocumentId, request.NewNumber, cancellationToken, ignoreRequestId: request.Id).ConfigureAwait(false);
        if (collisions.Count > 0)
            throw new BusinessRuleException("docnum.number_in_use", "الرقم أصبح مستخدماً / The proposed number is now in use.", "newNumber");

        var now = Clock.GetUtcNow();
        doc.SetNumber(request.NewNumber, now);
        request.State = (byte)DocumentNumberChangeState.Applied;
        request.DecidedByUserId = userId;
        request.DecidedAtUtc = now;
        request.DecisionReason = reason;
        request.UpdatedAtUtc = now;

        var last = await context.DocumentNumberAudit.AsNoTracking().Where(a => a.CompanyId == companyId).OrderByDescending(a => a.Sequence)
            .Select(a => new { a.Sequence, a.Hash }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var audit = new DocumentNumberAuditRow
        {
            Id = Guid.CreateVersion7(now), CompanyId = companyId, BranchId = branchId, Sequence = (last?.Sequence ?? 0) + 1, RequestId = request.Id,
            Family = request.Family, DocumentId = request.DocumentId, OldNumber = request.OldNumber, NewNumber = request.NewNumber,
            RequestedByUserId = request.RequestedByUserId, ApprovedByUserId = userId, OccurredAtUtc = now,
            PreviousHash = last?.Hash ?? GenesisHash, Hash = string.Empty,
        };
        audit.Hash = ComputeHash(audit);
        context.DocumentNumberAudit.Add(audit);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Map(request);
    }

    public Task<DocumentNumberChangeResult> RejectAsync(Guid companyId, Guid branchId, Guid userId, Guid requestId, DocumentNumberDecisionCommand command,
        CancellationToken cancellationToken = default) =>
        CloseAsync(companyId, branchId, userId, requestId, command, DocumentNumberChangeState.Rejected, requesterOnly: false, cancellationToken);

    public Task<DocumentNumberChangeResult> CancelAsync(Guid companyId, Guid branchId, Guid userId, Guid requestId, DocumentNumberDecisionCommand command,
        CancellationToken cancellationToken = default) =>
        CloseAsync(companyId, branchId, userId, requestId, command, DocumentNumberChangeState.Cancelled, requesterOnly: true, cancellationToken);

    private async Task<DocumentNumberChangeResult> CloseAsync(Guid companyId, Guid branchId, Guid userId, Guid requestId, DocumentNumberDecisionCommand command,
        DocumentNumberChangeState target, bool requesterOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = Reason(command.Reason, "docnum.reason_required");
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        var request = await LoadRequestAsync(companyId, branchId, requestId, cancellationToken).ConfigureAwait(false);
        if (request.State != (byte)DocumentNumberChangeState.Pending)
            throw new ResourceConflictException("docnum.not_pending", "الطلب لم يعد معلقاً / The request is no longer pending.");
        if (requesterOnly && request.RequestedByUserId != userId)
            throw new BusinessRuleException("docnum.cancel_requester_only", "الإلغاء لمقدّم الطلب فقط؛ استخدم الرفض / Only the requester cancels; others reject.");
        var now = Clock.GetUtcNow();
        request.State = (byte)target;
        request.DecidedByUserId = userId;
        request.DecidedAtUtc = now;
        request.DecisionReason = reason;
        request.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Map(request);
    }

    public async Task<IReadOnlyList<DocumentNumberChangeResult>> ListAsync(Guid companyId, Guid branchId, DocumentNumberChangeState? state, int take,
        CancellationToken cancellationToken = default)
    {
        var query = context.DocumentNumberChanges.AsNoTracking().Where(r => r.CompanyId == companyId && r.BranchId == branchId);
        if (state is { } s) query = query.Where(r => r.State == (byte)s);
        var rows = await query.OrderByDescending(r => r.RequestedAtUtc).Take(Math.Clamp(take, 1, 200)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<DocumentNumberAuditEntry>> AuditAsync(Guid companyId, Guid branchId, int take, CancellationToken cancellationToken = default)
    {
        var rows = await context.DocumentNumberAudit.AsNoTracking().Where(a => a.CompanyId == companyId && a.BranchId == branchId)
            .OrderByDescending(a => a.Sequence).Take(Math.Clamp(take, 1, 500)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(a => new DocumentNumberAuditEntry(a.Sequence, a.RequestId, (DocumentNumberFamily)a.Family, a.DocumentId, a.OldNumber, a.NewNumber,
            a.RequestedByUserId, a.ApprovedByUserId, a.OccurredAtUtc, a.PreviousHash, a.Hash)).ToList();
    }

    public async Task<DocumentNumberChainVerification> VerifyAuditChainAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var previous = GenesisHash;
        long expected = 1, checkedCount = 0;
        await foreach (var row in context.DocumentNumberAudit.AsNoTracking().Where(a => a.CompanyId == companyId).OrderBy(a => a.Sequence)
            .AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (row.Sequence != expected || row.PreviousHash != previous || row.Hash != ComputeHash(row))
                return new(false, checkedCount, row.Sequence);
            previous = row.Hash;
            expected++;
            checkedCount++;
        }
        return new(true, checkedCount, null);
    }

    internal static string ComputeHash(DocumentNumberAuditRow r)
    {
        var text = string.Join('|', r.PreviousHash, r.CompanyId.ToString("N"), r.Sequence.ToString(CultureInfo.InvariantCulture), r.RequestId.ToString("N"),
            r.Family.ToString(CultureInfo.InvariantCulture), r.DocumentId.ToString("N"), r.OldNumber ?? "\u0001", r.NewNumber,
            r.RequestedByUserId.ToString("N"), r.ApprovedByUserId.ToString("N"), r.OccurredAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private async Task<DocumentNumberChangeRow> LoadRequestAsync(Guid companyId, Guid branchId, Guid requestId, CancellationToken cancellationToken) =>
        await context.DocumentNumberChanges.SingleOrDefaultAsync(r => r.CompanyId == companyId && r.BranchId == branchId && r.Id == requestId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("DocumentNumberChange");

    private sealed record LoadedDocument(string? Number, Action<string, DateTimeOffset> SetNumber);

    private async Task<LoadedDocument> LoadAsync(Guid companyId, Guid branchId, DocumentNumberFamily family, Guid id, bool track, CancellationToken cancellationToken)
    {
        switch (family)
        {
            case DocumentNumberFamily.SalesInvoice:
            {
                var q = track ? context.SalesInvoices : context.SalesInvoices.AsNoTracking();
                var row = await q.SingleOrDefaultAsync(d => d.CompanyId == companyId && d.BranchId == branchId && d.Id == id, cancellationToken).ConfigureAwait(false)
                    ?? throw new ResourceNotFoundException("SalesInvoice");
                return new(row.InvoiceNumber, (n, now) => { row.InvoiceNumber = n; row.UpdatedAtUtc = now; });
            }
            case DocumentNumberFamily.PurchaseInvoice:
            {
                var q = track ? context.PurchaseInvoices : context.PurchaseInvoices.AsNoTracking();
                var row = await q.SingleOrDefaultAsync(d => d.CompanyId == companyId && d.BranchId == branchId && d.Id == id, cancellationToken).ConfigureAwait(false)
                    ?? throw new ResourceNotFoundException("PurchaseInvoice");
                return new(row.InvoiceNumber, (n, now) => { row.InvoiceNumber = n; row.UpdatedAtUtc = now; });
            }
            case DocumentNumberFamily.Voucher:
            {
                var q = track ? context.Vouchers : context.Vouchers.AsNoTracking();
                var row = await q.SingleOrDefaultAsync(d => d.CompanyId == companyId && d.BranchId == branchId && d.Id == id, cancellationToken).ConfigureAwait(false)
                    ?? throw new ResourceNotFoundException("Voucher");
                return new(row.VoucherNumber, (n, now) => { row.VoucherNumber = n; row.UpdatedAtUtc = now; });
            }
            default:
                throw new BusinessRuleException("docnum.family_invalid", "نوع المستند غير مدعوم / Unsupported document family.", "family");
        }
    }

    private async Task<List<DocumentNumberCollision>> CollisionsAsync(Guid companyId, Guid branchId, DocumentNumberFamily family, Guid documentId, string number,
        CancellationToken cancellationToken, Guid? ignoreRequestId = null)
    {
        var found = new List<DocumentNumberCollision>();
        var ids = family switch
        {
            DocumentNumberFamily.SalesInvoice => await context.SalesInvoices.AsNoTracking().Where(d => d.CompanyId == companyId && d.BranchId == branchId
                && d.InvoiceNumber == number && d.Id != documentId).Select(d => d.Id).Take(5).ToListAsync(cancellationToken).ConfigureAwait(false),
            DocumentNumberFamily.PurchaseInvoice => await context.PurchaseInvoices.AsNoTracking().Where(d => d.CompanyId == companyId && d.BranchId == branchId
                && d.InvoiceNumber == number && d.Id != documentId).Select(d => d.Id).Take(5).ToListAsync(cancellationToken).ConfigureAwait(false),
            DocumentNumberFamily.Voucher => await context.Vouchers.AsNoTracking().Where(d => d.CompanyId == companyId && d.BranchId == branchId
                && d.VoucherNumber == number && d.Id != documentId).Select(d => d.Id).Take(5).ToListAsync(cancellationToken).ConfigureAwait(false),
            _ => [],
        };
        found.AddRange(ids.Select(id => new DocumentNumberCollision(family, id, "Document")));
        var pending = await context.DocumentNumberChanges.AsNoTracking().Where(r => r.CompanyId == companyId && r.BranchId == branchId && r.Family == (byte)family
            && r.NewNumber == number && r.State == (byte)DocumentNumberChangeState.Pending && r.DocumentId != documentId && r.Id != ignoreRequestId)
            .Select(r => r.DocumentId).Take(5).ToListAsync(cancellationToken).ConfigureAwait(false);
        found.AddRange(pending.Select(id => new DocumentNumberCollision(family, id, "PendingRequest")));
        return found;
    }

    private static string NormalizeNumber(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength || text.Any(char.IsControl))
            throw new BusinessRuleException("docnum.number_invalid", "رقم المستند نص مطبوع من 1 إلى 64 محرفاً / A document number is printable text of 1 to 64 characters.", "newNumber");
        return text;
    }

    private static string Reason(string? reason, string code)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 500)
            throw new BusinessRuleException(code, "السبب مطلوب (حتى 500 محرف) / A reason of at most 500 characters is required.", "reason");
        return text;
    }

    private static DocumentNumberChangeResult Map(DocumentNumberChangeRow r) => new(r.Id, (DocumentNumberFamily)r.Family, r.DocumentId, r.OldNumber, r.NewNumber,
        r.Reason, (DocumentNumberChangeState)r.State, r.RequestedByUserId, r.RequestedAtUtc, r.DecidedByUserId, r.DecidedAtUtc, r.DecisionReason);
}
