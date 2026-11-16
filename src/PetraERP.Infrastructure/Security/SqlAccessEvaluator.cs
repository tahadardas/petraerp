using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

/// <summary>
/// Resolves authorization from current database rows in one round trip. The requested company and
/// branch are only lookup targets: access requires role permission, an active company with an explicit
/// company grant, and (for branch permissions) an active branch of that same company with a branch grant.
/// </summary>
public sealed class SqlAccessEvaluator : IAccessEvaluator
{
    private readonly CentralDataContext context;

    public SqlAccessEvaluator(CentralDataContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
    }

    public async Task<AccessDecision> EvaluatePermissionAsync(Guid userId, PermissionDefinition permission, Guid? companyId,
        Guid? branchId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permission);
        var code = permission.Code;
        var company = companyId ?? Guid.Empty;
        var branch = branchId ?? Guid.Empty;
        var facts = await context.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new AccessFacts(
                user.IsActive,
                context.UserRoles.Any(grant => grant.UserId == user.Id
                    && context.RolePermissions.Any(rolePermission => rolePermission.RoleId == grant.RoleId
                        && rolePermission.PermissionCode == code)),
                context.UserCompanyScopes.Any(scope => scope.UserId == user.Id && scope.CompanyId == company
                    && context.Companies.Any(row => row.Id == company && row.IsActive)),
                context.UserBranchScopes.Any(scope => scope.UserId == user.Id && scope.CompanyId == company
                    && scope.BranchId == branch
                    && context.Branches.Any(row => row.Id == branch && row.CompanyId == company && row.IsActive))))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (facts is null || !facts.IsActive)
        {
            return AccessDecision.UserInactive;
        }

        if (!facts.HasPermission)
        {
            return AccessDecision.PermissionDenied;
        }

        return permission.Scope switch
        {
            PermissionScope.Installation => AccessDecision.Granted,
            PermissionScope.Company => companyId is not null && facts.HasCompany
                ? AccessDecision.Granted
                : AccessDecision.CompanyScopeDenied,
            PermissionScope.Branch when companyId is null || !facts.HasCompany => AccessDecision.CompanyScopeDenied,
            PermissionScope.Branch => branchId is not null && facts.HasBranch
                ? AccessDecision.Granted
                : AccessDecision.BranchScopeDenied,
            _ => AccessDecision.PermissionDenied,
        };
    }

    public async Task<AccessDecision> EvaluateCompanyScopeAsync(Guid userId, Guid companyId, CancellationToken cancellationToken)
    {
        var facts = await context.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.IsActive,
                HasCompany = context.UserCompanyScopes.Any(scope => scope.UserId == user.Id && scope.CompanyId == companyId
                    && context.Companies.Any(row => row.Id == companyId && row.IsActive)),
            })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (facts is null || !facts.IsActive)
        {
            return AccessDecision.UserInactive;
        }

        return facts.HasCompany ? AccessDecision.Granted : AccessDecision.CompanyScopeDenied;
    }

    private sealed record AccessFacts(bool IsActive, bool HasPermission, bool HasCompany, bool HasBranch);
}
