using System.Net;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Security;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Tests;

public sealed partial class LedgerManagementHttpSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CostCenterHistoryUsesAuthenticatedActorBranchCorrelationPagingAndReadPermission()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            using var context = host.CreateContext();
            var company = host.CompanyId;
            var branch = host.BranchId;
            var root = $"/api/v1/companies/{company}/branches/{branch}/ledger/chart/cost-centers";
            var correlation = Guid.NewGuid(); var input = new CostCenterRequest { Code = "HQ", NameArabic = "إدارة دمشق", NameEnglish = null };
            var created = await SendAsync<CostCenterResponse>(host, client, HttpMethod.Post, root, admin.AccessToken, input, correlationId: correlation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, created.Status);
            var id = created.Body!.Id; var path = root + $"/{id}/audit";
            var first = await SendAsync<CostCenterHistoryPageResponse>(host, client, HttpMethod.Get, path + "?pageSize=1", admin.AccessToken).ConfigureAwait(true);
            var item = Assert.Single(first.Body!.Items);
            Assert.Null(item.Before); Assert.Equal("إدارة دمشق", item.After.NameArabic); Assert.Null(item.After.NameEnglish);
            Assert.Equal((branch, correlation, "مدير النظام"), (item.WorkingBranchId, item.CorrelationId, item.ActorNameArabic));
            var changed = await SendAsync<CostCenterResponse>(host, client, HttpMethod.Put, root + $"/{id}?expectedVersion={Uri.EscapeDataString(created.Body.Version)}",
                admin.AccessToken, input with { NameArabic = "الإدارة الرئيسية" }, correlationId: correlation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, changed.Status);
            var activated = await SendAsync<CostCenterResponse>(host, client, HttpMethod.Post, root + $"/{id}/activation", admin.AccessToken,
                new SetActiveRequest { ExpectedVersion = changed.Body!.Version, IsActive = false }, correlationId: correlation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, activated.Status);
            var newest = await SendAsync<CostCenterHistoryPageResponse>(host, client, HttpMethod.Get, path + "?pageSize=1", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(3, newest.Body!.TotalCount); Assert.Equal(CostCenterAuditAction.Deactivated, Assert.Single(newest.Body.Items).Action);
            Assert.All(newest.Body.Items, row => Assert.Equal((branch, correlation), (row.WorkingBranchId, row.CorrelationId)));
            foreach (var suffix in new[] { "?pageSize=201", "?page=0" })
                Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync<CostCenterHistoryPageResponse>(host, client, HttpMethod.Get, path + suffix, admin.AccessToken).ConfigureAwait(true)).Status);
            await CreateLimitedUserAsync(host, company, branch, PermissionCatalog.CostCentersView).ConfigureAwait(true);
            var viewer = await ApiSqlHost.LoginAsync(client, "limited", LimitedPassword).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, (await SendAsync<CostCenterHistoryPageResponse>(host, client, HttpMethod.Get, path, viewer.AccessToken).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<CostCenterResponse>(host, client, HttpMethod.Put, root + $"/{id}?expectedVersion={Uri.EscapeDataString(activated.Body!.Version)}", viewer.AccessToken, input).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<CostCenterHistoryPageResponse>(host, client, HttpMethod.Get,
                path.Replace(company.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal), viewer.AccessToken).ConfigureAwait(true)).Status);
            // Company-wide read permission still requires the route branch to be an authorized branch of the workspace
            // (the independent-workspace middleware validates company AND branch as the resolved financial scope).
            var unknownBranchRoot = root.Replace(branch.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync<CostCenterHistoryPageResponse>(host, client, HttpMethod.Get, unknownBranchRoot + $"/{id}/audit", viewer.AccessToken).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync<CostCenterResponse>(host, client, HttpMethod.Post, unknownBranchRoot, admin.AccessToken, input with { Code = "INVALID" }).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync<CostCenterHistoryPageResponse>(host, client, HttpMethod.Get, root + $"/{Guid.NewGuid()}/audit", viewer.AccessToken).ConfigureAwait(true)).Status);
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [security].[RolePermissions] WHERE [PermissionCode]=N'Ledger.CostCenters.View';", Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<CostCenterHistoryPageResponse>(host, client, HttpMethod.Get, path, viewer.AccessToken).ConfigureAwait(true)).Status);
        }
    }
}
