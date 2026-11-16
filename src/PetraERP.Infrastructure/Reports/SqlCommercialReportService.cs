using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Patterns;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Reports;

/// <summary>
/// Read-only financial and commercial reports (PT-118..PT-124). Values derive from posted journal lines, posted documents and
/// authoritative stock movements; nothing is cached and no report writes data.
/// </summary>
public sealed partial class SqlCommercialReportService(FinancialDbContext context) : ICommercialReportService
{
    /// <summary>A reversed journal stays posted; its reversal is a separate posted journal.</summary>
    private static readonly byte[] PostedStates = [(byte)JournalEntryState.Posted, (byte)JournalEntryState.Reversed];
    private const int MaxLines = 5000;
    private const byte Assets = (byte)AccountTypeCode.Assets;
    private const byte Liabilities = (byte)AccountTypeCode.Liabilities;
    private const byte Equity = (byte)AccountTypeCode.Equity;
    private const byte Revenue = (byte)AccountTypeCode.Revenue;
    private const byte Expenses = (byte)AccountTypeCode.Expenses;

    public async Task<IncomeStatementResult> GetIncomeStatementAsync(LedgerReportScope scope, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        EnsureRange(fromDate, toDate);
        await EnsureScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var totals = await AccountTotalsAsync(scope, [Revenue, Expenses], fromDate, toDate, cancellationToken).ConfigureAwait(false);
        var lines = totals.Select(t => Line(t, t.Type == Revenue ? t.Credit - t.Debit : t.Debit - t.Credit)).Where(l => l.Amount != 0m).ToList();
        var revenue = lines.Where(l => l.Type == AccountTypeCode.Revenue).Sum(l => l.Amount);
        var expenses = lines.Where(l => l.Type == AccountTypeCode.Expenses).Sum(l => l.Amount);
        return new(fromDate, toDate, await BaseCurrencyAsync(scope.CompanyId, cancellationToken).ConfigureAwait(false), lines, revenue, expenses, revenue - expenses);
    }

    public async Task<BalanceSheetResult> GetBalanceSheetAsync(LedgerReportScope scope, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var totals = await AccountTotalsAsync(scope, [Assets, Liabilities, Equity, Revenue, Expenses], null, asOf, cancellationToken).ConfigureAwait(false);
        var lines = totals.Where(t => t.Type is Assets or Liabilities or Equity)
            .Select(t => Line(t, t.Type == Assets ? t.Debit - t.Credit : t.Credit - t.Debit)).Where(l => l.Amount != 0m).ToList();
        // No closing entries exist yet, so profit to date is presented as unclosed current earnings within equity.
        var earnings = totals.Where(t => t.Type is Revenue or Expenses).Sum(t => t.Credit - t.Debit);
        var assets = lines.Where(l => l.Type == AccountTypeCode.Assets).Sum(l => l.Amount);
        var liabilities = lines.Where(l => l.Type == AccountTypeCode.Liabilities).Sum(l => l.Amount);
        var equity = lines.Where(l => l.Type == AccountTypeCode.Equity).Sum(l => l.Amount);
        return new(asOf, await BaseCurrencyAsync(scope.CompanyId, cancellationToken).ConfigureAwait(false), lines, assets, liabilities, equity, earnings,
            assets == liabilities + equity + earnings);
    }

    public async Task<IReadOnlyList<CashAccountResult>> GetCashAccountsAsync(LedgerReportScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var roles = await MappedAccountsAsync(scope.CompanyId, ["Cash", "Bank"], cancellationToken).ConfigureAwait(false);
        var ids = roles.Keys.ToList();
        var accounts = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId && ids.Contains(a.Id))
            .OrderBy(a => a.Code).Select(a => new { a.Id, a.Code, a.NameArabic }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return accounts.Select(a => new CashAccountResult(a.Id, a.Code, a.NameArabic, roles[a.Id])).ToList();
    }

    public async Task<CashStatementResult> GetCashStatementAsync(LedgerReportScope scope, Guid accountId, DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        EnsureRange(fromDate, toDate);
        await EnsureScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var roles = await MappedAccountsAsync(scope.CompanyId, ["Cash", "Bank"], cancellationToken).ConfigureAwait(false);
        var account = await context.Accounts.AsNoTracking().Where(a => a.Id == accountId && a.CompanyId == scope.CompanyId)
            .Select(a => new { a.Code, a.NameArabic }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("account");
        if (!roles.ContainsKey(accountId))
            throw new BusinessRuleException("reports.not_cash_account", "The account is not mapped as a Cash or Bank account in any document pattern.", "accountId");

        var lines = PostedLines(scope).Where(l => l.AccountId == accountId);
        var opening = await lines.Where(l => l.Journal!.AccountingDate < fromDate).SumAsync(l => l.BaseDebit - l.BaseCredit, cancellationToken).ConfigureAwait(false);
        var period = lines.Where(l => l.Journal!.AccountingDate >= fromDate && l.Journal.AccountingDate <= toDate);
        var totals = await period.GroupBy(_ => 1).Select(g => new { Debit = g.Sum(l => l.BaseDebit), Credit = g.Sum(l => l.BaseCredit), Count = g.Count() })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var rows = await period.OrderBy(l => l.Journal!.AccountingDate).ThenBy(l => l.Journal!.PostingSequence).ThenBy(l => l.Id).Take(MaxLines)
            .Select(l => new
            {
                l.Journal!.AccountingDate, l.JournalId, l.Journal.PostingSequence, Description = l.Journal.Description + " — " + l.Description,
                l.TransactionCurrencyCode, l.TransactionDebit, l.TransactionCredit, l.BaseDebit, l.BaseCredit,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var sources = await SourceDocumentsAsync(scope.CompanyId, rows.Select(r => r.JournalId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        var balance = opening;
        var result = new List<CashStatementLine>(rows.Count);
        foreach (var r in rows)
        {
            balance += r.BaseDebit - r.BaseCredit;
            sources.TryGetValue(r.JournalId, out var source);
            result.Add(new(r.AccountingDate, r.JournalId, r.PostingSequence, r.Description, source?.Type, source?.Id, source?.Number,
                r.TransactionCurrencyCode, r.TransactionDebit, r.TransactionCredit, r.BaseDebit, r.BaseCredit, balance));
        }
        var debit = totals?.Debit ?? 0m; var credit = totals?.Credit ?? 0m;
        return new(accountId, account.Code, account.NameArabic, fromDate, toDate, await BaseCurrencyAsync(scope.CompanyId, cancellationToken).ConfigureAwait(false),
            opening, result, debit, credit, opening + debit - credit, (totals?.Count ?? 0) > MaxLines);
    }

    private sealed record SourceDocument(string Type, Guid Id, string? Number);

    /// <summary>Resolves the business document that produced each journal (posting or reversal journal).</summary>
    private async Task<Dictionary<Guid, SourceDocument>> SourceDocumentsAsync(Guid companyId, List<Guid> journalIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, SourceDocument>();
        if (journalIds.Count == 0) return result;
        var ids = journalIds.Select(id => (Guid?)id).ToList();
        foreach (var s in await context.SalesInvoices.AsNoTracking().Where(i => i.CompanyId == companyId && ids.Contains(i.JournalEntryId))
            .Select(i => new { i.JournalEntryId, i.Id, i.InvoiceNumber, i.ReversesInvoiceId, i.ReturnOfInvoiceId }).ToListAsync(cancellationToken).ConfigureAwait(false))
            result[s.JournalEntryId!.Value] = new(s.ReturnOfInvoiceId is not null ? "SalesReturn" : s.ReversesInvoiceId is not null ? "SalesReversal" : "SalesInvoice",
                s.Id, s.InvoiceNumber);
        foreach (var v in await context.Vouchers.AsNoTracking().Where(v => v.CompanyId == companyId && (ids.Contains(v.JournalEntryId) || ids.Contains(v.ReversalJournalEntryId)))
            .Select(v => new { v.JournalEntryId, v.ReversalJournalEntryId, v.Id, v.VoucherNumber, v.Kind }).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            var type = v.Kind == 1 ? "ReceiptVoucher" : "PaymentVoucher";
            if (v.JournalEntryId is { } posted) result[posted] = new(type, v.Id, v.VoucherNumber);
            if (v.ReversalJournalEntryId is { } reversal) result[reversal] = new(type + "Reversal", v.Id, v.VoucherNumber);
        }
        foreach (var p in await context.PurchaseInvoices.AsNoTracking().Where(i => i.CompanyId == companyId && ids.Contains(i.JournalEntryId))
            .Select(i => new { i.JournalEntryId, i.Id, i.InvoiceNumber, i.ReturnOfInvoiceId, i.ReversesInvoiceId }).ToListAsync(cancellationToken).ConfigureAwait(false))
            result[p.JournalEntryId!.Value] = new(p.ReturnOfInvoiceId is not null ? "PurchaseReturn"
                : p.ReversesInvoiceId is not null ? "PurchaseReversal" : "PurchaseInvoice", p.Id, p.InvoiceNumber);
        foreach (var d in await context.InventoryDocuments.AsNoTracking().Where(i => i.CompanyId == companyId && (ids.Contains(i.JournalId) || ids.Contains(i.ReversalJournalId)))
            .Select(i => new { i.JournalId, i.ReversalJournalId, i.Id, i.Number }).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (d.JournalId is { } posted) result[posted] = new("InventoryDocument", d.Id, d.Number);
            if (d.ReversalJournalId is { } reversal) result[reversal] = new("InventoryDocumentReversal", d.Id, d.Number);
        }
        return result;
    }

    private sealed record AccountTotal(Guid Id, string Code, string NameArabic, string? NameEnglish, byte Type, decimal Debit, decimal Credit);

    private async Task<List<AccountTotal>> AccountTotalsAsync(LedgerReportScope scope, byte[] types, DateOnly? from, DateOnly to, CancellationToken cancellationToken)
    {
        var lines = PostedLines(scope).Where(l => l.Journal!.AccountingDate <= to);
        if (from is { } start) lines = lines.Where(l => l.Journal!.AccountingDate >= start);
        var accounts = context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId && types.Contains(a.AccountType));
        return await lines.Join(accounts, l => l.AccountId, a => a.Id, (l, a) => new { a.Id, a.Code, a.NameArabic, a.NameEnglish, a.AccountType, l.BaseDebit, l.BaseCredit })
            .GroupBy(x => new { x.Id, x.Code, x.NameArabic, x.NameEnglish, x.AccountType })
            .OrderBy(g => g.Key.Code)
            .Select(g => new AccountTotal(g.Key.Id, g.Key.Code, g.Key.NameArabic, g.Key.NameEnglish, g.Key.AccountType, g.Sum(x => x.BaseDebit), g.Sum(x => x.BaseCredit)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private static StatementAccountLine Line(AccountTotal t, decimal amount) =>
        new(t.Id, t.Code, t.NameArabic, t.NameEnglish, (AccountTypeCode)t.Type, amount);

    /// <summary>Account ids mapped to the given roles by any published or retired pattern revision of the company.</summary>
    private async Task<Dictionary<Guid, string>> MappedAccountsAsync(Guid companyId, string[] roles, CancellationToken cancellationToken)
    {
        var configurations = await context.PatternRevisions.AsNoTracking()
            .Where(r => r.Pattern!.CompanyId == companyId && (r.State == (int)PatternRevisionState.Published || r.State == (int)PatternRevisionState.Retired))
            .Select(r => r.ConfigurationJson).ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<Guid, string>();
        foreach (var json in configurations)
        {
            PatternConfiguration? configuration;
            try { configuration = JsonSerializer.Deserialize<PatternConfiguration>(json); }
            catch (JsonException) { continue; }
            foreach (var mapping in configuration?.AccountMappings ?? [])
            {
                var role = roles.FirstOrDefault(r => string.Equals(r, mapping.Role, StringComparison.OrdinalIgnoreCase));
                if (role is not null && mapping.AccountId != Guid.Empty) result.TryAdd(mapping.AccountId, role);
            }
        }
        return result;
    }

    private IQueryable<JournalLineRow> PostedLines(LedgerReportScope scope)
    {
        var lines = context.JournalLines.AsNoTracking()
            .Where(l => l.CompanyId == scope.CompanyId && l.Journal!.CompanyId == scope.CompanyId && PostedStates.Contains(l.Journal.State));
        return scope.Consolidated ? lines : lines.Where(l => l.Journal!.BranchId == scope.BranchId);
    }

    /// <summary>The route branch is authorized by policy; consolidation additionally needs every branch of the company.</summary>
    private async Task EnsureScopeAsync(LedgerReportScope scope, CancellationToken cancellationToken)
    {
        if (!scope.Consolidated) return;
        var authorizedBranches = await context.Identity.UserBranchScopes.AsNoTracking()
            .Where(s => s.UserId == scope.ActorUserId && s.CompanyId == scope.CompanyId).Select(s => s.BranchId)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var missing = await context.Branches.AsNoTracking().Where(b => b.CompanyId == scope.CompanyId)
            .AnyAsync(b => !authorizedBranches.Contains(b.Id), cancellationToken).ConfigureAwait(false);
        if (missing) throw new ReportScopeDeniedException();
    }

    private async Task<string> BaseCurrencyAsync(Guid companyId, CancellationToken cancellationToken) =>
        await context.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.BaseCurrencyCode)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? throw new ResourceNotFoundException("company");

    private static void EnsureRange(DateOnly from, DateOnly to)
    {
        if (to < from) throw new RequestValidationException("reports.range_invalid", "The end date precedes the start date.", "to");
        if (to.DayNumber - from.DayNumber > 3660) throw new RequestValidationException("reports.range_invalid", "A report range may not exceed ten years.", "to");
    }

    private static DateTimeOffset StartUtc(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>Exclusive upper bound: the first instant after the given day (UTC).</summary>
    private static DateTimeOffset EndUtc(DateOnly date) => new(date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
