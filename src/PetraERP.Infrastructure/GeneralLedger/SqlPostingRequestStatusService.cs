using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Posting;
using PetraERP.Infrastructure.DocumentProcessing;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>
/// Read-only view of the durable idempotency registers: journal posting and sales invoice posting (Guid keys) and the
/// save-and-process document bindings (text keys of sales invoices, purchase invoices and vouchers).
/// </summary>
public sealed class SqlPostingRequestStatusService(FinancialDbContext context) : IPostingRequestStatusService
{
    private const int PostedStatus = 2;

    public async Task<PostingRequestStatus?> GetAsync(Guid companyId, Guid branchId, Guid actorUserId, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (Guid.TryParse(idempotencyKey, out var key))
        {
            var journal = await context.PostingRequests.AsNoTracking()
                .Where(r => r.IdempotencyKey == key && r.CompanyId == companyId && r.BranchId == branchId && r.ActorUserId == actorUserId)
                .Select(r => new PostingRequestStatus(idempotencyKey, PostingRequestKind.Journal, r.Operation, r.JournalId,
                    r.CompletedAtUtc != null ? PostingRequestState.Completed : PostingRequestState.NotCompleted, r.OutcomeJournalId, r.RegisteredAtUtc, r.CompletedAtUtc))
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (journal is not null) return journal;
            var sales = await context.SalesPostingRequests.AsNoTracking()
                .Where(r => r.IdempotencyKey == key && r.CompanyId == companyId && r.BranchId == branchId && r.ActorUserId == actorUserId)
                .Select(r => new PostingRequestStatus(idempotencyKey, PostingRequestKind.SalesInvoice, r.Operation, r.InvoiceId,
                    r.CompletedAtUtc != null ? PostingRequestState.Completed : PostingRequestState.NotCompleted, r.OutcomeJournalId, r.RegisteredAtUtc, r.CompletedAtUtc))
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (sales is not null) return sales;
        }

        // Text keys of save-and-process requests: the binding records which document the key created or updated; whether that
        // document reached the posted state is then read from the document itself.
        var binding = await context.Set<DocumentRequestBindingRow>().AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.BranchId == branchId && r.IdempotencyKey == idempotencyKey && r.ActorUserId == actorUserId)
            .OrderBy(r => r.RegisteredAtUtc).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (binding is null) return null;
        var family = binding.DocumentKind.Split('.')[0];
        var (kind, posted) = family switch
        {
            "SalesInvoice" => (PostingRequestKind.SalesInvoice, await context.SalesInvoices.AsNoTracking()
                .AnyAsync(d => d.Id == binding.DocumentId && d.Status == PostedStatus, cancellationToken).ConfigureAwait(false)),
            "PurchaseInvoice" => (PostingRequestKind.PurchaseInvoice, await context.PurchaseInvoices.AsNoTracking()
                .AnyAsync(d => d.Id == binding.DocumentId && d.Status == PostedStatus, cancellationToken).ConfigureAwait(false)),
            "Voucher" => (PostingRequestKind.Voucher, await context.Vouchers.AsNoTracking()
                .AnyAsync(d => d.Id == binding.DocumentId && d.Status == PostedStatus, cancellationToken).ConfigureAwait(false)),
            _ => (PostingRequestKind.Journal, false),
        };
        return new PostingRequestStatus(idempotencyKey, kind, 0, binding.DocumentId,
            posted ? PostingRequestState.Completed : PostingRequestState.NotCompleted, null, binding.RegisteredAtUtc, null);
    }
}
