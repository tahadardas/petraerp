using System.Net;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Security;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Tests;

public sealed partial class LedgerManagementHttpSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AccountCardContextIsPagedScopedAndAuthorized()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            using var context = host.CreateContext();
            var company = host.CompanyId;
            var branch = host.BranchId;
            var root = $"/api/v1/companies/{company}/branches/{branch}/ledger/chart/accounts";
            var account = await SendAsync<AccountResponse>(host, client, HttpMethod.Post, root, admin.AccessToken, new AccountRequest
            {
                Code = "1000", NameArabic = "الأصول", AccountType = AccountType.Assets,
                PostingAllowed = false, CostCenterPolicy = CostCenterPolicy.Optional, CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly,
            }).ConfigureAwait(true);
            var path = root + $"/{account.Body!.Id}/context";
            await CreateLimitedUserAsync(host, company, branch, PermissionCatalog.AccountsView).ConfigureAwait(true);
            var viewer = await ApiSqlHost.LoginAsync(client, "limited", LimitedPassword).ConfigureAwait(true);
            var result = await SendAsync<AccountCardContextResponse>(host, client, HttpMethod.Get, path + "?pageSize=1", viewer.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, result.Status); Assert.Equal("الأصول", Assert.Single(result.Body!.Path).NameArabic);
            Assert.Null(result.Body.Path[0].NameEnglish); Assert.Empty(result.Body.Children.Items);
            foreach (var suffix in new[] { "?pageSize=201", "?page=0", "?pageSize=0" })
                Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync<AccountCardContextResponse>(host, client, HttpMethod.Get, path + suffix, viewer.AccessToken).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync<AccountCardContextResponse>(host, client, HttpMethod.Get,
                root + $"/{Guid.NewGuid()}/context", viewer.AccessToken).ConfigureAwait(true)).Status);
            foreach (var scope in new[] { company, branch })
                Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<AccountCardContextResponse>(host, client, HttpMethod.Get,
                    path.Replace(scope.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal), viewer.AccessToken).ConfigureAwait(true)).Status);
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [security].[RolePermissions] WHERE [PermissionCode]=N'Ledger.Accounts.View';", Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<AccountCardContextResponse>(host, client, HttpMethod.Get, path, viewer.AccessToken).ConfigureAwait(true)).Status);
        }
    }
}
