using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PetraERP.Contracts.Workspaces;
using PetraERP.Infrastructure.Tests;

namespace PetraERP.Api.Tests;

public sealed class WorkspaceListHttpSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task UnregisteredLegacyYearsAreNotWorkspacesAndOnlyCatalogCurrenciesAreAccepted()
    {
        var host = await ApiSqlHost.CreateAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            Guid companyId;
            using (var context = host.CreateContext())
                (companyId, _, _) = await LedgerSeed.CalendarAsync(context).ConfigureAwait(true);
            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);

            // A legacy fiscal year without an independent database registration is not an authorized workspace.
            using var list = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/me/workspaces", admin.AccessToken);
            using var listResponse = await client.SendAsync(list, Cancellation).ConfigureAwait(true);
            listResponse.EnsureSuccessStatusCode();
            var workspaces = await listResponse.Content.ReadFromJsonAsync<WorkspaceListResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.DoesNotContain(workspaces!.Items, w => w.CompanyId == companyId);

            using var currencies = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/workspaces/currencies", admin.AccessToken);
            using var currencyResponse = await client.SendAsync(currencies, Cancellation).ConfigureAwait(true);
            currencyResponse.EnsureSuccessStatusCode();
            var supported = await currencyResponse.Content.ReadFromJsonAsync<SupportedCurrencyListResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
            foreach (var code in new[] { "SYP", "USD", "EUR", "JOD", "SAR" }) Assert.Contains(supported!.Items, c => c.Code == code);

            // A typed, non-catalog currency is rejected with a stable code instead of a database foreign-key failure.
            using var create = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/workspaces/companies", admin.AccessToken, JsonContent.Create(new CreateCompanyWorkspaceRequest
            {
                Code = "BADCUR", NameArabic = "شركة اختبار", BaseCurrencyCode = "LYR", InitialYear = 2026,
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
                AdministratorPassword = ApiSqlHost.AdminPassword,
            }, options: ApiJson.Options));
            using var createResponse = await client.SendAsync(create, Cancellation).ConfigureAwait(true);
            Assert.False(createResponse.IsSuccessStatusCode);
            Assert.NotEqual(HttpStatusCode.InternalServerError, createResponse.StatusCode);
            using var problem = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal("company.currency_unsupported", problem.RootElement.GetProperty("code").GetString());
        }
    }
}
