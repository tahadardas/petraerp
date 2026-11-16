using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.AlAmeenMigration;
using PetraERP.Application.Security;

namespace PetraERP.Api.Endpoints;

internal static class AlAmeenMigrationEndpoints
{
    internal static void MapAlAmeenMigrationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/migration/alameen")
            .WithTags("Al-Ameen Migration Engine")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.DatabaseManage));

        group.MapPost("/preview", PreviewMigrationAsync)
            .WithName("PreviewAlAmeenMigration")
            .WithSummary("Validates Al-Ameen source schema and previews data counts.")
            .Produces<MigrationPreviewResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/execute", ExecuteMigrationAsync)
            .WithName("ExecuteAlAmeenMigration")
            .WithSummary("Executes migration into an isolated target accounting database.")
            .Produces<MigrationExecuteResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{migrationRunId:guid}/reconciliation", ReconcileMigrationAsync)
            .WithName("ReconcileAlAmeenMigration")
            .WithSummary("Compares source and target accounting balances across 14 reconciliation checks.")
            .Produces<MigrationReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/activate", ActivateMigrationAsync)
            .WithName("ActivateAlAmeenMigration")
            .WithSummary("Finalizes and activates the migrated company-year workspace.")
            .Produces<MigrationActivationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> PreviewMigrationAsync(
        MigrationPreviewRequest request,
        IMigrationCoordinator coordinator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = httpContext.RequireSession();
        var result = await coordinator.PreviewAsync(request, session.UserId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> ExecuteMigrationAsync(
        MigrationExecuteRequest request,
        IMigrationCoordinator coordinator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = httpContext.RequireSession();
        var result = await coordinator.ExecuteAsync(request, session.UserId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> ReconcileMigrationAsync(
        Guid migrationRunId,
        IMigrationCoordinator coordinator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = httpContext.RequireSession();
        var result = await coordinator.ReconcileAsync(migrationRunId, session.UserId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> ActivateMigrationAsync(
        MigrationActivationRequest request,
        IMigrationCoordinator coordinator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = httpContext.RequireSession();
        var result = await coordinator.ActivateAsync(request, session.UserId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }
}
