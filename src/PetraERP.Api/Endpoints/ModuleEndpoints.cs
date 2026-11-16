using System.ComponentModel.DataAnnotations;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Modules.Abstractions;
using PetraERP.Modules.Host;

namespace PetraERP.Api.Endpoints;

public sealed record ModuleSummaryResponse(
    string Id, string DisplayName, string Version, string Publisher, string State, bool IsRunning, string? LastError, IReadOnlyList<string> Permissions);

public sealed record ModuleCommandRequest
{
    /// <summary>Command arguments as text; the module parses and validates them.</summary>
    [MaxLength(64)]
    public Dictionary<string, string>? Arguments { get; init; }
}

public sealed record ModuleCommandResponse(bool Success, string Message, Dictionary<string, string> Data);

/// <summary>
/// Operating external modules. Installing a package is a server-side CLI action (it needs access to the host's files and
/// its trusted-publisher list), so over HTTP an administrator can only list, enable and disable what is already installed,
/// and users can run the commands a module declares - never more than their own permissions and the module's approved ones.
/// </summary>
internal static class ModuleEndpoints
{
    internal static void MapModuleEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/modules").WithTags("Modules");
        var manage = PetraPolicies.Permission(PermissionCatalog.DatabaseManage);

        admin.MapGet("", ListAsync).WithName("ListModules").WithSummary("Lists installed modules and whether each is running.")
            .RequireAuthorization(manage).Produces<IReadOnlyList<ModuleSummaryResponse>>().ProducesStandardProblems();
        admin.MapPost("/{moduleId}/enable", EnableAsync).WithName("EnableModule").WithSummary("Enables an installed module and starts it.")
            .RequireAuthorization(manage).Produces<ModuleSummaryResponse>().ProducesStandardProblems();
        admin.MapPost("/{moduleId}/disable", DisableAsync).WithName("DisableModule").WithSummary("Stops a module without deleting its data.")
            .RequireAuthorization(manage).Produces<ModuleSummaryResponse>().ProducesStandardProblems();

        api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/modules/{moduleId}").WithTags("Modules")
            .WithMetadata(FinancialWorkspaceMetadata.Instance).RequireAuthorization(PetraPolicies.CompanyScope)
            .MapPost("/commands/{commandKey}", ExecuteAsync).WithName("ExecuteModuleCommand")
            .WithSummary("Runs a command a module declares, with the caller's own permissions limited to those the module was approved for.")
            .Validate<ModuleCommandRequest>().Produces<ModuleCommandResponse>().ProducesStandardProblems();
    }

    private static async Task<IResult> ListAsync(ModuleManager manager, ModuleRuntime runtime, CancellationToken token)
    {
        var records = await manager.ListAsync(token).ConfigureAwait(false);
        return TypedResults.Ok(records.Select(r => Summarize(r, runtime)).ToList());
    }

    private static Task<IResult> EnableAsync(string moduleId, ModuleManager manager, ModuleRuntime runtime, CancellationToken token) =>
        ChangeAsync(moduleId, enable: true, manager, runtime, token);

    private static Task<IResult> DisableAsync(string moduleId, ModuleManager manager, ModuleRuntime runtime, CancellationToken token) =>
        ChangeAsync(moduleId, enable: false, manager, runtime, token);

    private static async Task<IResult> ChangeAsync(string moduleId, bool enable, ModuleManager manager, ModuleRuntime runtime, CancellationToken token)
    {
        ModuleOperationResult result;
        try
        {
            result = enable ? await manager.EnableAsync(moduleId, token).ConfigureAwait(false) : await manager.DisableAsync(moduleId, token).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            // Unknown module or an illegal lifecycle jump: the module is not in a state that allows this action.
            throw new ResourceConflictException("module.state_conflict", exception.Message);
        }

        var record = result.Record!;
        if (enable)
        {
            var activation = await runtime.ActivateAsync(record, token).ConfigureAwait(false);
            if (!activation.Success) record = (await manager.ListAsync(token).ConfigureAwait(false)).First(r => r.Id == record.Id);
        }
        else
        {
            await runtime.DeactivateAsync(moduleId, token).ConfigureAwait(false);
        }

        return TypedResults.Ok(Summarize(record, runtime));
    }

    private static async Task<IResult> ExecuteAsync(Guid companyId, Guid branchId, string moduleId, string commandKey, ModuleCommandRequest request,
        ModuleManager manager, ModuleRuntime runtime, IAccessEvaluator access, FinancialRequestScope scope, HttpContext http, CancellationToken token)
    {
        var record = (await manager.ListAsync(token).ConfigureAwait(false)).FirstOrDefault(r => string.Equals(r.Id, moduleId, StringComparison.Ordinal));
        if (record is null || !runtime.IsActive(moduleId))
            throw new ResourceNotFoundException("running module");

        // The caller's real permissions in this company and branch, restricted to what the module declares; the runtime then
        // narrows them again to what the operator approved.
        var userId = http.RequireSession().UserId;
        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in record.DeclaredPermissions)
        {
            if (PermissionCatalog.TryGet(code, out var definition)
                && await access.EvaluatePermissionAsync(userId, definition, companyId, branchId, token).ConfigureAwait(false) == AccessDecision.Granted)
                granted.Add(code);
        }

        var context = new ModuleRequestContext(companyId, scope.FiscalYearId, branchId, userId, "ar", granted);
        var result = await runtime.ExecuteAsync(moduleId, context, commandKey, request.Arguments ?? [], token).ConfigureAwait(false);
        return TypedResults.Ok(new ModuleCommandResponse(result.Success, result.Message, new Dictionary<string, string>(result.Data)));
    }

    private static ModuleSummaryResponse Summarize(ModuleRecord record, ModuleRuntime runtime) => new(
        record.Id, record.DisplayName, record.Version, record.Publisher, record.State.ToString(), runtime.IsActive(record.Id),
        record.LastError, record.DeclaredPermissions);
}
