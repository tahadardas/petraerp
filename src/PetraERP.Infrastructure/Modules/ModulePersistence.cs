using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Registry;
using PetraERP.Modules.Abstractions;
using PetraERP.Modules.Host;
using PetraERP.Modules.Packaging;

namespace PetraERP.Infrastructure.Modules;

/// <summary>
/// Central persistence for module state. A module identity is unique per installation, and a concurrent write from
/// another API instance is refused rather than last-write-wins, because two half-applied installs are worse than a
/// visible conflict.
/// </summary>
public sealed class ModuleRegistryStore(SystemDbContext context, TimeProvider timeProvider) : IModuleStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SystemDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<IReadOnlyList<ModuleRecord>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await _context.ModuleRegistry.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        // Ordinal ordering cannot be translated to SQL, so the (small) registry is ordered after it is read.
        return [.. rows.OrderBy(r => r.ModuleId, StringComparer.Ordinal).Select(Map)];
    }

    public async Task<ModuleRecord?> FindAsync(string moduleId, CancellationToken cancellationToken)
    {
        var row = await _context.ModuleRegistry.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ModuleId == moduleId, cancellationToken).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task SaveAsync(ModuleRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var existing = await _context.ModuleRegistry
            .FirstOrDefaultAsync(r => r.ModuleId == record.Id, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();

        if (existing is null)
        {
            _context.ModuleRegistry.Add(new ModuleRegistryRow
            {
                Id = Guid.NewGuid(),
                ModuleId = record.Id,
                DisplayName = record.DisplayName,
                Publisher = record.Publisher,
                Version = record.Version,
                ContractVersion = record.ContractVersion,
                State = (int)record.State,
                PackageFingerprint = record.PackageFingerprint,
                DataDirectory = record.DataDirectory,
                LastError = record.LastError,
                DeclaredPermissions = Serialize(record.DeclaredPermissions),
                Dependencies = Serialize(record.Dependencies),
                LicenseRequirement = record.LicenseRequirement,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }
        else
        {
            existing.DisplayName = record.DisplayName;
            existing.Publisher = record.Publisher;
            existing.Version = record.Version;
            existing.ContractVersion = record.ContractVersion;
            existing.State = (int)record.State;
            existing.PackageFingerprint = record.PackageFingerprint;
            existing.DataDirectory = record.DataDirectory;
            existing.LastError = record.LastError;
            existing.DeclaredPermissions = Serialize(record.DeclaredPermissions);
            existing.Dependencies = Serialize(record.Dependencies);
            existing.LicenseRequirement = record.LicenseRequirement;
            existing.UpdatedAtUtc = now;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
            throw new BusinessRuleException("modules.state_conflict", "تعذر حفظ حالة الموديول: توجد نسخة أحدث في السجل المركزي.");
        }
    }

    public async Task DeleteAsync(string moduleId, CancellationToken cancellationToken)
    {
        var row = await _context.ModuleRegistry.FirstOrDefaultAsync(r => r.ModuleId == moduleId, cancellationToken).ConfigureAwait(false);
        if (row is null)
            return;
        _context.ModuleRegistry.Remove(row);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Serialize<T>(IReadOnlyList<T> values) => JsonSerializer.Serialize(values, JsonOptions);

    private static ModuleRecord Map(ModuleRegistryRow row) =>
        new(
            row.ModuleId,
            row.DisplayName,
            row.Publisher,
            row.Version,
            row.ContractVersion,
            (ModuleState)row.State,
            row.PackageFingerprint,
            row.DataDirectory,
            row.UpdatedAtUtc,
            row.LastError,
            Deserialize<string[]>(row.DeclaredPermissions) ?? [],
            Deserialize<ModuleDependency[]>(row.Dependencies) ?? [],
            row.LicenseRequirement);

    private static T? Deserialize<T>(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}

/// <summary>
/// Extracts a verified package into a staging directory and only moves it into place when the whole extraction
/// succeeded. A failed stage never leaves a partially-extracted module in the live directory.
/// </summary>
public sealed class DirectoryModuleStager(string rootDirectory, TimeProvider timeProvider) : IModulePackageStager
{
    private readonly string _root = string.IsNullOrEmpty(rootDirectory)
        ? throw new ArgumentException("A module root directory is required.", nameof(rootDirectory))
        : Path.GetFullPath(rootDirectory);
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public Task<StagingResult> StageAsync(string packagePath, ModuleManifest manifest, string fingerprint, CancellationToken cancellationToken)
    {
        var staging = Path.Combine(_root, "staging", $"{Sanitize(manifest.Id)}-{Sanitize(manifest.Version)}-{fingerprint[..Math.Min(12, fingerprint.Length)]}");
        var target = Path.Combine(_root, "modules", Sanitize(manifest.Id), Sanitize(manifest.Version));
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
            System.IO.Compression.ZipFile.ExtractToDirectory(packagePath, staging);

            // The extracted set must equal what the manifest declared; anything extra is a smuggled file.
            var declared = manifest.Files.Select(f => f.Path.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
            var actual = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(staging, f).Replace('\\', '/'))
                .Where(f => f != ModulePackageVerifier.ManifestName)
                .ToHashSet(StringComparer.Ordinal);
            if (!actual.SetEquals(declared))
                return Task.FromResult(new StagingResult(false, null, "الملفات المستخرجة لا تطابق ما أعلنه البيان."));

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(staging, file);
                var destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }

            // The live copy is authoritative; keeping the staging copy would leave two versions of an applied module.
            TryClean(staging);
            return Task.FromResult(new StagingResult(true, target, null));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryClean(staging);
            return Task.FromResult(new StagingResult(false, null, $"تعذر تجهيز الحزمة: {exception.Message}"));
        }
    }

    public Task<bool> ApplyAsync(string dataDirectory, ModuleManifest manifest, CancellationToken cancellationToken)
    {
        var entry = Path.Combine(dataDirectory, manifest.EntryAssembly);
        var present = File.Exists(entry) && File.Exists(Path.Combine(dataDirectory, ModulePackageVerifier.ManifestName));
        return Task.FromResult(present);
    }

    private static void TryClean(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Staging leftovers are harmless; the live module directory was never touched.
        }
    }

    /// <summary>
    /// Names a path segment from declared data. Dots are kept (versions are "1.0.0"), but any segment that could
    /// escape its parent or collide with the filesystem is refused outright rather than silently rewritten.
    /// </summary>
    private static string Sanitize(string value)
    {
        var text = (value ?? string.Empty).Trim();
        var invalid = Path.GetInvalidFileNameChars();
        if (text.Length == 0 || text.Contains("..", StringComparison.Ordinal) || text.IndexOfAny(invalid) >= 0 || text.Contains('/'))
            throw new InvalidOperationException($"اسم مكوّن غير صالح لمسار الموديول: \"{value}\"");
        return text;
    }
}
