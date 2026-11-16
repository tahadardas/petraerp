using PetraERP.Modules.Abstractions;
using PetraERP.Modules.Packaging;

namespace PetraERP.Modules.Host;

/// <summary>One module as Petra knows it. Nothing here is derived from an assembly path alone.</summary>
public sealed record ModuleRecord(
    string Id,
    string DisplayName,
    string Publisher,
    string Version,
    int ContractVersion,
    ModuleState State,
    string PackageFingerprint,
    string DataDirectory,
    DateTimeOffset UpdatedAtUtc,
    string? LastError,
    IReadOnlyList<string> DeclaredPermissions,
    IReadOnlyList<ModuleDependency> Dependencies,
    string LicenseRequirement);

/// <summary>
/// Durable module state must outlive the process. The host speaks only to this store, so the persistence choice
/// (central registry table today, anything else later) does not leak into lifecycle rules.
/// </summary>
public interface IModuleStateStore
{
    Task<IReadOnlyList<ModuleRecord>> ListAsync(CancellationToken cancellationToken);

    Task<ModuleRecord?> FindAsync(string moduleId, CancellationToken cancellationToken);

    Task SaveAsync(ModuleRecord record, CancellationToken cancellationToken);

    Task DeleteAsync(string moduleId, CancellationToken cancellationToken);
}

public sealed record ModuleOperationResult(bool Success, ModuleRecord? Record, string Message, IReadOnlyList<PackageIssue> Issues);

/// <summary>
/// Applies the module lifecycle: verify before stage, stage before install, install before enable, and never destroy
/// data-bearing state on disable. Version, contract, publisher and dependency checks all happen before any assembly is
/// loaded, because a load is already executable code.
/// </summary>
public sealed class ModuleManager(
    IModuleStateStore store,
    IModulePackageStager stager,
    TimeProvider timeProvider,
    PublisherTrust? trust = null)
{
    private readonly IModuleStateStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IModulePackageStager _stager = stager ?? throw new ArgumentNullException(nameof(stager));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public Task<IReadOnlyList<ModuleRecord>> ListAsync(CancellationToken cancellationToken) => _store.ListAsync(cancellationToken);

    public async Task<ModuleOperationResult> InstallAsync(
        string packagePath,
        string coreVersion,
        IReadOnlySet<string> trustedPublishers,
        CancellationToken cancellationToken)
    {
        var verification = ModulePackageVerifier.Verify(packagePath, coreVersion, trustedPublishers, trust);
        if (verification.Manifest is null)
            return Failed(null, verification, "الحزمة لا تحمل بيانا صالحاً.");

        // Verification is the gate, not a report: an untrusted publisher, a bad checksum or an unsupported contract
        // must stop the install before any state is written and long before any assembly is loaded.
        if (!verification.IsValid)
            return Failed(await _store.FindAsync(verification.Manifest.Id, cancellationToken).ConfigureAwait(false), verification,
                "الحزمة لم تجتز التحقق: " + string.Join(" | ", verification.Issues.Select(i => $"{i.Failure}:{i.Detail}")));

        var manifest = verification.Manifest;
        var existing = await _store.FindAsync(manifest.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null && existing.State is not (ModuleState.Disabled or ModuleState.Failed))
        {
            if (string.Equals(existing.Version, manifest.Version, StringComparison.Ordinal))
                return Failed(existing, verification, $"الموديول \"{manifest.Id}\" بالإصدار نفسه مثبّت بالفعل.");
            return Failed(existing, verification, $"الموديول \"{manifest.Id}\" مثبّت بإصدار {existing.Version}؛ استخدم الترقية.");
        }

        var dependencyIssue = await CheckDependenciesAsync(manifest, cancellationToken).ConfigureAwait(false);
        if (dependencyIssue is not null)
            return Failed(existing, verification, dependencyIssue);

        var record = ToRecord(manifest, verification, ModuleState.Verified, existing?.DataDirectory ?? string.Empty, null);
        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);

        var staged = await _stager.StageAsync(packagePath, manifest, verification.PackageFingerprint, cancellationToken).ConfigureAwait(false);
        if (!staged.Success)
            return await FailAsync(record, verification, staged.Error ?? "تعذير تجهيز الحزمة.", cancellationToken).ConfigureAwait(false);

        var stagedRecord = record with { State = ModuleState.Staged, DataDirectory = staged.Directory ?? record.DataDirectory };
        await _store.SaveAsync(stagedRecord, cancellationToken).ConfigureAwait(false);

        var installed = await _stager.ApplyAsync(stagedRecord.DataDirectory, manifest, cancellationToken).ConfigureAwait(false);
        if (!installed)
            return await FailAsync(stagedRecord, verification, "فشل تثبيت ملفات الموديول.", cancellationToken).ConfigureAwait(false);

        var installedRecord = stagedRecord with { State = ModuleState.Installed, UpdatedAtUtc = _timeProvider.GetUtcNow(), LastError = null };
        await _store.SaveAsync(installedRecord, cancellationToken).ConfigureAwait(false);
        return new ModuleOperationResult(true, installedRecord, $"تم تثبيت \"{manifest.DisplayName}\" الإصدار {manifest.Version}.", verification.Issues);
    }

    public Task<ModuleOperationResult> EnableAsync(string moduleId, CancellationToken cancellationToken) =>
        TransitionAsync(moduleId, ModuleState.Enabled, "تم تفعيل الموديول.", cancellationToken);

    public Task<ModuleOperationResult> DisableAsync(string moduleId, CancellationToken cancellationToken) =>
        TransitionAsync(moduleId, ModuleState.Disabled, "تم تعطيل الموديول؛ لم تُحذف أي بيانات أو أرصدة.", cancellationToken);

    public async Task<ModuleOperationResult> UpgradeAsync(
        string packagePath,
        string coreVersion,
        IReadOnlySet<string> trustedPublishers,
        CancellationToken cancellationToken)
    {
        var verification = ModulePackageVerifier.Verify(packagePath, coreVersion, trustedPublishers, trust);
        if (verification.Manifest is null)
            return Failed(null, verification, "الحزمة لا تحمل بيانا صالحاً.");

        var manifest = verification.Manifest;
        var existing = await _store.FindAsync(manifest.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"لا يوجد موديول مثبّت باسم \"{manifest.Id}\"؛ ثبّته أولاً.");

        if (!ModuleVersionRange.IsCompatible(existing.Version, manifest.MinCoreVersion, manifest.MaxCoreVersion, coreVersion))
            return Failed(existing, verification, "إصدار النواة الحالي خارج حدود توافق الحزمة الجديدة.");

        if (ModuleVersionRange.Compare(existing.Version, manifest.Version) >= 0)
            return Failed(existing, verification, $"الإصدار الجديد {manifest.Version} ليس أعلى من المثبّت {existing.Version}.");

        ModuleLifecycle.EnsureTransition(existing.State, ModuleState.UpgradePending);
        var pending = existing with { State = ModuleState.UpgradePending, UpdatedAtUtc = _timeProvider.GetUtcNow(), LastError = null };
        await _store.SaveAsync(pending, cancellationToken).ConfigureAwait(false);

        return await InstallInternalAsync(packagePath, verification, manifest, pending, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ModuleOperationResult> InstallInternalAsync(
        string packagePath,
        PackageVerification verification,
        ModuleManifest manifest,
        ModuleRecord pending,
        CancellationToken cancellationToken)
    {
        var dependencyIssue = await CheckDependenciesAsync(manifest, cancellationToken).ConfigureAwait(false);
        if (dependencyIssue is not null)
            return Failed(pending, verification, dependencyIssue);

        var verified = pending with { State = ModuleState.Verified };
        await _store.SaveAsync(verified, cancellationToken).ConfigureAwait(false);

        var staged = await _stager.StageAsync(packagePath, manifest, verification.PackageFingerprint, cancellationToken).ConfigureAwait(false);
        if (!staged.Success)
            return await FailAsync(verified, verification, staged.Error ?? "تعذر تجهيز الترقية.", cancellationToken).ConfigureAwait(false);

        var stagedRecord = verified with { State = ModuleState.Staged, DataDirectory = staged.Directory ?? verified.DataDirectory };
        await _store.SaveAsync(stagedRecord, cancellationToken).ConfigureAwait(false);

        if (!await _stager.ApplyAsync(stagedRecord.DataDirectory, manifest, cancellationToken).ConfigureAwait(false))
            return await FailAsync(stagedRecord, verification, "فشل تثبيت ملفات الترقية.", cancellationToken).ConfigureAwait(false);

        var result = ToRecord(manifest, verification, ModuleState.Installed, stagedRecord.DataDirectory, null);
        await _store.SaveAsync(result, cancellationToken).ConfigureAwait(false);
        return new ModuleOperationResult(true, result, $"تمت ترقية \"{manifest.DisplayName}\" إلى {manifest.Version}.", verification.Issues);
    }

    private async Task<ModuleOperationResult> TransitionAsync(string moduleId, ModuleState target, string successMessage, CancellationToken cancellationToken)
    {
        var existing = await _store.FindAsync(moduleId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"الموديول \"{moduleId}\" غير مسجل.");

        ModuleLifecycle.EnsureTransition(existing.State, target);
        var updated = existing with { State = target, UpdatedAtUtc = _timeProvider.GetUtcNow(), LastError = null };
        await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return new ModuleOperationResult(true, updated, successMessage, []);
    }

    private async Task<string?> CheckDependenciesAsync(ModuleManifest manifest, CancellationToken cancellationToken)
    {
        foreach (var dependency in manifest.Dependencies)
        {
            if (string.Equals(dependency.ModuleId, manifest.Id, StringComparison.Ordinal))
                return $"الموديول \"{manifest.Id}\" يعلن اعتماداً على نفسه.";

            var installed = await _store.FindAsync(dependency.ModuleId, cancellationToken).ConfigureAwait(false);
            if (installed is null)
            {
                if (dependency.Optional)
                    continue;
                return $"اعتماد مطلوب غير مثبّت: \"{dependency.ModuleId}\" {dependency.VersionRange}.";
            }

            if (!ModuleVersionRange.Matches(installed.Version, dependency.VersionRange))
                return $"إصدار الاعتماد \"{dependency.ModuleId}\" هو {installed.Version} وهو خارج النطاق {dependency.VersionRange}.";

            if (installed.State is ModuleState.Failed or ModuleState.RollbackRequired)
                return $"الاعتماد \"{dependency.ModuleId}\" في حالة فاشلة؛ التثبيت مرفوض.";

            var cycle = await HasDependencyPathAsync(dependency.ModuleId, manifest.Id, cancellationToken).ConfigureAwait(false);
            if (cycle)
                return $"دورة اعتماد بين \"{manifest.Id}\" و\"{dependency.ModuleId}\".";
        }

        return null;
    }

    private async Task<bool> HasDependencyPathAsync(string from, string target, CancellationToken cancellationToken, int depth = 0)
    {
        if (depth > 16)
            return true;
        if (string.Equals(from, target, StringComparison.Ordinal))
            return true;

        var record = await _store.FindAsync(from, cancellationToken).ConfigureAwait(false);
        if (record is null)
            return false;

        foreach (var dependency in record.Dependencies)
            if (await HasDependencyPathAsync(dependency.ModuleId, target, cancellationToken, depth + 1).ConfigureAwait(false))
                return true;

        return false;
    }

    private async Task<ModuleOperationResult> FailAsync(ModuleRecord record, PackageVerification verification, string message, CancellationToken cancellationToken)
    {
        var failed = record with
        {
            State = ModuleState.Failed,
            LastError = message,
            UpdatedAtUtc = _timeProvider.GetUtcNow(),
        };
        await _store.SaveAsync(failed, cancellationToken).ConfigureAwait(false);
        return new ModuleOperationResult(false, failed, message, verification.Issues);
    }

    private ModuleRecord ToRecord(ModuleManifest manifest, PackageVerification verification, ModuleState state, string dataDirectory, string? error) =>
        new(
            manifest.Id,
            manifest.DisplayName,
            manifest.Publisher,
            manifest.Version,
            manifest.ContractVersion,
            state,
            verification.PackageFingerprint,
            dataDirectory,
            _timeProvider.GetUtcNow(),
            error,
            manifest.Permissions,
            [.. manifest.Dependencies.Select(d => new ModuleDependency(d.ModuleId, d.VersionRange, d.Optional))],
            manifest.LicenseRequirement);

    private static ModuleOperationResult Failed(ModuleRecord? record, PackageVerification verification, string message) =>
        new(false, record, message, verification.Issues);
}

/// <summary>Prepares a package's files in a staging directory and applies them. Implemented by the host layer that owns disk layout.</summary>
public interface IModulePackageStager
{
    Task<StagingResult> StageAsync(string packagePath, ModuleManifest manifest, string fingerprint, CancellationToken cancellationToken);

    Task<bool> ApplyAsync(string dataDirectory, ModuleManifest manifest, CancellationToken cancellationToken);
}

public sealed record StagingResult(bool Success, string? Directory, string? Error);
