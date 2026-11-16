#pragma warning disable CA1707

using System.Security.Cryptography;
using PetraERP.Modules.Abstractions;
using PetraERP.Modules.Host;
using PetraERP.Modules.Packaging;

namespace PetraERP.Modules.Tests;

/// <summary>
/// Gate G: a publisher NAME on the trust list is not trust. A package installs under signature trust only if it is signed by
/// the publisher's known key, is untouched since signing, and the publisher is not revoked.
/// </summary>
public sealed class PackageSigningTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly IReadOnlySet<string> Names = new HashSet<string> { "petra" };

    private static string Package(ECDsa? signer, string publisher = "petra", Action<string>? tamper = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "petra-sign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "module.dll"), "payload");
        var package = Path.Combine(root, "m.pmpkg");
        ModulePackageBuilder.Build(root, new ModuleManifest
        {
            Id = "signed.demo", DisplayName = "Demo", Version = "1.0.0", ContractVersion = ModuleIdentity.CurrentContractVersion,
            MinCoreVersion = "1.0.0", Publisher = publisher, EntryAssembly = "module.dll", Permissions = ["x.use"],
        }, package, signer);
        tamper?.Invoke(package);
        return package;
    }

    private static PublisherTrust Trust(ECDsa key, params string[] revoked) =>
        new(new Dictionary<string, string> { ["petra"] = PackageSigning.ExportPublicKey(key) }, new HashSet<string>(revoked));

    [Fact]
    public void SignedPackageFromAKnownPublisherVerifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var result = ModulePackageVerifier.Verify(Package(key), "10.0.0", Names, Trust(key));
        Assert.True(result.IsValid, string.Join("|", result.Issues.Select(i => i.Detail)));
    }

    [Fact]
    public void UnsignedPackageIsRefusedUnderSignatureTrustEvenIfTheNameIsListed()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var result = ModulePackageVerifier.Verify(Package(signer: null), "10.0.0", Names, Trust(key));
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Failure == PackageFailure.SignatureInvalid);
    }

    [Fact]
    public void SignatureFromAnotherKeyIsRefused()
    {
        using var publisherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var result = ModulePackageVerifier.Verify(Package(attackerKey), "10.0.0", Names, Trust(publisherKey));
        Assert.Contains(result.Issues, i => i.Failure == PackageFailure.SignatureInvalid);
    }

    [Fact]
    public void ManifestChangedAfterSigningIsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var package = Package(key, tamper: path =>
        {
            // Rewrite the manifest inside the archive with an extra permission, keeping the old signature.
            using var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update);
            var entry = archive.GetEntry(ModulePackageVerifier.ManifestName)!;
            string text;
            using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
            entry.Delete();
            var replaced = archive.CreateEntry(ModulePackageVerifier.ManifestName);
            using var writer = new StreamWriter(replaced.Open());
            writer.Write(text.Replace("x.use", "ledger.everything", StringComparison.Ordinal));
        });
        var result = ModulePackageVerifier.Verify(package, "10.0.0", Names, Trust(key));
        Assert.Contains(result.Issues, i => i.Failure == PackageFailure.SignatureInvalid);
    }

    [Fact]
    public void RevokedPublisherIsRefusedEvenWithAValidSignature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var result = ModulePackageVerifier.Verify(Package(key), "10.0.0", Names, Trust(key, "petra"));
        Assert.Contains(result.Issues, i => i.Failure == PackageFailure.PublisherRevoked);
    }

    [Fact]
    public void PublisherWithoutAKnownKeyIsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var result = ModulePackageVerifier.Verify(Package(key, publisher: "stranger"), "10.0.0", new HashSet<string> { "stranger" }, Trust(key));
        Assert.Contains(result.Issues, i => i.Failure == PackageFailure.SignatureInvalid);
    }

    [Fact]
    public async Task ManagerInstallsOnlyWhatTheSignatureTrustAccepts()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new MemoryStore();
        var manager = new ModuleManager(store, new FolderStager(), TimeProvider.System, Trust(key));

        var unsigned = await manager.InstallAsync(Package(signer: null), "10.0.0", Names, Cancellation).ConfigureAwait(true);
        Assert.False(unsigned.Success);
        Assert.Empty(await store.ListAsync(Cancellation).ConfigureAwait(true));      // nothing was written

        var signed = await manager.InstallAsync(Package(key), "10.0.0", Names, Cancellation).ConfigureAwait(true);
        Assert.True(signed.Success, signed.Message);
    }

    private sealed class MemoryStore : IModuleStateStore
    {
        private readonly Dictionary<string, ModuleRecord> records = [];

        public Task<IReadOnlyList<ModuleRecord>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModuleRecord>>(records.Values.ToList());

        public Task<ModuleRecord?> FindAsync(string moduleId, CancellationToken cancellationToken) => Task.FromResult(records.GetValueOrDefault(moduleId));

        public Task SaveAsync(ModuleRecord record, CancellationToken cancellationToken)
        {
            records[record.Id] = record;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string moduleId, CancellationToken cancellationToken)
        {
            records.Remove(moduleId);
            return Task.CompletedTask;
        }
    }

    private sealed class FolderStager : IModulePackageStager
    {
        public Task<StagingResult> StageAsync(string packagePath, ModuleManifest manifest, string fingerprint, CancellationToken cancellationToken)
        {
            var target = Path.Combine(Path.GetTempPath(), "petra-staged-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(target);
            return Task.FromResult(new StagingResult(true, target, null));
        }

        public Task<bool> ApplyAsync(string dataDirectory, ModuleManifest manifest, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
