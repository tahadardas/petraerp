#pragma warning disable CA1707

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.AlAmeenMigration;
using PetraERP.Application.AlAmeenMigration.Mapping;
using PetraERP.Application.AlAmeenMigration.Reconciliation;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.AlAmeenMigration;
using PetraERP.Infrastructure.AlAmeenMigration.Mapping;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;
using PetraERP.Infrastructure.Tests.Security;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.Tests;

public sealed class AlAmeenMigrationSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly Guid UsdCurrencyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CashAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CapitalAccountId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid InventoryAccountId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SupplierAccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid SupplierPartyId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid StoreId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid ItemId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid Voucher1Id = Guid.Parse("99999999-9999-9999-9999-999999999991");
    private static readonly Guid Voucher2Id = Guid.Parse("99999999-9999-9999-9999-999999999992");

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SchemaValidation_ValidAndCorruptSchemas_EvaluatedCorrectly()
    {
        var sourceDb = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (sourceDb.ConfigureAwait(true))
        {
            await SeedMockAlAmeenSchemaAsync(sourceDb.ConnectionString, Cancellation).ConfigureAwait(true);
            var validResult = await AlAmeenSourceSchemaValidator.ValidateSchemaAsync(sourceDb.ConnectionString, Cancellation).ConfigureAwait(true);

            Assert.True(validResult.IsValid);
            Assert.Empty(validResult.MissingTables);
            Assert.Contains("ac000", validResult.PresentTables);
            Assert.Contains("my000", validResult.PresentTables);
            Assert.Contains("ce000", validResult.PresentTables);
            Assert.Contains("en000", validResult.PresentTables);
        }

        var emptyDb = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (emptyDb.ConfigureAwait(true))
        {
            var invalidResult = await AlAmeenSourceSchemaValidator.ValidateSchemaAsync(emptyDb.ConnectionString, Cancellation).ConfigureAwait(true);

            Assert.False(invalidResult.IsValid);
            Assert.NotEmpty(invalidResult.MissingTables);
            Assert.Contains("ac000", invalidResult.MissingTables);
            Assert.Contains("en000", invalidResult.MissingTables);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SnapshotCapture_HonoursCutoffAndFixesRowCounts()
    {
        var sourceDb = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (sourceDb.ConfigureAwait(true))
        {
            await SeedMockAlAmeenSchemaAsync(sourceDb.ConnectionString, Cancellation).ConfigureAwait(true);
            await SeedMockAlAmeenDataAsync(sourceDb.ConnectionString, Cancellation).ConfigureAwait(true);

            var before = await new AlAmeenSourceSnapshotReader(sourceDb.ConnectionString, TimeProvider.System)
                .CaptureAsync(new DateOnly(2026, 2, 10), Cancellation).ConfigureAwait(true);
            var after = await new AlAmeenSourceSnapshotReader(sourceDb.ConnectionString, TimeProvider.System)
                .CaptureAsync(new DateOnly(2026, 12, 31), Cancellation).ConfigureAwait(true);

            // Only VCH-001 exists on or before 10 February; the cutoff must bound the ledger, not merely reorder it.
            Assert.Single(before.Vouchers);
            Assert.Equal(2, after.Vouchers.Count);
            Assert.Equal(10000m, before.Vouchers.Sum(v => v.Lines.Sum(l => l.Debit)));
            Assert.NotEqual(before.SourceFingerprint, after.SourceFingerprint);
            Assert.Equal(after.SourceFingerprint, (await new AlAmeenSourceSnapshotReader(sourceDb.ConnectionString, TimeProvider.System)
                .CaptureAsync(new DateOnly(2026, 12, 31), Cancellation).ConfigureAwait(true)).SourceFingerprint);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task IdentityResolver_ResolvesEveryCodeAndRefusesToGuessAUnit()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);

            var sourceDb = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
            await using (sourceDb.ConfigureAwait(true))
            {
                await SeedMockAlAmeenSchemaAsync(sourceDb.ConnectionString, Cancellation).ConfigureAwait(true);
                await SeedMockAlAmeenDataAsync(sourceDb.ConnectionString, Cancellation).ConfigureAwait(true);

                using var targetContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true);
                var company = await targetContext.Companies.SingleAsync(Cancellation).ConfigureAwait(true);
                var branch = await targetContext.Branches.SingleAsync(Cancellation).ConfigureAwait(true);
                var year = await targetContext.FiscalYears.SingleAsync(Cancellation).ConfigureAwait(true);

                var snapshot = await new AlAmeenSourceSnapshotReader(sourceDb.ConnectionString, TimeProvider.System)
                    .CaptureAsync(year.RangeEnd, Cancellation).ConfigureAwait(true);

                // Before provisioning nothing exists, so every code must be reported unresolved.
                var unresolved = await new AlAmeenIdentityResolver(fixture.Factory, fixture.System, TimeProvider.System)
                    .ResolveAsync(snapshot, company.Id, provisioned.FiscalYearId, branch.Id, persistBindings: false, Cancellation).ConfigureAwait(true);
                Assert.False(unresolved.IsComplete);
                Assert.Contains(unresolved.Issues, i => i.IssueKind == IdentityIssueKind.MissingFromTarget);

                await new AlAmeenMasterDataProvisioner(fixture.Factory, TimeProvider.System)
                    .ProvisionAsync(snapshot, company.Id, provisioned.FiscalYearId, branch.Id, fixture.Actor, Cancellation).ConfigureAwait(true);

                var resolved = await new AlAmeenIdentityResolver(fixture.Factory, fixture.System, TimeProvider.System)
                    .ResolveAsync(snapshot, company.Id, provisioned.FiscalYearId, branch.Id, persistBindings: true, Cancellation).ConfigureAwait(true);
                Assert.True(resolved.IsComplete, string.Join(" | ", resolved.Issues.Select(i => $"{i.Kind}:{i.SourceCode}:{i.Detail}")));
                Assert.Equal(7, resolved.BindingsOf(MigrationEntityKind.Account).Count);
                Assert.Single(resolved.BindingsOf(MigrationEntityKind.Party));
                Assert.Single(resolved.BindingsOf(MigrationEntityKind.Warehouse));
                Assert.Single(resolved.BindingsOf(MigrationEntityKind.Item));
                Assert.Equal("USD", Assert.Contains(AlAmeenIdentityGuids.UsdSourceId, resolved.CurrencyCodes));

                // The binding is durable, so a second run cannot silently resolve to a different object.
                var rerun = await new AlAmeenIdentityResolver(fixture.Factory, fixture.System, TimeProvider.System)
                    .ResolveAsync(snapshot, company.Id, provisioned.FiscalYearId, branch.Id, persistBindings: false, Cancellation).ConfigureAwait(true);
                Assert.True(rerun.IsComplete);
                Assert.Equal(resolved.Bindings.Count, rerun.Bindings.Count);
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task HistoricalReplay_IsIdempotentAndNeverWritesAPartialJournal()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            var sourceDb = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
            await using (sourceDb.ConfigureAwait(true))
            {
                await SeedMockAlAmeenSchemaAsync(sourceDb.ConnectionString, Cancellation).ConfigureAwait(true);
                await SeedMockAlAmeenDataAsync(sourceDb.ConnectionString, Cancellation).ConfigureAwait(true);

                using var targetContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true);
                var company = await targetContext.Companies.SingleAsync(Cancellation).ConfigureAwait(true);
                var branch = await targetContext.Branches.SingleAsync(Cancellation).ConfigureAwait(true);
                var cutoff = (await targetContext.FiscalYears.SingleAsync(Cancellation).ConfigureAwait(true)).RangeEnd;

                var snapshot = await new AlAmeenSourceSnapshotReader(sourceDb.ConnectionString, TimeProvider.System)
                    .CaptureAsync(cutoff, Cancellation).ConfigureAwait(true);
                await new AlAmeenMasterDataProvisioner(fixture.Factory, TimeProvider.System)
                    .ProvisionAsync(snapshot, company.Id, provisioned.FiscalYearId, branch.Id, fixture.Actor, Cancellation).ConfigureAwait(true);
                var identities = await new AlAmeenIdentityResolver(fixture.Factory, fixture.System, TimeProvider.System)
                    .ResolveAsync(snapshot, company.Id, provisioned.FiscalYearId, branch.Id, persistBindings: true, Cancellation).ConfigureAwait(true);

                var migrator = new AlAmeenHistoricalMigrator(fixture.Factory, fixture.System, TimeProvider.System);
                var first = await migrator.MigrateHistoryAsync(snapshot, identities, company.Id, branch.Id, provisioned.FiscalYearId, fixture.Actor, cancellationToken: Cancellation).ConfigureAwait(true);
                Assert.Equal(2, first.VouchersImported);
                Assert.Equal(0, first.VouchersBlocked);
                Assert.True(first.Success, first.Message);

                using (var after = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
                {
                    Assert.Equal(2, await after.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
                    Assert.Equal(2, await fixture.System.GlobalEntityMappings.CountAsync(m => m.EntityType == "ameen.doc.voucher", Cancellation).ConfigureAwait(true));
                    var sequences = await after.JournalEntries.Where(j => j.PostingSequence != null).Select(j => j.PostingSequence!.Value).ToListAsync(Cancellation).ConfigureAwait(true);
                    Assert.Equal(2, sequences.Count);
                }

                var second = await migrator.MigrateHistoryAsync(snapshot, identities, company.Id, branch.Id, provisioned.FiscalYearId, fixture.Actor, cancellationToken: Cancellation).ConfigureAwait(true);
                Assert.Equal(0, second.VouchersImported);
                Assert.Equal(2, second.VouchersSkippedExisting);
                using (var afterReplay = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
                {
                    Assert.Equal(2, await afterReplay.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
                }
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task HistoricalReplay_BlocksAnUnbalancedVoucher_WithARecordedReason()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            var sourceDb = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
            await using (sourceDb.ConfigureAwait(true))
            {
                await SeedMockAlAmeenSchemaAsync(sourceDb.ConnectionString, Cancellation).ConfigureAwait(true);
                await SeedMockAlAmeenDataAsync(sourceDb.ConnectionString, Cancellation).ConfigureAwait(true);
                await RunSourceSqlAsync(sourceDb.ConnectionString, @"
INSERT INTO [ce000] ([GUID], [Number], [Date], [CurrencyGUID], [CurrencyVal], [Notes])
VALUES (NEWID(), N'VCH-BAD', '2026-03-01', (SELECT TOP 1 [GUID] FROM [my000]), 1, N'unbalanced voucher');
INSERT INTO [en000] ([GUID], [Number], [ParentGUID], [AccountGUID], [CurrencyGUID], [CurrencyVal], [Debit], [Credit], [Notes])
SELECT NEWID(), 1, c.[GUID], (SELECT TOP 1 [GUID] FROM [ac000] WHERE [Code] = N'101'), c.[CurrencyGUID], 1, 500.0, 0.0, N'one-sided line'
FROM [ce000] c WHERE c.[Number] = N'VCH-BAD';
", Cancellation).ConfigureAwait(true);

                using var targetContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true);
                var company = await targetContext.Companies.SingleAsync(Cancellation).ConfigureAwait(true);
                var branch = await targetContext.Branches.SingleAsync(Cancellation).ConfigureAwait(true);
                var cutoff = (await targetContext.FiscalYears.SingleAsync(Cancellation).ConfigureAwait(true)).RangeEnd;

                var snapshot = await new AlAmeenSourceSnapshotReader(sourceDb.ConnectionString, TimeProvider.System)
                    .CaptureAsync(cutoff, Cancellation).ConfigureAwait(true);
                await new AlAmeenMasterDataProvisioner(fixture.Factory, TimeProvider.System)
                    .ProvisionAsync(snapshot, company.Id, provisioned.FiscalYearId, branch.Id, fixture.Actor, Cancellation).ConfigureAwait(true);
                var identities = await new AlAmeenIdentityResolver(fixture.Factory, fixture.System, TimeProvider.System)
                    .ResolveAsync(snapshot, company.Id, provisioned.FiscalYearId, branch.Id, persistBindings: true, Cancellation).ConfigureAwait(true);

                var result = await new AlAmeenHistoricalMigrator(fixture.Factory, fixture.System, TimeProvider.System)
                    .MigrateHistoryAsync(snapshot, identities, company.Id, branch.Id, provisioned.FiscalYearId, fixture.Actor, cancellationToken: Cancellation).ConfigureAwait(true);

                Assert.False(result.Success);
                Assert.Equal(2, result.VouchersImported);
                Assert.Equal(1, result.VouchersBlocked);
                var blocked = result.Documents.Single(d => d.Outcome == HistoricalDocumentOutcome.Blocked);
                Assert.NotNull(blocked.Reason);

                using var check = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true);
                Assert.Equal(2, await check.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(0, await check.JournalLines.CountAsync(l => l.TransactionDebit == 500m, Cancellation).ConfigureAwait(true));
            }
        }
    }

    private static class AlAmeenIdentityGuids
    {
        public static readonly Guid UsdSourceId = UsdCurrencyId;
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task FastCutover_AndReconciliation_MatchSourceAgainstTarget()
    {
        var run = await RunCutoverAsync().ConfigureAwait(true);
        await using (run.Fixture.ConfigureAwait(true))
        {
            Assert.True(run.Result.Success, run.Result.Message);
            Assert.Equal(7, run.Result.AccountsImported); // 4 posting accounts + 3 chart headings
            Assert.Equal(1, run.Result.ItemsImported);
            Assert.NotEqual(Guid.Empty, run.Result.OpeningBalanceBatchId);
            Assert.Equal(12000m, run.Result.TotalOpeningBalance);
            Assert.Equal(2000m, run.Result.StockValuationTotal);

            Assert.True(run.Reconciliation.Passed, string.Join(" | ", run.Reconciliation.Checks.Where(c => !c.Passed).Select(c => $"{c.CheckName}:{c.Details}")));
            Assert.Equal("0.0000", run.Reconciliation.TrialBalanceDifference);
            Assert.Equal("0.0000", run.Reconciliation.InventoryDifference);
            Assert.Equal("0.0000", run.Reconciliation.ArDifference);
            Assert.Equal("0.0000", run.Reconciliation.ApDifference);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task Reconciliation_ReportsTheDifference_WhenTheTargetDoesNotMatchTheCapture()
    {
        var run = await RunCutoverAsync().ConfigureAwait(true);
        var fixture = run.Fixture;
        await using (fixture.ConfigureAwait(true))
        {
            Assert.True(run.Reconciliation.Passed);

            // Break one migrated payable inside the disposable database and re-compare the same frozen capture.
            // Raw SQL, deliberately bypassing the guarded write path: this simulates target data that no longer agrees
            // with the frozen capture, which is precisely what a reconciliation must catch.
            using (var tamper = await fixture.Factory.CreateDbContextAsync(run.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                await tamper.Database.ExecuteSqlRawAsync("UPDATE parties.OpenItems SET OriginalAmount = OriginalAmount + 250.0000, OutstandingAmount = OutstandingAmount + 250.0000, CurrentBaseCarryingAmount = CurrentBaseCarryingAmount + 250.0000", Cancellation).ConfigureAwait(true);
            }

            var after = await new AlAmeenReconciliationRunner(fixture.Factory, TimeProvider.System).ReconcileAsync(
                run.Snapshot, run.Identities, run.Identities.BranchId, run.Cutoff, AccountComparisonMode.Cutover, run.FiscalYearId, Cancellation).ConfigureAwait(true);

            Assert.False(after.Passed);
            Assert.NotEqual("0.0000", after.ApDifference);
            var ap = after.Checks.Single(c => c.CheckName == "PAYABLES");
            Assert.False(ap.Passed);
            Assert.Contains("AMIN-PARTY-SUP-01", ap.Details, StringComparison.Ordinal);

            // Restoring the value must return a clean comparison, proving the check measures the data and not a constant.
            using (var restore = await fixture.Factory.CreateDbContextAsync(run.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                await restore.Database.ExecuteSqlRawAsync("UPDATE parties.OpenItems SET OriginalAmount = OriginalAmount - 250.0000, OutstandingAmount = OutstandingAmount - 250.0000, CurrentBaseCarryingAmount = CurrentBaseCarryingAmount - 250.0000", Cancellation).ConfigureAwait(true);
            }

            var restored = await new AlAmeenReconciliationRunner(fixture.Factory, TimeProvider.System).ReconcileAsync(
                run.Snapshot, run.Identities, run.Identities.BranchId, run.Cutoff, AccountComparisonMode.Cutover, run.FiscalYearId, Cancellation).ConfigureAwait(true);
            Assert.True(restored.Passed, string.Join(" | ", restored.Checks.Where(c => !c.Passed).Select(c => $"{c.CheckName}:{c.Details}")));
        }
    }

    private sealed record CutoverRun(
        IndependentAccountingSqlFixture Fixture,
        Guid WorkspaceId,
        Guid FiscalYearId,
        SourceSnapshot Snapshot,
        IdentityResolution Identities,
        DateOnly Cutoff,
        FastCutoverResult Result,
        MigrationReconciliationResponse Reconciliation);

    private static async Task<CutoverRun> RunCutoverAsync()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
        var sourceDb = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);

        await SeedMockAlAmeenSchemaAsync(sourceDb.ConnectionString, Cancellation).ConfigureAwait(true);
        await SeedMockAlAmeenDataAsync(sourceDb.ConnectionString, Cancellation).ConfigureAwait(true);

        using var targetContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true);
        var company = await targetContext.Companies.SingleAsync(Cancellation).ConfigureAwait(true);
        var branch = await targetContext.Branches.SingleAsync(Cancellation).ConfigureAwait(true);
        var cutoff = (await targetContext.FiscalYears.SingleAsync(Cancellation).ConfigureAwait(true)).RangeEnd;

        var snapshot = await new AlAmeenSourceSnapshotReader(sourceDb.ConnectionString, TimeProvider.System)
            .CaptureAsync(cutoff, Cancellation).ConfigureAwait(true);
        await new AlAmeenMasterDataProvisioner(fixture.Factory, TimeProvider.System)
            .ProvisionAsync(snapshot, company.Id, provisioned.FiscalYearId, branch.Id, fixture.Actor, Cancellation).ConfigureAwait(true);

        var identities = await new AlAmeenIdentityResolver(fixture.Factory, fixture.System, TimeProvider.System)
            .ResolveAsync(snapshot, company.Id, provisioned.FiscalYearId, branch.Id, persistBindings: true, Cancellation).ConfigureAwait(true);
        Assert.True(identities.IsComplete, string.Join(" | ", identities.Issues.Select(i => $"{i.Kind}:{i.SourceCode}")));

        var result = await new AlAmeenFastCutoverService(fixture.Factory, TimeProvider.System).ExecuteAsync(
            snapshot, identities, company.Id, branch.Id, provisioned.FiscalYearId, fixture.Actor, "USD", Cancellation).ConfigureAwait(true);

        var reconciliation = await new AlAmeenReconciliationRunner(fixture.Factory, TimeProvider.System).ReconcileAsync(
            snapshot, identities, branch.Id, cutoff, AccountComparisonMode.Cutover, result.MigrationRunId, Cancellation).ConfigureAwait(true);

        await sourceDb.DisposeAsync().ConfigureAwait(true);
        return new CutoverRun(fixture, provisioned.WorkspaceId, provisioned.FiscalYearId, snapshot, identities, cutoff, result, reconciliation);
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MigrationGuards_BlockPreviewExecuteAndActivate()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);

            var confirmation = new SqlAdministratorConfirmation(
                new CentralDataContext(fixture.System),
                SecuritySqlFixture.Hasher,
                fixture.Authority,
                TimeProvider.System);

            var coordinator = new AlAmeenMigrationCoordinator(
                fixture.System,
                fixture.Factory,
                fixture.Service,
                confirmation,
                TimeProvider.System);

            var previewFailure = await Assert.ThrowsAsync<BusinessRuleException>(() => coordinator.PreviewAsync(
                new MigrationPreviewRequest(
                    "Server=unused;Database=unused;Integrated Security=true;",
                    provisioned.CompanyId,
                    "FY2027",
                    new DateOnly(2027, 1, 1),
                    new DateOnly(2027, 12, 31),
                    "USD",
                    MigrationMode.FastCutover),
                fixture.Actor,
                Cancellation)).ConfigureAwait(true);

            Assert.Equal("migration.read_blocked", previewFailure.Code);

            var executeFailure = await Assert.ThrowsAsync<BusinessRuleException>(() => coordinator.ExecuteAsync(
                new MigrationExecuteRequest(Guid.NewGuid(), SecuritySqlFixture.AdminPassword), fixture.Actor, Cancellation)).ConfigureAwait(true);
            Assert.Equal("migration.write_blocked", executeFailure.Code);

            var activateFailure = await Assert.ThrowsAsync<BusinessRuleException>(() => coordinator.ActivateAsync(
                new MigrationActivationRequest(Guid.NewGuid(), SecuritySqlFixture.AdminPassword), fixture.Actor, Cancellation)).ConfigureAwait(true);
            Assert.Equal("migration.write_blocked", activateFailure.Code);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MigrationRunRegistry_PersistsPreviewStateAndRejectsForeignOwner()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);

            var coordinator = new AlAmeenMigrationCoordinator(
                fixture.System,
                fixture.Factory,
                fixture.Service,
                new SqlAdministratorConfirmation(new CentralDataContext(fixture.System), SecuritySqlFixture.Hasher, fixture.Authority, TimeProvider.System),
                TimeProvider.System);

            // Preview is guarded, so the durable row is written directly to prove the registry contract itself.
            var runId = Guid.NewGuid();
            fixture.System.MigrationRuns.Add(new PetraERP.Infrastructure.Persistence.Registry.MigrationRunRow
            {
                Id = runId,
                CompanyId = provisioned.CompanyId,
                Mode = 1,
                State = 5,
                CutoffDate = new DateOnly(2026, 6, 30),
                SourceFingerprint = new string('a', 64),
                SourceDatabaseName = "ALAMEEN_SRC",
                IdentityIssueCount = 0,
                CreatedByUserId = fixture.Actor,
            });
            await fixture.System.SaveChangesAsync(Cancellation).ConfigureAwait(true);

            var owner = await fixture.System.MigrationRuns.SingleAsync(r => r.Id == runId, Cancellation).ConfigureAwait(true);
            Assert.Equal(5, owner.State);
            Assert.Null(owner.FiscalYearId);
            Assert.Null(owner.ReconciliationPassed);

            // Activation cannot be reached from a state that was never reconciled: the row has no PASS evidence.
            Assert.Null(owner.ReconciliationPassed);
            Assert.NotEqual((int)MigrationState.Activated, owner.State);

            // The guard still refuses the real entry points, so nothing here opens the write path.
            await Assert.ThrowsAsync<BusinessRuleException>(() => coordinator.ActivateAsync(
                new MigrationActivationRequest(runId, SecuritySqlFixture.AdminPassword), fixture.Actor, Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => coordinator.ReconcileAsync(runId, Guid.NewGuid(), Cancellation)).ConfigureAwait(true);
        }
    }

    // The mock mirrors the column names of a real Al-Ameen export (verified against the Dardas 2025/2026 databases):
    // [Code]/[Name] on accounts, [Type] = 1 for posting-chart rows, [CurrencyVal] as the rate, [Notes], [Qty], [AvgPrice].
    private static async Task SeedMockAlAmeenSchemaAsync(string connectionString, CancellationToken ct)
    {
        await RunSourceSqlAsync(connectionString, @"
CREATE TABLE [ac000] (
    [GUID] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Number] INT NOT NULL DEFAULT 0,
    [Code] NVARCHAR(50) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [ParentGUID] UNIQUEIDENTIFIER NULL,
    [Type] INT NOT NULL DEFAULT 1
);

CREATE TABLE [cu000] (
    [GUID] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Number] NVARCHAR(50) NOT NULL,
    [CustomerName] NVARCHAR(200) NOT NULL,
    [AccountGUID] UNIQUEIDENTIFIER NULL,
    [Phone1] NVARCHAR(50) NULL,
    [Address] NVARCHAR(200) NULL
);

CREATE TABLE [mt000] (
    [GUID] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Number] INT NOT NULL DEFAULT 0,
    [Code] NVARCHAR(50) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [GroupGUID] UNIQUEIDENTIFIER NULL,
    [BarCode] NVARCHAR(50) NULL,
    [Unity] NVARCHAR(50) NOT NULL DEFAULT N'unit',
    [Retail] DECIMAL(18, 4) NOT NULL DEFAULT 0,
    [AvgPrice] DECIMAL(18, 4) NOT NULL DEFAULT 0,
    [LastPrice] DECIMAL(18, 4) NOT NULL DEFAULT 0
);

CREATE TABLE [st000] (
    [GUID] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Number] INT NOT NULL DEFAULT 0,
    [Code] NVARCHAR(50) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [AccountGUID] UNIQUEIDENTIFIER NULL
);

CREATE TABLE [my000] (
    [GUID] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Number] INT NOT NULL DEFAULT 0,
    [Code] NVARCHAR(50) NOT NULL,
    [Name] NVARCHAR(50) NOT NULL,
    [CurrencyVal] FLOAT NOT NULL DEFAULT 1,
    [PartPrecision] INT NOT NULL DEFAULT 100
);

CREATE TABLE [ce000] (
    [GUID] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Number] NVARCHAR(50) NOT NULL,
    [Date] DATETIME2 NOT NULL,
    [CurrencyGUID] UNIQUEIDENTIFIER NULL,
    [CurrencyVal] FLOAT NOT NULL DEFAULT 1,
    [Notes] NVARCHAR(500) NULL
);

CREATE TABLE [en000] (
    [GUID] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Number] INT NOT NULL DEFAULT 0,
    [ParentGUID] UNIQUEIDENTIFIER NOT NULL,
    [AccountGUID] UNIQUEIDENTIFIER NOT NULL,
    [CurrencyGUID] UNIQUEIDENTIFIER NULL,
    [CurrencyVal] FLOAT NOT NULL DEFAULT 1,
    [Debit] FLOAT NOT NULL DEFAULT 0,
    [Credit] FLOAT NOT NULL DEFAULT 0,
    [Notes] NVARCHAR(500) NULL,
    [CostGUID] UNIQUEIDENTIFIER NULL
);

CREATE TABLE [bt000] ([GUID] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [Name] NVARCHAR(50) NOT NULL);
CREATE TABLE [bu000] ([GUID] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [Number] NVARCHAR(50) NOT NULL);
CREATE TABLE [bi000] ([GUID] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [Number] NVARCHAR(50) NOT NULL);
CREATE TABLE [co000] ([GUID] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [Name] NVARCHAR(200) NOT NULL);

CREATE TABLE [ms000] (
    [GUID] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [MatGUID] UNIQUEIDENTIFIER NOT NULL,
    [StoreGUID] UNIQUEIDENTIFIER NOT NULL,
    [Qty] FLOAT NOT NULL DEFAULT 0,
    PRIMARY KEY ([MatGUID], [StoreGUID])
);
", ct).ConfigureAwait(true);
    }

    // Chart layout follows the real export: root 1 = assets, root 2 = liabilities, root 10 = equity; postings sit on leaves.
    private const string AssetsRootId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
    private const string LiabilitiesRootId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2";
    private const string EquityRootId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3";

    private static async Task SeedMockAlAmeenDataAsync(string connectionString, CancellationToken ct)
        => await RunSourceSqlAsync(connectionString, $@"
INSERT INTO [my000] ([GUID], [Number], [Code], [Name], [CurrencyVal], [PartPrecision])
VALUES ('{UsdCurrencyId}', 1, N'USD', N'US Dollar', 1, 100);

INSERT INTO [ac000] ([GUID], [Number], [Code], [Name], [ParentGUID], [Type]) VALUES
('{AssetsRootId}', 1, N'1', N'Assets', NULL, 1),
('{LiabilitiesRootId}', 2, N'2', N'Liabilities', NULL, 1),
('{EquityRootId}', 3, N'10', N'Equity', NULL, 1),
('{CashAccountId}', 4, N'101', N'Cash', '{AssetsRootId}', 1),
('{CapitalAccountId}', 5, N'301', N'Capital', '{EquityRootId}', 1),
('{InventoryAccountId}', 6, N'131', N'Merchandise inventory', '{AssetsRootId}', 1),
('{SupplierAccountId}', 7, N'221', N'Suppliers', '{LiabilitiesRootId}', 1);

INSERT INTO [cu000] ([GUID], [Number], [CustomerName], [AccountGUID], [Phone1], [Address])
VALUES ('{SupplierPartyId}', N'SUP-01', N'Main supplier', '{SupplierAccountId}', N'0790000000', N'Amman');

INSERT INTO [st000] ([GUID], [Number], [Code], [Name], [AccountGUID])
VALUES ('{StoreId}', 1, N'WH-01', N'Main warehouse', '{InventoryAccountId}');

INSERT INTO [mt000] ([GUID], [Number], [Code], [Name], [GroupGUID], [BarCode], [Unity], [Retail], [AvgPrice], [LastPrice])
VALUES ('{ItemId}', 1, N'ITM-01', N'Test item', NULL, N'62810001', N'unit', 250.0, 200.0, 200.0);

INSERT INTO [ms000] ([MatGUID], [StoreGUID], [Qty])
VALUES ('{ItemId}', '{StoreId}', 10.0);

INSERT INTO [ce000] ([GUID], [Number], [Date], [CurrencyGUID], [CurrencyVal], [Notes]) VALUES
('{Voucher1Id}', N'VCH-001', '2026-02-10', '{UsdCurrencyId}', 1, N'Capital deposit'),
('{Voucher2Id}', N'VCH-002', '2026-02-15', '{UsdCurrencyId}', 1, N'Inventory purchase on credit');

INSERT INTO [en000] ([GUID], [Number], [ParentGUID], [AccountGUID], [CurrencyGUID], [CurrencyVal], [Debit], [Credit], [Notes]) VALUES
(NEWID(), 1, '{Voucher1Id}', '{CashAccountId}', '{UsdCurrencyId}', 1, 10000.0, 0.0, N'Cash received'),
(NEWID(), 2, '{Voucher1Id}', '{CapitalAccountId}', '{UsdCurrencyId}', 1, 0.0, 10000.0, N'Capital'),
(NEWID(), 1, '{Voucher2Id}', '{InventoryAccountId}', '{UsdCurrencyId}', 1, 2000.0, 0.0, N'Inventory'),
(NEWID(), 2, '{Voucher2Id}', '{SupplierAccountId}', '{UsdCurrencyId}', 1, 0.0, 2000.0, N'Payable');
", ct).ConfigureAwait(true);

    private static async Task RunSourceSqlAsync(string connectionString, string sql, CancellationToken ct)
    {
        var conn = new SqlConnection(connectionString);
        await using (conn.ConfigureAwait(true))
        {
            await conn.OpenAsync(ct).ConfigureAwait(true);
            var cmd = conn.CreateCommand();
            await using (cmd.ConfigureAwait(true))
            {
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(true);
            }
        }
    }
}
