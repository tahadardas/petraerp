using PetraERP.Desktop.Administration;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Connection;

public sealed class AdministrationClient(DesktopSession session) : IAdministrationClient
{
    public Task<UserPageResponse> UsersAsync(string? search, int page, CancellationToken cancellationToken) => session.Api.ListUsersAsync(search, page, 50, cancellationToken);
    public Task<UserDetailsResponse> UserAsync(Guid id, CancellationToken cancellationToken) => session.Api.GetUserAsync(id, cancellationToken);
    public Task<UserDetailsResponse> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken) => session.Api.CreateUserAsync(request, cancellationToken);
    public Task<UserDetailsResponse> ProfileAsync(Guid id, UpdateUserProfileRequest request, CancellationToken cancellationToken) => session.Api.UpdateUserProfileAsync(id, request, cancellationToken);
    public Task<UserDetailsResponse> StatusAsync(Guid id, SetUserStatusRequest request, CancellationToken cancellationToken) => session.Api.SetUserStatusAsync(id, request, cancellationToken);
    public Task<UserDetailsResponse> UnlockAsync(Guid id, UnlockUserRequest request, CancellationToken cancellationToken) => session.Api.UnlockUserAsync(id, request, cancellationToken);
    public Task<UserDetailsResponse> PasswordAsync(Guid id, ResetUserPasswordRequest request, CancellationToken cancellationToken) => session.Api.ResetUserPasswordAsync(id, request, cancellationToken);
    public Task<UserDetailsResponse> RolesAsync(Guid id, SetUserRolesRequest request, CancellationToken cancellationToken) => session.Api.SetUserRolesAsync(id, request, cancellationToken);
    public Task<UserDetailsResponse> ScopesAsync(Guid id, SetUserScopesRequest request, CancellationToken cancellationToken) => session.Api.SetUserScopesAsync(id, request, cancellationToken);
    public Task<RevokeUserSessionsResponse> RevokeAsync(Guid id, RevokeUserSessionsRequest request, CancellationToken cancellationToken) => session.Api.RevokeUserSessionsAsync(id, request, cancellationToken);
    public Task<RoleListResponse> RolesAsync(CancellationToken cancellationToken) => session.Api.ListRolesAsync(cancellationToken);
    public Task<RoleDetailsResponse> RoleAsync(Guid id, CancellationToken cancellationToken) => session.Api.GetRoleAsync(id, cancellationToken);
    public Task<RoleDetailsResponse> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken) => session.Api.CreateRoleAsync(request, cancellationToken);
    public Task<RoleDetailsResponse> UpdateRoleAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken) => session.Api.UpdateRoleAsync(id, request, cancellationToken);
    public Task<PermissionListResponse> PermissionsAsync(CancellationToken cancellationToken) => session.Api.ListPermissionsAsync(cancellationToken);
    public Task<OrganizationListResponse> OrganizationAsync(CancellationToken cancellationToken) => session.Api.ListOrganizationAsync(cancellationToken);
    public Task<SecurityAuditPageResponse> AuditAsync(Guid? userId, SecurityAuditEventType? eventType, DateTimeOffset? from, DateTimeOffset? until, int page, CancellationToken cancellationToken) => session.Api.ListSecurityAuditEventsAsync(userId, eventType, from, until, page, 50, cancellationToken);
}
