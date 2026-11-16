using System.Net;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Security;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Tests;

public sealed partial class LedgerManagementHttpSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AccountAuditIsPagedAuthorizedScopedAndKeepsArabicSnapshots()
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
            var correlation = Guid.NewGuid();
            var created = await SendAsync<AccountResponse>(host, client, HttpMethod.Post, root, admin.AccessToken, new AccountRequest
            {
                Code = "1001", NameArabic = "الصندوق الرئيسي", NameEnglish = null, AccountType = AccountType.Assets,
                PostingAllowed = true, CostCenterPolicy = CostCenterPolicy.Optional, CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly,
            }, correlationId: correlation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, created.Status);
            var path = root + $"/{created.Body!.Id}/audit";
            var history = await SendAsync<AccountHistoryPageResponse>(host, client, HttpMethod.Get, path + "?pageSize=1", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, history.Status);
            var item = Assert.Single(history.Body!.Items);
            Assert.Equal(AccountAuditAction.Created, item.Action); Assert.Null(item.Before);
            Assert.Equal("الصندوق الرئيسي", item.After!.NameArabic); Assert.Null(item.After.NameEnglish);
            Assert.Equal(branch, item.WorkingBranchId); Assert.Equal("مدير النظام", item.ActorNameArabic);
            Assert.Equal(correlation, item.CorrelationId);
            var invalid = await SendAsync<AccountHistoryPageResponse>(host, client, HttpMethod.Get, path + "?pageSize=201", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
            await CreateLimitedUserAsync(host, company, branch, PermissionCatalog.AccountsView).ConfigureAwait(true);
            var viewer = await ApiSqlHost.LoginAsync(client, "limited", LimitedPassword).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, (await SendAsync<AccountHistoryPageResponse>(host, client, HttpMethod.Get, path, viewer.AccessToken).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<AccountHistoryPageResponse>(host, client, HttpMethod.Get,
                path.Replace(company.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal), viewer.AccessToken).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<AccountHistoryPageResponse>(host, client, HttpMethod.Get,
                path.Replace(branch.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal), viewer.AccessToken).ConfigureAwait(true)).Status);
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [security].[RolePermissions] WHERE [PermissionCode]=N'Ledger.Accounts.View';", Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<AccountHistoryPageResponse>(host, client, HttpMethod.Get, path, viewer.AccessToken).ConfigureAwait(true)).Status);
        }
    }
}
