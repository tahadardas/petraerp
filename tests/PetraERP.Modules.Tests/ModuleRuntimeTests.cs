#pragma warning disable CA1707

using System.IO.Compression;
using PetraERP.Modules.Abstractions;
using PetraERP.Modules.Host;
using PetraERP.Modules.Packaging;
using PetraERP.Modules.Sample;

namespace PetraERP.Modules.Tests;

/// <summary>
/// The whole external-module path with a real assembly: package, verify, install, enable, load in its own context, run a
/// command that posts through the gateway, and cancel it again. The gateway is a recording fake; its real SQL behaviour is
/// covered by the Infrastructure tests.
/// </summary>
public sealed class ModuleRuntimeTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly string[] Both = [CollectionsModule.PostPermission, CollectionsModule.ReversePermission];

    [Fact]
    public async Task SampleModule_FullLifecycle_PostsAndCancelsThroughTheGateway()
    {
        var (runtime, gateway, _, _) = await StartAsync();
        Assert.Contains(CollectionsModule.ModuleId, runtime.ActiveModules);

        var context = Context(Both);
        var key = Guid.NewGuid();
        var cash = Guid.NewGuid();
        var revenue = Guid.NewGuid();
        var posted = await runtime.ExecuteAsync(CollectionsModule.ModuleId, context, CollectionsModule.RecordCommand, new Dictionary<string, string>
        {
            ["amount"] = "125.50", ["date"] = "2026-03-10", ["reference"] = "INV-7", ["key"] = key.ToString("D"),
            ["cashAccount"] = cash.ToString("D"), ["revenueAccount"] = revenue.ToString("D"), ["currency"] = "JOD",
        }, Cancellation);

        Assert.True(posted.Success, posted.Message);
        var proposal = Assert.Single(gateway.Posted);
        Assert.Equal(CollectionsModule.ModuleId, proposal.Context.ModuleId);   // stamped by the host, not by the module
        Assert.Equal(key, proposal.Proposal.IdempotencyKey);
        Assert.Equal(125.50m, proposal.Proposal.Lines.Sum(l => l.Debit));
        Assert.Equal(125.50m, proposal.Proposal.Lines.Sum(l => l.Credit));

        var cancelKey = Guid.NewGuid();
        var cancelled = await runtime.ExecuteAsync(CollectionsModule.ModuleId, context, CollectionsModule.CancelCommand, new Dictionary<string, string>
        {
            ["journalId"] = posted.Data["journalId"], ["key"] = cancelKey.ToString("D"), ["reason"] = "customer cancelled",
        }, Cancellation);
        Assert.True(cancelled.Success, cancelled.Message);
        var reversal = Assert.Single(gateway.Reversed);
        Assert.Equal(Guid.Parse(posted.Data["journalId"]), reversal.JournalId);
        Assert.Equal(CollectionsModule.ModuleId, reversal.Context.ModuleId);
    }

    [Fact]
    public async Task Command_WithoutThePermission_IsRefusedBeforeTheModuleRuns()
    {
        var (runtime, gateway, _, _) = await StartAsync();
        var result = await runtime.ExecuteAsync(CollectionsModule.ModuleId, Context([]), CollectionsModule.RecordCommand, new Dictionary<string, string>(), Cancellation);
        Assert.False(result.Success);
        Assert.Contains(CollectionsModule.PostPermission, result.Message, StringComparison.Ordinal);
        Assert.Empty(gateway.Posted);

        var unknown = await runtime.ExecuteAsync(CollectionsModule.ModuleId, Context(Both), "collections.delete-everything", new Dictionary<string, string>(), Cancellation);
        Assert.False(unknown.Success);
    }

    [Fact]
    public async Task ModuleOnlyActsWithThePermissionsTheOperatorApproved()
    {
        // The operator approved only posting; the user may hold both, but the module never receives reversal authority.
        var (runtime, gateway, store, _) = await StartAsync(approved: [CollectionsModule.PostPermission], expectStart: false);
        Assert.Empty(runtime.ActiveModules);
        var record = (await store.FindAsync(CollectionsModule.ModuleId, Cancellation))!;
        Assert.Equal(ModuleState.Failed, record.State);
        Assert.Contains(CollectionsModule.ReversePermission, record.LastError, StringComparison.Ordinal);
        Assert.Empty(gateway.Posted);
    }

    [Fact]
    public async Task FileChangedAfterInstallation_IsNeverLoaded()
    {
        var (runtime, _, store, directory) = await StartAsync(startRuntime: false);
        File.AppendAllText(Path.Combine(directory, "PetraERP.Modules.Sample.dll"), "tampered");

        var results = await runtime.StartEnabledAsync(Cancellation);

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Contains("changed after installation", result.Message, StringComparison.Ordinal);
        Assert.Empty(runtime.ActiveModules);
        Assert.Equal(ModuleState.Failed, (await store.FindAsync(CollectionsModule.ModuleId, Cancellation))!.State);
    }

    [Fact]
    public async Task Deactivate_StopsTheModuleAndRefusesFurtherCommands()
    {
        var (runtime, gateway, _, _) = await StartAsync();
        await runtime.DeactivateAsync(CollectionsModule.ModuleId, Cancellation);

        var result = await runtime.ExecuteAsync(CollectionsModule.ModuleId, Context(Both), CollectionsModule.RecordCommand, new Dictionary<string, string>(), Cancellation);

        Assert.False(result.Success);
        Assert.Empty(runtime.ActiveModules);
        Assert.Empty(gateway.Posted);
    }

    private static ModuleRequestContext Context(IEnumerable<string> granted) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ar", new HashSet<string>(granted));

    private static async Task<(ModuleRuntime Runtime, RecordingGateway Gateway, MemoryStore Store, string Directory)> StartAsync(
        IReadOnlyList<string>? approved = null, bool startRuntime = true, bool expectStart = true)
    {
        var source = Path.GetDirectoryName(typeof(CollectionsModule).Assembly.Location)!;
        var work = Path.Combine(Path.GetTempPath(), "petra-runtime-" + Guid.NewGuid().ToString("N"));
        var payload = Path.Combine(work, "payload");
        Directory.CreateDirectory(payload);
        // Only the module's own assembly is shipped; the contracts always come from the host.
        File.Copy(Path.Combine(source, "PetraERP.Modules.Sample.dll"), Path.Combine(payload, "PetraERP.Modules.Sample.dll"));
        var package = Path.Combine(work, "sample.pmpkg");
        ModulePackageBuilder.Build(payload, new ModuleManifest
        {
            Id = CollectionsModule.ModuleId, DisplayName = "Collections", Version = "1.0.0", ContractVersion = ModuleIdentity.CurrentContractVersion,
            MinCoreVersion = "1.0.0", Publisher = "petra", EntryAssembly = "PetraERP.Modules.Sample.dll", Permissions = approved ?? Both,
        }, package);

        var store = new MemoryStore();
        var manager = new ModuleManager(store, new ExtractingStager(Path.Combine(work, "modules")), TimeProvider.System);
        var installed = await manager.InstallAsync(package, "10.0.0", new HashSet<string> { "petra" }, Cancellation).ConfigureAwait(false);
        Assert.True(installed.Success, installed.Message);
        var enabled = await manager.EnableAsync(CollectionsModule.ModuleId, Cancellation).ConfigureAwait(false);
        Assert.True(enabled.Success, enabled.Message);

        var gateway = new RecordingGateway();
        var runtime = new ModuleRuntime(store, new AssemblyModuleLoader(),
            () => new ModuleServiceReferences(gateway, null!, null!, null!), TimeProvider.System, (_, _) => { });
        if (startRuntime)
        {
            var results = await runtime.StartEnabledAsync(Cancellation).ConfigureAwait(false);
            Assert.Equal(expectStart, Assert.Single(results).Success);
        }

        return (runtime, gateway, store, enabled.Record!.DataDirectory);
    }

    private sealed record PostedCall(ModuleRequestContext Context, PostingProposal Proposal);

    private sealed record ReversedCall(ModuleRequestContext Context, Guid JournalId, Guid Key, string Reason);

    private sealed class RecordingGateway : IFinancialPostingGateway
    {
        public List<PostedCall> Posted { get; } = [];

        public List<ReversedCall> Reversed { get; } = [];

        public Task<PostingProposalResult> ValidateAsync(ModuleRequestContext context, PostingProposal proposal, CancellationToken cancellationToken) =>
            Task.FromResult(new PostingProposalResult(true, null, []));

        public Task<PostingReceipt> PostAsync(ModuleRequestContext context, PostingProposal proposal, CancellationToken cancellationToken)
        {
            Posted.Add(new PostedCall(context, proposal));
            return Task.FromResult(new PostingReceipt(Guid.NewGuid(), DateTimeOffset.UtcNow, Posted.Count, false));
        }

        public Task<PostingReceipt> ReverseAsync(ModuleRequestContext context, Guid journalId, Guid idempotencyKey, string reason, CancellationToken cancellationToken)
        {
            Reversed.Add(new ReversedCall(context, journalId, idempotencyKey, reason));
            return Task.FromResult(new PostingReceipt(Guid.NewGuid(), DateTimeOffset.UtcNow, 100 + Reversed.Count, false));
        }
    }

    private sealed class MemoryStore : IModuleStateStore
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

    /// <summary>Extracts the verified package into the module directory, like the production directory stager.</summary>
    private sealed class ExtractingStager(string root) : IModulePackageStager
    {
        public Task<StagingResult> StageAsync(string packagePath, ModuleManifest manifest, string fingerprint, CancellationToken cancellationToken)
        {
            var target = Path.Combine(root, manifest.Id, manifest.Version);
            Directory.CreateDirectory(target);
            ZipFile.ExtractToDirectory(packagePath, target, overwriteFiles: true);
            return Task.FromResult(new StagingResult(true, target, null));
        }

        public Task<bool> ApplyAsync(string dataDirectory, ModuleManifest manifest, CancellationToken cancellationToken) =>
            Task.FromResult(File.Exists(Path.Combine(dataDirectory, manifest.EntryAssembly)));
    }
}
