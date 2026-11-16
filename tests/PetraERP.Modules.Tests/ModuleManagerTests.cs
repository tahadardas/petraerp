#pragma warning disable CA1707

using PetraERP.Modules.Abstractions;
using PetraERP.Modules.Host;
using PetraERP.Modules.Packaging;

namespace PetraERP.Modules.Tests;

/// <summary>
/// Lifecycle rules are safety rules: a module must not be able to reach Enabled without verification, staging and
/// installation, and disabling must never be a data-destroying operation.
/// </summary>
public sealed class ModuleManagerTests
{
    [Fact]
    public async Task Install_VerifiesThenStagesThenInstalls()
    {
        var package = Package("alpha", "1.0.0");
        var store = new InMemoryStore();
        var stager = new FakeStager();
        var manager = new ModuleManager(store, stager, TimeProvider.System);

        var result = await manager.InstallAsync(package, "10.0.0", Trusted("petra"), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.Equal(ModuleState.Installed, result.Record!.State);
        Assert.Equal(1, stager.StageCalls);
        Assert.Equal(1, stager.ApplyCalls);
        Assert.Equal("alpha", (await store.FindAsync("alpha", TestContext.Current.CancellationToken))!.Id);
    }

    [Fact]
    public async Task Install_UntrustedPublisher_WritesNothingAndFails()
    {
        var package = Package("alpha", "1.0.0");
        var store = new InMemoryStore();
        var stager = new FakeStager();
        var manager = new ModuleManager(store, stager, TimeProvider.System);

        var result = await manager.InstallAsync(package, "10.0.0", Trusted("someone-else"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("UntrustedPublisher", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, stager.StageCalls);
        Assert.Empty(await store.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Enable_FollowsInstall_AndDisableKeepsData()
    {
        var manager = new ModuleManager(new InMemoryStore(), new FakeStager(), TimeProvider.System);
        await manager.InstallAsync(Package("alpha", "1.0.0"), "10.0.0", Trusted("petra"), TestContext.Current.CancellationToken);

        var enabled = await manager.EnableAsync("alpha", TestContext.Current.CancellationToken);
        Assert.True(enabled.Success);
        Assert.Equal(ModuleState.Enabled, enabled.Record!.State);

        var disabled = await manager.DisableAsync("alpha", TestContext.Current.CancellationToken);
        Assert.True(disabled.Success);
        Assert.Equal(ModuleState.Disabled, disabled.Record!.State);
        Assert.True(ModuleLifecycle.IsDataBearing(ModuleState.Disabled));

        var reEnabled = await manager.EnableAsync("alpha", TestContext.Current.CancellationToken);
        Assert.True(reEnabled.Success);
    }

    [Fact]
    public void Transitions_IllegalJumpsAreRefused()
    {
        Assert.False(ModuleLifecycle.CanTransition(ModuleState.Discovered, ModuleState.Enabled));
        Assert.False(ModuleLifecycle.CanTransition(ModuleState.Verified, ModuleState.Installed));
        Assert.True(ModuleLifecycle.CanTransition(ModuleState.Installed, ModuleState.Enabled));
        Assert.False(ModuleLifecycle.CanTransition(ModuleState.Installed, ModuleState.Verified));
        Assert.Throws<InvalidOperationException>(() => ModuleLifecycle.EnsureTransition(ModuleState.Verified, ModuleState.Enabled));
    }

    [Fact]
    public async Task Install_MissingRequiredDependency_IsRefused()
    {
        var package = Package("beta", "1.0.0", dependencies: [new ManifestDependency("alpha", "^1.0.0", Optional: false)]);
        var manager = new ModuleManager(new InMemoryStore(), new FakeStager(), TimeProvider.System);

        var result = await manager.InstallAsync(package, "10.0.0", Trusted("petra"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("اعتماد مطلوب", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_DependencyVersionOutsideRange_IsRefused()
    {
        var store = new InMemoryStore();
        var manager = new ModuleManager(store, new FakeStager(), TimeProvider.System);
        await manager.InstallAsync(Package("alpha", "1.0.0"), "10.0.0", Trusted("petra"), TestContext.Current.CancellationToken);

        var result = await manager.InstallAsync(
            Package("beta", "1.0.0", dependencies: [new ManifestDependency("alpha", "^2.0.0", Optional: false)]),
            "10.0.0", Trusted("petra"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("خارج النطاق", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_SelfDependency_IsRefused()
    {
        var manager = new ModuleManager(new InMemoryStore(), new FakeStager(), TimeProvider.System);
        var result = await manager.InstallAsync(
            Package("alpha", "1.0.0", dependencies: [new ManifestDependency("alpha", "^1.0.0", Optional: false)]),
            "10.0.0", Trusted("petra"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("اعتماداً على نفسه", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_SameVersionTwice_IsRefusedAsDuplicate()
    {
        var manager = new ModuleManager(new InMemoryStore(), new FakeStager(), TimeProvider.System);
        await manager.InstallAsync(Package("alpha", "1.0.0"), "10.0.0", Trusted("petra"), TestContext.Current.CancellationToken);

        var again = await manager.InstallAsync(Package("alpha", "1.0.0"), "10.0.0", Trusted("petra"), TestContext.Current.CancellationToken);

        Assert.False(again.Success);
        Assert.Contains("بالإصدار نفسه", again.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upgrade_RejectsEqualOrLowerVersionAndAppliesHigherOne()
    {
        var manager = new ModuleManager(new InMemoryStore(), new FakeStager(), TimeProvider.System);
        await manager.InstallAsync(Package("alpha", "1.2.0"), "10.0.0", Trusted("petra"), TestContext.Current.CancellationToken);
        await manager.EnableAsync("alpha", TestContext.Current.CancellationToken);

        var same = await manager.UpgradeAsync(Package("alpha", "1.2.0"), "10.0.0", Trusted("petra"), TestContext.Current.CancellationToken);
        Assert.False(same.Success);

        var higher = await manager.UpgradeAsync(Package("alpha", "1.3.0"), "10.0.0", Trusted("petra"), TestContext.Current.CancellationToken);
        Assert.True(higher.Success, higher.Message);
        Assert.Equal("1.3.0", higher.Record!.Version);
        Assert.Equal(ModuleState.Installed, higher.Record.State);
    }

    [Theory]
    [InlineData("1.2.3", "^1.0.0", true)]
    [InlineData("2.0.0", "^1.0.0", false)]
    [InlineData("1.2.3", "~1.2.0", true)]
    [InlineData("1.3.0", "~1.2.0", false)]
    [InlineData("1.2.3", ">=1.2.3", true)]
    [InlineData("1.2.2", ">=1.2.3", false)]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", "", true)]
    [InlineData("1.2.3", "garbage", false)]
    public void VersionRange_MatchesDeclaredSemantics(string version, string range, bool expected) =>
        Assert.Equal(expected, ModuleVersionRange.Matches(version, range));

    private static HashSet<string> Trusted(string publisher) => new(StringComparer.Ordinal) { publisher };

    private static string Package(
        string id,
        string version,
        IReadOnlyList<ManifestDependency>? dependencies = null,
        int contractVersion = ModuleIdentity.CurrentContractVersion,
        string minCore = "10.0.0")
    {
        var root = Path.Combine(Path.GetTempPath(), "petra-module-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "module.dll"), $"payload {id} {version}");
        File.WriteAllText(Path.Combine(root, "content.txt"), "content");
        var package = Path.Combine(root, id + ".pmpkg");
        ModulePackageBuilder.Build(root, new ModuleManifest
        {
            Id = id,
            DisplayName = "موديول " + id,
            Version = version,
            ContractVersion = contractVersion,
            MinCoreVersion = minCore,
            Publisher = "petra",
            EntryAssembly = "module.dll",
            Permissions = ["module." + id + ".use"],
            Dependencies = dependencies ?? [],
        }, package);
        return package;
    }

    private sealed class InMemoryStore : IModuleStateStore
    {
        private readonly Dictionary<string, ModuleRecord> _records = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<ModuleRecord>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModuleRecord>>(_records.Values.ToList());

        public Task<ModuleRecord?> FindAsync(string moduleId, CancellationToken cancellationToken) =>
            Task.FromResult(_records.TryGetValue(moduleId, out var record) ? record : null);

        public Task SaveAsync(ModuleRecord record, CancellationToken cancellationToken)
        {
            _records[record.Id] = record;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string moduleId, CancellationToken cancellationToken)
        {
            _records.Remove(moduleId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStager : IModulePackageStager
    {
        public int StageCalls { get; private set; }

        public int ApplyCalls { get; private set; }

        public Task<StagingResult> StageAsync(string packagePath, ModuleManifest manifest, string fingerprint, CancellationToken cancellationToken)
        {
            StageCalls++;
            var directory = Path.Combine(Path.GetTempPath(), "petra-staged-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return Task.FromResult(new StagingResult(true, directory, null));
        }

        public Task<bool> ApplyAsync(string dataDirectory, ModuleManifest manifest, CancellationToken cancellationToken)
        {
            ApplyCalls++;
            return Task.FromResult(Directory.Exists(dataDirectory));
        }
    }
}
