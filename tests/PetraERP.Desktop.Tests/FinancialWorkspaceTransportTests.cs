using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;

namespace PetraERP.Desktop.Tests;

/// <summary>HTTP transport evidence only; SQL authorization and isolation are verified by the API integration suite.</summary>
public sealed class FinancialWorkspaceTransportTests
{
    [Fact]
    public async Task SelectedRegistrationIsCarriedByTheFinancialRequest()
    {
        using var authentication = new AuthenticationTransport();
        using var resources = new CaptureTransport();
        using var session = new DesktopSession(new Uri("https://localhost/"), authentication, resources);
        await session.LoginAsync("user", "not-recorded", TestContext.Current.CancellationToken).ConfigureAwait(true);
        var company = Guid.NewGuid();
        var registration = Guid.NewGuid();
        var selector = new DesktopClient(session);
        selector.SelectWorkspace(registration, company, Guid.NewGuid());

        await session.Api.GetAccountTreeAsync(company, Guid.NewGuid(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        var request = Assert.Single(resources.Requests);
        Assert.Equal(registration.ToString("D"), request.Workspace);
        Assert.Contains(company.ToString("D"), request.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Bearer", request.AuthenticationScheme);
    }

    [Fact]
    public async Task WorkspaceIsCapturedBeforeRefreshAndSwitchCannotRetargetPendingRequest()
    {
        using var authentication = new AuthenticationTransport { ExpireLogin = true, PauseRefresh = true };
        using var resources = new CaptureTransport();
        using var session = new DesktopSession(new Uri("https://localhost/"), authentication, resources);
        await session.LoginAsync("user", "not-recorded", TestContext.Current.CancellationToken).ConfigureAwait(true);
        var firstCompany = Guid.NewGuid();
        var secondCompany = Guid.NewGuid();
        var firstRegistration = Guid.NewGuid();
        var secondRegistration = Guid.NewGuid();
        session.SelectWorkspace(firstRegistration, firstCompany, Guid.NewGuid());

        var pending = session.Api.GetAccountTreeAsync(firstCompany, Guid.NewGuid(), TestContext.Current.CancellationToken);
        await authentication.RefreshEntered.Task.WaitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        session.SelectWorkspace(secondRegistration, secondCompany, Guid.NewGuid());
        authentication.ContinueRefresh.TrySetResult();
        await pending.ConfigureAwait(true);
        await session.Api.GetAccountTreeAsync(secondCompany, Guid.NewGuid(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal([firstRegistration.ToString("D"), secondRegistration.ToString("D")], resources.Requests.Select(item => item.Workspace));
    }

    [Fact]
    public async Task UnauthorizedReadRetryRetainsItsOriginalRegistration()
    {
        using var authentication = new AuthenticationTransport { PauseRefresh = true };
        using var resources = new CaptureTransport { RejectFirstRequest = true };
        using var session = new DesktopSession(new Uri("https://localhost/"), authentication, resources);
        await session.LoginAsync("user", "not-recorded", TestContext.Current.CancellationToken).ConfigureAwait(true);
        var company = Guid.NewGuid();
        var original = Guid.NewGuid();
        session.SelectWorkspace(original, company, Guid.NewGuid());

        var pending = session.Api.GetAccountTreeAsync(company, Guid.NewGuid(), TestContext.Current.CancellationToken);
        await authentication.RefreshEntered.Task.WaitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        session.SelectWorkspace(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        authentication.ContinueRefresh.TrySetResult();
        await pending.ConfigureAwait(true);

        Assert.Equal(2, resources.Requests.Count);
        Assert.All(resources.Requests, item => Assert.Equal(original.ToString("D"), item.Workspace));
    }

    [Fact]
    public async Task ClearingWorkspaceAndSigningOutDoNotLeakSelectionIntoTheNextSession()
    {
        using var authentication = new AuthenticationTransport();
        using var resources = new CaptureTransport();
        using var session = new DesktopSession(new Uri("https://localhost/"), authentication, resources);
        var company = Guid.NewGuid();
        var branch = Guid.NewGuid();
        await session.LoginAsync("first", "not-recorded", TestContext.Current.CancellationToken).ConfigureAwait(true);
        session.SelectWorkspace(Guid.NewGuid(), company, Guid.NewGuid());
        session.ClearWorkspace();
        await session.Api.GetAccountTreeAsync(company, branch, TestContext.Current.CancellationToken).ConfigureAwait(true);
        session.SelectWorkspace(Guid.NewGuid(), company, Guid.NewGuid());
        await session.ClearAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await Assert.ThrowsAsync<SessionUnavailableException>(() => session.Api.GetAccountTreeAsync(company, branch, TestContext.Current.CancellationToken)).ConfigureAwait(true);
        await session.LoginAsync("second", "not-recorded", TestContext.Current.CancellationToken).ConfigureAwait(true);
        await session.Api.GetAccountTreeAsync(Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, resources.Requests.Count);
        Assert.All(resources.Requests, item => Assert.Null(item.Workspace));
    }

    [Fact]
    public async Task LogoutDiscardsThePreviouslySelectedRegistration()
    {
        using var authentication = new AuthenticationTransport();
        using var resources = new CaptureTransport();
        using var session = new DesktopSession(new Uri("https://localhost/"), authentication, resources);
        await session.LoginAsync("first", "not-recorded", TestContext.Current.CancellationToken).ConfigureAwait(true);
        session.SelectWorkspace(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await session.LogoutAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await session.LoginAsync("second", "not-recorded", TestContext.Current.CancellationToken).ConfigureAwait(true);

        await session.Api.GetAccountTreeAsync(Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(Assert.Single(resources.Requests).Workspace);
    }

    private sealed class AuthenticationTransport : HttpMessageHandler
    {
        public bool ExpireLogin { get; init; }
        public bool PauseRefresh { get; init; }
        public TaskCompletionSource RefreshEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueRefresh { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/logout", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.NoContent);
            var refreshing = request.RequestUri.AbsolutePath.EndsWith("/refresh", StringComparison.Ordinal);
            if (refreshing && PauseRefresh)
            {
                RefreshEntered.TrySetResult();
                await ContinueRefresh.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            var token = new TokenResponse
            {
                AccessToken = refreshing ? "rotated-access" : "access", RefreshToken = "refresh", TokenType = "Bearer", SessionId = Guid.NewGuid(),
                AccessTokenExpiresAtUtc = !refreshing && ExpireLogin ? DateTimeOffset.UtcNow.AddSeconds(-1) : DateTimeOffset.UtcNow.AddMinutes(10),
                RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), SessionExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2), MustChangePassword = false,
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(token)) };
        }
    }

    private sealed record CapturedRequest(string Path, string? Workspace, string? AuthenticationScheme);

    private sealed class CaptureTransport : HttpMessageHandler
    {
        public bool RejectFirstRequest { get; init; }
        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(new CapturedRequest(request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues(DesktopSession.WorkspaceHeaderName, out var values) ? Assert.Single(values) : null,
                request.Headers.Authorization?.Scheme));
            return Task.FromResult(new HttpResponseMessage(RejectFirstRequest && Requests.Count == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
            { Content = new StringContent("[]") });
        }
    }
}
