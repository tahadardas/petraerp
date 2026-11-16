using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Security;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Modules.Abstractions;
using ModulePostingReceipt = PetraERP.Modules.Abstractions.PostingReceipt;

namespace PetraERP.Infrastructure.Modules;

public sealed partial class SqlFinancialPostingGateway
{
    public async Task<ModulePostingReceipt> ReverseAsync(ModuleRequestContext context, Guid journalId, Guid idempotencyKey, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        RequirePermission(context, PermissionCatalog.JournalsReverse);
        if (journalId == Guid.Empty) throw new BusinessRuleException("module.posting_invalid", "journal id is required.");
        if (idempotencyKey == Guid.Empty) throw new BusinessRuleException("module.posting_invalid", "idempotency key is required.");
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("module.posting_invalid", "a reversal reason is required.");
        reason = reason.Trim();
        var shape = ScopeShape(context);
        if (shape.Count > 0) throw new BusinessRuleException("module.posting_invalid", shape[0]);

        var scope = Scope(context);
        JournalDetailsDto original;
        JournalDetailsDto draft;
        var read = await contexts.OpenAsync(context, null, cancellationToken).ConfigureAwait(false);
        await using (read.ConfigureAwait(false))
        {
            var readJournals = new SqlJournalEntryService(read.Db, time);
            var existing = await FindRequestAsync(read.Db, context, ReversalOperation, idempotencyKey, cancellationToken).ConfigureAwait(false);
            if (existing?.OutcomeJournalId is { } reversalId)
            {
                if (existing.OriginalJournalId != journalId)
                    throw new PostingIdempotencyConflictException(idempotencyKey, "This idempotency key was already used to reverse a different journal.");
                return Receipt(await readJournals.GetJournalAsync(scope, reversalId, cancellationToken).ConfigureAwait(false), wasAlready: true);
            }

            original = await readJournals.GetJournalAsync(scope, journalId, cancellationToken).ConfigureAwait(false);
            // A module may only reverse entries it posted itself (the owner tag is written by PostAsync).
            if (context.ModuleId is { Length: > 0 } owner && !original.Description.Contains(ModuleTag(owner), StringComparison.Ordinal))
                throw new PostingAuthorizationException($"The journal was not posted by module '{owner}'.");

            // An unposted reversal draft of this journal (from an attempt that stopped early) is resumed, never duplicated.
            var openDraftId = await read.Db.JournalEntries.AsNoTracking()
                .Where(j => j.CompanyId == context.CompanyId && j.BranchId == context.BranchId && j.ReversesEntryId == journalId && j.State < (byte)JournalEntryState.Posted)
                .Select(j => (Guid?)j.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (openDraftId is { } resume)
            {
                draft = await readJournals.GetJournalAsync(scope, resume, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var fiscalYear = await read.Db.FiscalYears.AsNoTracking()
                    .SingleOrDefaultAsync(y => y.Id == context.FiscalYearId && y.CompanyId == context.CompanyId, cancellationToken).ConfigureAwait(false);
                // Reverse today when that day lies in the working fiscal year; otherwise keep the original accounting date.
                var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
                var date = fiscalYear is not null && today >= fiscalYear.RangeStart && today <= fiscalYear.RangeEnd ? today : original.AccountingDate;
                var command = new CreateReversalDraftCommand
                {
                    OriginalJournalId = original.Id,
                    OriginalExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(original.VersionBase64)),
                    AccountingDate = date,
                    Reason = reason,
                };
                draft = await RunAsync(context, AccountingOperation.ManageJournals,
                    (db, journals) => journals.CreateReversalDraftAsync(scope, command, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
        }

        draft = await DriveToApprovedAsync(context, scope, draft, reason, cancellationToken).ConfigureAwait(false);
        var request = PostingRequest.Reverse(PostingRequestScope.Create(context.CompanyId, context.BranchId, context.UserId),
            idempotencyKey, draft.Id, ExpectedJournalVersion.FromBase64(draft.VersionBase64), original.Id,
            ExpectedJournalVersion.FromBase64(original.VersionBase64), AuditReason.Create(reason));
        var final = await RunAsync(context, AccountingOperation.ReverseJournal, async (db, journals) =>
        {
            await new SqlPostingService(db).PostAsync(request, cancellationToken).ConfigureAwait(false);
            return await journals.GetJournalAsync(scope, draft.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return Receipt(final, wasAlready: false);
    }
}
