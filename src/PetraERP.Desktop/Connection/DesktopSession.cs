using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using PetraERP.ApiClient;
using PetraERP.Desktop.Security;

namespace PetraERP.Desktop.Connection;

/// <summary>Serialized single-use refresh. Only idempotent, content-free reads may recover once from 401.</summary>
public sealed class DesktopSession : IDisposable, IFinancialWorkspaceSelector
{
    public const string WorkspaceHeaderName = "X-Petra-Workspace";
    private readonly object workspaceLock = new();
    private SelectedWorkspace? workspace;
    private long sessionGeneration;
    private bool sessionIsActive;
    private readonly HttpClient authenticationHttp;
    private readonly HttpClient resourceHttp;
    private readonly PetraApiClient authentication;
    private readonly IProtectedSessionStore store;
    private readonly SemaphoreSlim refresh = new(1, 1);
    private TokenResponse? tokens;
    public DesktopSession(Uri baseUri, IProtectedSessionStore store) : this(baseUri, CreateTransport(), CreateTransport(), store) { }

    private static SocketsHttpHandler CreateTransport() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        AutomaticDecompression = DecompressionMethods.All,
        EnableMultipleHttp2Connections = true
    };
    public DesktopSession(Uri baseUri) : this(baseUri, new VolatileSessionStore()) { }
    internal DesktopSession(Uri baseUri, HttpMessageHandler authenticationTransport, HttpMessageHandler resourceTransport, IProtectedSessionStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps || baseUri.UserInfo.Length != 0 || baseUri.Query.Length != 0 || baseUri.Fragment.Length != 0)
            throw new ArgumentException("The API address must be an absolute HTTPS URI without credentials, query or fragment.", nameof(baseUri));
        this.store = store ?? new VolatileSessionStore();
        authenticationHttp = new HttpClient(authenticationTransport) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };
        authentication = new PetraApiClient(authenticationHttp);
        resourceHttp = new HttpClient(new SessionHandler(this, new TransientRetryHandler(resourceTransport))) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };
        Api = new PetraApiClient(resourceHttp);
    }
    public IPetraApiClient Api { get; }
    public Task<ServiceStatusResponse> StatusAsync(CancellationToken cancellationToken) => authentication.GetServiceStatusAsync(cancellationToken);
    public async Task<bool> LoginAsync(string userName, string password, CancellationToken cancellationToken)
    {
        await refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ClearCore();
            var result = await authentication.LoginAsync(new LoginRequest { UserName = userName, Password = password, ClientKind = ClientKind.Desktop,
                DeviceName = Environment.MachineName }, cancellationToken).ConfigureAwait(false);
            Accept(result);
            return result.MustChangePassword;
        }
        finally { refresh.Release(); }
    }
    public async Task<bool> ResumeAsync(CancellationToken cancellationToken)
    {
        await refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var saved = store.Read();
            if (saved is null) return false;
            ClearCore(); // Delete before consuming a single-use persisted refresh token.
            if (saved.RefreshExpiresUtc <= DateTimeOffset.UtcNow || saved.SessionExpiresUtc <= DateTimeOffset.UtcNow) return false;
            await RotateAsync(saved.RefreshToken, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { refresh.Release(); }
    }
    public async Task ChangePasswordAsync(string current, string replacement, CancellationToken cancellationToken)
    {
        await refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
            authenticationHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
            // The operation revokes the old session. Never resume it after an uncertain result.
            ClearCore();
            Accept(await authentication.ChangePasswordAsync(new ChangePasswordRequest { CurrentPassword = current, NewPassword = replacement,
                ClientKind = ClientKind.Desktop }, cancellationToken).ConfigureAwait(false));
        }
        catch { ClearCore(); throw; }
        finally { authenticationHttp.DefaultRequestHeaders.Authorization = null; refresh.Release(); }
    }
    public Task LogoutAsync(CancellationToken cancellationToken) => LogoutAsync(false, cancellationToken);
    public async Task LogoutAsync(bool all, CancellationToken cancellationToken)
    {
        await refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (tokens is null) return;
            await EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
            authenticationHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
            if (all) await authentication.LogoutAllSessionsAsync(cancellationToken).ConfigureAwait(false);
            else await authentication.LogoutAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { ClearCore(); authenticationHttp.DefaultRequestHeaders.Authorization = null; refresh.Release(); }
    }
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { ClearCore(); } finally { refresh.Release(); }
    }
    public void SelectWorkspace(Guid workspaceId, Guid companyId, Guid fiscalYearId)
    {
        if (workspaceId == Guid.Empty || companyId == Guid.Empty || fiscalYearId == Guid.Empty)
            throw new ArgumentException("A registered workspace, company and fiscal year are required.", nameof(workspaceId));
        lock (workspaceLock)
        {
            if (!sessionIsActive) throw new SessionUnavailableException();
            workspace = new SelectedWorkspace(workspaceId, companyId, fiscalYearId);
        }
        // Answers still in flight were requested for the workspace the user just left; they must not reach the new one.
        Caching.ReferenceDataCache.Instance.AdvanceContext();
    }

    /// <summary>The cache scope of the selected workspace, or null when none is selected (callers then bypass the cache).</summary>
    internal Caching.CacheScope? CacheScope(Guid? branchId = null)
    {
        var (selected, _) = CaptureWorkspace();
        return selected is null ? null : new Caching.CacheScope(selected.CompanyId, selected.FiscalYearId, selected.WorkspaceId, branchId, Caching.PermissionFingerprint.Current);
    }
    /// <summary>
    /// Runs a keyed post. If its response is lost for good (the retry handler already retried with the same key), asks the server what
    /// it recorded for the key and raises <see cref="PostingOutcomeException"/> so the user learns whether the document was posted.
    /// </summary>
    internal Task<T> PostWithOutcomeAsync<T>(Guid companyId, Guid branchId, string? idempotencyKey, Func<Task<T>> post, CancellationToken cancellationToken) =>
        PostingOutcomeProbe.RunAsync(idempotencyKey, post, token => Api.GetPostingRequestStatusAsync(companyId, branchId, idempotencyKey!, token), cancellationToken);

    public void ClearWorkspace()
    {
        lock (workspaceLock) workspace = null;
        Caching.ReferenceDataCache.Instance.AdvanceContext();
    }
    /// <summary>Reference data for the selected workspace; with no workspace selected the cache is bypassed, never shared.</summary>
    internal Task<T> CachedAsync<T>(Guid branchId, string category, string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, CancellationToken cancellationToken) =>
        CacheScope(branchId) is { } scope
            ? Caching.ReferenceDataCache.Instance.GetOrCreateAsync(scope, category, key, factory, ttl, cancellationToken)
            : factory(cancellationToken);

    /// <summary>Retires one category of the selected workspace after a change made through this client.</summary>
    internal void InvalidateCached(string category)
    {
        if (CacheScope() is { } scope) Caching.ReferenceDataCache.Instance.InvalidateCategory(scope, category);
    }

    private (SelectedWorkspace? Workspace, long Generation) CaptureWorkspace()
    {
        lock (workspaceLock) return (workspace, sessionGeneration);
    }
    private void ClearCore(bool preserveWorkspace = false)
    {
        lock (workspaceLock)
        {
            tokens = null;
            if (!preserveWorkspace) { sessionIsActive = false; workspace = null; Interlocked.Increment(ref sessionGeneration); }
        }
        // Signing out (or an expired session) may change what the next user is allowed to see: nothing cached survives it.
        if (!preserveWorkspace) Caching.ReferenceDataCache.Instance.InvalidateAll();
        store.Delete();
    }
    private void Accept(TokenResponse result)
    {
        try
        {
            // Forced-change credentials are never persisted.
            if (!result.MustChangePassword) store.Save(new StoredSession(result.RefreshToken, result.RefreshTokenExpiresAtUtc, result.SessionExpiresAtUtc));
            lock (workspaceLock) { tokens = result; sessionIsActive = true; }
        }
        catch { ClearCore(); throw; }
    }
    private async Task RotateAsync(string secret, CancellationToken cancellationToken)
    {
        try { Accept(await authentication.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = secret }, cancellationToken).ConfigureAwait(false)); }
        catch (Exception exception) when (exception is HttpRequestException or PetraApiException or OperationCanceledException)
        { ClearCore(); throw new SessionUnavailableException("Refresh could not be confirmed. Sign in again.", exception); }
    }
    private async Task EnsureFreshAsync(CancellationToken cancellationToken, bool force = false)
    {
        var current = tokens ?? throw new SessionUnavailableException();
        if (!force && current.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30)) return;
        ClearCore(preserveWorkspace: true);
        if (current.RefreshTokenExpiresAtUtc <= DateTimeOffset.UtcNow || current.SessionExpiresAtUtc <= DateTimeOffset.UtcNow)
        { ClearCore(); throw new SessionUnavailableException(); }
        await RotateAsync(current.RefreshToken, cancellationToken).ConfigureAwait(false);
    }
    private async Task<string> AccessAsync(long generation, string? rejected, CancellationToken cancellationToken)
    {
        await refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (generation != Interlocked.Read(ref sessionGeneration)) throw new SessionUnavailableException();
            await EnsureFreshAsync(cancellationToken, rejected is not null && tokens?.AccessToken == rejected).ConfigureAwait(false);
            return tokens!.AccessToken;
        }
        finally { refresh.Release(); }
    }
    public void Dispose() { lock (workspaceLock) { tokens = null; sessionIsActive = false; workspace = null; Interlocked.Increment(ref sessionGeneration); } authenticationHttp.Dispose(); resourceHttp.Dispose(); refresh.Dispose(); }
    private sealed record SelectedWorkspace(Guid WorkspaceId, Guid CompanyId, Guid FiscalYearId);
    private sealed class SessionHandler(DesktopSession session, HttpMessageHandler transport) : DelegatingHandler(transport)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Capture before authentication/refresh can yield. A concurrent switch cannot retarget this request or its retry.
            var captured = session.CaptureWorkspace();
            request.Headers.Remove(WorkspaceHeaderName);
            if (captured.Workspace is { } selected)
                request.Headers.Add(WorkspaceHeaderName, selected.WorkspaceId.ToString("D"));
            var access = await session.AccessAsync(captured.Generation, null, cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
            if ((request.Method != HttpMethod.Get && request.Method != HttpMethod.Head) || request.Content is not null)
            { await session.ClearAsync(cancellationToken).ConfigureAwait(false); return response; }
            response.Dispose();
            var replacement = await session.AccessAsync(captured.Generation, access, cancellationToken).ConfigureAwait(false);
            using var retry = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version, VersionPolicy = request.VersionPolicy };
            foreach (var header in request.Headers) retry.Headers.TryAddWithoutValidation(header.Key, header.Value);
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", replacement);
            var recovered = await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
            if (recovered.StatusCode == HttpStatusCode.Unauthorized) await session.ClearAsync(cancellationToken).ConfigureAwait(false);
            return recovered;
        }
    }
}
