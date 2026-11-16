using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Branches;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Organizations;

/// <summary>
/// Branch-by-branch trial balance. The caller needs the report permission at every branch that is read; omitting the branch
/// list reads all branches of the company and therefore needs the permission at all of them (no silent narrowing).
/// </summary>
public sealed class SqlBranchConsolidationService(FinancialDbContext context, IAccessEvaluator access) : IBranchConsolidationService
{
    private static readonly byte[] PostedStates = [(byte)JournalEntryState.Posted, (byte)JournalEntryState.Reversed];

    public async Task<BranchTrialBalanceResult> TrialBalanceAsync(Guid companyId, Guid actorUserId, BranchTrialBalanceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.From > query.To)
            throw new BusinessRuleException("branches.report_range_invalid", "بداية الفترة بعد نهايتها / The period start is after its end.", "from");
        var company = await context.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Company");
        var all = await context.Branches.AsNoTracking().Where(b => b.CompanyId == companyId).OrderBy(b => b.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        var selected = query.BranchIds is { Count: > 0 } ids
            ? all.Where(b => ids.Contains(b.Id)).ToList()
            : all;
        if (query.BranchIds is { Count: > 0 } requested && selected.Count != requested.Distinct().Count())
            throw new ResourceNotFoundException("Branch");
        if (selected.Count == 0) throw new ResourceNotFoundException("Branch");

        var report = PermissionCatalog.Get(PermissionCatalog.ReportsView);
        foreach (var branch in selected)
            if (await access.EvaluatePermissionAsync(actorUserId, report, companyId, branch.Id, cancellationToken).ConfigureAwait(false) != AccessDecision.Granted)
                throw new BusinessRuleException("branches.report_scope_denied",
                    $"لا تملك صلاحية عرض التقارير على الفرع {branch.Code} / You lack the report permission on branch {branch.Code}.", "branchIds");

        var branchIds = selected.Select(b => b.Id).ToList();
        var from = query.From;
        var to = query.To;
        var aggregates = await context.JournalLines.AsNoTracking()
            .Where(l => l.CompanyId == companyId && PostedStates.Contains(l.Journal!.State) && l.Journal.AccountingDate <= to && branchIds.Contains(l.Journal.BranchId))
            .GroupBy(l => new { l.Journal!.BranchId, l.AccountId })
            .Select(g => new
            {
                g.Key.BranchId, g.Key.AccountId,
                Opening = g.Sum(l => l.Journal!.AccountingDate < from ? l.BaseDebit - l.BaseCredit : 0m),
                Debit = g.Sum(l => l.Journal!.AccountingDate >= from ? l.BaseDebit : 0m),
                Credit = g.Sum(l => l.Journal!.AccountingDate >= from ? l.BaseCredit : 0m),
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var accountIds = aggregates.Select(a => a.AccountId).Distinct().ToList();
        var accounts = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Code, a.NameArabic }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var clearing = (await context.BranchAccountLinks.AsNoTracking()
            .Where(l => l.CompanyId == companyId && l.Role == (byte)BranchAccountRole.InterBranchClearing).Select(l => l.AccountId).Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();

        var rows = new List<BranchTrialBalanceRow>();
        foreach (var account in accounts.OrderBy(a => a.Code, StringComparer.Ordinal))
        {
            var columns = selected.Select(b =>
            {
                var a = aggregates.FirstOrDefault(x => x.AccountId == account.Id && x.BranchId == b.Id);
                return a is null ? new BranchBalanceColumn(b.Id, 0m, 0m, 0m, 0m)
                    : new BranchBalanceColumn(b.Id, a.Opening, a.Debit, a.Credit, a.Opening + a.Debit - a.Credit);
            }).ToList();
            var isClearing = clearing.Contains(account.Id);
            var closing = columns.Sum(c => c.Closing);
            rows.Add(new(account.Id, account.Code, account.NameArabic, isClearing, columns, columns.Sum(c => c.Opening), columns.Sum(c => c.Debit),
                columns.Sum(c => c.Credit), closing, query.EliminateInterBranch && isClearing ? 0m : closing));
        }
        return new(from, to, company.BaseCurrencyCode, query.EliminateInterBranch,
            selected.Select(b => new BranchTrialBalanceBranch(b.Id, b.Code, b.NameArabic)).ToList(), rows,
            rows.Sum(r => r.TotalDebit), rows.Sum(r => r.TotalCredit), rows.Sum(r => r.ConsolidatedClosing),
            rows.Where(r => r.IsInterBranchClearing).Sum(r => r.TotalClosing));
    }
}
