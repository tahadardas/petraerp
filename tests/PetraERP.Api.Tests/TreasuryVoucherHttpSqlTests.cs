using System.Net;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Security;
using PetraERP.Contracts.Treasury;

namespace PetraERP.Api.Tests;

public sealed partial class LedgerManagementHttpSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task TreasuryVoucherRoutesEnforceEachPermissionScopeAndPayloadContract()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            var company = host.CompanyId;
            var branch = host.BranchId;
            var root = $"/api/v1/companies/{company}/branches/{branch}/treasury";
            var voucher = $"{root}/vouchers/{Guid.NewGuid()}";
            var save = new SaveVoucherRequest
            {
                Kind = VoucherKind.Receipt, PatternRevisionId = Guid.NewGuid(), PartyId = Guid.NewGuid(), VoucherDateUtc = DateTimeOffset.UtcNow,
                CurrencyCode = "JOD", ExchangeRate = "1", Amount = "10.5",
            };
            var post = new PostVoucherRequest { ExpectedVersion = "AAAAAAAAB9E=", IdempotencyKey = "k" };
            var reverse = new ReverseVoucherRequest { ExpectedVersion = "AAAAAAAAB9E=", Reason = "سبب" };

            await CreateLimitedUserAsync(host, company, branch, PermissionCatalog.TreasuryVouchersView).ConfigureAwait(true);
            var limited = await ApiSqlHost.LoginAsync(client, "limited", LimitedPassword).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<VoucherPageResponse>(host, client, HttpMethod.Get,
                $"{root}/vouchers?journalEntryId={Guid.NewGuid()}", limited.AccessToken).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Get, voucher, limited.AccessToken).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Post, $"{root}/vouchers", limited.AccessToken, save).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Post, voucher + "/post", limited.AccessToken, post).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Post, voucher + "/reverse", limited.AccessToken, reverse).ConfigureAwait(true)).Status);
            // Outstanding invoices expose Sales data: Treasury view alone is not enough.
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<List<OutstandingInvoiceDto>>(host, client, HttpMethod.Get,
                $"{root}/customers/{Guid.NewGuid()}/outstanding-invoices?currencyCode=JOD", limited.AccessToken).ConfigureAwait(true)).Status);

            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Post, voucher + "/post", admin.AccessToken, post).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync<List<OutstandingInvoiceDto>>(host, client, HttpMethod.Get,
                $"{root}/customers/{Guid.NewGuid()}/outstanding-invoices?currencyCode=JOD", admin.AccessToken).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Post, $"{root}/vouchers", admin.AccessToken, save).ConfigureAwait(true)).Status);
            foreach (var body in new[] { save with { Amount = "0" }, save with { Amount = "1.00001" }, save with { ExchangeRate = "-1" }, save with { CurrencyCode = "JO" },
                save with { Allocations = [new() { SalesInvoiceId = Guid.NewGuid(), Amount = "-1" }] } })
                Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Post, $"{root}/vouchers", admin.AccessToken, body).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Put, voucher, admin.AccessToken, save).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Delete, voucher, admin.AccessToken).ConfigureAwait(true)).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Post, voucher + "/post", admin.AccessToken, post with { IdempotencyKey = "" }).ConfigureAwait(true)).Status);
            var foreign = voucher.Replace(branch.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Get, foreign, admin.AccessToken).ConfigureAwait(true)).Status);
            var list = await SendAsync<VoucherPageResponse>(host, client, HttpMethod.Get, $"{root}/vouchers?kind=Receipt", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, list.Status); Assert.Empty(list.Body!.Items);
            var journalLinks = await SendAsync<VoucherPageResponse>(host, client, HttpMethod.Get,
                $"{root}/vouchers?journalEntryId={Guid.NewGuid()}", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, journalLinks.Status); Assert.Empty(journalLinks.Body!.Items);
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync<VoucherPageResponse>(host, client, HttpMethod.Get,
                $"{root}/vouchers?journalEntryId=invalid", admin.AccessToken).ConfigureAwait(true)).Status);
        }
    }
}
