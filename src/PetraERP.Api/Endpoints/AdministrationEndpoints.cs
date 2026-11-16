using Microsoft.AspNetCore.Http.HttpResults;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Contracts.Administration;

namespace PetraERP.Api.Endpoints;

internal static class AdministrationEndpoints
{
    internal static void MapAdministrationEndpoints(this RouteGroupBuilder api)
    {
        var users = api.MapGroup("/admin/users").WithTags("Administration: users");
        var view = PetraPolicies.Permission(PermissionCatalog.UsersView);
        var manage = PetraPolicies.Permission(PermissionCatalog.UsersManage);

        users.MapGet("", ListUsersAsync).WithName("ListUsers").RequireAuthorization(view)
            .WithSummary("Lists users with server-side search and paging.")
            .Produces<UserPageResponse>().ProducesStandardProblems();
        users.MapGet("/{userId:guid}", GetUserAsync).WithName("GetUser").RequireAuthorization(view)
            .Produces<UserDetailsResponse>().ProducesStandardProblems();
        users.MapPost("", CreateUserAsync).WithName("CreateUser").RequireAuthorization(manage).Validate<CreateUserRequest>()
            .WithSummary("Creates a user with a temporary password that must be changed at first sign-in.")
            .Produces<UserDetailsResponse>(StatusCodes.Status201Created).ProducesStandardProblems();
        users.MapPut("/{userId:guid}/profile", UpdateUserProfileAsync).WithName("UpdateUserProfile").RequireAuthorization(manage)
            .Validate<UpdateUserProfileRequest>().Produces<UserDetailsResponse>().ProducesStandardProblems();
        users.MapPut("/{userId:guid}/status", SetUserStatusAsync).WithName("SetUserStatus").RequireAuthorization(manage)
            .WithSummary("Enables or disables a user. Disabling revokes every session immediately.")
            .Validate<SetUserStatusRequest>().Produces<UserDetailsResponse>().ProducesStandardProblems();
        users.MapPost("/{userId:guid}/unlock", UnlockUserAsync).WithName("UnlockUser").RequireAuthorization(manage)
            .Validate<UnlockUserRequest>().Produces<UserDetailsResponse>().ProducesStandardProblems();
        users.MapPost("/{userId:guid}/reset-password", ResetUserPasswordAsync).WithName("ResetUserPassword")
            .RequireAuthorization(manage).Validate<ResetUserPasswordRequest>()
            .WithSummary("Sets a temporary password, requires a change at next sign-in and revokes every session.")
            .Produces<UserDetailsResponse>().ProducesStandardProblems();
        users.MapPut("/{userId:guid}/roles", SetUserRolesAsync).WithName("SetUserRoles").RequireAuthorization(manage)
            .Validate<SetUserRolesRequest>().Produces<UserDetailsResponse>().ProducesStandardProblems();
        users.MapPut("/{userId:guid}/scopes", SetUserScopesAsync).WithName("SetUserScopes").RequireAuthorization(manage)
            .WithSummary("Replaces the user's explicit company and branch scope grants.")
            .Validate<SetUserScopesRequest>().Produces<UserDetailsResponse>().ProducesStandardProblems();
        users.MapPost("/{userId:guid}/revoke-sessions", RevokeUserSessionsAsync).WithName("RevokeUserSessions")
            .RequireAuthorization(manage).Validate<RevokeUserSessionsRequest>()
            .Produces<RevokeUserSessionsResponse>().ProducesStandardProblems();

        var roles = api.MapGroup("/admin/roles").WithTags("Administration: roles");
        var rolesView = PetraPolicies.Permission(PermissionCatalog.RolesView);
        var rolesManage = PetraPolicies.Permission(PermissionCatalog.RolesManage);
        roles.MapGet("", ListRolesAsync).WithName("ListRoles").RequireAuthorization(rolesView)
            .Produces<RoleListResponse>().ProducesStandardProblems();
        roles.MapGet("/{roleId:guid}", GetRoleAsync).WithName("GetRole").RequireAuthorization(rolesView)
            .Produces<RoleDetailsResponse>().ProducesStandardProblems();
        roles.MapPost("", CreateRoleAsync).WithName("CreateRole").RequireAuthorization(rolesManage).Validate<CreateRoleRequest>()
            .Produces<RoleDetailsResponse>(StatusCodes.Status201Created).ProducesStandardProblems();
        roles.MapPut("/{roleId:guid}", UpdateRoleAsync).WithName("UpdateRole").RequireAuthorization(rolesManage)
            .Validate<UpdateRoleRequest>().Produces<RoleDetailsResponse>().ProducesStandardProblems();

        api.MapGet("/admin/permissions", ListPermissions).WithTags("Administration: roles").WithName("ListPermissions")
            .WithSummary("Lists every permission code enforced by the server and the scope it is evaluated against.")
            .RequireAuthorization(rolesView).Produces<PermissionListResponse>().ProducesStandardProblems();
        api.MapGet("/admin/organization", ListOrganizationAsync).WithTags("Administration: users").WithName("ListOrganization")
            .WithSummary("Lists all companies and branches for scope assignment.")
            .RequireAuthorization(view).Produces<OrganizationListResponse>().ProducesStandardProblems();
    }

    internal static RouteHandlerBuilder ProducesStandardProblems(this RouteHandlerBuilder builder) =>
        builder.ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static AdministrativeActor Actor(HttpContext httpContext) =>
        new(httpContext.RequireSession().UserId, httpContext.ToClientContext());

    private static async Task<IResult> ListUsersAsync(string? search, int? page, int? pageSize,
        ISecurityAdministrationService administration, CancellationToken cancellationToken)
    {
        var result = await administration.ListUsersAsync(new UserListQuery(search, PageRequest.Create(page, pageSize)),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }

    private static async Task<IResult> GetUserAsync(Guid userId, ISecurityAdministrationService administration,
        CancellationToken cancellationToken) =>
        TypedResults.Ok((await administration.GetUserAsync(userId, cancellationToken).ConfigureAwait(false)).ToResponse());

    private static async Task<IResult> CreateUserAsync(CreateUserRequest request, ISecurityAdministrationService administration,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        var user = await administration.CreateUserAsync(
            new CreateUserCommand(request.UserName, request.DisplayNameArabic, request.DisplayNameEnglish, request.InitialPassword),
            Actor(httpContext), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created(new Uri("/api/v1/admin/users/" + user.Id.ToString("D"), UriKind.Relative), user.ToResponse());
    }

    private static async Task<IResult> UpdateUserProfileAsync(Guid userId, UpdateUserProfileRequest request,
        ISecurityAdministrationService administration, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var command = new UpdateUserProfileCommand(request.DisplayNameArabic, request.DisplayNameEnglish,
            ApiRequestGuards.RequireVersion(request.Version));
        var user = await administration.UpdateUserProfileAsync(userId, command, Actor(httpContext), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(user.ToResponse());
    }

    private static async Task<IResult> SetUserStatusAsync(Guid userId, SetUserStatusRequest request,
        ISecurityAdministrationService administration, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var user = await administration.SetUserActiveAsync(userId, request.IsActive, request.Reason,
            ApiRequestGuards.RequireVersion(request.Version), Actor(httpContext), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(user.ToResponse());
    }

    private static async Task<IResult> UnlockUserAsync(Guid userId, UnlockUserRequest request,
        ISecurityAdministrationService administration, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var user = await administration.UnlockUserAsync(userId, ApiRequestGuards.RequireVersion(request.Version), Actor(httpContext),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(user.ToResponse());
    }

    private static async Task<IResult> ResetUserPasswordAsync(Guid userId, ResetUserPasswordRequest request,
        ISecurityAdministrationService administration, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var command = new ResetPasswordCommand(request.TemporaryPassword, request.Reason, ApiRequestGuards.RequireVersion(request.Version));
        var user = await administration.ResetPasswordAsync(userId, command, Actor(httpContext), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(user.ToResponse());
    }

    private static async Task<IResult> SetUserRolesAsync(Guid userId, SetUserRolesRequest request,
        ISecurityAdministrationService administration, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var user = await administration.SetUserRolesAsync(userId, request.RoleIds.ToArray(),
            ApiRequestGuards.RequireVersion(request.Version), Actor(httpContext), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(user.ToResponse());
    }

    private static async Task<IResult> SetUserScopesAsync(Guid userId, SetUserScopesRequest request,
        ISecurityAdministrationService administration, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var scopes = request.Scopes.Select(static scope => new CompanyScopeGrant(scope.CompanyId, scope.BranchIds ?? [])).ToArray();
        var user = await administration.SetUserScopesAsync(userId, scopes, ApiRequestGuards.RequireVersion(request.Version),
            Actor(httpContext), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(user.ToResponse());
    }

    private static async Task<IResult> RevokeUserSessionsAsync(Guid userId, RevokeUserSessionsRequest request,
        ISecurityAdministrationService administration, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var revoked = await administration.RevokeUserSessionsAsync(userId, request.Reason, Actor(httpContext), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(new RevokeUserSessionsResponse { RevokedSessions = revoked });
    }

    private static async Task<IResult> ListRolesAsync(ISecurityAdministrationService administration,
        CancellationToken cancellationToken) =>
        TypedResults.Ok((await administration.ListRolesAsync(cancellationToken).ConfigureAwait(false)).ToResponse());

    private static async Task<IResult> GetRoleAsync(Guid roleId, ISecurityAdministrationService administration,
        CancellationToken cancellationToken) =>
        TypedResults.Ok((await administration.GetRoleAsync(roleId, cancellationToken).ConfigureAwait(false)).ToResponse());

    private static async Task<IResult> CreateRoleAsync(CreateRoleRequest request, ISecurityAdministrationService administration,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        var role = await administration.CreateRoleAsync(
            new CreateRoleCommand(request.Code, request.NameArabic, request.NameEnglish, request.PermissionCodes.ToArray()),
            Actor(httpContext), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created(new Uri("/api/v1/admin/roles/" + role.Id.ToString("D"), UriKind.Relative), role.ToResponse());
    }

    private static async Task<IResult> UpdateRoleAsync(Guid roleId, UpdateRoleRequest request,
        ISecurityAdministrationService administration, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var role = await administration.UpdateRoleAsync(roleId,
            new UpdateRoleCommand(request.NameArabic, request.NameEnglish, request.PermissionCodes.ToArray(),
                ApiRequestGuards.RequireVersion(request.Version)),
            Actor(httpContext), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(role.ToResponse());
    }

    private static Ok<PermissionListResponse> ListPermissions() => TypedResults.Ok(AdministrationMapping.PermissionCatalogResponse());

    private static async Task<IResult> ListOrganizationAsync(ISecurityAdministrationService administration,
        CancellationToken cancellationToken) =>
        TypedResults.Ok((await administration.ListOrganizationAsync(cancellationToken).ConfigureAwait(false)).ToResponse());
}
