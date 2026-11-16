using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;

namespace PetraERP.Modules.Packaging;

/// <summary>
/// Builds a package from a publish directory. The manifest is rewritten with the computed hashes of the actual bytes, so
/// a package can never carry a checksum that was copied from a different build.
/// </summary>
public static class ModulePackageBuilder
{
    private static readonly JsonSerializerOptions ManifestOptions = new(JsonSerializerDefaults.Web);

    public static string Build(string sourceDirectory, ModuleManifest manifest, string outputPackagePath, System.Security.Cryptography.ECDsa? signingKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPackagePath);
        ArgumentNullException.ThrowIfNull(manifest);

        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"دليل مصدر الموديول غير موجود: {sourceDirectory}");

        var files = new List<ManifestFile>();
        foreach (var path in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, path).Replace('\\', '/');
            if (string.Equals(relative, ModulePackageVerifier.ManifestName, StringComparison.OrdinalIgnoreCase))
                continue;
            var bytes = File.ReadAllBytes(path);
            files.Add(new ManifestFile(relative, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.LongLength));
        }

        var ordered = files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        var withFiles = manifest with { Files = ordered };
        if (signingKey is not null) withFiles = withFiles with { Signature = PackageSigning.Sign(withFiles, signingKey) };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPackagePath))!);
        if (File.Exists(outputPackagePath))
            File.Delete(outputPackagePath);

        // The archive must be closed before it can be re-read for its fingerprint: Windows keeps the handle exclusive.
        using (var archive = ZipFile.Open(outputPackagePath, ZipArchiveMode.Create))
        {
        var manifestEntry = archive.CreateEntry(ModulePackageVerifier.ManifestName, CompressionLevel.Optimal);
        using (var stream = manifestEntry.Open())
        {
            JsonSerializer.Serialize(stream, withFiles, ManifestOptions);
        }

        foreach (var file in ordered)
        {
            var source = Path.Combine(sourceDirectory, file.Path.Replace('/', Path.DirectorySeparatorChar));
            archive.CreateEntryFromFile(source, file.Path, CompressionLevel.Optimal);
        }
        }

        return ModulePackageVerifier.Fingerprint(outputPackagePath);
    }
}
