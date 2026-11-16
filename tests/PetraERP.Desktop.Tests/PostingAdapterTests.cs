using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;

namespace PetraERP.Desktop.Tests;

public sealed class PostingAdapterTests
{
    [Fact]
    public async Task GeneratedPostingAdapterPreservesCallerKeyAndDoesNotAutomaticallyRetryAnAmbiguousWrite()
    {
        using var auth = new LoginHandler(); using var resource = new PostingHandler();
        using var session = new DesktopSession(new Uri("https://localhost/"), auth, resource);
        await session.LoginAsync("test", "ephemeral", Cancellation).ConfigureAwait(true);
        var adapter = new GeneralLedgerClient(session); var company = Guid.NewGuid(); var branch = Guid.NewGuid(); var journal = Guid.NewGuid(); var key = Guid.NewGuid();
        var request = new PostJournalRequest { ExpectedVersion = "AAAAAAAAAAE=", Reason = "ترحيل بعد المراجعة" };
        await Assert.ThrowsAsync<PetraApiException<ProblemDetails>>(() => adapter.PostJournalAsync(company, branch, journal, request, key, Cancellation)).ConfigureAwait(true);
        Assert.Single(resource.Calls); // Transport must not manufacture a second financial write.
        var receipt = await adapter.PostJournalAsync(company, branch, journal, request, key, Cancellation).ConfigureAwait(true);
        Assert.Equal(key, receipt.IdempotencyKey); Assert.Equal(2, resource.Calls.Count);
        Assert.All(resource.Calls, call =>
        {
            Assert.Equal(key.ToString("D"), call.Key); Assert.Equal($"/api/v1/companies/{company}/branches/{branch}/ledger/journals/{journal}/post", call.Path);
            using var json = JsonDocument.Parse(call.Body); Assert.Equal(request.Reason, json.RootElement.GetProperty("reason").GetString());
            Assert.Equal(request.ExpectedVersion, json.RootElement.GetProperty("expectedVersion").GetString());
        });
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.PostJournalAsync(company, branch, journal, request, Guid.Empty, Cancellation)).ConfigureAwait(true);
        Assert.Equal(2, resource.Calls.Count);
    }

    private sealed class LoginHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new TokenResponse
            {
                AccessToken = "test-only", RefreshToken = "test-only", TokenType = "Bearer", SessionId = Guid.NewGuid(),
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10), RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), SessionExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            })),
        });
    }

    private sealed class PostingHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Options = CreateOptions();
        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new JsonStringEnumConverter()); return options;
        }
        internal List<(string Key, string Path, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Headers.GetValues("Idempotency-Key").Single(), request.RequestUri!.AbsolutePath,
                await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
            if (Calls.Count == 1) return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"code\":\"server.unavailable\"}") };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new PostingReceiptResponse
            {
                IdempotencyKey = Guid.Parse(Calls[^1].Key), JournalId = Guid.NewGuid(), Operation = PostingOperation.PostJournal,
                PostingSequence = 42,
            }, Options)) };
        }
    }
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
}
