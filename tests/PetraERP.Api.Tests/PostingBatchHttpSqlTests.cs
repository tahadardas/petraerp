using System.Net;
using Microsoft.EntityFrameworkCore;
using PetraERP.Contracts.DocumentProcessing;
using PetraERP.Contracts.Sales;

namespace PetraERP.Api.Tests;

/// <summary>
/// HTTP acceptance for the bulk posting endpoints: preview, confirmed start, replay, read-back and resume all go
/// through the real API on a disposable SQL Server workspace, and the posted invoice must have one journal and one
/// stock movement.
/// </summary>
public sealed partial class LedgerManagementHttpSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PostingBatchPreviewStartReplayResultsAndResumeDoNotDoublePost()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var fixture = await PrepareNumberedSalesAsync(host, client, token).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";

            var created = await SendAsync<SalesInvoiceDetailDto>(host, client, HttpMethod.Post, root + "/sales/invoices", token,
                new CreateSalesInvoiceRequest
                {
                    PatternRevisionId = fixture.RevisionId, CustomerId = fixture.CustomerId, WarehouseId = fixture.WarehouseId,
                    InvoiceDateUtc = new DateTimeOffset(2026, 1, 20, 12, 0, 0, TimeSpan.Zero), CurrencyCode = "JOD",
                    ExchangeRate = "1", PaymentMethod = PetraERP.Contracts.Sales.PaymentMethod.Credit,
                    Lines = [new() { ItemId = fixture.ItemId, UnitId = fixture.UnitId, Quantity = "2", UnitPrice = "10", DisplayOrder = 1 }],
                }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, created.Status);
            var invoiceId = created.Body!.Id;
            var filter = new BulkPostingFilterDto { DocumentTypes = [BulkPostingDocumentType.SalesInvoice] };

            var preview = await SendAsync<BulkPostingPreviewResponse>(host, client, HttpMethod.Post,
                root + "/posting-batches/preview", token, new BulkPostingPreviewRequest { Filter = filter }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, preview.Status);
            Assert.Equal(1, preview.Body!.TotalExamined);
            Assert.Equal(1, preview.Body.NeedsApproval);
            var candidate = Assert.Single(preview.Body.Items);
            Assert.Equal(invoiceId, candidate.DocumentId);
            Assert.True(candidate.Postable);
            Assert.Equal(BulkPostingClassification.CanBeApprovedAutomatically, candidate.Classification);

            var request = new StartBulkPostingRequest
            {
                IdempotencyKey = "http-posting-batch-1",
                Filter = filter,
                Confirmed = true,
                Reason = "اختبار HTTP للترحيل الجماعي",
            };
            var unconfirmed = await SendAsync<BulkPostingBatchDto>(host, client, HttpMethod.Post, root + "/posting-batches",
                token, request with { Confirmed = false }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, unconfirmed.Status);
            Assert.Equal("posting.batch_confirmation_required", unconfirmed.Code);
            using (var untouched = host.AnnualContext())
                Assert.Null((await untouched.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, Cancellation).ConfigureAwait(true)).JournalEntryId);

            var started = await SendAsync<BulkPostingBatchDto>(host, client, HttpMethod.Post, root + "/posting-batches", token, request)
                .ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, started.Status);
            Assert.Equal(BulkPostingBatchStatus.Completed, started.Body!.Status);
            Assert.Equal(1, started.Body.Posted);
            Assert.Equal(1, started.Body.Eligible);
            Assert.Equal(0, started.Body.Failed);
            var batchId = started.Body.BatchId;

            var replay = await SendAsync<BulkPostingBatchDto>(host, client, HttpMethod.Post, root + "/posting-batches", token, request)
                .ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, replay.Status);
            Assert.Equal(batchId, replay.Body!.BatchId);
            Assert.Equal(1, replay.Body.Posted);

            var fetched = await SendAsync<BulkPostingBatchDto>(host, client, HttpMethod.Get,
                root + $"/posting-batches/{batchId}", token).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, fetched.Status);
            Assert.Equal(batchId, fetched.Body!.BatchId);
            Assert.Equal(1, fetched.Body.Posted);

            var results = await SendAsync<BulkPostingResultsResponse>(host, client, HttpMethod.Get,
                root + $"/posting-batches/{batchId}/results", token).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, results.Status);
            Assert.Equal(1, results.Body!.TotalCount);
            var row = Assert.Single(results.Body.Items);
            Assert.Equal(invoiceId, row.DocumentId);
            Assert.Equal(BulkPostingItemStatus.Posted, row.Status);
            Assert.Equal("Posted", row.NewStatus);
            Assert.NotNull(row.JournalEntryId);
            Assert.False(string.IsNullOrWhiteSpace(row.PostingNumber));
            Assert.Equal(1, row.AttemptCount);

            var resumed = await SendAsync<BulkPostingBatchDto>(host, client, HttpMethod.Post,
                root + $"/posting-batches/{batchId}/resume", token, new ResumeBulkPostingRequest { RetryFailed = true })
                .ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, resumed.Status);
            Assert.Equal(batchId, resumed.Body!.BatchId);
            Assert.Equal(1, resumed.Body.Posted);

            using var annual = host.AnnualContext();
            var invoice = await annual.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal((int)PetraERP.Domain.Sales.SalesInvoiceStatus.Posted, invoice.Status);
            Assert.Equal(row.JournalEntryId, invoice.JournalEntryId);
            Assert.Equal(1, await annual.JournalEntries.CountAsync(j => j.Id == invoice.JournalEntryId, Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await annual.StockMovements.CountAsync(m => m.DocumentId == invoiceId, Cancellation).ConfigureAwait(true));
            var journal = await annual.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == invoice.JournalEntryId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)PetraERP.Domain.GeneralLedger.JournalEntryState.Posted, journal.State);
        }
    }
}
