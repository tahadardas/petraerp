using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Workspaces;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Workspaces;

public sealed class SqlWorkspaceAuthorizationService : IWorkspaceAuthorizationService
{
    private readonly SystemDbContext systemContext;
    private readonly IFiscalDatabaseResolver resolver;

    public SqlWorkspaceAuthorizationService(SystemDbContext systemContext, IFiscalDatabaseResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(systemContext);
        ArgumentNullException.ThrowIfNull(resolver);

        this.systemContext = systemContext;
        this.resolver = resolver;
    }

    public async Task<bool> CanAccessWorkspaceAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var workspace = await systemContext.FiscalDatabases.AsNoTracking()
            .FirstOrDefaultAsync(db => db.Id == workspaceId, cancellationToken)
            .ConfigureAwait(false);

        if (workspace is null || workspace.State is not ("Active" or "OpeningBalancesPending")) return false;

        // Check company scope
        var hasScope = await systemContext.UserCompanyScopes.AsNoTracking()
            .AnyAsync(s => s.UserId == userId && s.CompanyId == workspace.CompanyId
                && systemContext.Users.Any(u => u.Id == userId && u.IsActive && !u.MustChangePassword)
                && systemContext.Companies.Any(c => c.Id == workspace.CompanyId && c.IsActive), cancellationToken)
            .ConfigureAwait(false);

        return hasScope;
    }

    public async Task<IReadOnlyList<ResolvedFiscalDatabase>> GetAuthorizedWorkspacesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var authorizedCompanyIds = await systemContext.UserCompanyScopes.AsNoTracking()
            .Where(s => s.UserId == userId
                && systemContext.Users.Any(u => u.Id == userId && u.IsActive && !u.MustChangePassword)
                && systemContext.Companies.Any(c => c.Id == s.CompanyId && c.IsActive))
            .Select(s => s.CompanyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var workspaces = await systemContext.FiscalDatabases.AsNoTracking()
            .Where(db => authorizedCompanyIds.Contains(db.CompanyId) && (db.State == "Active" || db.State == "OpeningBalancesPending"))
            .OrderBy(db => db.CompanyId)
            .ThenByDescending(db => db.YearNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var list = new List<ResolvedFiscalDatabase>();
        foreach (var w in workspaces)
        {
            list.Add(await resolver.ResolveAsync(w.Id, cancellationToken).ConfigureAwait(false));
        }

        return list.AsReadOnly();
    }
}
