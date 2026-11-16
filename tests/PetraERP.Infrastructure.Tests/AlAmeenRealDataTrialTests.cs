#pragma warning disable CA1707

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.AlAmeenMigration.Reconciliation;
using PetraERP.Infrastructure.AlAmeenMigration;
using PetraERP.Application.Workspaces;
using PetraERP.Infrastructure.AlAmeenMigration.Mapping;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// Trial of the Al-Ameen engine against a real exported Al-Ameen database (Dardas). Explicit: it needs the export loaded by
/// tools/AlAmeenCsvLoader and PETRAERP_ALAMEEN_TRIAL_SOURCE set to its connection string. The outcome (counts and every
/// reconciliation check) is written to PETRAERP_ALAMEEN_TRIAL_OUT so the result is evidence, not an assertion of success.
/// </summary>
public sealed class AlAmeenRealDataTrialTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task Dardas_FastCutover_LoadsAndReconciles()
    {
        var source = Environment.GetEnvironmentVariable("PETRAERP_ALAMEEN_TRIAL_SOURCE");
        var outPath = Environment.GetEnvironmentVariable("PETRAERP_ALAMEEN_TRIAL_OUT");
        Assert.False(string.IsNullOrWhiteSpace(source), "Set PETRAERP_ALAMEEN_TRIAL_SOURCE.");
        Assert.False(string.IsNullOrWhiteSpace(outPath), "Set PETRAERP_ALAMEEN_TRIAL_OUT.");
        var year = int.Parse(Environment.GetEnvironmentVariable("PETRAERP_ALAMEEN_TRIAL_YEAR") ?? "2026", System.Globalization.CultureInfo.InvariantCulture);

        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync(new ProvisionCompanyInput(
                "DARDAS", "دردس", "Dardas", "SYP", year, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31))).ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);

            using var targetContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true);
            var company = await targetContext.Companies.SingleAsync(Cancellation).ConfigureAwait(true);
            var branch = await targetContext.Branches.SingleAsync(Cancellation).ConfigureAwait(true);
            var cutoff = (await targetContext.FiscalYears.SingleAsync(Cancellation).ConfigureAwait(true)).RangeEnd;

            var report = new List<string>();
            var snapshot = await new AlAmeenSourceSnapshotReader(source!, TimeProvider.System, "SYP")
                .CaptureAsync(cutoff, Cancellation).ConfigureAwait(true);
            report.Add($"snapshot accounts={snapshot.Accounts.Count} (posting={snapshot.Accounts.Count(a => a.FinalAccount != 0)}) currencies={string.Join(',', snapshot.Currencies.Select(c => c.Code))} parties={snapshot.Parties.Count} warehouses={snapshot.Warehouses.Count} items={snapshot.Items.Count} stock={snapshot.Stock.Count} vouchers={snapshot.Vouchers.Count} lines={snapshot.Vouchers.Sum(v => v.Lines.Count)} guarantee={snapshot.Guarantee}");
            report.Add($"stock rows with non-positive quantity (not postable): {snapshot.Stock.Count(s => s.Quantity <= 0m)}; zero-cost positive rows: {snapshot.Stock.Count(s => s.Quantity > 0m && s.UnitCost == 0m)}");
            report.Add($"source debit={snapshot.Vouchers.SelectMany(v => v.Lines).Sum(l => l.Debit)} credit={snapshot.Vouchers.SelectMany(v => v.Lines).Sum(l => l.Credit)}");

            var created = await new AlAmeenMasterDataProvisioner(fixture.Factory, TimeProvider.System)
                .ProvisionAsync(snapshot, company.Id, provisioned.FiscalYearId, branch.Id, fixture.Actor, Cancellation).ConfigureAwait(true);
            report.Add($"provisioned rows={created}");

            var identities = await new AlAmeenIdentityResolver(fixture.Factory, fixture.System, TimeProvider.System)
                .ResolveAsync(snapshot, company.Id, provisioned.FiscalYearId, branch.Id, persistBindings: true, Cancellation).ConfigureAwait(true);
            report.Add($"identities complete={identities.IsComplete} issues={identities.Issues.Count}");
            foreach (var issue in identities.Issues.Take(20))
                report.Add($"  issue {issue.Kind}:{issue.SourceCode}");

            try
            {
                if (identities.IsComplete)
                {
                    var result = await new AlAmeenFastCutoverService(fixture.Factory, TimeProvider.System).ExecuteAsync(
                        snapshot, identities, company.Id, branch.Id, provisioned.FiscalYearId, fixture.Actor, "SYP", Cancellation).ConfigureAwait(true);
                    report.Add($"cutover {JsonSerializer.Serialize(result)}");

                    var reconciliation = await new AlAmeenReconciliationRunner(fixture.Factory, TimeProvider.System).ReconcileAsync(
                        snapshot, identities, branch.Id, cutoff, AccountComparisonMode.Cutover, result.MigrationRunId, Cancellation).ConfigureAwait(true);
                    using var probe = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true);
                    var items = await probe.OpenItems.AsNoTracking().OrderByDescending(i => i.OriginalBaseCarryingAmount).Take(5).ToListAsync(Cancellation).ConfigureAwait(true);
                    foreach (var item in items)
                        report.Add($"openitem {item.CurrencyCode} amount={item.OriginalAmount} rate={item.ExchangeRate} base={item.OriginalBaseCarryingAmount}");
                    report.Add($"openitems total={await probe.OpenItems.CountAsync(Cancellation).ConfigureAwait(true)}");
                    report.Add($"reconciliation {JsonSerializer.Serialize(reconciliation, Indented)}");
                }
            }
            catch (Exception ex)
            {
                for (var inner = ex; inner is not null; inner = inner.InnerException)
                    report.Add($"FAILED {inner.GetType().Name}: {inner.Message}");
                report.Add(ex.StackTrace ?? string.Empty);
            }

            await File.WriteAllLinesAsync(outPath!, report, Cancellation).ConfigureAwait(true);
        }
    }
}
