using PetraERP.ApiClient;

namespace PetraERP.Desktop.Administration;

public interface IAdministrationClient
{
    Task<UserPageResponse> UsersAsync(string? search, int page, CancellationToken cancellationToken);
    Task<UserDetailsResponse> UserAsync(Guid id, CancellationToken cancellationToken);
    Task<UserDetailsResponse> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task<UserDetailsResponse> ProfileAsync(Guid id, UpdateUserProfileRequest request, CancellationToken cancellationToken);
    Task<UserDetailsResponse> StatusAsync(Guid id, SetUserStatusRequest request, CancellationToken cancellationToken);
    Task<UserDetailsResponse> UnlockAsync(Guid id, UnlockUserRequest request, CancellationToken cancellationToken);
    Task<UserDetailsResponse> PasswordAsync(Guid id, ResetUserPasswordRequest request, CancellationToken cancellationToken);
    Task<UserDetailsResponse> RolesAsync(Guid id, SetUserRolesRequest request, CancellationToken cancellationToken);
    Task<UserDetailsResponse> ScopesAsync(Guid id, SetUserScopesRequest request, CancellationToken cancellationToken);
    Task<RevokeUserSessionsResponse> RevokeAsync(Guid id, RevokeUserSessionsRequest request, CancellationToken cancellationToken);
    Task<RoleListResponse> RolesAsync(CancellationToken cancellationToken);
    Task<RoleDetailsResponse> RoleAsync(Guid id, CancellationToken cancellationToken);
    Task<RoleDetailsResponse> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken);
    Task<RoleDetailsResponse> UpdateRoleAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken);
    Task<PermissionListResponse> PermissionsAsync(CancellationToken cancellationToken);
    Task<OrganizationListResponse> OrganizationAsync(CancellationToken cancellationToken);
    Task<SecurityAuditPageResponse> AuditAsync(Guid? userId, SecurityAuditEventType? eventType, DateTimeOffset? from, DateTimeOffset? until, int page, CancellationToken cancellationToken);
}
