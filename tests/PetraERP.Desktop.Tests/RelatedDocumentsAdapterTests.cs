using System.Net;
using System.Text.Json;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Relationships;

namespace PetraERP.Desktop.Tests;

public sealed class RelatedDocumentsAdapterTests
{
    [Theory]
    [InlineData(RelatedDocumentSource.SupplierPurchases, "supplierId")]
    [InlineData(RelatedDocumentSource.CustomerReceipts, "partyId")]
    [InlineData(RelatedDocumentSource.SupplierPayments, "partyId")]
    [InlineData(RelatedDocumentSource.InvoiceReceipts, "salesInvoiceId")]
    [InlineData(RelatedDocumentSource.JournalVouchers, "journalEntryId")]
    public async Task GeneratedClientSendsSourceAndScopeAndPreservesServerMoney(RelatedDocumentSource source, string filter)
    {
        using var auth = new AuthHandler(); using var resource = new ResourceHandler();
        using var session = new DesktopSession(new Uri("https://localhost/"), auth, resource);
        await session.LoginAsync("test", "test", TestContext.Current.CancellationToken).ConfigureAwait(true);
        var company = Guid.NewGuid(); var branch = Guid.NewGuid(); var id = Guid.NewGuid();
        var result = await new RelatedDocumentsClient(session).ListAsync(company, branch, source, id, 2, 50, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var request = Assert.Single(resource.Requests);
        Assert.Contains($"/api/v1/companies/{company}/branches/{branch}/", request.AbsolutePath);
        Assert.Contains($"{filter}={id}", request.Query); Assert.Contains("page=2", request.Query); Assert.Contains("pageSize=50", request.Query);
        Assert.Equal("99999999999.1234", Assert.Single(result.Items).Amount); Assert.Equal(2, result.Page); Assert.Equal(51, result.TotalCount);
        if (source == RelatedDocumentSource.SupplierPayments) Assert.Contains("kind=Payment", request.Query);
        if (source is RelatedDocumentSource.CustomerReceipts or RelatedDocumentSource.InvoiceReceipts) Assert.Contains("kind=Receipt", request.Query);
    }

    private sealed class AuthHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new TokenResponse
            {
                AccessToken = "test", RefreshToken = "test", SessionId = Guid.NewGuid(), TokenType = "Bearer",
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10), RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), SessionExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            })),
        });
    }

    private sealed class ResourceHandler : HttpMessageHandler
    {
        internal List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var amountName = request.RequestUri!.AbsolutePath.Contains("purchases", StringComparison.Ordinal) ? "grandTotal" : "amount";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"page\":2,\"pageSize\":50,\"totalCount\":51,\"items\":[{{\"id\":\"{Guid.NewGuid()}\",\"{amountName}\":\"99999999999.1234\",\"currencyCode\":\"SYP\"}}]}}"),
            });
        }
    }
}
