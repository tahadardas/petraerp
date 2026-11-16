using System.Net;
using Microsoft.EntityFrameworkCore;
using PetraERP.Contracts.Sales;

namespace PetraERP.Api.Tests;

public sealed partial class LedgerManagementHttpSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SalesDraftWritesRequireAWellFormedVersionAndAcceptEightDecimalRates()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            var company = host.CompanyId;
            var branch = host.BranchId;
            var invoice = $"/api/v1/companies/{company}/branches/{branch}/sales/invoices/{Guid.NewGuid()}";
            const string WellFormed = "AAAAAAAAB9E=";
            var update = new UpdateSalesInvoiceRequest { CustomerId = Guid.NewGuid(), WarehouseId = Guid.NewGuid(), ExpectedVersion = WellFormed, ExchangeRate = "1.23456789" };

            async Task<HttpStatusCode> PutAsync(UpdateSalesInvoiceRequest body) =>
                (await SendAsync<SalesInvoiceDetailDto>(host, client, HttpMethod.Put, invoice, admin.AccessToken, body).ConfigureAwait(true)).Status;
            async Task<HttpStatusCode> ApproveAsync(ApproveSalesInvoiceRequest body) =>
                (await SendAsync<SalesInvoiceDetailDto>(host, client, HttpMethod.Post, invoice + "/approve", admin.AccessToken, body).ConfigureAwait(true)).Status;

            // A valid version and an eight-decimal rate pass the wire contract and reach the scoped lookup.
            Assert.Equal(HttpStatusCode.NotFound, await PutAsync(update).ConfigureAwait(true));
            Assert.Equal(HttpStatusCode.NotFound, await ApproveAsync(new() { ExpectedVersion = WellFormed }).ConfigureAwait(true));
            foreach (var version in new[] { "", "AAAAAAAAB9E", "AAAAAAAAAAAAAAAA", "not-a-version" })
            {
                Assert.Equal(HttpStatusCode.BadRequest, await PutAsync(update with { ExpectedVersion = version }).ConfigureAwait(true));
                Assert.Equal(HttpStatusCode.BadRequest, await ApproveAsync(new() { ExpectedVersion = version }).ConfigureAwait(true));
            }

            // Excess fraction digits, a sign, or more integer digits than decimal(19,8) holds are rejected, never rounded.
            foreach (var rate in new[] { "1.234567891", "-1.5", "0", "123456789012.5" })
                Assert.Equal(HttpStatusCode.BadRequest, await PutAsync(update with { ExchangeRate = rate }).ConfigureAwait(true));
        }
    }
}
