using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetraERP.Infrastructure.Modules;
using PetraERP.Modules.Abstractions;
using PetraERP.Modules.Host;
using PetraERP.Modules.Packaging;
using PetraERP.Modules.Sample;

namespace PetraERP.Api.Tests;

/// <summary>
/// An external module through the real API on SQL Server: installed from a package, enabled over HTTP, and used to record and
/// cancel a collection. The journal must be an ordinary posted entry tagged with the module, and a replay must not post twice.
/// </summary>
public sealed class ExternalModuleHttpSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ModuleInstalledByPackageIsEnabledOverHttpAndPostsAndCancelsARealJournal()
    {
        var root = Path.Combine(Path.GetTempPath(), "petra-http-modules-" + Guid.NewGuid().ToString("N"));
        using var publisherKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var host = await ApiSqlHost.CreateWithWorkspaceAsync(overrides: services => services.Configure<ModuleHostOptions>(o =>
        {
            o.Root = root;
            o.TrustedPublishers = ["petra"];
            // Production default: a package must be signed by a publisher whose public key the installation holds.
            o.RequireSignedPackages = true;
            o.PublisherKeys = new Dictionary<string, string> { ["petra"] = PackageSigning.ExportPublicKey(publisherKey) };
        })).ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;

            var (cash, revenue, currency, date) = await SeedAsync(host).ConfigureAwait(true);
            await InstallSamplePackageAsync(host, root, publisherKey).ConfigureAwait(true);

            _ = host.Factory.Services.GetRequiredService<ModuleRuntime>();
            var listedByManager = await host.Factory.Services.GetRequiredService<ModuleManager>().ListAsync(Cancellation).ConfigureAwait(true);
            Assert.Single(listedByManager);

            // Listed as installed but not running; enabling over HTTP starts it.
            var before = await ListAsync(host, client, token).ConfigureAwait(true);
            var listed = Assert.Single(before);
            Assert.False(listed.GetProperty("isRunning").GetBoolean());
            var enabled = await SendAsync(host, client, HttpMethod.Post, $"/api/v1/modules/{CollectionsModule.ModuleId}/enable", token, null).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, enabled.Status);
            Assert.True(enabled.Body.GetProperty("isRunning").GetBoolean());

            var path = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}/modules/{CollectionsModule.ModuleId}/commands/";
            var key = Guid.NewGuid();
            var record = new { arguments = new Dictionary<string, string>
            {
                ["amount"] = "75.25", ["date"] = date, ["reference"] = "HTTP-1", ["key"] = key.ToString("D"),
                ["cashAccount"] = cash.ToString("D"), ["revenueAccount"] = revenue.ToString("D"), ["currency"] = currency,
            } };
            var posted = await SendAsync(host, client, HttpMethod.Post, path + CollectionsModule.RecordCommand, token, record).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, posted.Status);
            Assert.True(posted.Body.GetProperty("success").GetBoolean(), posted.Body.GetProperty("message").GetString());
            var journalId = Guid.Parse(posted.Body.GetProperty("data").GetProperty("journalId").GetString()!);

            var replay = await SendAsync(host, client, HttpMethod.Post, path + CollectionsModule.RecordCommand, token, record).ConfigureAwait(true);
            Assert.True(replay.Body.GetProperty("success").GetBoolean(), replay.Body.GetProperty("message").GetString());
            Assert.Equal("true", replay.Body.GetProperty("data").GetProperty("replayed").GetString());
            Assert.Equal(journalId, Guid.Parse(replay.Body.GetProperty("data").GetProperty("journalId").GetString()!));

            using (var annual = host.AnnualContext())
            {
                var rows = await annual.Database.SqlQuery<string>($"SELECT [Description] AS [Value] FROM [ledger].[JournalEntries] WHERE [State] = 4").ToListAsync(Cancellation).ConfigureAwait(true);
                var entry = Assert.Single(rows);
                Assert.Contains("[module:" + CollectionsModule.ModuleId + "]", entry, StringComparison.Ordinal);
            }

            var cancelled = await SendAsync(host, client, HttpMethod.Post, path + CollectionsModule.CancelCommand, token,
                new { arguments = new Dictionary<string, string> { ["journalId"] = journalId.ToString("D"), ["key"] = Guid.NewGuid().ToString("D"), ["reason"] = "cancelled by test" } }).ConfigureAwait(true);
            Assert.True(cancelled.Body.GetProperty("success").GetBoolean(), cancelled.Body.GetProperty("message").GetString());
            using (var annual = host.AnnualContext())
            {
                var states = await annual.Database.SqlQuery<int>($"SELECT CAST([State] AS int) AS [Value] FROM [ledger].[JournalEntries] ORDER BY [State]").ToListAsync(Cancellation).ConfigureAwait(true);
                Assert.Equal([4, 5], states); // the reversal is Posted, the original is Reversed
            }

            // Disabling stops the module; its commands are refused afterwards.
            var disabled = await SendAsync(host, client, HttpMethod.Post, $"/api/v1/modules/{CollectionsModule.ModuleId}/disable", token, null).ConfigureAwait(true);
            Assert.False(disabled.Body.GetProperty("isRunning").GetBoolean());
            var refused = await SendAsync(host, client, HttpMethod.Post, path + CollectionsModule.RecordCommand, token, record).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.NotFound, refused.Status);
        }
    }

    private static async Task<(Guid Cash, Guid Revenue, string Currency, string Date)> SeedAsync(ApiSqlHost host)
    {
        using var annual = host.AnnualContext();
        var cash = Guid.NewGuid();
        var revenue = Guid.NewGuid();
        await annual.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[Accounts]([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive]) VALUES({cash},{host.CompanyId},N'1001',N'Cash',1,1,1,1,1)", Cancellation).ConfigureAwait(false);
        await annual.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[Accounts]([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive]) VALUES({revenue},{host.CompanyId},N'4001',N'Revenue',4,1,1,1,1)", Cancellation).ConfigureAwait(false);
        var currency = await annual.Database.SqlQuery<string>($"SELECT [BaseCurrencyCode] AS [Value] FROM [system].[Companies] WHERE [Id] = {host.CompanyId}").SingleAsync(Cancellation).ConfigureAwait(false);
        var start = await annual.Database.SqlQuery<DateOnly>($"SELECT [RangeStart] AS [Value] FROM [ledger].[FiscalYears] WHERE [Id] = {host.FiscalYearId}").SingleAsync(Cancellation).ConfigureAwait(false);
        return (cash, revenue, currency, start.AddDays(5).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task InstallSamplePackageAsync(ApiSqlHost host, string root, System.Security.Cryptography.ECDsa publisherKey)
    {
        var payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);
        File.Copy(typeof(CollectionsModule).Assembly.Location, Path.Combine(payload, "PetraERP.Modules.Sample.dll"));
        var package = Path.Combine(root, "sample.pmpkg");
        ModulePackageBuilder.Build(payload, new ModuleManifest
        {
            Id = CollectionsModule.ModuleId, DisplayName = "Collections", Version = "1.0.0", ContractVersion = ModuleIdentity.CurrentContractVersion,
            MinCoreVersion = "1.0.0", Publisher = "petra", EntryAssembly = "PetraERP.Modules.Sample.dll",
            Permissions = [CollectionsModule.PostPermission, CollectionsModule.ReversePermission],
        }, package, publisherKey);
        var manager = host.Factory.Services.GetRequiredService<ModuleManager>();
        var installed = await manager.InstallAsync(package, "10.0.0", new HashSet<string> { "petra" }, Cancellation).ConfigureAwait(false);
        Assert.True(installed.Success, installed.Message);
    }

    private static async Task<IReadOnlyList<JsonElement>> ListAsync(ApiSqlHost host, HttpClient client, string token)
    {
        var result = await SendAsync(host, client, HttpMethod.Get, "/api/v1/modules", token, null).ConfigureAwait(false);
        Assert.True(result.Status == HttpStatusCode.OK, result.Body.ToString());
        return result.Body.EnumerateArray().ToList();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(ApiSqlHost host, HttpClient client, HttpMethod method, string path, string token, object? body)
    {
        using var request = host.WorkspaceRequest(method, path, token, body is null ? null : JsonContent.Create(body, body.GetType(), options: ApiJson.Options));
        using var response = await client.SendAsync(request, Cancellation).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(false);
        using var document = JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        return (response.StatusCode, document.RootElement.Clone());
    }
}
