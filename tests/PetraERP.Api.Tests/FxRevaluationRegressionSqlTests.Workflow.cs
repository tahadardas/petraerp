using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Tests;

public sealed partial class FxRevaluationRegressionSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task LostSuccessfulResponseReplaysTheCommittedJournal()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, Oct, 5000).ConfigureAwait(true);
            var token = await AdminTokenAsync(host).ConfigureAwait(true);
            using var lost = new HttpClient(new LoseFxResponseHandler(host.Factory.Server.CreateHandler())) { BaseAddress = new Uri("https://localhost/") };
            lost.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            lost.DefaultRequestHeaders.Add(ApiSqlHost.WorkspaceHeader, host.WorkspaceId.ToString("D"));
            var request = Exec(a, Oct, Guid.NewGuid().ToString("N"));
            await Assert.ThrowsAsync<HttpRequestException>(async () => await ExecuteAsync(lost, host, request).ConfigureAwait(false)).ConfigureAwait(true);
            using var retry = Workspace(host, token);
            var (status, result, _) = await ExecuteAsync(retry, host, request).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            using var annual = host.AnnualContext();
            Assert.Equal(result!.JournalEntryId, await annual.JournalEntries.Where(j => j.PostingSequence < 5000).Select(j => j.Id).SingleAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(9m, await GainBalanceAsync(host, a.Gain).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task FxReversalRestoresOriginalCarryingValueAndCanBeRevaluedAgain()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, Oct, 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));
            var (status, fx, _) = await ExecuteAsync(client, host, Exec(a, Oct, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}/ledger/journals";
            var original = await client.GetFromJsonAsync<JournalDetailsResponse>(new Uri($"{root}/{fx!.JournalEntryId}", UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
            using var create = await client.PostAsJsonAsync(new Uri($"{root}/{fx.JournalEntryId}/create-reversal-draft", UriKind.Relative),
                new CreateReversalDraftRequest { OriginalJournalId = fx.JournalEntryId, OriginalExpectedVersion = original!.Version, AccountingDate = Oct, Reason = "عكس فروقات الصرف" }, ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var draft = (await create.Content.ReadFromJsonAsync<JournalDetailsResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true))!;
            foreach (var action in new[] { "validate", "approve" })
            {
                using var response = await client.PostAsJsonAsync(new Uri($"{root}/drafts/{draft.Id}/{action}", UriKind.Relative),
                    new JournalActionRequest { ExpectedVersion = draft.Version, Reason = "عكس فروقات الصرف" }, ApiJson.Options, Cancellation).ConfigureAwait(true);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                draft = (await response.Content.ReadFromJsonAsync<JournalDetailsResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true))!;
            }
            original = await client.GetFromJsonAsync<JournalDetailsResponse>(new Uri($"{root}/{fx.JournalEntryId}", UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
            using var post = new HttpRequestMessage(HttpMethod.Post, new Uri($"{root}/{draft.Id}/post-reversal", UriKind.Relative));
            post.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
            post.Content = JsonContent.Create(new PostJournalReversalRequest { ExpectedVersion = draft.Version, OriginalJournalId = fx.JournalEntryId, OriginalExpectedVersion = original!.Version, Reason = "عكس فروقات الصرف" }, options: ApiJson.Options);
            using var posted = await client.SendAsync(post, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
            var (previewStatus, preview, _) = await PreviewAsync(client, host, Oct).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, previewStatus);
            Assert.Equal("9.0000", preview!.TotalUnrealizedGains);
            var (again, result, _) = await ExecuteAsync(client, host, Exec(a, Oct, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, again);
            Assert.Equal("9.0000", result!.TotalGainAmount);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ForeignSupplierCreditExposureProducesAnUnrealizedLoss()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, Oct, 5000, debitExposure: false).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));
            var (status, body, _) = await ExecuteAsync(client, host, Exec(a, Oct, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("9.0000", body!.TotalLossAmount);
            var (previewStatus, preview, _) = await PreviewAsync(client, host, Oct).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, previewStatus);
            Assert.Empty(preview!.Items);
        }
    }

    private sealed class LoseFxResponseHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode && request.RequestUri!.AbsolutePath.EndsWith("/fx-revaluation/execute", StringComparison.Ordinal))
            {
                response.Dispose();
                throw new HttpRequestException("Controlled transport loss after the real API committed.");
            }
            return response;
        }
    }
}
