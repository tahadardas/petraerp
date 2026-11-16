using System.Net;
using System.Net.Http;
using System.Text.Json;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;

namespace PetraERP.Desktop.Tests;

public sealed class DesktopSessionTests
{
    [Fact]
    public async Task ConcurrentRequestsRotateOnceAndLogoutClearsLocalAccess()
    {
        using var authentication = new AuthenticationTransport(); using var resources = new ResourceTransport();
        using var session = new DesktopSession(new Uri("https://localhost/"), authentication, resources);
        await session.LoginAsync("user", "not-recorded", TestContext.Current.CancellationToken).ConfigureAwait(true);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => session.Api.ListMyCompaniesAsync(TestContext.Current.CancellationToken))).ConfigureAwait(true);
        Assert.Equal(1, authentication.RefreshCount); Assert.Equal(12, resources.RequestCount);
        await session.LogoutAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await Assert.ThrowsAsync<SessionUnavailableException>(() => session.Api.ListMyCompaniesAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
        Assert.Equal(1, authentication.LogoutCount);
    }
    [Fact]
    public async Task AFailedRotationIsNotRetriedWithTheConsumedToken()
    {
        using var authentication = new AuthenticationTransport { FailRefresh = true }; using var resources = new ResourceTransport();
        using var session = new DesktopSession(new Uri("https://localhost/"), authentication, resources);
        await session.LoginAsync("user", "not-recorded", TestContext.Current.CancellationToken).ConfigureAwait(true);
        await Assert.ThrowsAsync<SessionUnavailableException>(() => session.Api.ListMyCompaniesAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
        await Assert.ThrowsAsync<SessionUnavailableException>(() => session.Api.ListMyCompaniesAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
        Assert.Equal(1, authentication.RefreshCount); Assert.Equal(0, resources.RequestCount);
    }
    private sealed class AuthenticationTransport : HttpMessageHandler
    {
        public bool FailRefresh { get; init; }
        public int RefreshCount { get; private set; }
        public int LogoutCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/logout", StringComparison.Ordinal))
            { LogoutCount++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
            var refresh = request.RequestUri.AbsolutePath.EndsWith("/refresh", StringComparison.Ordinal);
            if (refresh) { RefreshCount++; if (FailRefresh) throw new HttpRequestException("Lost rotation response."); }
            var body = new TokenResponse { AccessToken = "access", RefreshToken = "refresh", TokenType = "Bearer", SessionId = Guid.NewGuid(),
                AccessTokenExpiresAtUtc = refresh ? DateTimeOffset.UtcNow.AddMinutes(10) : DateTimeOffset.UtcNow.AddSeconds(-1),
                RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), SessionExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2), MustChangePassword = false };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) });
        }
    }
    private sealed class ResourceTransport : HttpMessageHandler
    {
        private int count;
        public int RequestCount => count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref count); Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") });
        }
    }
}
