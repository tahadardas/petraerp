using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using PetraERP.Api.Security;
using PetraERP.Application.Security;
using PetraERP.Contracts.Platform;

namespace PetraERP.Api.Endpoints;

internal static class PlatformEndpoints
{
    internal const string ApiVersion = "v1";

    internal static string ProductVersion { get; } =
        typeof(PlatformEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? "0.0.0";

    internal static void MapPlatformEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/system/status", GetServiceStatus)
            .WithTags("Platform")
            .WithName("GetServiceStatus")
            .WithSummary("Reports that the API process is running, with its contract and product versions.")
            .AllowAnonymous()
            .Produces<ServiceStatusResponse>();
        api.MapGet("/system/write-gate", () => TypedResults.Ok(FinancialWriteGate.Shared.Snapshot()))
            .WithTags("Platform")
            .WithName("GetWriteGateStatus")
            .WithSummary("Admission-control state of financial writes: slots, queue length, wait percentiles and timeouts since the process started.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.DatabaseManage))
            .Produces<WriteGateSnapshot>();
    }

    private static Ok<ServiceStatusResponse> GetServiceStatus(TimeProvider time) => TypedResults.Ok(new ServiceStatusResponse
    {
        Status = "ok",
        ApiVersion = ApiVersion,
        ProductVersion = ProductVersion,
        ServerTimeUtc = time.GetUtcNow(),
    });
}
