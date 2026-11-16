using System.Net;
using System.Net.Http;
using System.Text.Json;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Security;

namespace PetraERP.Desktop.Tests;

public sealed class SessionRecoveryTests
{
    [Fact]
    public async Task SafeReadRecoversOnceAndPersistsOnlyTheRotatedSecret()
    {
        var store = new TestStore(); using var auth = new Auth(store); using var resources = new Resources();
        using var session = new DesktopSession(new Uri("https://localhost/"), auth, resources, store);
        await session.LoginAsync("user", "private-password", Cancellation).ConfigureAwait(true);
        await session.Api.ListMyCompaniesAsync(Cancellation).ConfigureAwait(true);
        Assert.Equal(1, auth.Refreshes); Assert.Equal(2, resources.Reads); Assert.Equal("refresh-1", store.Value!.RefreshToken);
        await session.LogoutAsync(true, Cancellation).ConfigureAwait(true); Assert.True(auth.LoggedOutAll); Assert.Null(store.Value);
    }
    [Fact]
    public async Task Repeated401StopsAndWrite401NeverReplays()
    {
        var store = new TestStore(); using var auth = new Auth(store); using var resources = new Resources { AlwaysUnauthorized = true };
        using var session = new DesktopSession(new Uri("https://localhost/"), auth, resources, store);
        await session.LoginAsync("user", "secret", Cancellation).ConfigureAwait(true);
        await Assert.ThrowsAsync<PetraApiException>(() => session.Api.ListMyCompaniesAsync(Cancellation)).ConfigureAwait(true);
        Assert.Equal(1, auth.Refreshes); Assert.Equal(2, resources.Reads); Assert.Null(store.Value);
        await Assert.ThrowsAsync<SessionUnavailableException>(() => session.Api.ListMyCompaniesAsync(Cancellation)).ConfigureAwait(true);
        await session.LoginAsync("user", "secret", Cancellation).ConfigureAwait(true);
        await Assert.ThrowsAsync<PetraApiException>(() => session.Api.RevokeUserSessionsAsync(Guid.NewGuid(), new RevokeUserSessionsRequest { Reason = "test" }, Cancellation)).ConfigureAwait(true);
        Assert.Equal(1, resources.Writes); Assert.Equal(1, auth.Refreshes); Assert.Null(store.Value);
    }
    [Fact]
    public async Task ResumeConsumesPersistedSecretBeforeNetworkAndFailureErasesIt()
    {
        var store = new TestStore { Value = new("saved", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddHours(2)) };
        using var auth = new Auth(store); using var resources = new Resources();
        using var session = new DesktopSession(new Uri("https://localhost/"), auth, resources, store);
        Assert.True(await session.ResumeAsync(Cancellation).ConfigureAwait(true)); Assert.Equal(1, auth.Refreshes); Assert.Equal("refresh-1", store.Value!.RefreshToken);
        auth.Fail = true;
        await Assert.ThrowsAsync<SessionUnavailableException>(() => session.ResumeAsync(Cancellation)).ConfigureAwait(true);
        Assert.Null(store.Value); Assert.False(await session.ResumeAsync(Cancellation).ConfigureAwait(true)); Assert.Equal(2, auth.Refreshes);
    }
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private sealed class TestStore : IProtectedSessionStore
    {
        public StoredSession? Value { get; set; }
        public StoredSession? Read() => Value;
        public void Save(StoredSession session) => Value = session;
        public void Delete() => Value = null;
    }
    private sealed class Auth(TestStore store) : HttpMessageHandler
    {
        public int Refreshes { get; private set; }
        public bool Fail { get; set; }
        public bool LoggedOutAll { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/logout-all", StringComparison.Ordinal)) { LoggedOutAll = true; return Task.FromResult(Json("{\"revokedSessions\":2}")); }
            if (request.RequestUri.AbsolutePath.EndsWith("/refresh", StringComparison.Ordinal))
            {
                Refreshes++; Assert.Null(store.Value);
                if (Fail) throw new HttpRequestException("uncertain result");
            }
            return Task.FromResult(Json(JsonSerializer.Serialize(new TokenResponse { AccessToken = "access-" + Refreshes, RefreshToken = "refresh-" + Refreshes,
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5), RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), SessionExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2) })));
        }
    }
    private sealed class Resources : HttpMessageHandler
    {
        public bool AlwaysUnauthorized { get; init; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) { Writes++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)); }
            Reads++;
            return Task.FromResult(AlwaysUnauthorized || request.Headers.Authorization?.Parameter == "access-0" ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Json("{\"items\":[]}"));
        }
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
}
