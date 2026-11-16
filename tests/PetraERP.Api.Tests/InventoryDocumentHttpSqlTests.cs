using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PetraERP.Api.Tests;

/// <summary>
/// Regression: the warehouse-operations read endpoints answer for an administrator over real HTTP. They used to fail with
/// accounting.write_scope_required because no operation was bound. Requires PETRAERP_SQL_TEST_SERVER.
/// </summary>
public sealed class InventoryDocumentHttpSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ListingAndBalancesWorkForAnAdministrator()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}/inventory";
            foreach (var path in new[] { "/documents", "/balances", "/movement-audit" })
            {
                using var request = host.WorkspaceRequest(HttpMethod.Get, root + path, token);
                using var response = await client.SendAsync(request, Cancellation).ConfigureAwait(true);
                var body = await response.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(true);
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {(int)response.StatusCode} {body}");
            }
        }
    }
}
