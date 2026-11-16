#pragma warning disable CA1707

using Microsoft.EntityFrameworkCore;
using PetraERP.Infrastructure.Modules;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Modules.Abstractions;
using PetraERP.Modules.Host;
using PetraERP.Modules.Packaging;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// Module state must be durable and uniquely keyed, and a package must only be usable after it has been extracted
/// exactly as its manifest declared. These run against a disposable central database only.
/// </summary>
public sealed class ModuleRegistrySqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task Store_RoundTripsState_AndRejectsDuplicateModuleIdentity()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var options = new DbContextOptionsBuilder<SystemDbContext>()
                .UseSqlServer(database.ConnectionString)
                .Options;
            using (var migrate = new PetraDbContext(new DbContextOptionsBuilder<PetraDbContext>().UseSqlServer(database.ConnectionString).Options))
            {
                await migrate.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            }

            var context = new SystemDbContext(options);
            await using (context.ConfigureAwait(false))
            {
            var store = new ModuleRegistryStore(context, TimeProvider.System);

            var record = new ModuleRecord(
                "alpha", "موديول ألف", "petra", "1.0.0", 1, ModuleState.Installed,
                new string('a', 64), "/modules/alpha/1.0.0", DateTimeOffset.UtcNow, null,
                ["module.alpha.use", "module.alpha.print"],
                [new ModuleDependency("core-ext", "^1.0.0", Optional: true)],
                "None");

            await store.SaveAsync(record, Cancellation).ConfigureAwait(true);

            var loaded = await store.FindAsync("alpha", Cancellation).ConfigureAwait(true);
            Assert.NotNull(loaded);
            Assert.Equal(ModuleState.Installed, loaded!.State);
            Assert.Equal(record.DeclaredPermissions, loaded.DeclaredPermissions);
            Assert.Single(loaded.Dependencies);
            Assert.Equal("core-ext", loaded.Dependencies[0].ModuleId);
            Assert.True(loaded.Dependencies[0].Optional);

            var updated = loaded with { State = ModuleState.Enabled, LastError = null };
            await store.SaveAsync(updated, Cancellation).ConfigureAwait(true);
            Assert.Equal(1, await context.ModuleRegistry.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(ModuleState.Enabled, (await store.FindAsync("alpha", Cancellation).ConfigureAwait(true))!.State);

            // The module id is the durable identity; a second row claiming it must be refused by the database.
            await context.ModuleRegistry.AddAsync(new Infrastructure.Persistence.Registry.ModuleRegistryRow
            {
                Id = Guid.NewGuid(),
                ModuleId = "alpha",
                DisplayName = "تكرار",
                Publisher = "petra",
                Version = "9.9.9",
                ContractVersion = 1,
                State = (int)ModuleState.Installed,
                PackageFingerprint = new string('b', 64),
                DataDirectory = "/tmp",
                DeclaredPermissions = "[]",
                Dependencies = "[]",
                LicenseRequirement = "None",
            }, Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync(Cancellation)).ConfigureAwait(true);

            // The rejected insert is still tracked by this context; a delete must not resurrect it.
            context.ChangeTracker.Clear();
            await store.DeleteAsync("alpha", Cancellation).ConfigureAwait(true);
            Assert.Null(await store.FindAsync("alpha", Cancellation).ConfigureAwait(true));
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task Stager_ExtractsOnlyDeclaredFiles_AndInstallThroughTheRealStoreSurvivesRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "petra-install-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "module.dll"), "payload bytes");
        File.WriteAllText(Path.Combine(source, "content.txt"), "content bytes");
        var package = Path.Combine(root, "alpha.pmpkg");
        ModulePackageBuilder.Build(source, new ModuleManifest
        {
            Id = "alpha",
            DisplayName = "موديول ألف",
            Version = "1.0.0",
            ContractVersion = 1,
            MinCoreVersion = "10.0.0",
            Publisher = "petra",
            EntryAssembly = "module.dll",
            Permissions = ["module.alpha.use"],
            Dependencies = [],
        }, package);

        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using (var migrate = new PetraDbContext(new DbContextOptionsBuilder<PetraDbContext>().UseSqlServer(database.ConnectionString).Options))
            {
                await migrate.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            }

            var installRoot = Path.Combine(root, "installed");
            Guid moduleId;

            var context = new SystemDbContext(new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(database.ConnectionString).Options);
            await using (context.ConfigureAwait(false))
            {
                var manager = new ModuleManager(
                    new ModuleRegistryStore(context, TimeProvider.System),
                    new DirectoryModuleStager(installRoot, TimeProvider.System),
                    TimeProvider.System);

                var result = await manager.InstallAsync(package, "10.0.0", new HashSet<string>(StringComparer.Ordinal) { "petra" }, Cancellation)
                    .ConfigureAwait(true);
                Assert.True(result.Success, result.Message + " | " + string.Join(" | ", result.Issues.Select(i => $"{i.Failure}:{i.Detail}")));
                Assert.Equal(ModuleState.Installed, result.Record!.State);
                moduleId = (await context.ModuleRegistry.SingleAsync(r => r.ModuleId == "alpha", Cancellation).ConfigureAwait(true)).Id;

                // The live directory contains exactly what the manifest declared, plus the manifest itself.
                var live = Path.Combine(installRoot, "modules", "alpha", "1.0.0");
                Assert.True(File.Exists(Path.Combine(live, "module.dll")));
                var liveNames = Directory.EnumerateFiles(live, "*", SearchOption.AllDirectories)
                    .Select(static path => Path.GetFileName(path)!)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                Assert.Equal(["content.txt", "module.dll", "module.manifest.json"], liveNames);
                var stagedWithPayload = Directory.Exists(Path.Combine(installRoot, "staging"))
                    ? Directory.EnumerateDirectories(Path.Combine(installRoot, "staging"))
                        .Where(d => File.Exists(Path.Combine(d, "module.dll")))
                        .ToArray()
                    : [];
                Assert.Empty(stagedWithPayload);
            }

            // A new context is a new process: the state has to come back from the database unchanged.
            var reopened = new SystemDbContext(new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(database.ConnectionString).Options);
            await using (reopened.ConfigureAwait(false))
            {
                var store = new ModuleRegistryStore(reopened, TimeProvider.System);
                var persisted = await store.FindAsync("alpha", Cancellation).ConfigureAwait(true);
                Assert.NotNull(persisted);
                Assert.Equal(ModuleState.Installed, persisted!.State);
                Assert.Single(persisted.DeclaredPermissions);
                Assert.True(await reopened.ModuleRegistry.AnyAsync(r => r.Id == moduleId, Cancellation).ConfigureAwait(true));
            }
        }

        Directory.Delete(root, recursive: true);
    }
}
