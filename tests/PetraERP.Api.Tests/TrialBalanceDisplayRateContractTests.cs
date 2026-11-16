using PetraERP.Api.Endpoints;
using PetraERP.Application.Common;
using System.Net;
using System.Text.Json;

namespace PetraERP.Api.Tests;

public sealed class TrialBalanceDisplayRateContractTests
{
    [Theory]
    [InlineData("0.00000001")]
    [InlineData("0.12345678")]
    [InlineData("1000000000")]
    public void RateParsesAsExactDecimal(string text)
    {
        Assert.Equal(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture), LedgerReportEndpoints.ParseDisplayRate(text));
    }

    [Theory]
    [InlineData("1e-8")]
    [InlineData("0.123456789")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("1,25")]
    [InlineData("")]
    public void InvalidRatesAreRejected(string text) =>
        Assert.Throws<RequestValidationException>(() => LedgerReportEndpoints.ParseDisplayRate(text));

    [Fact]
    public void MissingRateRemainsMissing() => Assert.Null(LedgerReportEndpoints.ParseDisplayRate(null));

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task HttpTrialBalanceRetainsEightDecimalRateAndRejectsExcessPrecision()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}/ledger/reports/trial-balance?to=2026-12-31&displayCurrency=EUR&displayRate=";
            using var validRequest = host.WorkspaceRequest(HttpMethod.Get, root + "0.12345678", token);
            using var valid = await client.SendAsync(validRequest, TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
            using var body = JsonDocument.Parse(await valid.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Equal("0.12345678", body.RootElement.GetProperty("displayRate").GetString());
            Assert.Equal("EUR", body.RootElement.GetProperty("displayCurrencyCode").GetString());

            using var invalidRequest = host.WorkspaceRequest(HttpMethod.Get, root + "0.123456789", token);
            using var invalid = await client.SendAsync(invalidRequest, TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
    }
}
