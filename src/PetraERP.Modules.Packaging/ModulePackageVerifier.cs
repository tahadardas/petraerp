using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetraERP.Modules.Packaging;

/// <summary>
/// The signed contract inside a module package. Version bounds and permission claims are data the host must verify
/// before anything is loaded; they are never trusted simply because a package declares them.
/// </summary>
public sealed record ModuleManifest
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Version { get; init; }

    public required int ContractVersion { get; init; }

    public required string MinCoreVersion { get; init; }

    public string? MaxCoreVersion { get; init; }

    public required string Publisher { get; init; }

    public required string EntryAssembly { get; init; }

    public IReadOnlyList<string> Permissions { get; init; } = [];

    public IReadOnlyList<ManifestDependency> Dependencies { get; init; } = [];

    public IReadOnlyList<ManifestFile> Files { get; init; } = [];

    public string LicenseRequirement { get; init; } = "None";

    public string? Signature { get; init; }

    /// <summary>SHA-256 of the manifest itself, excluding the fingerprint field. Recorded so a later tamper is detectable.</summary>
    public string? ContentFingerprint { get; init; }
}

public sealed record ManifestDependency(
    [property: JsonPropertyName("moduleId")] string ModuleId,
    [property: JsonPropertyName("versionRange")] string VersionRange,
    [property: JsonPropertyName("optional")] bool Optional);

public sealed record ManifestFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("length")] long Length);

public enum PackageFailure
{
    MissingManifest = 1,
    MalformedManifest = 2,
    UnsafePath = 3,
    MissingFile = 4,
    ChecksumMismatch = 5,
    SizeExceeded = 6,
    DuplicateEntry = 7,
    UntrustedPublisher = 8,
    UnsupportedContract = 9,
    SignatureInvalid = 10,
    PublisherRevoked = 11,
}

public sealed record PackageIssue(PackageFailure Failure, string Detail);

public sealed record PackageVerification(bool IsValid, ModuleManifest? Manifest, string PackageFingerprint, IReadOnlyList<PackageIssue> Issues);

/// <summary>
/// Opens and verifies a module package on disk. Verification happens entirely before any assembly is loaded, and every
/// declared file hash is re-computed rather than trusted from the manifest.
/// </summary>
public static class ModulePackageVerifier
{
    public const string ManifestName = "module.manifest.json";
    private const long MaxEntryBytes = 64L * 1024 * 1024;
    private const long MaxTotalBytes = 256L * 1024 * 1024;

    private static readonly JsonSerializerOptions ManifestOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Name-only trust (legacy, tests and first-party development). Prefer the overload with <see cref="PublisherTrust"/>.</summary>
    public static PackageVerification Verify(string packagePath, string coreVersion, IReadOnlySet<string> trustedPublishers) =>
        Verify(packagePath, coreVersion, trustedPublishers, null);

    /// <summary>
    /// With <paramref name="trust"/> a package must be signed: the publisher's public key must be known and not revoked, and the
    /// manifest signature must verify. The name list alone is then not enough.
    /// </summary>
    public static PackageVerification Verify(string packagePath, string coreVersion, IReadOnlySet<string> trustedPublishers, PublisherTrust? trust)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(coreVersion);
        ArgumentNullException.ThrowIfNull(trustedPublishers);

        if (!File.Exists(packagePath))
            return Invalid([new PackageIssue(PackageFailure.MissingManifest, $"الملف غير موجود: {Path.GetFileName(packagePath)}")], null);

        ModuleManifest? manifest = null;
        var issues = new List<PackageIssue>();
        var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var total = 0L;

        using (var archive = System.IO.Compression.ZipFile.OpenRead(packagePath))
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.Contains("..", StringComparison.Ordinal)
                    || Path.IsPathRooted(entry.FullName)
                    || entry.FullName.Contains(':'))
                {
                    issues.Add(new PackageIssue(PackageFailure.UnsafePath, $"مسار غير آمن داخل الحزمة: {entry.FullName}"));
                    continue;
                }

                if (entries.ContainsKey(entry.FullName))
                {
                    issues.Add(new PackageIssue(PackageFailure.DuplicateEntry, $"تكرار اسم الملف: {entry.FullName}"));
                    continue;
                }

                if (entry.Length > MaxEntryBytes)
                {
                    issues.Add(new PackageIssue(PackageFailure.SizeExceeded, $"حجم الملف يتجاوز الحد: {entry.FullName}"));
                    continue;
                }

                total += entry.Length;
                if (total > MaxTotalBytes)
                {
                    issues.Add(new PackageIssue(PackageFailure.SizeExceeded, $"الحجم الإجمالي للحزمة يتجاوز الحد المسموح."));
                    break;
                }

                using var stream = entry.Open();
                using var buffer = new MemoryStream((int)Math.Min(entry.Length, MaxEntryBytes));
                stream.CopyTo(buffer);
                entries[entry.FullName] = buffer.ToArray();
            }
        }

        if (!entries.TryGetValue(ManifestName, out var manifestBytes))
        {
            issues.Add(new PackageIssue(PackageFailure.MissingManifest, $"لا يوجد {ManifestName} في جذر الحزمة."));
            return Invalid(issues, null);
        }

        try
        {
            manifest = JsonSerializer.Deserialize<ModuleManifest>(manifestBytes, ManifestOptions);
        }
        catch (JsonException exception)
        {
            issues.Add(new PackageIssue(PackageFailure.MalformedManifest, $"بيان الحزمة غير صالح: {exception.Message}"));
        }

        if (manifest is null)
            return Invalid(issues, null);

        if (manifest.ContractVersion != Abstractions.ModuleIdentity.CurrentContractVersion)
            issues.Add(new PackageIssue(
                PackageFailure.UnsupportedContract,
                $"عقد الموديول {manifest.ContractVersion} غير مدعوم؛ الدعم الحالي {Abstractions.ModuleIdentity.CurrentContractVersion}."));

        if (!IsCoreVersionAllowed(manifest, coreVersion))
            issues.Add(new PackageIssue(PackageFailure.UnsupportedContract, $"نواة Petra {coreVersion} خارج حدود التوافق التي تعلنها الحزمة."));

        if (!trustedPublishers.Contains(manifest.Publisher))
            issues.Add(new PackageIssue(PackageFailure.UntrustedPublisher, $"الناشر \"{manifest.Publisher}\" ليس ضمن قائمة الناشرين الموثوقين."));

        if (trust is not null)
        {
            if (trust.RevokedPublishers.Contains(manifest.Publisher))
                issues.Add(new PackageIssue(PackageFailure.PublisherRevoked, $"الناشر \"{manifest.Publisher}\" مُبطَل الثقة."));
            else if (!trust.PublicKeys.TryGetValue(manifest.Publisher, out var publicKey))
                issues.Add(new PackageIssue(PackageFailure.SignatureInvalid, $"لا يوجد مفتاح عام موثوق للناشر \"{manifest.Publisher}\"."));
            else if (!PackageSigning.Verify(manifest, publicKey))
                issues.Add(new PackageIssue(PackageFailure.SignatureInvalid, "توقيع البيان غير صالح أو أن الحزمة عُدِّلت بعد التوقيع."));
        }

        if (manifest.Files.Count == 0)
            issues.Add(new PackageIssue(PackageFailure.MissingFile, "الحزمة لا تعلن أي ملف؛ لا يمكن التحقق من سلامتها."));

        if (!entries.ContainsKey(manifest.EntryAssembly))
            issues.Add(new PackageIssue(PackageFailure.MissingFile, $"ملف الدخول {manifest.EntryAssembly} غير موجود داخل الحزمة."));

        foreach (var file in manifest.Files)
        {
            if (!entries.TryGetValue(file.Path, out var bytes))
            {
                issues.Add(new PackageIssue(PackageFailure.MissingFile, $"الملف المعلن مفقود: {file.Path}"));
                continue;
            }

            var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(actual, file.Sha256.ToLowerInvariant(), StringComparison.Ordinal))
                issues.Add(new PackageIssue(PackageFailure.ChecksumMismatch, $"بصمة الملف لا تطابق المعلن: {file.Path}"));
            else if (bytes.LongLength != file.Length)
                issues.Add(new PackageIssue(PackageFailure.ChecksumMismatch, $"حجم الملف لا يطابق المعلن: {file.Path}"));
        }

        var fingerprint = Fingerprint(packagePath);
        if (issues.Count > 0)
            return new PackageVerification(false, manifest, fingerprint, issues);

        return new PackageVerification(true, manifest, fingerprint, []);
    }

    private static bool IsCoreVersionAllowed(ModuleManifest manifest, string coreVersion)
    {
        if (!Version.TryParse(Trim(coreVersion), out var current))
            return false;
        if (!Version.TryParse(Trim(manifest.MinCoreVersion), out var minimum))
            return false;
        if (current < minimum)
            return false;
        if (manifest.MaxCoreVersion is string maximum && Version.TryParse(Trim(maximum), out var cap) && current > cap)
            return false;
        return true;

        static string Trim(string value)
        {
            var parts = value.Split('.', StringSplitOptions.TrimEntries);
            return parts.Length >= 3 ? string.Join('.', parts[..3]) : value;
        }
    }

    public static string Fingerprint(string packagePath)
    {
        using var stream = File.OpenRead(packagePath);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static PackageVerification Invalid(List<PackageIssue> issues, ModuleManifest? manifest) =>
        new(false, manifest, string.Empty, issues.Count > 0 ? issues : [new PackageIssue(PackageFailure.MissingManifest, "تعذر التحقق من الحزمة.")]);
}
