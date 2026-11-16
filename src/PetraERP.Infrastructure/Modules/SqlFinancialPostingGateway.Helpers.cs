using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Security;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Modules.Abstractions;
using ModulePostingReceipt = PetraERP.Modules.Abstractions.PostingReceipt;

namespace PetraERP.Infrastructure.Modules;

public sealed partial class SqlFinancialPostingGateway
{
    /// <summary>Runs one step on a context bound to exactly one write operation, then releases it.</summary>
    private async Task<T> RunAsync<T>(ModuleRequestContext context, AccountingOperation operation,
        Func<FinancialDbContext, SqlJournalEntryService, Task<T>> step, CancellationToken cancellationToken)
    {
        var opened = await contexts.OpenAsync(context, operation, cancellationToken).ConfigureAwait(false);
        await using (opened.ConfigureAwait(false))
        {
            try
            {
                return await step(opened.Db, new SqlJournalEntryService(opened.Db, time)).ConfigureAwait(false);
            }
            catch
            {
                opened.Db.ChangeTracker.Clear();
                throw;
            }
        }
    }

    /// <summary>Validate (manage) then approve (approve) from whatever state the draft is in; each is its own authorized write.</summary>
    private async Task<JournalDetailsDto> DriveToApprovedAsync(ModuleRequestContext context, JournalEntryScope scope, JournalDetailsDto journal, string reason, CancellationToken cancellationToken)
    {
        if (journal.State == (byte)JournalEntryState.Draft)
        {
            var current = journal;
            journal = await RunAsync(context, AccountingOperation.ManageJournals,
                (db, journals) => journals.ValidateDraftAsync(scope, current.Id, Action(current, reason), cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        if (journal.State == (byte)JournalEntryState.Validated)
        {
            var current = journal;
            journal = await RunAsync(context, AccountingOperation.ApproveJournal,
                (db, journals) => journals.ApproveDraftAsync(scope, current.Id, Action(current, reason), cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        return journal;
    }

    private static string Marker(Guid key) => $" #{key:N}";

    private static string ModuleTag(string moduleId) => $"[module:{moduleId}]";

    private static JournalEntryScope Scope(ModuleRequestContext context) => JournalEntryScope.Create(context.CompanyId, context.BranchId, context.UserId);

    private static JournalDraftActionCommand Action(JournalDetailsDto journal, string reason) => new()
    {
        ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(journal.VersionBase64)),
        Reason = reason,
    };

    private static void RequirePermission(ModuleRequestContext context, string permission)
    {
        if (!context.Has(permission))
            throw new PostingAuthorizationException($"The module was not granted '{permission}'.");
    }

    /// <summary>Description, external reference, owning module and the idempotency marker; the tail always survives truncation.</summary>
    private static string Describe(ModuleRequestContext context, PostingProposal proposal)
    {
        var tail = (context.ModuleId is { Length: > 0 } module ? $" {ModuleTag(module)}" : string.Empty) + Marker(proposal.IdempotencyKey);
        var text = $"{proposal.Description.Trim()} | {proposal.ExternalReference.Trim()}";
        var room = JournalEntry.MaximumDescriptionLength - tail.Length;
        return (text.Length > room ? text[..room] : text) + tail;
    }

    private static string Reason(PostingProposal proposal) => $"{proposal.Description.Trim()} | {proposal.ExternalReference.Trim()}";

    private static async Task<string> BaseCurrencyAsync(FinancialDbContext db, Guid companyId, CancellationToken cancellationToken) =>
        await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.BaseCurrencyCode).SingleAsync(cancellationToken).ConfigureAwait(false);

    private static List<JournalDraftLineInput> ToLines(PostingProposal proposal, string baseCurrency) =>
        proposal.Lines.Select(l => new JournalDraftLineInput
        {
            AccountId = l.AccountId,
            CostCenterId = l.CostCenterId,
            TransactionCurrencyCode = l.CurrencyCode,
            TransactionDebit = l.Debit,
            TransactionCredit = l.Credit,
            RateValue = l.Rate,
            RateSourceCurrencyCode = l.CurrencyCode,
            RateTargetCurrencyCode = baseCurrency,
            Description = string.IsNullOrWhiteSpace(l.Description) ? proposal.Description : l.Description,
        }).ToList();

    private static List<string> ScopeShape(ModuleRequestContext context)
    {
        var errors = new List<string>();
        if (context.CompanyId == Guid.Empty) errors.Add("company is required.");
        if (context.BranchId == Guid.Empty) errors.Add("branch is required.");
        if (context.UserId == Guid.Empty) errors.Add("user is required.");
        if (context.FiscalYearId == Guid.Empty) errors.Add("fiscal year is required.");
        return errors;
    }

    /// <summary>Checks that need no database: a proposal that fails them can never be valid.</summary>
    private static List<string> ShapeErrors(ModuleRequestContext context, PostingProposal proposal)
    {
        var errors = ScopeShape(context);
        if (string.IsNullOrWhiteSpace(proposal.Description)) errors.Add("a description is required.");
        if (string.IsNullOrWhiteSpace(proposal.ExternalReference)) errors.Add("an external reference is required.");
        if (proposal.IdempotencyKey == Guid.Empty) errors.Add("an idempotency key is required.");
        if (proposal.Lines is null || proposal.Lines.Count < 2) { errors.Add("at least two lines are required."); return errors; }
        if (proposal.Lines.Count > MaximumLines) errors.Add($"at most {MaximumLines} lines are allowed.");
        for (var i = 0; i < proposal.Lines.Count; i++)
        {
            var line = proposal.Lines[i];
            if (line.AccountId == Guid.Empty) errors.Add($"line {i + 1}: account is required.");
            if (string.IsNullOrWhiteSpace(line.CurrencyCode)) errors.Add($"line {i + 1}: currency is required.");
            if (line.Debit < 0 || line.Credit < 0) errors.Add($"line {i + 1}: amounts cannot be negative.");
            if ((line.Debit > 0) == (line.Credit > 0)) errors.Add($"line {i + 1}: exactly one of debit or credit must be positive.");
            if (line.Rate <= 0) errors.Add($"line {i + 1}: the exchange rate must be positive.");
        }

        return errors;
    }

    /// <summary>The company, branch and fiscal year must exist together, and the date must lie inside that year.</summary>
    private static async Task<List<string>> ScopeErrorsAsync(FinancialDbContext db, ModuleRequestContext context, DateOnly date, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (!await db.Branches.AsNoTracking().AnyAsync(b => b.Id == context.BranchId && b.CompanyId == context.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            errors.Add("the branch does not belong to the company.");
            return errors;
        }

        var year = await db.FiscalYears.AsNoTracking()
            .SingleOrDefaultAsync(y => y.Id == context.FiscalYearId && y.CompanyId == context.CompanyId, cancellationToken).ConfigureAwait(false);
        if (year is null) errors.Add("the fiscal year does not belong to the company.");
        else if (date < year.RangeStart || date > year.RangeEnd) errors.Add("the accounting date is outside the fiscal year.");
        return errors;
    }

    private static Task<PostingRequestRow?> FindRequestAsync(FinancialDbContext db, ModuleRequestContext context, byte operation, Guid key, CancellationToken cancellationToken) =>
        db.PostingRequests.AsNoTracking().SingleOrDefaultAsync(r => r.CompanyId == context.CompanyId && r.BranchId == context.BranchId
            && r.Operation == operation && r.IdempotencyKey == key, cancellationToken);

    /// <summary>A replay must be the same entry; the same key with other content is a client bug, not a second posting.</summary>
    private static void EnsureSameProposal(PostingProposal proposal, IReadOnlyList<JournalDraftLineInput> lines, JournalDetailsDto original)
    {
        static string Shape(IEnumerable<(Guid Account, decimal Debit, decimal Credit)> rows) =>
            string.Join(";", rows.OrderBy(r => r.Account).ThenBy(r => r.Debit).ThenBy(r => r.Credit).Select(r => $"{r.Account:N}:{r.Debit:0.####}:{r.Credit:0.####}"));
        var sent = Shape(lines.Select(l => (l.AccountId, l.TransactionDebit, l.TransactionCredit)));
        var stored = Shape(original.Lines.Select(l => (l.AccountId, l.TransactionDebit, l.TransactionCredit)));
        if (original.AccountingDate != proposal.AccountingDate || sent != stored)
            throw new PostingIdempotencyConflictException(proposal.IdempotencyKey, "This idempotency key was already used for a different posting.");
    }

    private static ModulePostingReceipt Receipt(JournalDetailsDto journal, bool wasAlready) => new(
        journal.Id, journal.UpdatedAtUtc, journal.PostingSequence ?? 0, wasAlready);
}
