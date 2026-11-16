using PetraERP.ApiClient;
using PetraERP.Desktop.Administration;

namespace PetraERP.Desktop.Tests;

internal sealed class AdministrationFixture : IAdministrationClient
{
    public int Writes { get; private set; }
    public SetUserScopesRequest? SavedScopes { get; private set; }
    public UpdateUserProfileRequest? SavedProfile { get; private set; }
    public Exception? WriteFailure { get; set; }
    public int RequestedPage { get; private set; }
    public string? RequestedSearch { get; private set; }
    public UserDetailsResponse User { get; } = new() { Id = Guid.NewGuid(), UserName = "operator", DisplayNameArabic = "موظف", Version = "AQIDBAUGBwg=", RoleIds = [], Scopes = [], IsActive = true };
    public OrganizationCompanyResponse Company { get; } = new() { Id = Guid.NewGuid(), Code = "A", NameArabic = "شركة", IsActive = true, Branches = [new() { Id = Guid.NewGuid(), Code = "MAIN", NameArabic = "رئيسي", IsActive = true }] };
    public Task<UserPageResponse> UsersAsync(string? search, int page, CancellationToken cancellationToken) { RequestedSearch = search; RequestedPage = page; return Task.FromResult(new UserPageResponse { Page = page, PageSize = 50, TotalCount = 101, Items = [new() { Id = User.Id, UserName = User.UserName, DisplayNameArabic = User.DisplayNameArabic }] }); }
    public Task<UserDetailsResponse> UserAsync(Guid id, CancellationToken cancellationToken) { return Task.FromResult(User); }
    public Task<UserDetailsResponse> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken) { throw new NotSupportedException(); }
    public Task<UserDetailsResponse> ProfileAsync(Guid id, UpdateUserProfileRequest request, CancellationToken cancellationToken) { Writes++; SavedProfile = request; return WriteFailure is null ? Task.FromResult(User) : Task.FromException<UserDetailsResponse>(WriteFailure); }
    public Task<UserDetailsResponse> StatusAsync(Guid id, SetUserStatusRequest request, CancellationToken cancellationToken) { throw new NotSupportedException(); }
    public Task<UserDetailsResponse> UnlockAsync(Guid id, UnlockUserRequest request, CancellationToken cancellationToken) { throw new NotSupportedException(); }
    public Task<UserDetailsResponse> PasswordAsync(Guid id, ResetUserPasswordRequest request, CancellationToken cancellationToken) { throw new NotSupportedException(); }
    public Task<UserDetailsResponse> RolesAsync(Guid id, SetUserRolesRequest request, CancellationToken cancellationToken) { throw new NotSupportedException(); }
    public Task<UserDetailsResponse> ScopesAsync(Guid id, SetUserScopesRequest request, CancellationToken cancellationToken) { Writes++; SavedScopes = request; return WriteFailure is null ? Task.FromResult(User) : Task.FromException<UserDetailsResponse>(WriteFailure); }
    public Task<RevokeUserSessionsResponse> RevokeAsync(Guid id, RevokeUserSessionsRequest request, CancellationToken cancellationToken) { throw new NotSupportedException(); }
    public Task<RoleListResponse> RolesAsync(CancellationToken cancellationToken) { return Task.FromResult(new RoleListResponse { Items = [new() { Id = Guid.Empty, Code = "READER", NameArabic = "قراءة النظام", NameEnglish = "System reader" }] }); }
    public Task<RoleDetailsResponse> RoleAsync(Guid id, CancellationToken cancellationToken) { return Task.FromResult(new RoleDetailsResponse { Id = id, Code = "READER", NameArabic = "قراءة النظام", NameEnglish = "System reader", PermissionCodes = ["System.Users.View"], Version = "AQIDBAUGBwg=" }); }
    public Task<RoleDetailsResponse> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken) { throw new NotSupportedException(); }
    public Task<RoleDetailsResponse> UpdateRoleAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken) { throw new NotSupportedException(); }
    public Task<PermissionListResponse> PermissionsAsync(CancellationToken cancellationToken) { return Task.FromResult(new PermissionListResponse { Items = [new() { Code = "System.Users.View", Scope = PermissionScope.Installation }, new() { Code = "System.Roles.View", Scope = PermissionScope.Installation }] }); }
    public Task<OrganizationListResponse> OrganizationAsync(CancellationToken cancellationToken) { return Task.FromResult(new OrganizationListResponse { Items = [Company] }); }
    public Task<SecurityAuditPageResponse> AuditAsync(Guid? userId, SecurityAuditEventType? eventType, DateTimeOffset? from, DateTimeOffset? until, int page, CancellationToken cancellationToken) { return Task.FromResult(new SecurityAuditPageResponse { Page = page, PageSize = 50, TotalCount = 1, Items = [new() { Id = Guid.NewGuid(), OccurredAtUtc = DateTimeOffset.UtcNow, EventType = SecurityAuditEventType.UserCreated, UserNameSnapshot = "operator", Reason = "إنشاء اختباري", Details = "{}" }] }); }
}
