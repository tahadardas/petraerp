using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Productivity;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Productivity;

/// <summary>Read-only global search and preview. Financial rows come exclusively from the independent annual
/// accounting database (<see cref="FinancialDbContext"/>, resolved lazily so central-only endpoints never force a
/// workspace binding); authorization and user preferences come from the central database (<see cref="CentralDataContext"/>).
/// No financial read ever falls back to the legacy context.</summary>
public sealed partial class SqlWorkspaceRepository(Func<FinancialDbContext> financialAccessor, CentralDataContext central) : IWorkspaceRepository
{
    // Central authorization snapshot: route-scope validity plus the per-category view permissions. Resolved against
    // the central database so the annual financial query never has to join across databases.
    private async Task<CategoryAccess> AccessAsync(WorkspaceScope scope, CancellationToken cancellationToken)
    {
        var snapshot = await central.Users.AsNoTracking().Where(user => user.Id == scope.UserId && user.IsActive).Select(user => new
        {
            ScopeValid = central.UserCompanyScopes.Any(grant => grant.UserId == user.Id && grant.CompanyId == scope.CompanyId)
                && central.UserBranchScopes.Any(grant => grant.UserId == user.Id && grant.CompanyId == scope.CompanyId && grant.BranchId == scope.BranchId)
                && central.Companies.Any(company => company.Id == scope.CompanyId && company.IsActive)
                && central.Branches.Any(branch => branch.Id == scope.BranchId && branch.CompanyId == scope.CompanyId && branch.IsActive),
            Accounts = central.UserRoles.Any(role => role.UserId == user.Id && central.RolePermissions.Any(permission => permission.RoleId == role.RoleId && permission.PermissionCode == PermissionCatalog.AccountsView)),
            Journals = central.UserRoles.Any(role => role.UserId == user.Id && central.RolePermissions.Any(permission => permission.RoleId == role.RoleId && permission.PermissionCode == PermissionCatalog.JournalsView)),
        }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return snapshot is null || !snapshot.ScopeValid ? CategoryAccess.None : new CategoryAccess(snapshot.Accounts, snapshot.Journals);
    }

    // Annual-only projection. Accounts and journals come from the bound workspace database; category visibility is
    // gated by the central permission snapshot (captured as query parameters, never a cross-database join).
    private static IQueryable<SearchProjection> Projection(FinancialDbContext annual, WorkspaceScope scope, CategoryAccess access) =>
        annual.Database.SqlQuery<SearchProjection>($"""
            SELECT a.[Id], CAST(0 AS int) AS [Category], CAST(a.[Code] AS nvarchar(64)) AS [Code],
                   CAST(a.[NameArabic] AS nvarchar(1000)) AS [Name], CAST(COALESCE(a.[NameEnglish], N'') AS nvarchar(1000)) AS [SearchName],
                   CAST(CASE WHEN a.[IsActive] = 1 THEN N'Active' ELSE N'Inactive' END AS nvarchar(32)) AS [Status],
                   CAST(NULL AS date) AS [Date]
            FROM [ledger].[Accounts] a WHERE a.[CompanyId] = {scope.CompanyId}
            UNION ALL
            SELECT j.[Id], CAST(6 AS int) AS [Category],
                   CAST(COALESCE(CAST(j.[PostingSequence] AS nvarchar(64)), CAST(j.[Id] AS nvarchar(64))) AS nvarchar(64)) AS [Code],
                   CAST(j.[Description] AS nvarchar(1000)) AS [Name], CAST(N'' AS nvarchar(1000)) AS [SearchName],
                   CAST(CASE j.[State] WHEN 1 THEN N'Draft' WHEN 2 THEN N'Validated' WHEN 3 THEN N'Approved'
                        WHEN 4 THEN N'Posted' WHEN 5 THEN N'Reversed' END AS nvarchar(32)) AS [Status], j.[AccountingDate] AS [Date]
            FROM [ledger].[JournalEntries] j WHERE j.[CompanyId] = {scope.CompanyId} AND j.[BranchId] = {scope.BranchId}
            """).Where(row => (row.Category == 0 && access.Accounts) || (row.Category == 6 && access.Journals));

    public async Task<PagedResult<SearchHit>> SearchAsync(WorkspaceScope scope, SearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var access = await AccessAsync(scope, cancellationToken).ConfigureAwait(false);
        if (!access.Any) return new PagedResult<SearchHit>([], query.Page.Page, query.Page.PageSize, 0);
        var source = Projection(financialAccessor(), scope, access);
        if (query.Category is { } category) source = source.Where(row => row.Category == (int)category);
        if (query.Text is { Length: > 0 } text) source = source.Where(row => EF.Functions.Collate(row.Code, "Latin1_General_100_CI_AS_SC").Contains(text) || EF.Functions.Collate(row.Name, "Latin1_General_100_CI_AS_SC").Contains(text) || EF.Functions.Collate(row.SearchName, "Latin1_General_100_CI_AS_SC").Contains(text));
        var count = await source.LongCountAsync(cancellationToken).ConfigureAwait(false);
        IOrderedQueryable<SearchProjection> sorted = (query.Order, query.Descending) switch
        {
            (SearchOrder.Name, false) => source.OrderBy(row => row.Name),
            (SearchOrder.Name, true) => source.OrderByDescending(row => row.Name),
            (SearchOrder.Date, false) => source.OrderBy(row => row.Date),
            (SearchOrder.Date, true) => source.OrderByDescending(row => row.Date),
            (_, true) => source.OrderByDescending(row => row.Code),
            _ => source.OrderBy(row => row.Code),
        };
        var rows = await sorted.ThenBy(row => row.Category).ThenBy(row => row.Id).Skip(query.Page.Skip).Take(query.Page.PageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<SearchHit>(rows.Select(Hit).ToArray(), query.Page.Page, query.Page.PageSize, count);
    }

    public async Task<IReadOnlyList<SearchHit>> ResolveRecentAsync(WorkspaceScope scope, IReadOnlyList<RecentReference> recent, CancellationToken cancellationToken)
    {
        // Only resolve references captured in the same company, branch and workspace; a reference carried over from a
        // different fiscal-year workspace never collides with this year's entities.
        var references = recent.Where(item => item.CompanyId == scope.CompanyId && item.BranchId == scope.BranchId && item.WorkspaceId == scope.WorkspaceId).Take(20).ToArray();
        var ids = references.Select(item => item.EntityId).ToArray();
        if (ids.Length == 0) return [];
        var access = await AccessAsync(scope, cancellationToken).ConfigureAwait(false);
        if (!access.Any) return [];
        var hits = await Projection(financialAccessor(), scope, access).Where(row => ids.Contains(row.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return references.Select(reference => hits.SingleOrDefault(row => row.Id == reference.EntityId && row.Category == (int)reference.Category))
            .Where(row => row is not null).Select(row => Hit(row!)).ToArray();
    }

    public async Task<EntityPreview?> PreviewAsync(WorkspaceScope scope, SearchCategory category, Guid id, int linePage, CancellationToken cancellationToken)
    {
        var access = await AccessAsync(scope, cancellationToken).ConfigureAwait(false);
        if (!access.Any) return null;
        var annual = financialAccessor();
        var header = await Projection(annual, scope, access).SingleOrDefaultAsync(row => row.Category == (int)category && row.Id == id, cancellationToken).ConfigureAwait(false);
        if (header is null) return null;
        if (category == SearchCategory.Accounts)
        {
            var account = await annual.Accounts.AsNoTracking().SingleAsync(row => row.Id == id && row.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false);
            return new EntityPreview(Hit(header), [new("الاسم العربي", "Arabic name", account.NameArabic),
                new("الاسم الإنكليزي", "English name", account.NameEnglish),
                new("قابل للترحيل", "Posting allowed", account.PostingAllowed.ToString(CultureInfo.InvariantCulture))], [], 1, 0, null, null, null);
        }
        var lines = annual.JournalLines.AsNoTracking().Where(row => row.JournalId == id && row.CompanyId == scope.CompanyId);
        var count = await lines.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var totals = await lines.GroupBy(row => row.BaseCurrencyCode).Select(group => new
            { Currency = group.Key, Debit = group.Sum(row => row.BaseDebit), Credit = group.Sum(row => row.BaseCredit) }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var page = await lines.OrderBy(row => row.Id).Skip((linePage - 1) * 50).Take(50)
            .Join(annual.Accounts.Where(account => account.CompanyId == scope.CompanyId), line => line.AccountId, account => account.Id, (line, account) => new
            { Account = account.Code, line.Description, Currency = line.TransactionCurrencyCode, Debit = line.TransactionDebit, Credit = line.TransactionCredit })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var total = totals.Count == 1 ? totals[0] : null;
        return new EntityPreview(Hit(header), [], page.Select(line => new PreviewLine(line.Account, line.Description, line.Currency,
            Exact(line.Debit), Exact(line.Credit))).ToArray(), linePage, count,
            total is null ? null : Exact(total.Debit), total is null ? null : Exact(total.Credit), total?.Currency);
    }
    private static string Exact(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static SearchHit Hit(SearchProjection row) => new(row.Id, (SearchCategory)row.Category, row.Code, row.Name, row.Status, row.Date);

    private readonly record struct CategoryAccess(bool Accounts, bool Journals)
    {
        internal static CategoryAccess None => new(false, false);
        internal bool Any => Accounts || Journals;
    }

    private sealed class SearchProjection
    {
        public Guid Id { get; set; }
        public int Category { get; set; }
        public required string Code { get; set; }
        public required string Name { get; set; }
        public required string SearchName { get; set; }
        public required string Status { get; set; }
        public DateOnly? Date { get; set; }
    }
}
