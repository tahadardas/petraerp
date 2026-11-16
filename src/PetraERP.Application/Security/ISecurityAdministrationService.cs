using PetraERP.Application.Common;

namespace PetraERP.Application.Security;

/// <summary>
/// Installation administration of users, roles and scopes. Callers must already hold the
/// matching System.* permission; every change is audited and protected by concurrency versions.
/// </summary>
public interface ISecurityAdministrationService
{
    Task<PagedResult<UserSummary>> ListUsersAsync(UserListQuery query, CancellationToken cancellationToken);

    Task<UserDetails> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserDetails> CreateUserAsync(CreateUserCommand command, AdministrativeActor actor, CancellationToken cancellationToken);

    Task<UserDetails> UpdateUserProfileAsync(Guid userId, UpdateUserProfileCommand command, AdministrativeActor actor,
        CancellationToken cancellationToken);

    Task<UserDetails> SetUserActiveAsync(Guid userId, bool isActive, string reason, ConcurrencyToken version,
        AdministrativeActor actor, CancellationToken cancellationToken);

    Task<UserDetails> UnlockUserAsync(Guid userId, ConcurrencyToken version, AdministrativeActor actor,
        CancellationToken cancellationToken);

    Task<UserDetails> ResetPasswordAsync(Guid userId, ResetPasswordCommand command, AdministrativeActor actor,
        CancellationToken cancellationToken);

    Task<UserDetails> SetUserRolesAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, ConcurrencyToken version,
        AdministrativeActor actor, CancellationToken cancellationToken);

    Task<UserDetails> SetUserScopesAsync(Guid userId, IReadOnlyCollection<CompanyScopeGrant> scopes, ConcurrencyToken version,
        AdministrativeActor actor, CancellationToken cancellationToken);

    Task<int> RevokeUserSessionsAsync(Guid userId, string reason, AdministrativeActor actor, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoleSummary>> ListRolesAsync(CancellationToken cancellationToken);

    Task<RoleDetails> GetRoleAsync(Guid roleId, CancellationToken cancellationToken);

    Task<RoleDetails> CreateRoleAsync(CreateRoleCommand command, AdministrativeActor actor, CancellationToken cancellationToken);

    Task<RoleDetails> UpdateRoleAsync(Guid roleId, UpdateRoleCommand command, AdministrativeActor actor,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationCompany>> ListOrganizationAsync(CancellationToken cancellationToken);
}
