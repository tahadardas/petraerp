using System.Net;
using System.Net.Http;
using System.Text;
using PetraERP.Desktop.Connection;
using Xunit;

namespace PetraERP.Desktop.Tests;

public sealed class TransientRetryHandlerTests
{
    private static readonly TimeSpan[] NoDelay = [TimeSpan.Zero, TimeSpan.Zero];

    private sealed class ScriptedHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int calls;
        public int Calls => calls;
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref calls) - 1;
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var status = statuses[Math.Min(index, statuses.Length - 1)];
            var body = status == HttpStatusCode.ServiceUnavailable ? "{\"code\":\"server.transient_failure\"}" : "{}";
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static HttpClient Client(ScriptedHandler inner) =>
        new(new TransientRetryHandler(inner, NoDelay)) { BaseAddress = new Uri("http://localhost/") };

    [Fact]
    public async Task KeyedPostIsReplayedWithSameBodyUntilSuccess()
    {
        var inner = new ScriptedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var client = Client(inner);
        using var content = new StringContent("{\"idempotencyKey\":\"k1\"}", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(new Uri("x", UriKind.Relative), content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
        Assert.All(inner.Bodies, b => Assert.Equal("{\"idempotencyKey\":\"k1\"}", b));
    }

    [Fact]
    public async Task PostWithoutIdempotencyKeyIsNeverReplayed()
    {
        var inner = new ScriptedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var client = Client(inner);
        using var content = new StringContent("{\"name\":\"a\"}", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(new Uri("x", UriKind.Relative), content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task RetriesStopAfterTwoReplays()
    {
        var inner = new ScriptedHandler(HttpStatusCode.ServiceUnavailable);
        using var client = Client(inner);
        using var response = await client.GetAsync(new Uri("x", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task OtherFailuresAreNotReplayed()
    {
        var inner = new ScriptedHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        using var client = Client(inner);
        using var response = await client.GetAsync(new Uri("x", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }
}
