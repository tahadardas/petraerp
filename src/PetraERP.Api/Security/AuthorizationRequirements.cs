using Microsoft.AspNetCore.Authorization;
using PetraERP.Api.Errors;
using PetraERP.Application.Security;

namespace PetraERP.Api.Security;

/// <summary>Requires a granular permission, evaluated with the route's company/branch as the target scope.</summary>
internal sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(PermissionDefinition permission) => Permission = permission;

    public PermissionDefinition Permission { get; }
}

/// <summary>Requires an explicit company-scope grant for the route's <c>companyId</c>.</summary>
internal sealed class CompanyScopeRequirement : IAuthorizationRequirement
{
}

/// <summary>Blocks every operation except credential maintenance while an administrator-assigned password is pending.</summary>
internal sealed class ActiveCredentialRequirement : IAuthorizationRequirement
{
}

internal sealed class ActiveCredentialHandler : AuthorizationHandler<ActiveCredentialRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveCredentialRequirement requirement)
    {
        if (context.User.HasClaim(static claim => claim.Type == PetraClaims.PasswordChangeRequired))
        {
            context.Fail(new AuthorizationFailureReason(this, ApiErrorCodes.PasswordChangeRequired));
        }
        else if (context.User.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

internal sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IAccessEvaluator evaluator;
    private readonly ISecurityAuditRecorder recorder;

    public PermissionHandler(IAccessEvaluator evaluator, ISecurityAuditRecorder recorder)
    {
        this.evaluator = evaluator;
        this.recorder = recorder;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.Resource is not HttpContext httpContext || !httpContext.TryGetValidatedSession(out var session))
        {
            context.Fail(new AuthorizationFailureReason(this, ApiErrorCodes.Forbidden));
            return;
        }

        var companyId = httpContext.RouteGuid("companyId");
        var branchId = httpContext.RouteGuid("branchId");
        var decision = await evaluator.EvaluatePermissionAsync(session.UserId, requirement.Permission, companyId, branchId,
            httpContext.RequestAborted).ConfigureAwait(false);
        if (decision == AccessDecision.Granted)
        {
            context.Succeed(requirement);
            return;
        }

        await recorder.RecordAuthorizationDeniedAsync(session.UserId, session.SessionId, requirement.Permission.Code, companyId,
            branchId, decision, httpContext.ToClientContext(), httpContext.RequestAborted).ConfigureAwait(false);
        context.Fail(new AuthorizationFailureReason(this, AuthorizationCodes.ForDecision(decision)));
    }
}

internal sealed class CompanyScopeHandler : AuthorizationHandler<CompanyScopeRequirement>
{
    private readonly IAccessEvaluator evaluator;
    private readonly ISecurityAuditRecorder recorder;

    public CompanyScopeHandler(IAccessEvaluator evaluator, ISecurityAuditRecorder recorder)
    {
        this.evaluator = evaluator;
        this.recorder = recorder;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CompanyScopeRequirement requirement)
    {
        if (context.Resource is not HttpContext httpContext || !httpContext.TryGetValidatedSession(out var session)
            || httpContext.RouteGuid("companyId") is not { } companyId)
        {
            context.Fail(new AuthorizationFailureReason(this, ApiErrorCodes.CompanyScopeDenied));
            return;
        }

        var decision = await evaluator.EvaluateCompanyScopeAsync(session.UserId, companyId, httpContext.RequestAborted)
            .ConfigureAwait(false);
        if (decision == AccessDecision.Granted)
        {
            context.Succeed(requirement);
            return;
        }

        await recorder.RecordAuthorizationDeniedAsync(session.UserId, session.SessionId, "scope:company", companyId, null,
            decision, httpContext.ToClientContext(), httpContext.RequestAborted).ConfigureAwait(false);
        context.Fail(new AuthorizationFailureReason(this, AuthorizationCodes.ForDecision(decision)));
    }
}

internal static class AuthorizationCodes
{
    internal static string ForDecision(AccessDecision decision) => decision switch
    {
        AccessDecision.PermissionDenied => ApiErrorCodes.PermissionDenied,
        AccessDecision.CompanyScopeDenied => ApiErrorCodes.CompanyScopeDenied,
        AccessDecision.BranchScopeDenied => ApiErrorCodes.BranchScopeDenied,
        AccessDecision.UserInactive => ApiErrorCodes.UserInactive,
        _ => ApiErrorCodes.Forbidden,
    };
}
