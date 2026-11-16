using System.Net.Http;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;

namespace PetraERP.Desktop.Tests;

public sealed class PostingOutcomeProbeTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static readonly Guid Document = Guid.NewGuid();

    private static Task<PostingRequestStatusResponse> Status(PostingRequestStateCode state) => Task.FromResult(new PostingRequestStatusResponse
    {
        IdempotencyKey = "k", Kind = PostingRequestKindCode.SalesInvoice, State = state, DocumentId = Document,
        RecommendedAction = state == PostingRequestStateCode.Completed ? PostingRecommendedAction.DoNotRepost : PostingRecommendedAction.RetryWithSameKey,
    });

    private static Task<int> LostResponse() => throw new HttpRequestException("connection reset after commit");

    [Fact]
    public async Task APostThatSucceedsNeverAsksTheServer()
    {
        var asked = false;
        var result = await PostingOutcomeProbe.RunAsync("k", () => Task.FromResult(7), _ => { asked = true; return Status(PostingRequestStateCode.Completed); }, Cancellation).ConfigureAwait(true);
        Assert.Equal(7, result);
        Assert.False(asked);
    }

    [Fact]
    public async Task LostResponseWithACompletedPostTellsTheUserNotToPostAgain()
    {
        var outcome = await Assert.ThrowsAsync<PostingOutcomeException>(() =>
            PostingOutcomeProbe.RunAsync("k", LostResponse, _ => Status(PostingRequestStateCode.Completed), Cancellation)).ConfigureAwait(true);
        Assert.True(outcome.Posted);
        Assert.Equal(Document, outcome.DocumentId);
        Assert.Equal("PostingAlreadyCompleted", DesktopErrors.Key(outcome));
        Assert.IsType<HttpRequestException>(outcome.InnerException);
    }

    [Fact]
    public async Task LostResponseWithAnIncompletePostSaysItIsSafeToRetry()
    {
        var outcome = await Assert.ThrowsAsync<PostingOutcomeException>(() =>
            PostingOutcomeProbe.RunAsync("k", LostResponse, _ => Status(PostingRequestStateCode.NotCompleted), Cancellation)).ConfigureAwait(true);
        Assert.False(outcome.Posted);
        Assert.Equal("PostingNotCompleted", DesktopErrors.Key(outcome));
    }

    [Fact]
    public async Task AKeyTheServerNeverRegisteredMeansNothingWasPosted()
    {
        var outcome = await Assert.ThrowsAsync<PostingOutcomeException>(() =>
            PostingOutcomeProbe.RunAsync("k", LostResponse, _ => throw new PetraApiException("not found", 404, string.Empty, new Dictionary<string, IEnumerable<string>>(), null), Cancellation)).ConfigureAwait(true);
        Assert.False(outcome.Posted);
        Assert.Equal(Guid.Empty, outcome.DocumentId);
    }

    [Fact]
    public async Task WhenTheStatusQuestionAlsoFailsTheOriginalErrorSurvives()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            PostingOutcomeProbe.RunAsync("k", LostResponse, _ => throw new HttpRequestException("still down"), Cancellation)).ConfigureAwait(true);
    }

    [Fact]
    public async Task ServerRejectionsAndMissingKeysAreNotTreatedAsLostResponses()
    {
        var asked = 0;
        Task<PostingRequestStatusResponse> Query(CancellationToken _) { asked++; return Status(PostingRequestStateCode.Completed); }
        await Assert.ThrowsAsync<PetraApiException>(() => PostingOutcomeProbe.RunAsync<int>("k",
            () => throw new PetraApiException("conflict", 409, string.Empty, new Dictionary<string, IEnumerable<string>>(), null), Query, Cancellation)).ConfigureAwait(true);
        await Assert.ThrowsAsync<HttpRequestException>(() => PostingOutcomeProbe.RunAsync(null, LostResponse, Query, Cancellation)).ConfigureAwait(true);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync().ConfigureAwait(true);
        await Assert.ThrowsAsync<HttpRequestException>(() => PostingOutcomeProbe.RunAsync("k", LostResponse, Query, cancelled.Token)).ConfigureAwait(true);
        Assert.Equal(0, asked);
    }
}
