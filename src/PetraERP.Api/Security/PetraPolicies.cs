using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using PetraERP.Api.Errors;
using PetraERP.Application.Security;

namespace PetraERP.Api.Security;

/// <summary>Named authorization policies. Every authenticated policy also requires a non-pending credential.</summary>
internal static class PetraPolicies
{
    /// <summary>Authenticated only; allowed while a password change is pending (me, change-password, logout).</summary>
    internal const string CredentialMaintenance = "petra:credential-maintenance";

    internal const string CompanyScope = "petra:scope:company";

    internal static string Permission(string code) => "petra:permission:" + code;

    internal static void Register(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.InvokeHandlersAfterFailure = false;
        var authenticated = new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
            .AddRequirements(new ActiveCredentialRequirement()).Build();
        options.DefaultPolicy = authenticated;
        options.FallbackPolicy = authenticated;
        options.AddPolicy(CredentialMaintenance, static policy => policy.RequireAuthenticatedUser());
        options.AddPolicy(CompanyScope, static policy => policy.RequireAuthenticatedUser()
            .AddRequirements(new ActiveCredentialRequirement(), new CompanyScopeRequirement()));
        foreach (var permission in PermissionCatalog.All)
        {
            options.AddPolicy(Permission(permission.Code), policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new ActiveCredentialRequirement(), new PermissionRequirement(permission)));
        }
    }
}

/// <summary>Writes 403 responses as ProblemDetails carrying the precise denial code.</summary>
internal sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true)
        {
            var code = authorizeResult.AuthorizationFailure?.FailureReasons
                .Select(static reason => reason.Message)
                .FirstOrDefault(static message => message.StartsWith("auth.", StringComparison.Ordinal))
                ?? ApiErrorCodes.Forbidden;
            await ApiProblems.WriteAsync(context, StatusCodes.Status403Forbidden, code).ConfigureAwait(false);
            return;
        }

        await fallback.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
    }
}
