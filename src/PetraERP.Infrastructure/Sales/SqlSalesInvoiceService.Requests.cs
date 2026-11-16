using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Sales;

public sealed partial class SqlSalesInvoiceService
{
    private async Task<SalesPostingRequestRow> RegisterSalesIntentAsync(Guid companyId, Guid branchId, Guid invoiceId,
        Guid actorId, Guid key, PostingOperation operation, AuditReason reason, CancellationToken cancellationToken, string? payloadDigest = null)
    {
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                await LockSalesScopeAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);
                await new SqlPostingService(context).VerifySalesActorAsync(PostingRequestScope.Create(companyId, branchId, actorId), operation, cancellationToken).ConfigureAwait(false);
                var invoice = await LoadInvoiceForUpdateAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
                var registered = await context.SalesPostingRequests.SingleOrDefaultAsync(r => r.CompanyId == companyId && r.BranchId == branchId
                    && r.Operation == (byte)operation && r.IdempotencyKey == key, cancellationToken).ConfigureAwait(false);
                var version = registered?.ExpectedInvoiceVersion ?? invoice.RowVersion.ToArray();
                // A length-delimited JSON string array preserves Arabic and cannot alias delimiter-containing reasons.
                string[] fields = [ "PetraERP.SalesIntent.v1", companyId.ToString("D"), branchId.ToString("D"),
                    ((byte)operation).ToString(System.Globalization.CultureInfo.InvariantCulture), invoiceId.ToString("D"), actorId.ToString("D"), Convert.ToBase64String(version), reason.Value ];
                // Documents with their own payload (returns) bind it too; existing intents keep their original fingerprint.
                var payload = JsonSerializer.Serialize(payloadDigest is null ? fields : [.. fields, payloadDigest]);
                var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
                if (registered is not null && registered.FingerprintHex != fingerprint)
                    throw new PostingIdempotencyConflictException(key, "The sales request key is already bound to different intent.");
                if (registered is null)
                {
                    registered = new SalesPostingRequestRow
                    {
                        CompanyId = companyId,
                        BranchId = branchId,
                        Operation = (byte)operation,
                        IdempotencyKey = key,
                        InvoiceId = invoiceId,
                        ActorUserId = actorId,
                        ExpectedInvoiceVersion = version,
                        Reason = reason.Value,
                        FingerprintHex = fingerprint,
                        RegisteredAtUtc = timeProvider.GetUtcNow()
                    };
                    context.SalesPostingRequests.Add(registered);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                context.ChangeTracker.Clear();
                return registered;
            }
            catch { context.ChangeTracker.Clear(); throw; }
        }
    }

    private static void VerifySalesIntentVersion(SalesPostingRequestRow intent, SalesInvoiceRow invoice)
    {
        if (intent.CompletedAtUtc is null && !intent.ExpectedInvoiceVersion.AsSpan().SequenceEqual(invoice.RowVersion))
            throw new PostingConcurrencyException(invoice.Id, "The invoice changed after this request was registered; submit a new intent key.");
    }

    private async Task CompleteSalesIntentAsync(SalesPostingRequestRow intent, Guid journalId, CancellationToken cancellationToken)
    {
        var row = await context.SalesPostingRequests.SingleAsync(r => r.CompanyId == intent.CompanyId && r.BranchId == intent.BranchId
            && r.Operation == intent.Operation && r.IdempotencyKey == intent.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        row.OutcomeJournalId = journalId; row.CompletedAtUtc = timeProvider.GetUtcNow();
    }
}
