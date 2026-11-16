using Microsoft.Extensions.Options;
using PetraERP.Infrastructure.Modules;
using PetraERP.Modules.Host;

namespace PetraERP.Api.Hosting;

/// <summary>
/// Starts the modules an operator has enabled, in the background so a slow or unavailable database never delays or breaks
/// server start-up. A module that fails is recorded as Failed by the runtime; the others keep running.
/// </summary>
internal sealed class ModuleStartupService(ModuleRuntime runtime, IOptions<ModuleHostOptions> options, ModuleRuntimeLog log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.AutoStart) return;
        try
        {
            foreach (var result in await runtime.StartEnabledAsync(stoppingToken).ConfigureAwait(false))
                log.Write($"module {result.ModuleId}: {(result.Success ? "started" : "not started")} - {result.Message}", null);
        }
        catch (OperationCanceledException)
        {
            // Shutting down before start-up finished is not a failure.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            log.Write("module start-up could not read the module registry", exception);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        foreach (var moduleId in runtime.ActiveModules)
            await runtime.DeactivateAsync(moduleId, cancellationToken).ConfigureAwait(false);
    }
}
