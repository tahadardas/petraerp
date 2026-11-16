using System.Net;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Security;
using PetraERP.Contracts.Purchases;

namespace PetraERP.Api.Tests;

public sealed partial class LedgerManagementHttpSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PurchaseRoutesEnforceEachPermissionScopeAndPayloadContract()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            var company = host.CompanyId;
            var branch = host.BranchId;
            var root = $"/api/v1/companies/{company}/branches/{branch}/purchases/invoices";
            var invoice = $"{root}/{Guid.NewGuid()}";
            var save = new SavePurchaseInvoiceRequest
            {
                PatternRevisionId = Guid.NewGuid(), SupplierId = Guid.NewGuid(), WarehouseId = Guid.NewGuid(), InvoiceDateUtc = DateTimeOffset.UtcNow,
                CurrencyCode = "JOD", ExchangeRate = "1", Lines = [new() { ItemId = Guid.NewGuid(), UnitId = Guid.NewGuid(), Quantity = "2.5", UnitCost = "100" }],
            };
            var post = new PostPurchaseInvoiceRequest { ExpectedVersion = "AAAAAAAAB9E=", IdempotencyKey = "k" };
            var returned = new CreatePurchaseReturnRequest { IdempotencyKey = "r", Reason = "تالف", Lines = [new() { SourceLineId = Guid.NewGuid(), Quantity = "1" }] };

            await CreateLimitedUserAsync(host, company, branch, PermissionCatalog.PurchaseInvoicesView).ConfigureAwait(true);
            var limited = await ApiSqlHost.LoginAsync(client, "limited", LimitedPassword).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync<PurchaseDetailDto>(host, client, HttpMethod.Get, invoice, limited.AccessToken).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<PurchaseDetailDto>(host, client, HttpMethod.Post, root, limited.AccessToken, save).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<PurchaseDetailDto>(host, client, HttpMethod.Post, invoice + "/post", limited.AccessToken, post).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<PurchaseDetailDto>(host, client, HttpMethod.Post, invoice + "/returns", limited.AccessToken, returned).ConfigureAwait(true)).Status);

            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync<PurchaseDetailDto>(host, client, HttpMethod.Post, invoice + "/post", admin.AccessToken, post).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync<PurchaseDetailDto>(host, client, HttpMethod.Post, invoice + "/returns", admin.AccessToken, returned).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync<PurchaseDetailDto>(host, client, HttpMethod.Post, root, admin.AccessToken, save).ConfigureAwait(true)).Status);
            foreach (var body in new[]
            {
                save with { Lines = [] }, save with { ExchangeRate = "0" }, save with { CurrencyCode = "JO" },
                save with { Lines = [new() { ItemId = Guid.NewGuid(), UnitId = Guid.NewGuid(), Quantity = "0", UnitCost = "1" }] },
                save with { Lines = [new() { ItemId = Guid.NewGuid(), UnitId = Guid.NewGuid(), Quantity = "1.00001", UnitCost = "1" }] },
                save with { Lines = [new() { ItemId = Guid.NewGuid(), UnitId = Guid.NewGuid(), Quantity = "1", UnitCost = "-1" }] },
            })
                Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync<PurchaseDetailDto>(host, client, HttpMethod.Post, root, admin.AccessToken, body).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync<PurchaseDetailDto>(host, client, HttpMethod.Put, invoice, admin.AccessToken, save).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync<PurchaseDetailDto>(host, client, HttpMethod.Delete, invoice, admin.AccessToken).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync<PurchaseDetailDto>(host, client, HttpMethod.Post, invoice + "/returns", admin.AccessToken,
                returned with { Lines = [new() { SourceLineId = Guid.NewGuid(), Quantity = "0" }] }).ConfigureAwait(true)).Status);
            var foreign = invoice.Replace(branch.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<PurchaseDetailDto>(host, client, HttpMethod.Get, foreign, admin.AccessToken).ConfigureAwait(true)).Status);
            var list = await SendAsync<PurchasePageResponse>(host, client, HttpMethod.Get, root, admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, list.Status); Assert.Empty(list.Body!.Items);
        }
    }
}
