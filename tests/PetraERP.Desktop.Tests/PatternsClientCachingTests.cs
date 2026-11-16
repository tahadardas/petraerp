using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PetraERP.ApiClient;
using PetraERP.Desktop.Caching;
using PetraERP.Desktop.Connection;

namespace PetraERP.Desktop.Tests;

[Collection(WpfApplicationGroup.Name)]
public sealed class PatternsClientCachingTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed class LoginTransport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = new TokenResponse
            {
                TokenType = "Bearer", AccessToken = "a", AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), RefreshToken = "r",
                RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2), SessionId = Guid.NewGuid(), SessionExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2), MustChangePassword = false,
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(token, Web), Encoding.UTF8, "application/json") });
        }
    }

    private sealed class CountingTransport : HttpMessageHandler
    {
        public int RunnableReads { get; private set; }
        public int DetailReads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (request.Method == HttpMethod.Get && path.EndsWith("/runnable", StringComparison.OrdinalIgnoreCase)) { RunnableReads++; body = "[]"; }
            else if (request.Method == HttpMethod.Get) { DetailReads++; body = "{}"; }
            else body = "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task RepeatedWindowOpeningsReadPatternsOnceUntilAPatternChanges()
    {
        ReferenceDataCache.Instance.InvalidateAll();
        var resource = new CountingTransport();
        using var session = new DesktopSession(new Uri("https://localhost/"), new LoginTransport(), resource);
        await session.LoginAsync("u", "p", Cancellation).ConfigureAwait(true);
        var company = Guid.NewGuid(); var branch = Guid.NewGuid(); var pattern = Guid.NewGuid();
        session.SelectWorkspace(Guid.NewGuid(), company, Guid.NewGuid());
        var client = new PatternsClient(session);

        for (var opening = 0; opening < 3; opening++)
        {
            await client.GetRunnablePatternsAsync(company, branch, Cancellation).ConfigureAwait(true);
            await client.GetPatternAsync(company, branch, pattern, Cancellation).ConfigureAwait(true);
        }

        Assert.Equal(1, resource.RunnableReads);
        Assert.Equal(1, resource.DetailReads);

        // A change made through the client retires the cached answers at once.
        await client.UpdatePatternAsync(company, branch, pattern, "v1", new UpdateDocumentPatternRequest(), Cancellation).ConfigureAwait(true);
        await client.GetRunnablePatternsAsync(company, branch, Cancellation).ConfigureAwait(true);
        await client.GetPatternAsync(company, branch, pattern, Cancellation).ConfigureAwait(true);
        Assert.Equal(2, resource.RunnableReads);
        Assert.Equal(2, resource.DetailReads);

        // Another branch never shares the answers.
        await client.GetPatternAsync(company, Guid.NewGuid(), pattern, Cancellation).ConfigureAwait(true);
        Assert.Equal(3, resource.DetailReads);
    }
}
