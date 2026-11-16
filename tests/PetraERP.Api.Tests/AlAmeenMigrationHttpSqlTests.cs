#pragma warning disable CA1707

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PetraERP.Application.AlAmeenMigration;
using PetraERP.Application.Workspaces;
using PetraERP.Infrastructure.AlAmeenMigration;

namespace PetraERP.Api.Tests;

/// <summary>
/// The Al-Ameen import driven exactly as the desktop window drives it: administrator login, preview, execute with the
/// administrator password, reconcile and activate, all over HTTP against a real central database and a company that
/// already exists. Explicit: it needs the Dardas exports loaded by tools/AlAmeenCsvLoader. The source connection strings
/// are PETRAERP_ALAMEEN_API_SOURCE_2025 / _2026 and the evidence file is PETRAERP_ALAMEEN_TRIAL_OUT.
/// </summary>
public sealed class AlAmeenMigrationHttpSqlTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task Dardas_BothYears_ImportReconcileAndActivateOverHttp()
    {
        var source2025 = Environment.GetEnvironmentVariable("PETRAERP_ALAMEEN_API_SOURCE_2025");
        var source2026 = Environment.GetEnvironmentVariable("PETRAERP_ALAMEEN_API_SOURCE_2026");
        var outPath = Environment.GetEnvironmentVariable("PETRAERP_ALAMEEN_TRIAL_OUT");
        Assert.False(string.IsNullOrWhiteSpace(source2025) || string.IsNullOrWhiteSpace(source2026) || string.IsNullOrWhiteSpace(outPath),
            "Set PETRAERP_ALAMEEN_API_SOURCE_2025, PETRAERP_ALAMEEN_API_SOURCE_2026 and PETRAERP_ALAMEEN_TRIAL_OUT.");

        var host = await ApiSqlHost.CreateAsync(overrides: services =>
            services.AddSingleton(new AlAmeenMigrationOptions { ReadsEnabled = true, WritesEnabled = true, BaseCurrencyCode = "SYP" })).ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            // The company exists before any import (a dummy opening year); each Al-Ameen year is then imported as its own year.
            Guid companyId;
            using (var scope = host.Factory.Services.CreateScope())
            {
                var provisioning = scope.ServiceProvider.GetRequiredService<ICompanyProvisioningService>();
                var company = await provisioning.ProvisionCompanyAsync(
                    new ProvisionCompanyInput("DARDAS", "Dardas", "Dardas", "SYP", 2024, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)),
                    host.UserId, Cancellation).ConfigureAwait(true);
                Assert.True(company.Succeeded, company.Error);
                await host.Database.RegisterAnnualDatabaseAsync(company.DatabaseName).ConfigureAwait(true);
                companyId = company.CompanyId;
            }

            using var client = host.Factory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(30); // a historical replay is long-running by design
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

            // PETRAERP_ALAMEEN_API_MODE=FullHistorical replays every voucher instead of posting the cutover balances.
            var mode = string.Equals(Environment.GetEnvironmentVariable("PETRAERP_ALAMEEN_API_MODE"), "FullHistorical", StringComparison.OrdinalIgnoreCase) ? MigrationMode.FullHistorical : MigrationMode.FastCutover;
            // PETRAERP_ALAMEEN_API_CUTOFF_DAY=n limits the import to 1 January..n January of each year (a bounded replay).
            int? cutoffDay = int.TryParse(Environment.GetEnvironmentVariable("PETRAERP_ALAMEEN_API_CUTOFF_DAY"), out var day) ? day : null;
            var report = new List<string> { "mode " + mode };
            foreach (var (year, source) in new[] { (2025, source2025!), (2026, source2026!) })
            {
                report.Add($"===== year {year}");
                var request = new MigrationPreviewRequest(source, companyId, "FY" + year, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), "SYP", mode, cutoffDay is null ? null : new DateOnly(year, 1, cutoffDay.Value));
                var preview = await PostAsync<MigrationPreviewResponse>(client, "/api/v1/migration/alameen/preview", request).ConfigureAwait(true);
                report.Add("preview " + JsonSerializer.Serialize(preview, Indented));
                Assert.NotEqual(Guid.Empty, preview.MigrationRunId);
                Assert.DoesNotContain(preview.ValidationIssues, i => i.Contains("blocking", StringComparison.OrdinalIgnoreCase));

                // Execution is refused without the administrator password and with a wrong one.
                var withoutPassword = await client.PostAsJsonAsync(new Uri("/api/v1/migration/alameen/execute", UriKind.Relative),
                    new MigrationExecuteRequest(preview.MigrationRunId, string.Empty, null), ApiJson.Options, Cancellation).ConfigureAwait(true);
                Assert.False(withoutPassword.IsSuccessStatusCode, "an empty password must not execute: " + withoutPassword.StatusCode);
                var wrongPassword = await client.PostAsJsonAsync(new Uri("/api/v1/migration/alameen/execute", UriKind.Relative),
                    new MigrationExecuteRequest(preview.MigrationRunId, "not-the-password", null), ApiJson.Options, Cancellation).ConfigureAwait(true);
                Assert.False(wrongPassword.IsSuccessStatusCode, "wrong password must not execute");
                report.Add($"password guards: empty={(int)withoutPassword.StatusCode} wrong={(int)wrongPassword.StatusCode}");

                var executed = await PostAsync<MigrationExecuteResponse>(client, "/api/v1/migration/alameen/execute",
                    new MigrationExecuteRequest(preview.MigrationRunId, ApiSqlHost.AdminPassword, null)).ConfigureAwait(true);
                report.Add("execute " + JsonSerializer.Serialize(executed, Indented));
                await File.WriteAllLinesAsync(outPath!, report, Cancellation).ConfigureAwait(true); // keep the evidence even when the run is blocked
                Assert.True(executed.Success, executed.Message);

                var reconciliation = await client.GetFromJsonAsync<MigrationReconciliationResponse>(
                    new Uri($"/api/v1/migration/alameen/{preview.MigrationRunId}/reconciliation", UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
                report.Add("reconciliation " + JsonSerializer.Serialize(reconciliation, Indented));
                Assert.NotNull(reconciliation);
                Assert.True(reconciliation.Passed, string.Join(" | ", reconciliation.Checks.Where(c => !c.Passed).Select(c => $"{c.CheckName}:{c.Details}")));

                var activated = await PostAsync<MigrationActivationResponse>(client, "/api/v1/migration/alameen/activate",
                    new MigrationActivationRequest(preview.MigrationRunId, ApiSqlHost.AdminPassword)).ConfigureAwait(true);
                report.Add("activate " + JsonSerializer.Serialize(activated, Indented));
                Assert.True(activated.Success, activated.Message);
            }

            await File.WriteAllLinesAsync(outPath!, report, Cancellation).ConfigureAwait(true);
        }
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, ApiJson.Options, Cancellation).ConfigureAwait(true);
        var text = await response.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(true);
        Assert.True(response.IsSuccessStatusCode, $"{path} -> {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, ApiJson.Options)!;
    }
}
