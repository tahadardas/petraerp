using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using PetraERP.Modules.Abstractions;
using PetraERP.Modules.Packaging;

namespace PetraERP.Modules.Host;

/// <summary>Turns an installed module's directory into a live <see cref="IModule"/> instance.</summary>
public interface IModuleLoader
{
    IModule Load(ModuleRecord record);
}

/// <summary>
/// Loads the entry assembly named in the installed manifest. Before any code runs, every file is re-hashed against the
/// manifest, because the directory is writable after installation and a load is already executable code. The module
/// shares the host's copy of the abstractions, so it can only talk to the core through the contracts.
/// </summary>
public sealed class AssemblyModuleLoader : IModuleLoader
{
    private static readonly JsonSerializerOptions ManifestOptions = new(JsonSerializerDefaults.Web);

    public IModule Load(ModuleRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var directory = Path.GetFullPath(record.DataDirectory);
        var manifestPath = Path.Combine(directory, ModulePackageVerifier.ManifestName);
        if (!File.Exists(manifestPath)) throw new InvalidOperationException("The installed module has no manifest.");
        var manifest = JsonSerializer.Deserialize<ModuleManifest>(File.ReadAllBytes(manifestPath), ManifestOptions)
            ?? throw new InvalidOperationException("The installed module manifest is unreadable.");
        if (!string.Equals(manifest.Id, record.Id, StringComparison.Ordinal) || !string.Equals(manifest.Version, record.Version, StringComparison.Ordinal))
            throw new InvalidOperationException("The installed manifest does not match the registered module.");

        foreach (var file in manifest.Files)
        {
            var path = Path.GetFullPath(Path.Combine(directory, file.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsafe module file path: {file.Path}");
            if (!File.Exists(path)) throw new InvalidOperationException($"Module file is missing: {file.Path}");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Module file was changed after installation: {file.Path}");
        }

        var entry = Path.GetFullPath(Path.Combine(directory, manifest.EntryAssembly));
        if (!entry.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(entry))
            throw new InvalidOperationException("The module entry assembly is missing.");

        var context = new ModuleLoadContext(record.Id, directory);
        var assembly = context.LoadFromAssemblyPath(entry);
        var types = assembly.GetTypes().Where(t => typeof(IModule).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false }).ToArray();
        if (types.Length != 1)
            throw new InvalidOperationException($"The entry assembly must expose exactly one IModule, found {types.Length}.");
        return (IModule)(Activator.CreateInstance(types[0]) ?? throw new InvalidOperationException("The module could not be created."));
    }

    /// <summary>Resolves the module's own files first; the contracts always come from the host so types are shared.</summary>
    private sealed class ModuleLoadContext(string name, string directory) : AssemblyLoadContext("module:" + name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is null || assemblyName.Name.StartsWith("PetraERP.Modules.Abstractions", StringComparison.Ordinal)) return null;
            var candidate = Path.Combine(directory, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }
}

public sealed record ModuleActivation(string ModuleId, bool Success, string Message);

/// <summary>
/// Starts and stops enabled modules. A module that does not match its registration, asks for more permissions than the
/// operator approved, or fails to start is marked Failed and never half-runs; one bad module cannot stop the others.
/// </summary>
public sealed class ModuleRuntime(IModuleStateStore store, IModuleLoader loader, Func<ModuleServiceReferences> services, TimeProvider timeProvider, Action<string, Exception?> log)
{
    private readonly ConcurrentDictionary<string, IModule> _active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlySet<string>> _approved = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> ActiveModules => _active.Keys.ToArray();

    public bool IsActive(string moduleId) => _active.ContainsKey(moduleId);

    public async Task<IReadOnlyList<ModuleActivation>> StartEnabledAsync(CancellationToken cancellationToken)
    {
        var results = new List<ModuleActivation>();
        foreach (var record in (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Where(r => r.State == ModuleState.Enabled))
            results.Add(await ActivateAsync(record, cancellationToken).ConfigureAwait(false));
        return results;
    }

    public async Task<ModuleActivation> ActivateAsync(ModuleRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.State != ModuleState.Enabled) return new ModuleActivation(record.Id, false, $"Module is {record.State}, not Enabled.");
        if (_active.ContainsKey(record.Id)) return new ModuleActivation(record.Id, true, "Already active.");

        IModule? module = null;
        try
        {
            module = loader.Load(record);
            var descriptor = module.Describe();
            if (!string.Equals(descriptor.Id, record.Id, StringComparison.Ordinal)
                || !string.Equals(descriptor.Version, record.Version, StringComparison.Ordinal)
                || descriptor.ContractVersion != record.ContractVersion
                || descriptor.ContractVersion != ModuleIdentity.CurrentContractVersion)
                throw new InvalidOperationException("The module describes itself differently from its registration.");
            var extra = descriptor.Permissions.Except(record.DeclaredPermissions, StringComparer.Ordinal).ToArray();
            if (extra.Length > 0)
                throw new InvalidOperationException($"The module asks for permissions that were not approved: {string.Join(", ", extra)}.");

            await module.InitializeAsync(new ModuleActivationContext(record.DataDirectory, new Dictionary<string, string>(), services(),
                (message, exception) => log($"[{record.Id}] {message}", exception)), cancellationToken).ConfigureAwait(false);
            _active[record.Id] = module;
            _approved[record.Id] = record.DeclaredPermissions.ToHashSet(StringComparer.Ordinal);
            return new ModuleActivation(record.Id, true, "Started.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log($"[{record.Id}] failed to start", exception);
            await SafeShutdownAsync(module, cancellationToken).ConfigureAwait(false);
            await store.SaveAsync(record with { State = ModuleState.Failed, LastError = exception.Message, UpdatedAtUtc = timeProvider.GetUtcNow() }, cancellationToken).ConfigureAwait(false);
            return new ModuleActivation(record.Id, false, exception.Message);
        }
    }

    public async Task DeactivateAsync(string moduleId, CancellationToken cancellationToken)
    {
        _approved.TryRemove(moduleId, out _);
        if (_active.TryRemove(moduleId, out var module))
            await SafeShutdownAsync(module, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one module action. The command must be a declared menu item whose permission the caller holds; the module only
    /// ever sees the permissions the operator approved for it, and its identity is stamped by the host, never by the module.
    /// </summary>
    public async Task<ModuleCommandResult> ExecuteAsync(string moduleId, ModuleRequestContext context, string commandKey,
        IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!_active.TryGetValue(moduleId, out var module) || module is not IModuleCommandHandler handler || module is not IModuleMenuContributor menu)
            return Refused($"Module '{moduleId}' is not running or exposes no commands.");
        var item = Flatten(menu.GetMenuItems(context)).FirstOrDefault(i => string.Equals(i.CommandKey, commandKey, StringComparison.Ordinal));
        if (item is null) return Refused($"Unknown command '{commandKey}'.");
        var approved = _approved[moduleId];
        if (!context.Has(item.RequiredPermission) || !approved.Contains(item.RequiredPermission))
            return Refused($"The command requires '{item.RequiredPermission}'.");

        var granted = context.GrantedPermissions.Where(approved.Contains).ToHashSet(StringComparer.Ordinal);
        return await handler.ExecuteAsync(context with { ModuleId = moduleId, GrantedPermissions = granted }, commandKey, arguments, cancellationToken).ConfigureAwait(false);
    }

    private static ModuleCommandResult Refused(string message) => new(false, message, new Dictionary<string, string>());

    private static IEnumerable<ModuleMenuItem> Flatten(IEnumerable<ModuleMenuItem> items) =>
        items.SelectMany(i => new[] { i }.Concat(Flatten(i.Children)));

    private async Task SafeShutdownAsync(IModule? module, CancellationToken cancellationToken)
    {
        if (module is null) return;
        try
        {
            await module.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log("module shutdown failed", exception);
        }
    }
}
