using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Security;

namespace PetraERP.Infrastructure.Persistence.Actors;

public sealed class SqlAccountingActorAuthority(SystemDbContext system) : IAccountingActorAuthority
{
    public async Task VerifyInstallationAdministratorAsync(Guid actorId, CancellationToken cancellationToken)
    {
        var permission = PermissionCatalog.DatabaseManage;
        if (!await system.Users.AsNoTracking().AnyAsync(u => u.Id == actorId && u.IsActive && !u.MustChangePassword
            && system.UserRoles.Any(ur => ur.UserId == u.Id && system.RolePermissions.Any(rp => rp.RoleId == ur.RoleId
                && rp.PermissionCode == permission)), cancellationToken).ConfigureAwait(false))
            throw Denied();
    }

    public async Task<VerifiedAccountingActor> VerifyAsync(AccountingWriteScope scope, bool provisioning, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.CompanyId == Guid.Empty || scope.FiscalYearId == Guid.Empty || scope.ActorUserId == Guid.Empty
            || string.IsNullOrWhiteSpace(scope.PermissionCode) || !PermissionCatalog.TryGet(scope.PermissionCode, out var permission)
            || provisioning != (scope.Operation == AccountingOperation.ProvisionWorkspace)
            || (permission.Scope == PermissionScope.Branch && scope.BranchId is null))
            throw Denied();
        var actor = await system.Users.AsNoTracking().Where(u => u.Id == scope.ActorUserId && u.IsActive && !u.MustChangePassword
                && system.UserRoles.Any(ur => ur.UserId == u.Id && system.RolePermissions.Any(rp => rp.RoleId == ur.RoleId && rp.PermissionCode == scope.PermissionCode))
                && system.Companies.Any(c => c.Id == scope.CompanyId && (c.IsActive || provisioning))
                && (provisioning || system.FiscalDatabases.Any(f => f.CompanyId == scope.CompanyId && f.FiscalYearId == scope.FiscalYearId && (f.State == "Active" || f.State == "OpeningBalancesPending") && f.SchemaVersion == 1))
                && system.UserCompanyScopes.Any(cs => cs.UserId == u.Id && cs.CompanyId == scope.CompanyId)
                && (scope.BranchId == null || (system.Branches.Any(b => b.Id == scope.BranchId && b.CompanyId == scope.CompanyId && b.IsActive)
                    && system.UserBranchScopes.Any(bs => bs.UserId == u.Id && bs.CompanyId == scope.CompanyId && bs.BranchId == scope.BranchId))))
            .Select(u => new VerifiedAccountingActor(u.Id, u.DisplayNameArabic, u.DisplayNameEnglish))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return actor ?? throw Denied();
    }

    private static BusinessRuleException Denied() => new("accounting.actor_denied",
        "The authoritative security database could not authorize an active, scoped actor with the required permission.");
}
