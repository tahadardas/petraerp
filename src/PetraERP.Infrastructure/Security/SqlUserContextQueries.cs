using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

public sealed class SqlUserContextQueries : IUserContextQueries
{
    private readonly CentralDataContext context;

    public SqlUserContextQueries(CentralDataContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
    }

    public async Task<CurrentUserProfile?> GetProfileAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var profile = await (
                from user in context.Users.AsNoTracking()
                join session in context.Sessions.AsNoTracking() on user.Id equals session.UserId
                where user.Id == userId && session.Id == sessionId
                select new
                {
                    user.Id,
                    user.UserName,
                    user.DisplayNameArabic,
                    user.DisplayNameEnglish,
                    user.MustChangePassword,
                    SessionId = session.Id,
                    session.ExpiresAtUtc,
                })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return null;
        }

        var permissions = await (
                from grant in context.UserRoles.AsNoTracking()
                join rolePermission in context.RolePermissions.AsNoTracking() on grant.RoleId equals rolePermission.RoleId
                where grant.UserId == userId
                select rolePermission.PermissionCode)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        permissions.Sort(StringComparer.Ordinal);

        return new CurrentUserProfile(profile.Id, profile.UserName, profile.DisplayNameArabic, profile.DisplayNameEnglish,
            profile.MustChangePassword, profile.SessionId, profile.ExpiresAtUtc, permissions.AsReadOnly());
    }

    public async Task<IReadOnlyList<CompanyContext>> GetCompaniesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var companies = await (
                from scope in context.UserCompanyScopes.AsNoTracking()
                join company in context.Companies.AsNoTracking() on scope.CompanyId equals company.Id
                where scope.UserId == userId && company.IsActive
                orderby company.Code
                select new CompanyContext(company.Id, company.Code, company.NameArabic, company.NameEnglish, company.BaseCurrencyCode))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return companies.AsReadOnly();
    }

    public async Task<IReadOnlyList<BranchContext>> GetBranchesAsync(Guid userId, Guid companyId, CancellationToken cancellationToken)
    {
        var branches = await (
                from scope in context.UserBranchScopes.AsNoTracking()
                join branch in context.Branches.AsNoTracking()
                    on new { scope.CompanyId, scope.BranchId } equals new { branch.CompanyId, BranchId = branch.Id }
                where scope.UserId == userId && scope.CompanyId == companyId && branch.IsActive
                orderby branch.Code
                select new BranchContext(branch.Id, branch.CompanyId, branch.Code, branch.NameArabic, branch.NameEnglish))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return branches.AsReadOnly();
    }
}
