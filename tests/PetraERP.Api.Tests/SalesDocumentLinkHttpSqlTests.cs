using System.Net;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Security;
using PetraERP.Contracts.Sales;
using PetraERP.Infrastructure.Parties;

namespace PetraERP.Api.Tests;

public sealed partial class LedgerManagementHttpSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SalesLinkHttpPoliciesValidateBothSourceAndDestinationAndNeverLeakForeignScope()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            using var db = host.CreateContext();
            var company = host.CompanyId;
            var branch = host.BranchId;
            var root = $"/api/v1/companies/{company}/branches/{branch}/sales/links";
            await CreateLimitedUserAsync(host, company, branch, PermissionCatalog.SalesInvoicesView).ConfigureAwait(true);
            var viewer = await ApiSqlHost.LoginAsync(client, "limited", LimitedPassword).ConfigureAwait(true);
            var limitedRoleId = await db.Roles.Where(r => r.Code == "LIMITED").Select(r => r.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var sources = new[] { ("customers", PermissionCatalog.PartiesView), ("items", PermissionCatalog.InventoryItemsView),
                ("journals", PermissionCatalog.JournalsView), ("movements", PermissionCatalog.InventoryMovementsView), ("invoices", PermissionCatalog.InventoryMovementsView) };
            foreach (var (path, permission) in sources)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [security].[RolePermissions] WHERE [RoleId]={limitedRoleId}", Cancellation).ConfigureAwait(true);
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) VALUES({limitedRoleId},{PermissionCatalog.SalesInvoicesView})", Cancellation).ConfigureAwait(true);
                var url = root + $"/{path}/{Guid.NewGuid()}";
                Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<SalesDocumentLinkPageResponse>(host, client, HttpMethod.Get, url, viewer.AccessToken).ConfigureAwait(true)).Status);
                var missing = await SendAsync<SalesDocumentLinkPageResponse>(host, client, HttpMethod.Get, url, admin.AccessToken).ConfigureAwait(true);
                Assert.True(missing.Status == HttpStatusCode.NotFound, $"{path}: {missing.Status}, {missing.Code}");
                foreach (var query in new[] { "?page=0", "?pageSize=0", "?pageSize=201", "?page=2147483647&pageSize=200" })
                    Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync<SalesDocumentLinkPageResponse>(host, client, HttpMethod.Get, url + query, admin.AccessToken).ConfigureAwait(true)).Status);
                var foreign = url.Replace(branch.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);
                Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<SalesDocumentLinkPageResponse>(host, client, HttpMethod.Get, foreign, admin.AccessToken).ConfigureAwait(true)).Status);
                var foreignCompany = url.Replace(company.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);
                Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<SalesDocumentLinkPageResponse>(host, client, HttpMethod.Get, foreignCompany, admin.AccessToken).ConfigureAwait(true)).Status);
                // Grant only source permission: Sales view remains independently required.
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [security].[RolePermissions] WHERE [RoleId]={limitedRoleId}", Cancellation).ConfigureAwait(true);
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) VALUES({limitedRoleId},{permission})", Cancellation).ConfigureAwait(true);
                Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<SalesDocumentLinkPageResponse>(host, client, HttpMethod.Get, url, viewer.AccessToken).ConfigureAwait(true)).Status);
            }
            var party = new PartyRow { Id = Guid.NewGuid(), CompanyId = company, Code = "LINKS", NameArabic = "زبون الروابط", Role = 1, CreditPolicyKind = 1 };
            using (var annual = host.AnnualContext()) { annual.Parties.Add(party); await annual.SaveChangesAsync(Cancellation).ConfigureAwait(true); }
            var result = await SendAsync<SalesDocumentLinkPageResponse>(host, client, HttpMethod.Get, root + $"/customers/{party.Id}", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, result.Status); Assert.Equal(1, result.Body!.Page); Assert.Equal(50, result.Body.PageSize); Assert.Empty(result.Body.Items);
        }
    }
}
