using PetraERP.Api.Errors;
using PetraERP.Api.Hosting;
using PetraERP.Api.Security;
using PetraERP.Application.Security;
using PetraERP.Contracts.Authentication;
using AppClientKind = PetraERP.Application.Security.ClientKind;
using ContractClientKind = PetraERP.Contracts.Authentication.ClientKind;

namespace PetraERP.Api.Endpoints;

internal static class AuthenticationEndpoints
{
    internal static void MapAuthenticationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Authentication");

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithSummary("Signs in with a login name and password and opens a session.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .Validate<LoginRequest>()
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status423Locked)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/refresh", RefreshAsync)
            .WithName("RefreshSession")
            .WithSummary("Rotates the refresh token and issues a new access token. A reused refresh token ends the session.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .Validate<RefreshSessionRequest>()
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("Revokes the current session.")
            .RequireAuthorization(PetraPolicies.CredentialMaintenance)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout-all", LogoutAllAsync)
            .WithName("LogoutAllSessions")
            .WithSummary("Revokes every session of the current user, on all devices.")
            .RequireAuthorization(PetraPolicies.CredentialMaintenance)
            .Produces<LogoutAllResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/change-password", ChangePasswordAsync)
            .WithName("ChangePassword")
            .WithSummary("Changes the current user's password, revokes all sessions and opens a new session.")
            .RequireAuthorization(PetraPolicies.CredentialMaintenance)
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .Validate<ChangePasswordRequest>()
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status423Locked)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, IAuthenticationService authentication,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        var command = new LoginCommand(request.UserName, request.Password, ToClientKind(request.ClientKind), request.DeviceName);
        var outcome = await authentication.LoginAsync(command, httpContext.ToClientContext(), cancellationToken).ConfigureAwait(false);
        return ToResult(httpContext, outcome);
    }

    private static async Task<IResult> RefreshAsync(RefreshSessionRequest request, IAuthenticationService authentication,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        var outcome = await authentication.RefreshAsync(request.RefreshToken, httpContext.ToClientContext(), cancellationToken)
            .ConfigureAwait(false);
        return ToResult(httpContext, outcome);
    }

    private static async Task<IResult> LogoutAsync(IAuthenticationService authentication, HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = httpContext.RequireSession();
        await authentication.LogoutAsync(session.UserId, session.SessionId, httpContext.ToClientContext(), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> LogoutAllAsync(IAuthenticationService authentication, HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = httpContext.RequireSession();
        var revoked = await authentication.LogoutAllAsync(session.UserId, session.SessionId, httpContext.ToClientContext(),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new LogoutAllResponse { RevokedSessions = revoked });
    }

    private static async Task<IResult> ChangePasswordAsync(ChangePasswordRequest request, IAuthenticationService authentication,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        var session = httpContext.RequireSession();
        var command = new ChangePasswordCommand(session.UserId, session.SessionId, request.CurrentPassword, request.NewPassword);
        var outcome = await authentication.ChangePasswordAsync(command, ToClientKind(request.ClientKind),
            httpContext.ToClientContext(), cancellationToken).ConfigureAwait(false);
        return ToResult(httpContext, outcome);
    }

    private static AppClientKind ToClientKind(ContractClientKind kind) =>
        Enum.IsDefined(kind) ? (AppClientKind)(int)kind : AppClientKind.Desktop;

    private static IResult ToResult(HttpContext httpContext, AuthenticationOutcome outcome)
    {
        if (outcome.Tokens is { } tokens)
        {
            return TypedResults.Ok(ToResponse(tokens));
        }

        switch (outcome.Failure)
        {
            case AuthenticationFailure.LockedOut:
                var seconds = outcome.LockoutEndUtc is { } end
                    ? Math.Max(1, (int)Math.Ceiling((end - DateTimeOffset.UtcNow).TotalSeconds))
                    : 60;
                ApiProblems.SetRetryAfter(httpContext.Response, seconds);
                return ApiProblems.Result(httpContext, StatusCodes.Status423Locked, ApiErrorCodes.LockedOut);
            case AuthenticationFailure.AccountDisabled:
                return ApiProblems.Result(httpContext, StatusCodes.Status403Forbidden, ApiErrorCodes.AccountDisabled);
            case AuthenticationFailure.InvalidRefreshToken:
                return ApiProblems.Result(httpContext, StatusCodes.Status401Unauthorized, ApiErrorCodes.InvalidRefreshToken);
            case AuthenticationFailure.RefreshTokenReused:
                return ApiProblems.Result(httpContext, StatusCodes.Status401Unauthorized, ApiErrorCodes.RefreshTokenReused);
            case AuthenticationFailure.SessionExpired:
                return ApiProblems.Result(httpContext, StatusCodes.Status401Unauthorized, ApiErrorCodes.SessionExpired);
            case AuthenticationFailure.InvalidCurrentPassword:
                return ApiProblems.Result(httpContext, StatusCodes.Status400BadRequest, ApiErrorCodes.InvalidCurrentPassword, null,
                    new Dictionary<string, string[]> { ["currentPassword"] = [ApiErrorCodes.InvalidCurrentPassword] });
            case AuthenticationFailure.PasswordPolicyViolation:
                var code = PasswordPolicy.ErrorCode(outcome.PolicyViolation ?? PasswordPolicyViolation.Required);
                return ApiProblems.Result(httpContext, StatusCodes.Status400BadRequest, code, null,
                    new Dictionary<string, string[]> { ["newPassword"] = [code] });
            default:
                return ApiProblems.Result(httpContext, StatusCodes.Status401Unauthorized, ApiErrorCodes.InvalidCredentials);
        }
    }

    private static TokenResponse ToResponse(SessionTokens tokens) => new()
    {
        TokenType = "Bearer",
        AccessToken = tokens.AccessToken.Value,
        AccessTokenExpiresAtUtc = tokens.AccessToken.ExpiresAtUtc,
        RefreshToken = tokens.RefreshToken,
        RefreshTokenExpiresAtUtc = tokens.RefreshTokenExpiresAtUtc,
        SessionId = tokens.SessionId,
        SessionExpiresAtUtc = tokens.SessionExpiresAtUtc,
        MustChangePassword = tokens.MustChangePassword,
    };
}
