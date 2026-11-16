using System.Net;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Security;
using PetraERP.Contracts.Sales;

namespace PetraERP.Api.Tests;

public sealed partial class LedgerManagementHttpSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SalesReturnRequiresReverseAndPostGrantsAndAValidPayload()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            using var db = host.CreateContext();
            var company = host.CompanyId;
            var branch = host.BranchId;
            var url = $"/api/v1/companies/{company}/branches/{branch}/sales/invoices/{Guid.NewGuid()}/returns";
            var valid = new CreateSalesReturnRequest
            {
                IdempotencyKey = "http-return", Reason = "إرجاع", Lines = [new() { SourceLineId = Guid.NewGuid(), Quantity = "1.5" }],
            };

            await CreateLimitedUserAsync(host, company, branch, PermissionCatalog.SalesInvoicesReverse).ConfigureAwait(true);
            var limited = await ApiSqlHost.LoginAsync(client, "limited", LimitedPassword).ConfigureAwait(true);
            var roleId = await db.Roles.Where(r => r.Code == "LIMITED").Select(r => r.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            foreach (var only in new[] { PermissionCatalog.SalesInvoicesReverse, PermissionCatalog.SalesInvoicesPost })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [security].[RolePermissions] WHERE [RoleId]={roleId}", Cancellation).ConfigureAwait(true);
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) VALUES({roleId},{only})", Cancellation).ConfigureAwait(true);
                Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<SalesInvoiceDetailDto>(host, client, HttpMethod.Post, url, limited.AccessToken, valid).ConfigureAwait(true)).Status);
            }

            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync<SalesInvoiceDetailDto>(host, client, HttpMethod.Post, url, admin.AccessToken, valid).ConfigureAwait(true)).Status);
            var invalid = new[]
            {
                valid with { Lines = [] },
                valid with { IdempotencyKey = "" },
                valid with { Reason = "" },
                valid with { Lines = [new() { SourceLineId = Guid.NewGuid(), Quantity = "0" }] },
                valid with { Lines = [new() { SourceLineId = Guid.NewGuid(), Quantity = "-1" }] },
                valid with { Lines = [new() { SourceLineId = Guid.NewGuid(), Quantity = "1.00001" }] },
            };
            foreach (var body in invalid)
                Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync<SalesInvoiceDetailDto>(host, client, HttpMethod.Post, url, admin.AccessToken, body).ConfigureAwait(true)).Status);
            var foreign = url.Replace(branch.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<SalesInvoiceDetailDto>(host, client, HttpMethod.Post, foreign, admin.AccessToken, valid).ConfigureAwait(true)).Status);
        }
    }
}
