using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>
/// Report read models computed in SQL from persisted journal lines. Posted truth is journals in state Posted or
/// Reversed (a reversed journal stays posted; its reversal is a separate posted journal). Nothing is cached.
/// </summary>
public sealed partial class SqlLedgerQueryService : ILedgerQueryService
{
    private static readonly byte[] PostedStates = [(byte)JournalEntryState.Posted, (byte)JournalEntryState.Reversed];
    private readonly FinancialDbContext context;

    private readonly ILedgerCursorSigner? cursorSigner;

    /// <param name="cursorSigner">Without a signer no cursor is issued and ledger paging stays offset-based.</param>
    public SqlLedgerQueryService(FinancialDbContext context, ILedgerCursorSigner? cursorSigner = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
        this.cursorSigner = cursorSigner;
    }

    public async Task<CompanyCurrencyContextDto> GetCurrencyContextAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await context.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId && c.IsActive, cancellationToken)
                          .ConfigureAwait(false) ?? throw new ResourceNotFoundException("company");
        var codes = await context.CompanyAdditionalCurrencies.AsNoTracking().Where(c => c.CompanyId == companyId)
            .Select(c => c.CurrencyCode).ToListAsync(cancellationToken).ConfigureAwait(false);
        codes.Add(company.BaseCurrencyCode);
        var currencies = await context.Currencies.AsNoTracking().Where(c => codes.Contains(c.Code))
            .ToDictionaryAsync(c => c.Code, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        CurrencyDto Describe(string code) => currencies.TryGetValue(code, out var row)
            ? new CurrencyDto(row.Code, row.NameArabic, row.NameEnglish)
            : new CurrencyDto(code, code, null);
        return new CompanyCurrencyContextDto(companyId, Describe(company.BaseCurrencyCode),
            codes.Where(c => c != company.BaseCurrencyCode).Order(StringComparer.Ordinal).Select(Describe).ToList().AsReadOnly());
    }

    public async Task<GeneralJournalDto> GetGeneralJournalAsync(LedgerReportScope scope, GeneralJournalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        EnsureRange(query.From, query.To);
        await EnsureScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var states = query.States is { Count: > 0 } requested ? requested.ToArray() : PostedStates;

        var lines = ScopedLines(scope).Where(l => states.Contains(l.Journal!.State));
        if (query.From is { } from) lines = lines.Where(l => l.Journal!.AccountingDate >= from);
        if (query.To is { } to) lines = lines.Where(l => l.Journal!.AccountingDate <= to);
        if (query.AccountId is { } accountId) lines = lines.Where(l => l.AccountId == accountId);
        if (query.CostCenterId is { } centerId) lines = lines.Where(l => l.CostCenterId == centerId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var text = query.Search.Trim();
            lines = long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var sequence)
                ? lines.Where(l => l.Journal!.PostingSequence == sequence || l.Journal.Description.Contains(text) || l.Description.Contains(text))
                : lines.Where(l => l.Journal!.Description.Contains(text) || l.Description.Contains(text));
        }

        var totals = await lines.GroupBy(static _ => 1).Select(g => new
        {
            Count = g.LongCount(),
            Debit = g.Sum(l => l.BaseDebit),
            Credit = g.Sum(l => l.BaseCredit),
        }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var page = await lines
            .OrderBy(l => l.Journal!.AccountingDate).ThenBy(l => l.Journal!.PostingSequence).ThenBy(l => l.JournalId).ThenBy(l => l.Id)
            .Skip(query.Page.Skip).Take(query.Page.PageSize)
            .Select(l => new GeneralJournalLineDto
            {
                JournalId = l.JournalId,
                LineId = l.Id,
                BranchId = l.Journal!.BranchId,
                AccountingDate = l.Journal.AccountingDate,
                PostingSequence = l.Journal.PostingSequence,
                State = l.Journal.State,
                JournalDescription = l.Journal.Description,
                LineDescription = l.Description,
                AccountId = l.AccountId,
                AccountCode = context.Accounts.Where(a => a.Id == l.AccountId).Select(a => a.Code).First(),
                AccountNameArabic = context.Accounts.Where(a => a.Id == l.AccountId).Select(a => a.NameArabic).First(),
                AccountNameEnglish = context.Accounts.Where(a => a.Id == l.AccountId).Select(a => a.NameEnglish).FirstOrDefault(),
                CostCenterId = l.CostCenterId,
                CostCenterCode = context.CostCenters.Where(c => c.Id == l.CostCenterId).Select(c => c.Code).FirstOrDefault(),
                TransactionCurrencyCode = l.TransactionCurrencyCode,
                TransactionDebit = l.TransactionDebit,
                TransactionCredit = l.TransactionCredit,
                BaseDebit = l.BaseDebit,
                BaseCredit = l.BaseCredit,
                ReversesEntryId = l.Journal.ReversesEntryId,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new GeneralJournalDto(page.AsReadOnly(), query.Page.Page, query.Page.PageSize, totals?.Count ?? 0,
            totals?.Debit ?? 0m, totals?.Credit ?? 0m, await BaseCurrencyAsync(scope.CompanyId, cancellationToken).ConfigureAwait(false));
    }

    private IQueryable<JournalLineRow> ScopedLines(LedgerReportScope scope)
    {
        var lines = context.JournalLines.AsNoTracking().Where(l => l.CompanyId == scope.CompanyId && l.Journal!.CompanyId == scope.CompanyId);
        return scope.Consolidated ? lines : lines.Where(l => l.Journal!.BranchId == scope.BranchId);
    }

    /// <summary>The route branch is authorized by policy; consolidation additionally needs every branch of the company.</summary>
    private async Task EnsureScopeAsync(LedgerReportScope scope, CancellationToken cancellationToken)
    {
        if (!scope.Consolidated)
        {
            return;
        }

        var authorizedBranches = await context.Identity.UserBranchScopes.AsNoTracking()
            .Where(s => s.UserId == scope.ActorUserId && s.CompanyId == scope.CompanyId).Select(s => s.BranchId)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var missing = await context.Branches.AsNoTracking().Where(b => b.CompanyId == scope.CompanyId)
            .AnyAsync(b => !authorizedBranches.Contains(b.Id), cancellationToken).ConfigureAwait(false);
        if (missing)
        {
            throw new ReportScopeDeniedException();
        }
    }

    private async Task<string> BaseCurrencyAsync(Guid companyId, CancellationToken cancellationToken) =>
        await context.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.BaseCurrencyCode)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? throw new ResourceNotFoundException("company");

    private static void EnsureRange(DateOnly? from, DateOnly? to)
    {
        if (from is { } start && to is { } end && end < start)
        {
            throw new RequestValidationException(LedgerMasterErrorCodes.ReportRangeInvalid, "The end date precedes the start date.", "to");
        }
    }
}
