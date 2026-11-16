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

/// <summary>
/// The only way a module changes the ledger. It never writes journal tables itself: a proposal becomes a normal draft,
/// goes through the same validate/approve steps and is posted by <see cref="SqlPostingService"/>, so company/branch scope,
/// actor permission, closed-period, posting-sequence and audit rules are exactly those of the built-in documents.
/// Each step (draft, approve, post) runs on its own context bound to exactly one write operation, because Petra authorizes
/// those operations separately. Idempotency reuses the engine's durable request rows: replaying a key returns the original
/// receipt, replaying it with different content is rejected, and an interrupted attempt is resumed, never duplicated.
/// </summary>
public sealed partial class SqlFinancialPostingGateway(IModuleLedgerContexts contexts, TimeProvider time) : IFinancialPostingGateway
{
    private const int MaximumLines = 500;
    private const byte PostOperation = 1;
    private const byte ReversalOperation = 2;

    public async Task<PostingProposalResult> ValidateAsync(ModuleRequestContext context, PostingProposal proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(proposal);
        var errors = ShapeErrors(context, proposal);
        if (errors.Count == 0)
        {
            var read = await contexts.OpenAsync(context, null, cancellationToken).ConfigureAwait(false);
            await using (read.ConfigureAwait(false))
            {
                errors.AddRange(await ScopeErrorsAsync(read.Db, context, proposal.AccountingDate, cancellationToken).ConfigureAwait(false));
                if (errors.Count == 0)
                {
                    var baseCurrency = await BaseCurrencyAsync(read.Db, context.CompanyId, cancellationToken).ConfigureAwait(false);
                    var result = await new SqlJournalEntryService(read.Db, time).ValidateJournalLinesAsync(
                        Scope(context), proposal.AccountingDate, ToLines(proposal, baseCurrency), cancellationToken).ConfigureAwait(false);
                    if (!result.IsValid) errors.AddRange(result.Errors);
                }
            }
        }

        return new PostingProposalResult(errors.Count == 0, errors.Count == 0 ? null : errors[0], errors);
    }

    public async Task<ModulePostingReceipt> PostAsync(ModuleRequestContext context, PostingProposal proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(proposal);
        RequirePermission(context, PermissionCatalog.JournalsPost);
        var errors = ShapeErrors(context, proposal);
        if (errors.Count > 0) throw new BusinessRuleException("module.posting_invalid", errors[0]);

        var scope = Scope(context);
        var reason = Reason(proposal);
        JournalDetailsDto draft;

        var read = await contexts.OpenAsync(context, null, cancellationToken).ConfigureAwait(false);
        await using (read.ConfigureAwait(false))
        {
            errors.AddRange(await ScopeErrorsAsync(read.Db, context, proposal.AccountingDate, cancellationToken).ConfigureAwait(false));
            if (errors.Count > 0) throw new BusinessRuleException("module.posting_invalid", errors[0]);

            var baseCurrency = await BaseCurrencyAsync(read.Db, context.CompanyId, cancellationToken).ConfigureAwait(false);
            var lines = ToLines(proposal, baseCurrency);
            var readJournals = new SqlJournalEntryService(read.Db, time);

            // A completed request is a replay: return its receipt, or refuse if the content changed.
            var existing = await FindRequestAsync(read.Db, context, PostOperation, proposal.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            if (existing?.OutcomeJournalId is { } posted)
            {
                var original = await readJournals.GetJournalAsync(scope, posted, cancellationToken).ConfigureAwait(false);
                EnsureSameProposal(proposal, lines, original);
                return Receipt(original, wasAlready: true);
            }

            // An earlier attempt that stopped after committing its draft is resumed (the draft carries the key).
            var marker = Marker(proposal.IdempotencyKey);
            var orphanId = await read.Db.JournalEntries.AsNoTracking()
                .Where(j => j.CompanyId == context.CompanyId && j.BranchId == context.BranchId && j.Description.EndsWith(marker)
                    && j.ReversesEntryId == null && j.State < (byte)JournalEntryState.Posted)
                .Select(j => (Guid?)j.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (orphanId is { } resume)
            {
                draft = await readJournals.GetJournalAsync(scope, resume, cancellationToken).ConfigureAwait(false);
                EnsureSameProposal(proposal, lines, draft);
            }
            else
            {
                // Reject what the engine would reject before any draft exists, so a bad proposal leaves no orphan entry.
                var check = await readJournals.ValidateJournalLinesAsync(scope, proposal.AccountingDate, lines, cancellationToken).ConfigureAwait(false);
                if (!check.IsValid) throw new BusinessRuleException("module.posting_invalid", check.Errors.Count > 0 ? check.Errors[0] : "The proposal is not a valid journal.");
                draft = await RunAsync(context, AccountingOperation.ManageJournals, (db, journals) =>
                    journals.CreateDraftAsync(scope, new CreateJournalDraftCommand
                    {
                        AccountingDate = proposal.AccountingDate,
                        Description = Describe(context, proposal),
                        Lines = lines,
                    }, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
        }

        draft = await DriveToApprovedAsync(context, scope, draft, reason, cancellationToken).ConfigureAwait(false);
        var request = PostingRequest.Post(PostingRequestScope.Create(context.CompanyId, context.BranchId, context.UserId),
            proposal.IdempotencyKey, draft.Id, ExpectedJournalVersion.FromBase64(draft.VersionBase64), AuditReason.Create(reason));
        var final = await RunAsync(context, AccountingOperation.PostJournal, async (db, journals) =>
        {
            await new SqlPostingService(db).PostAsync(request, cancellationToken).ConfigureAwait(false);
            return await journals.GetJournalAsync(scope, draft.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return Receipt(final, wasAlready: false);
    }
}
