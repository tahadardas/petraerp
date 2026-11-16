using Microsoft.Extensions.Diagnostics.HealthChecks;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Api.Hosting;

/// <summary>Readiness: the API can open a connection to its configured customer database.</summary>
internal sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly PetraDbContext context;

    public DatabaseHealthCheck(PetraDbContext context) => this.context = context;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var reachable = await this.context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        return reachable ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("The database is not reachable.");
    }
}
