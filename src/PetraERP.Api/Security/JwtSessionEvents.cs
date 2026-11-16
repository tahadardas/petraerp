using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using PetraERP.Api.Errors;
using PetraERP.Application.Security;

namespace PetraERP.Api.Security;

/// <summary>
/// JWT bearer hooks: after signature/issuer/audience/lifetime validation, the session must still be valid in
/// the database (not revoked, not expired, user active, security stamp unchanged). Challenges are written as
/// ProblemDetails with a precise error code.
/// </summary>
internal static class JwtSessionEvents
{
    private static readonly object FailureCodeKey = new();

    internal static JwtBearerEvents Create() => new()
    {
        OnTokenValidated = ValidateSessionAsync,
        OnChallenge = WriteChallengeAsync,
    };

    private static async Task ValidateSessionAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirst(PetraClaims.Subject)?.Value, out var userId)
            || !Guid.TryParse(principal?.FindFirst(PetraClaims.Session)?.Value, out var sessionId)
            || principal?.Identity is not ClaimsIdentity identity)
        {
            context.HttpContext.Items[FailureCodeKey] = ApiErrorCodes.TokenInvalid;
            context.Fail("The access token does not identify a session.");
            return;
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<ISessionValidator>();
        var session = await validator.ValidateAsync(userId, sessionId, context.HttpContext.RequestAborted).ConfigureAwait(false);
        if (session is null)
        {
            context.HttpContext.Items[FailureCodeKey] = ApiErrorCodes.SessionRevoked;
            context.Fail("The session is no longer valid.");
            return;
        }

        if (session.MustChangePassword)
        {
            identity.AddClaim(new Claim(PetraClaims.PasswordChangeRequired, "true"));
        }

        context.HttpContext.SetValidatedSession(session);
    }

    private static async Task WriteChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        var code = context.HttpContext.Items.TryGetValue(FailureCodeKey, out var value) && value is string recorded
            ? recorded
            : context.AuthenticateFailure switch
            {
                SecurityTokenExpiredException => ApiErrorCodes.TokenExpired,
                null => ApiErrorCodes.Unauthenticated,
                _ => ApiErrorCodes.TokenInvalid,
            };
        context.Response.Headers[HeaderNames.WWWAuthenticate] = code == ApiErrorCodes.Unauthenticated
            ? "Bearer"
            : "Bearer error=\"invalid_token\"";
        await ApiProblems.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, code).ConfigureAwait(false);
    }
}
