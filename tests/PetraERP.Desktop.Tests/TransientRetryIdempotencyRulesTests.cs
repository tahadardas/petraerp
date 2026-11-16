#pragma warning disable CA1707

using System.Net;
using System.Net.Http;
using System.Text;
using PetraERP.Desktop.Connection;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// Gate B / P0: a financial POST may be replayed after a transient 503 only when it provably carries a valid idempotency
/// key. Searching the body for the word "idempotencyKey" was not proof.
/// </summary>
public sealed class TransientRetryIdempotencyRulesTests
{
    private static readonly TimeSpan[] NoDelay = [TimeSpan.Zero, TimeSpan.Zero];

    private sealed class AlwaysTransient : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public List<string?> Keys { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Keys.Add(request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{\"code\":\"server.transient_failure\"}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private static async Task<int> CallsAsync(HttpMethod method, HttpContent? content, string? headerKey = null)
    {
        var inner = new AlwaysTransient();
        using var client = new HttpClient(new TransientRetryHandler(inner, NoDelay)) { BaseAddress = new Uri("http://localhost/") };
        using var request = new HttpRequestMessage(method, "x") { Content = content };
        if (headerKey is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", headerKey);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        return inner.Calls;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task ValidUuidHeaderKeyAllowsReplayWithTheSameKey()
    {
        var key = Guid.NewGuid().ToString("D");
        var inner = new AlwaysTransient();
        using var client = new HttpClient(new TransientRetryHandler(inner, NoDelay)) { BaseAddress = new Uri("http://localhost/") };
        using var request = new HttpRequestMessage(HttpMethod.Post, "x") { Content = Json("{\"reason\":\"post\"}") };
        request.Headers.Add("Idempotency-Key", key);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(3, inner.Calls);
        Assert.All(inner.Keys, k => Assert.Equal(key, k));   // never a new key on a replay
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task MalformedOrEmptyHeaderKeyIsNeverReplayed(string key) =>
        Assert.Equal(1, await CallsAsync(HttpMethod.Post, Json("{\"reason\":\"post\"}"), key).ConfigureAwait(true));

    [Theory]
    [InlineData("{\"idempotencyKey\":\"op-123\"}", 3)]
    [InlineData("{\"IdempotencyKey\":\"op-123\",\"x\":1}", 3)]
    [InlineData("{\"note\":\"mentions idempotencyKey in text\"}", 1)]
    [InlineData("{\"nested\":{\"idempotencyKey\":\"op-123\"}}", 1)]
    [InlineData("{\"idempotencyKey\":\"\"}", 1)]
    [InlineData("{\"idempotencyKey\":\"   \"}", 1)]
    [InlineData("{\"idempotencyKey\":null}", 1)]
    [InlineData("{\"idempotencyKey\":12345}", 1)]
    [InlineData("{\"idempotencyKey\":\"has space\"}", 1)]
    [InlineData("[\"idempotencyKey\"]", 1)]
    [InlineData("not json but idempotencyKey", 1)]
    public async Task BodyKeyIsParsedNotSearched(string body, int expectedCalls) =>
        Assert.Equal(expectedCalls, await CallsAsync(HttpMethod.Post, Json(body)).ConfigureAwait(true));

    [Fact]
    public async Task OverlongBodyKeyIsNotTrusted() =>
        Assert.Equal(1, await CallsAsync(HttpMethod.Post, Json($"{{\"idempotencyKey\":\"{new string('k', 129)}\"}}")).ConfigureAwait(true));

    [Fact]
    public async Task NonJsonBodiesAreNeverReplayedEvenWithTheWordInside()
    {
        using var text = new StringContent("idempotencyKey=abc", Encoding.UTF8, "application/x-www-form-urlencoded");
        Assert.Equal(1, await CallsAsync(HttpMethod.Post, text).ConfigureAwait(true));
        using var multipart = new MultipartFormDataContent { { new StringContent("idempotencyKey"), "file" } };
        Assert.Equal(1, await CallsAsync(HttpMethod.Post, multipart).ConfigureAwait(true));
    }

    [Fact]
    public async Task DeleteWithABodyAndOversizedBodiesAreNotReplayed()
    {
        Assert.Equal(1, await CallsAsync(HttpMethod.Delete, Json("{\"idempotencyKey\":\"op-1\"}")).ConfigureAwait(true));
        var big = $"{{\"idempotencyKey\":\"op-1\",\"padding\":\"{new string('x', 1_100_000)}\"}}";
        Assert.Equal(1, await CallsAsync(HttpMethod.Post, Json(big)).ConfigureAwait(true));
    }

    [Fact]
    public async Task ReadsAreStillReplayed()
    {
        Assert.Equal(3, await CallsAsync(HttpMethod.Get, null).ConfigureAwait(true));
        Assert.Equal(3, await CallsAsync(HttpMethod.Head, null).ConfigureAwait(true));
    }
}

public sealed class LostResponseReplayTests
{
    private static readonly TimeSpan[] NoDelay = [TimeSpan.Zero, TimeSpan.Zero];

    private sealed class FailsThenAnswers(int failures) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public List<string> Keys { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Keys.Add(request.Headers.TryGetValues("Idempotency-Key", out var v) ? v.Single() : string.Empty);
            if (Calls <= failures) throw new HttpRequestException("connection reset after commit");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    private static async Task<(HttpStatusCode? Status, int Calls, List<string> Keys)> SendAsync(int failures, bool keyed)
    {
        var inner = new FailsThenAnswers(failures);
        using var client = new HttpClient(new TransientRetryHandler(inner, NoDelay)) { BaseAddress = new Uri("http://localhost/") };
        using var request = new HttpRequestMessage(HttpMethod.Post, "x") { Content = new StringContent("{\"a\":1}", Encoding.UTF8, "application/json") };
        if (keyed) request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        try
        {
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(true);
            return (response.StatusCode, inner.Calls, inner.Keys);
        }
        catch (HttpRequestException)
        {
            return (null, inner.Calls, inner.Keys);
        }
    }

    [Fact]
    public async Task KeyedPostIsReplayedWithTheSameKeyAfterALostResponse()
    {
        var (status, calls, keys) = await SendAsync(failures: 2, keyed: true).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, calls);
        Assert.Single(keys.Distinct());      // one key for every attempt, never a new one
    }

    [Fact]
    public async Task UnkeyedPostIsNeverReplayedAfterALostResponse()
    {
        var (status, calls, _) = await SendAsync(failures: 1, keyed: false).ConfigureAwait(true);
        Assert.Null(status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ReplaysAfterLostResponsesAreBounded()
    {
        var (status, calls, _) = await SendAsync(failures: 10, keyed: true).ConfigureAwait(true);
        Assert.Null(status);
        Assert.Equal(3, calls);
    }
}
