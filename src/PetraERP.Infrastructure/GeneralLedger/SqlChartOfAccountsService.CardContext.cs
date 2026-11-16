using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Domain.Primitives;

namespace PetraERP.Infrastructure.GeneralLedger;

public sealed partial class SqlChartOfAccountsService
{
    public async Task<AccountCardContextDto> GetAccountCardContextAsync(LedgerMasterScope scope, Guid accountId, PageRequest childrenPage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(childrenPage);
        var path = await ReadAccountPathAsync(scope.CompanyId, accountId, cancellationToken).ConfigureAwait(false);
        var ids = path.Select(row => row.Id).ToArray();
        var facts = await context.Accounts.AsNoTracking().Where(row => row.CompanyId == scope.CompanyId && ids.Contains(row.Id))
            .Select(row => new { row.Id, HasChildren = context.Accounts.Any(child => child.CompanyId == scope.CompanyId && child.ParentId == row.Id),
                Used = context.JournalLines.Any(line => line.CompanyId == scope.CompanyId && line.AccountId == row.Id) })
            .ToDictionaryAsync(row => row.Id, cancellationToken).ConfigureAwait(false);
        var children = context.Accounts.AsNoTracking().Where(row => row.CompanyId == scope.CompanyId && row.ParentId == accountId);
        var total = await children.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var page = await children.OrderBy(row => row.Code).Skip(childrenPage.Skip).Take(childrenPage.PageSize)
            .Select(row => new { Row = row, HasChildren = context.Accounts.Any(child => child.CompanyId == scope.CompanyId && child.ParentId == row.Id),
                Used = context.JournalLines.Any(line => line.CompanyId == scope.CompanyId && line.AccountId == row.Id) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new AccountCardContextDto(path.Select((row, level) => Map(row, level, facts[row.Id].HasChildren, facts[row.Id].Used)).ToArray(),
            new PagedResult<AccountDto>(page.Select(item => Map(item.Row, path.Count, item.HasChildren, item.Used)).ToArray(),
                childrenPage.Page, childrenPage.PageSize, total));
    }

    // The anchor is a company-authorized primary-key lookup; recursion follows only its ancestors. Neither sibling
    // count nor the company's chart size affects returned rows. The visited path stops corrupt multi-row cycles;
    // SQL's maximum recursion guard remains in place. No persisted depth limit is introduced by this read model.
    private async Task<List<AccountRow>> ReadAccountPathAsync(Guid companyId, Guid accountId, CancellationToken cancellationToken)
    {
        var rows = await context.Accounts.FromSqlInterpolated($"""
            WITH [AccountPath] AS (
                SELECT [Id], [ParentId], 0 AS [Depth], CAST(CONVERT(varchar(36),[Id]) AS varchar(max)) AS [Visited]
                FROM [ledger].[Accounts] WHERE [CompanyId]={companyId} AND [Id]={accountId}
                UNION ALL
                SELECT a.[Id], a.[ParentId], p.[Depth]+1, CAST(p.[Visited]+'|'+CONVERT(varchar(36),a.[Id]) AS varchar(max))
                FROM [ledger].[Accounts] a INNER JOIN [AccountPath] p ON a.[Id]=p.[ParentId]
                WHERE a.[CompanyId]={companyId} AND CHARINDEX(CONVERT(varchar(36),a.[Id]),p.[Visited])=0
            )
            SELECT a.* FROM [AccountPath] p INNER JOIN [ledger].[Accounts] a ON a.[Id]=p.[Id] AND a.[CompanyId]={companyId}
            ORDER BY p.[Depth] DESC OPTION (MAXRECURSION 32767)
            """).AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0) throw new ResourceNotFoundException("account");
        if (rows[0].ParentId is not null)
            throw RuleViolation.Create(RuleViolation.AccountParentInvalid, "The stored account ancestry is cyclic or crosses a company boundary.");
        return rows;
    }
}
