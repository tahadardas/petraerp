using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PetraERP.Application.CompanyOptions;
using PetraERP.Contracts.DocumentProcessing;
using PetraERP.Contracts.Posting;
using PetraERP.Contracts.Sales;

namespace PetraERP.Api.Tests;

public sealed partial class LedgerManagementHttpSqlTests
{
    /// <summary>
    /// The lost-response case on real SQL: the invoice was posted and the client never saw the reply. Asking the server for the
    /// status of the key it used must say "completed, do not post again" and name the document; repeating the same request
    /// must replay the stored result without a second posting; an unknown key must be 404, never a guess.
    /// </summary>
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task StatusOfAKeyAfterALostResponseSaysPostedAndTheReplayDoesNotPostTwice()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var fixture = await PrepareNumberedSalesAsync(host, client, token, openingStock: "50").ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);

            var key = $"lost-{Guid.NewGuid():N}";
            var body = new SaveAndProcessSalesInvoiceRequest { Document = TwoLineSale(fixture), IdempotencyKey = key };
            async Task<(HttpStatusCode Status, string Text)> PostAsync()
            {
                using var request = host.WorkspaceRequest(HttpMethod.Post, root + "/sales/invoices/save-and-process", token, JsonContent.Create(body, body.GetType(), options: ApiJson.Options));
                using var response = await client.SendAsync(request, Cancellation).ConfigureAwait(true);
                return (response.StatusCode, await response.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(true));
            }

            async Task<(HttpStatusCode Status, PostingRequestStatusResponse? Body)> StatusAsync(string statusKey)
            {
                using var request = host.WorkspaceRequest(HttpMethod.Get, root + "/posting-requests/" + Uri.EscapeDataString(statusKey), token);
                using var response = await client.SendAsync(request, Cancellation).ConfigureAwait(true);
                var text = await response.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(true);
                return (response.StatusCode, response.IsSuccessStatusCode ? JsonSerializer.Deserialize<PostingRequestStatusResponse>(text, ApiJson.Options) : null);
            }

            Assert.Equal(HttpStatusCode.NotFound, (await StatusAsync(key).ConfigureAwait(true)).Status); // nothing registered yet

            var first = await PostAsync().ConfigureAwait(true);
            Assert.True(first.Status is HttpStatusCode.OK or HttpStatusCode.Created, first.Text);
            // From here the client behaves as if that response never arrived.
            var status = await StatusAsync(key).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status.Status);
            Assert.Equal(PostingRequestStateCode.Completed, status.Body!.State);
            Assert.Equal(PostingRecommendedAction.DoNotRepost, status.Body.RecommendedAction);
            Assert.Equal(PostingRequestKindCode.SalesInvoice, status.Body.Kind);
            Assert.NotEqual(Guid.Empty, status.Body.DocumentId);

            using var annual = host.AnnualContext();
            var invoicesBefore = Convert.ToInt32(await ScalarAsync(annual, "SELECT COUNT(*) FROM sales.Invoices").ConfigureAwait(true), System.Globalization.CultureInfo.InvariantCulture);
            var journalsBefore = Convert.ToInt32(await ScalarAsync(annual, "SELECT COUNT(*) FROM ledger.JournalEntries").ConfigureAwait(true), System.Globalization.CultureInfo.InvariantCulture);
            var replay = await PostAsync().ConfigureAwait(true);
            Assert.True(replay.Status is HttpStatusCode.OK or HttpStatusCode.Created, replay.Text);
            Assert.Equal(invoicesBefore, Convert.ToInt32(await ScalarAsync(annual, "SELECT COUNT(*) FROM sales.Invoices").ConfigureAwait(true), System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(journalsBefore, Convert.ToInt32(await ScalarAsync(annual, "SELECT COUNT(*) FROM ledger.JournalEntries").ConfigureAwait(true), System.Globalization.CultureInfo.InvariantCulture));

            Assert.Equal(HttpStatusCode.NotFound, (await StatusAsync("never-used-" + Guid.NewGuid().ToString("N")).ConfigureAwait(true)).Status);
        }
    }
}
