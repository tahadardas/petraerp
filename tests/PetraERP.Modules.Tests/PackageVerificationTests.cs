#pragma warning disable CA1707

using PetraERP.Modules.Abstractions;
using PetraERP.Modules.Packaging;

namespace PetraERP.Modules.Tests;

/// <summary>
/// The verifier must reject a package on evidence, not on the manifest's own claims. Each case builds a real package on
/// disk and then changes exactly one thing.
/// </summary>
public sealed class PackageVerificationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "petra-modules-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Verify_WellFormedPackage_IsValid()
    {
        var package = BuildPackage("alpha", "1.0.0");
        var result = ModulePackageVerifier.Verify(package, "10.0.0", Trusted("petra"));

        Assert.True(result.IsValid, string.Join(" | ", result.Issues.Select(i => $"{i.Failure}:{i.Detail}")));
        Assert.NotNull(result.Manifest);
        Assert.Equal("alpha", result.Manifest!.Id);
        Assert.Equal(64, result.PackageFingerprint.Length);
    }

    [Fact]
    public void Verify_TamperedFilePayload_IsRejectedWithChecksumMismatch()
    {
        var package = BuildPackage("alpha", "1.0.0");

        // Overwrite one payload file in place. The manifest still carries the hash of the original bytes, so the
        // mismatch is exactly what a real tamper looks like.
        using (var archive = System.IO.Compression.ZipFile.Open(package, System.IO.Compression.ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("content.txt")!;
            using var stream = entry.Open();
            stream.SetLength(0);
            var replacement = System.Text.Encoding.UTF8.GetBytes("different bytes than the manifest claims");
            stream.Write(replacement, 0, replacement.Length);
        }

        var result = ModulePackageVerifier.Verify(package, "10.0.0", Trusted("petra"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Failure == PackageFailure.ChecksumMismatch);
    }

    [Fact]
    public void Verify_UntrustedPublisher_IsRejected()
    {
        var package = BuildPackage("alpha", "1.0.0");
        var result = ModulePackageVerifier.Verify(package, "10.0.0", Trusted("someone-else"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Failure == PackageFailure.UntrustedPublisher);
    }

    [Fact]
    public void Verify_FutureContractVersion_IsRejected()
    {
        var package = BuildPackage("alpha", "1.0.0", contractVersion: ModuleIdentity.CurrentContractVersion + 5);
        var result = ModulePackageVerifier.Verify(package, "10.0.0", Trusted("petra"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Failure == PackageFailure.UnsupportedContract);
    }

    [Fact]
    public void Verify_CoreOutsideDeclaredBounds_IsRejected()
    {
        var package = BuildPackage("alpha", "1.0.0", minCore: "11.0.0");
        var result = ModulePackageVerifier.Verify(package, "10.0.0", Trusted("petra"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Failure == PackageFailure.UnsupportedContract);
    }

    [Fact]
    public void Verify_PackageWithoutManifest_IsRejected()
    {
        var orphan = Path.Combine(_root, "orphan.pmpkg");
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
        File.WriteAllText(Path.Combine(_root, "empty", "readme.txt"), "no manifest here");
        System.IO.Compression.ZipFile.CreateFromDirectory(Path.Combine(_root, "empty"), orphan);

        var result = ModulePackageVerifier.Verify(orphan, "10.0.0", Trusted("petra"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Failure == PackageFailure.MissingManifest);
    }

    [Fact]
    public void Verify_EntryAssemblyMustExistInsidePackage()
    {
        var package = BuildPackage("alpha", "1.0.0");
        var source = Path.Combine(_root, "src-alpha");

        var result = ModulePackageVerifier.Verify(package, "10.0.0", Trusted("petra"));
        Assert.True(result.IsValid);

        // Rebuild with a manifest naming an assembly that is not in the payload.
        ModulePackageBuilder.Build(source, Manifest("beta", "1.0.0", entry: "not-there.dll"), Path.Combine(_root, "missing-entry.pmpkg"));
        var missing = ModulePackageVerifier.Verify(Path.Combine(_root, "missing-entry.pmpkg"), "10.0.0", Trusted("petra"));

        Assert.False(missing.IsValid);
        Assert.Contains(missing.Issues, i => i.Failure == PackageFailure.MissingFile);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static HashSet<string> Trusted(string publisher) => new(StringComparer.Ordinal) { publisher };

    private string BuildPackage(string id, string version, int contractVersion = ModuleIdentity.CurrentContractVersion, string minCore = "10.0.0", string entry = "module.dll")
    {
        var source = Path.Combine(_root, "src-" + id);
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "content.txt"), "module payload for " + id);
        File.WriteAllText(Path.Combine(source, entry), "fake assembly bytes");
        var package = Path.Combine(_root, id + ".pmpkg");
        ModulePackageBuilder.Build(source, Manifest(id, version, contractVersion, minCore, entry), package);
        return package;
    }

    private static ModuleManifest Manifest(string id, string version, int contractVersion = ModuleIdentity.CurrentContractVersion, string minCore = "10.0.0", string entry = "module.dll") =>
        new()
        {
            Id = id,
            DisplayName = "تجربة " + id,
            Version = version,
            ContractVersion = contractVersion,
            MinCoreVersion = minCore,
            Publisher = "petra",
            EntryAssembly = entry,
            Permissions = ["module.alpha.use"],
            Dependencies = [],
        };
}
