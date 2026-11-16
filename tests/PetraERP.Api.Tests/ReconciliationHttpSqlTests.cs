using System.Net;
using System.Text.Json;

namespace PetraERP.Api.Tests;

public sealed class ReconciliationHttpSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ExportUsesFinancialContractAndRejectsWrongBranch()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}/reconciliation";

            using var request = host.WorkspaceRequest(HttpMethod.Get, root + "/export/json", token);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            using var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Equal(host.CompanyId, report.RootElement.GetProperty("companyId").GetGuid());
            Assert.Equal(host.FiscalYearId, report.RootElement.GetProperty("fiscalYearId").GetGuid());
            var findings = report.RootElement.GetProperty("findings");
            Assert.True(findings.GetArrayLength() > 0);
            foreach (var finding in findings.EnumerateArray())
                foreach (var field in new[] { "sourceAmount", "calculatedAmount", "difference" })
                    Assert.Contains(finding.GetProperty(field).ValueKind, new[] { JsonValueKind.String, JsonValueKind.Null });

            var wrongBranch = $"/api/v1/companies/{host.CompanyId}/branches/{Guid.NewGuid()}/reconciliation/export/json";
            using var deniedRequest = host.WorkspaceRequest(HttpMethod.Get, wrongBranch, token);
            using var denied = await client.SendAsync(deniedRequest, TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.False(denied.IsSuccessStatusCode);

            using var missingWorkspace = ApiSqlHost.Authorized(HttpMethod.Get, root + "/export/json", token);
            using var rejected = await client.SendAsync(missingWorkspace, TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.False(rejected.IsSuccessStatusCode);
        }
    }
}
