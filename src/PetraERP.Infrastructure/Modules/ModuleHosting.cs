using Microsoft.Extensions.DependencyInjection;
using PetraERP.Modules.Host;

namespace PetraERP.Infrastructure.Modules;

/// <summary>Where modules live and whom the installation trusts. Bound from the "Modules" configuration section.</summary>
public sealed class ModuleHostOptions
{
    public const string SectionName = "Modules";

    /// <summary>Root folder of installed modules. Empty means "modules" next to the API content root.</summary>
    public string Root { get; set; } = string.Empty;

    /// <summary>Publishers whose packages may be installed. Empty means nothing can be installed.</summary>
    public string[] TrustedPublishers { get; set; } = [];

    /// <summary>
    /// A package must carry a valid ECDSA P-256 signature from a publisher whose public key is listed in
    /// <see cref="PublisherKeys"/> (Base64 SubjectPublicKeyInfo). A publisher NAME alone never grants trust. Turning this off is
    /// for first-party development only.
    /// </summary>
    public bool RequireSignedPackages { get; set; } = true;

    /// <summary>Publisher id to public key.</summary>
    public Dictionary<string, string> PublisherKeys { get; set; } = [];

    /// <summary>Publishers whose trust was withdrawn; their installed modules are refused at start-up and upgrade.</summary>
    public string[] RevokedPublishers { get; set; } = [];

    /// <summary>Start every Enabled module when the server starts.</summary>
    public bool AutoStart { get; set; } = true;
}

/// <summary>
/// The runtime lives as long as the server, but the module registry is a database table owned by a request scope.
/// This store opens a fresh scope for every call so the runtime never holds on to a database context.
/// </summary>
public sealed class ScopedModuleStateStore(IServiceScopeFactory scopes) : IModuleStateStore
{
    public Task<IReadOnlyList<ModuleRecord>> ListAsync(CancellationToken cancellationToken) => InScopeAsync(s => s.ListAsync(cancellationToken));

    public Task<ModuleRecord?> FindAsync(string moduleId, CancellationToken cancellationToken) => InScopeAsync(s => s.FindAsync(moduleId, cancellationToken));

    public Task SaveAsync(ModuleRecord record, CancellationToken cancellationToken) => InScopeAsync(async s =>
    {
        await s.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return true;
    });

    public Task DeleteAsync(string moduleId, CancellationToken cancellationToken) => InScopeAsync(async s =>
    {
        await s.DeleteAsync(moduleId, cancellationToken).ConfigureAwait(false);
        return true;
    });

    private async Task<T> InScopeAsync<T>(Func<ModuleRegistryStore, Task<T>> action)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            return await action(scope.ServiceProvider.GetRequiredService<ModuleRegistryStore>()).ConfigureAwait(false);
        }
    }
}

/// <summary>The runtime's log sink (module start, failure and shutdown messages).</summary>
public sealed partial class ModuleRuntimeLog(Microsoft.Extensions.Logging.ILogger<ModuleRuntimeLog> logger)
{
    public void Write(string message, Exception? exception) => Entry(logger, message, exception);

    [Microsoft.Extensions.Logging.LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "{Message}")]
    private static partial void Entry(Microsoft.Extensions.Logging.ILogger logger, string message, Exception? exception);
}
