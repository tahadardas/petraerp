using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Tests;

/// <summary>HTTP idempotency and concurrency semantics of the existing durable posting engine (explicit SQL tests).
/// Runs against the provisioned independent annual workspace selected through X-Petra-Workspace.</summary>
public sealed class LedgerPostingHttpSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PostingReplayReturnsTheSameReceiptAndAChangedBodyWithTheSameKeyIsAConflict()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var (companyId, branchId, _) = await LedgerSeed.CalendarAsync(host).ConfigureAwait(true);
            var (journalId, version) = await LedgerSeed.ApprovedJournalAsync(host, "JOD").ConfigureAwait(true);

            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            var key = Guid.NewGuid();
            var path = $"/api/v1/companies/{companyId}/branches/{branchId}/ledger/journals/{journalId}/post";
            var body = new PostJournalRequest { ExpectedVersion = version, Reason = "ترحيل قيد مبيعات معتمد" };

            var first = await PostAsync(host, client, admin.AccessToken, path, key, body).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, first.Status);
            Assert.Equal(1, first.Receipt!.PostingSequence);
            Assert.Equal(journalId, first.Receipt.JournalId);
            Assert.Equal(PostingOperation.PostJournal, first.Receipt.Operation);

            // Exact replay after a lost response: same receipt, no second posting or number.
            var replay = await PostAsync(host, client, admin.AccessToken, path, key, body).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, replay.Status);
            Assert.Equal(first.Receipt, replay.Receipt);

            var changed = await PostAsync(host, client, admin.AccessToken, path, key, body with { Reason = "سبب مختلف" }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Conflict, changed.Status);
            Assert.Equal("ledger.idempotency_conflict", changed.Code);

            // A different key against the already posted journal fails the version/state check, never posts twice.
            var otherKey = await PostAsync(host, client, admin.AccessToken, path, Guid.NewGuid(), body).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Conflict, otherKey.Status);
            Assert.Equal("concurrency.conflict", otherKey.Code);

            using var missingKey = host.WorkspaceRequest(HttpMethod.Post, path, admin.AccessToken, JsonContent.Create(body, options: ApiJson.Options));
            using var missingKeyResponse = await client.SendAsync(missingKey, Cancellation).ConfigureAwait(true);
            await ProblemAssertions.AssertProblemAsync(missingKeyResponse, HttpStatusCode.BadRequest, "request.idempotency_key_invalid")
                .ConfigureAwait(true);

            using var verify = host.AnnualContext();
            var posted = await verify.JournalEntries.AsNoTracking().SingleAsync(row => row.Id == journalId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)4, posted.State);
            Assert.Equal(1L, posted.PostingSequence);
            Assert.Equal(4L, await verify.JournalAudit.LongCountAsync(row => row.JournalId == journalId, Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await verify.PostingRequests.CountAsync(row => row.CompletedAtUtc != null, Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ConcurrentDuplicateHttpRequestsPostExactlyOnce()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var (companyId, branchId, _) = await LedgerSeed.CalendarAsync(host).ConfigureAwait(true);
            var (journalId, version) = await LedgerSeed.ApprovedJournalAsync(host, "JOD").ConfigureAwait(true);

            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            var key = Guid.NewGuid();
            var path = $"/api/v1/companies/{companyId}/branches/{branchId}/ledger/journals/{journalId}/post";
            var body = new PostJournalRequest { ExpectedVersion = version, Reason = "ترحيل متزامن" };

            var results = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => PostAsync(host, client, admin.AccessToken, path, key, body))).ConfigureAwait(true);
            var successes = results.Where(static result => result.Status == HttpStatusCode.OK).ToArray();
            Assert.NotEmpty(successes);
            Assert.All(successes, success => Assert.Equal(successes[0].Receipt, success.Receipt));
            Assert.All(results.Where(static result => result.Status != HttpStatusCode.OK), static result =>
                Assert.Equal(HttpStatusCode.ServiceUnavailable, result.Status));

            using var verify = host.AnnualContext();
            var posted = await verify.JournalEntries.AsNoTracking().SingleAsync(row => row.Id == journalId, Cancellation).ConfigureAwait(true);
            Assert.Equal(1L, posted.PostingSequence);
            var sequence = await verify.PostingSequences.AsNoTracking().SingleAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(1L, sequence.LastSequence);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task StaleVersionsAndClosedPeriodsAreRejectedWithoutPosting()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var (companyId, branchId, periodId) = await LedgerSeed.CalendarAsync(host).ConfigureAwait(true);
            var (journalId, _) = await LedgerSeed.ApprovedJournalAsync(host, "JOD").ConfigureAwait(true);
            var (closedJournalId, closedVersion) = await LedgerSeed.ApprovedJournalAsync(host, "JOD").ConfigureAwait(true);

            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            var stale = await PostAsync(host, client, admin.AccessToken,
                $"/api/v1/companies/{companyId}/branches/{branchId}/ledger/journals/{journalId}/post", Guid.NewGuid(),
                new PostJournalRequest { ExpectedVersion = "AAAAAAAAAAE=", Reason = "إصدار قديم" }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Conflict, stale.Status);
            Assert.Equal("concurrency.conflict", stale.Code);

            using (var context = host.AnnualContext())
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE [ledger].[AccountingPeriods] SET [State] = 2 WHERE [Id] = {periodId}", Cancellation).ConfigureAwait(true);
            }

            var closed = await PostAsync(host, client, admin.AccessToken,
                $"/api/v1/companies/{companyId}/branches/{branchId}/ledger/journals/{closedJournalId}/post", Guid.NewGuid(),
                new PostJournalRequest { ExpectedVersion = closedVersion, Reason = "فترة مغلقة" }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, closed.Status);
            Assert.Equal("ledger.period_closed", closed.Code);

            using var verify = host.AnnualContext();
            Assert.Equal(0, await verify.JournalEntries.CountAsync(static row => row.State == 4, Cancellation).ConfigureAwait(true));
            Assert.Equal(0, await verify.PostingSequences.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    private static async Task<PostResult> PostAsync(ApiSqlHost host, HttpClient client, string accessToken, string path, Guid key, object body)
    {
        using var request = host.WorkspaceRequest(HttpMethod.Post, path, accessToken, JsonContent.Create(body, options: ApiJson.Options));
        request.Headers.Add("Idempotency-Key", key.ToString("D"));
        using var response = await client.SendAsync(request, Cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return new PostResult(response.StatusCode,
                await response.Content.ReadFromJsonAsync<PostingReceiptResponse>(ApiJson.Options, Cancellation).ConfigureAwait(false), null);
        }

        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(false));
        return new PostResult(response.StatusCode, null, problem.RootElement.GetProperty("code").GetString());
    }

    private sealed record PostResult(HttpStatusCode Status, PostingReceiptResponse? Receipt, string? Code);
}
