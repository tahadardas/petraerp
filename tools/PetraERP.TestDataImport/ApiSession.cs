using System.Net;
using System.Net.Http.Headers;
using PetraERP.ApiClient;

namespace PetraERP.TestDataImport;

/// <summary>Bearer session that logs in again shortly before the access token expires or after a 401.</summary>
internal sealed class ApiSession : IDisposable
{
    private readonly HttpClient authenticationHttp;
    private readonly HttpClient resourceHttp;
    private readonly PetraApiClient authentication;
    private readonly string userName;
    private readonly string password;
    private readonly SemaphoreSlim gate = new(1, 1);
    private TokenResponse? tokens;

    public ApiSession(Uri baseUri, string userName, string password)
    {
        this.userName = userName; this.password = password;
        authenticationHttp = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(60) };
        authentication = new PetraApiClient(authenticationHttp);
#pragma warning disable CA2000 // The handler is owned and disposed by resourceHttp.
        resourceHttp = new HttpClient(new BearerHandler(this)) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(120) };
#pragma warning restore CA2000
        Api = new PetraApiClient(resourceHttp);
    }

    public PetraApiClient Api { get; }

    public async Task<string> AccessTokenAsync(bool renew, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (renew || tokens is null || tokens.AccessTokenExpiresAtUtc - DateTimeOffset.UtcNow < TimeSpan.FromMinutes(1))
                tokens = await authentication.LoginAsync(new LoginRequest { UserName = userName, Password = password }, cancellationToken).ConfigureAwait(false);
            return tokens.AccessToken;
        }
        finally { gate.Release(); }
    }

    public void Dispose() { authenticationHttp.Dispose(); resourceHttp.Dispose(); gate.Dispose(); }

    private sealed class BearerHandler(ApiSession session) : DelegatingHandler(new HttpClientHandler())
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await session.AccessTokenAsync(false, cancellationToken).ConfigureAwait(false));
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            // The rate limiter rejects before the request is processed, so waiting and resending is safe.
            for (var attempt = 0; response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 30; attempt++)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                response.Dispose();
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
            // A 401 is decided before the request body is processed, so resending once is safe.
            response.Dispose();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await session.AccessTokenAsync(true, cancellationToken).ConfigureAwait(false));
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
