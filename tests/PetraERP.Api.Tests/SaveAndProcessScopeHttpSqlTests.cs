using System.Net;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.CompanyOptions;
using PetraERP.Contracts.DocumentProcessing;
using PetraERP.Contracts.Purchases;
using PetraERP.Contracts.Sales;

namespace PetraERP.Api.Tests;

/// <summary>
/// Phase 01 regression: /save-and-process must run Save, Approve and Post each under its own bound accounting
/// operation. Before the fix, the request context stayed bound to ManageSalesInvoices and AccountingMutationPolicy
/// rejected Draft→Approved with HTTP 422 accounting.operation_denied.
/// </summary>
public sealed partial class LedgerManagementHttpSqlTests
{
    [Theory(Explicit = true)]
    [InlineData(DocumentPostingMode.ApproveOnly)]
    [InlineData(DocumentPostingMode.ApproveAndPost)]
    [Trait("Category", "SqlServer")]
    public async Task SalesSaveAndProcessNewTwoLineInvoiceAdvancesUnderEachPhaseOperation(DocumentPostingMode mode)
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var fixture = await PrepareNumberedSalesAsync(host, client, token).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, mode).ConfigureAwait(true);
            using (var before = host.AnnualContext())
            {
                var journalsBefore = await before.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true);
                var response = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, root + "/sales/invoices/save-and-process", token,
                    new SaveAndProcessSalesInvoiceRequest
                    {
                        Document = TwoLineSale(fixture), IdempotencyKey = $"scope-{mode}-{Guid.NewGuid():N}",
                    }).ConfigureAwait(true);

                Assert.True(response.Status == HttpStatusCode.Created, $"HTTP {(int)response.Status} / {response.Code} (accounting.operation_denied = the Phase 01 defect)");
                var body = response.Body!;
                using var after = host.AnnualContext();
                var row = await after.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == body.DocumentId, Cancellation).ConfigureAwait(true);
                if (mode == DocumentPostingMode.ApproveOnly)
                {
                    Assert.Equal("Approved", body.Outcome);
                    Assert.Equal((int)PetraERP.Domain.Sales.SalesInvoiceStatus.Approved, row.Status);
                    Assert.Equal(journalsBefore, await after.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
                    Assert.Equal(0, await after.StockMovements.CountAsync(m => m.DocumentId == body.DocumentId, Cancellation).ConfigureAwait(true));
                }
                else
                {
                    Assert.Equal("Posted", body.Outcome);
                    Assert.Equal((int)PetraERP.Domain.Sales.SalesInvoiceStatus.Posted, row.Status);
                    Assert.Equal(journalsBefore + 1, await after.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
                    Assert.Equal(1, await after.JournalEntries.CountAsync(j => j.Id == body.JournalEntryId, Cancellation).ConfigureAwait(true));
                }
            }
        }
    }

    [Theory(Explicit = true)]
    [InlineData(DocumentPostingMode.ApproveOnly)]
    [InlineData(DocumentPostingMode.ApproveAndPost)]
    [Trait("Category", "SqlServer")]
    public async Task SalesSaveAndProcessEditOfExistingDraftAdvancesUnderEachPhaseOperation(DocumentPostingMode mode)
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var fixture = await PrepareNumberedSalesAsync(host, client, token).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, mode).ConfigureAwait(true);
            var draft = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, root + "/sales/invoices/save-and-process", token,
                new SaveAndProcessSalesInvoiceRequest { Document = TwoLineSale(fixture), SaveAsDraft = true, IdempotencyKey = $"draft-{Guid.NewGuid():N}" }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, draft.Status);
            Assert.Equal("SavedDraft", draft.Body!.Outcome);

            var edited = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Put,
                root + $"/sales/invoices/{draft.Body.DocumentId}/save-and-process", token,
                new SaveAndProcessSalesInvoiceRequest
                {
                    Document = TwoLineSale(fixture), InvoiceId = draft.Body.DocumentId, ExpectedVersion = draft.Body.Version,
                    IdempotencyKey = $"edit-{mode}-{Guid.NewGuid():N}",
                }).ConfigureAwait(true);

            Assert.True(edited.Status == HttpStatusCode.OK, $"HTTP {(int)edited.Status} / {edited.Code}");
            Assert.Equal(draft.Body.DocumentId, edited.Body!.DocumentId);
            Assert.Equal(mode == DocumentPostingMode.ApproveOnly ? "Approved" : "Posted", edited.Body.Outcome);
        }
    }

    [Theory(Explicit = true)]
    [InlineData(DocumentPostingMode.ApproveOnly)]
    [InlineData(DocumentPostingMode.ApproveAndPost)]
    [Trait("Category", "SqlServer")]
    public async Task PurchaseSaveAndProcessNewInvoiceAdvancesUnderEachPhaseOperation(DocumentPostingMode mode)
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            await PrepareNumberedSalesAsync(host, client, token).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, mode).ConfigureAwait(true);
            using var annual = host.AnnualContext();
            var source = await annual.PurchaseInvoices.AsNoTracking().Include(i => i.Lines).SingleAsync(Cancellation).ConfigureAwait(true);
            var line = source.Lines.Single();

            var response = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, root + "/purchases/invoices/save-and-process", token,
                new SaveAndProcessPurchaseInvoiceRequest
                {
                    Document = new SavePurchaseInvoiceRequest
                    {
                        PatternRevisionId = source.PatternRevisionId, SupplierId = source.SupplierId, WarehouseId = source.WarehouseId,
                        InvoiceDateUtc = source.InvoiceDateUtc, CurrencyCode = "JOD", ExchangeRate = "1",
                        Lines = [new() { ItemId = line.ItemId, UnitId = line.UnitId, Quantity = "1", UnitCost = "4" },
                                 new() { ItemId = line.ItemId, UnitId = line.UnitId, Quantity = "2", UnitCost = "5" }],
                    },
                    IdempotencyKey = $"pscope-{mode}-{Guid.NewGuid():N}",
                }).ConfigureAwait(true);

            Assert.True(response.Status == HttpStatusCode.Created, $"HTTP {(int)response.Status} / {response.Code}");
            Assert.Equal(mode == DocumentPostingMode.ApproveOnly ? "Approved" : "Posted", response.Body!.Outcome);
        }
    }

    private static CreateSalesInvoiceRequest TwoLineSale(NumberedSaleFixture fixture) => new()
    {
        PatternRevisionId = fixture.RevisionId, CustomerId = fixture.CustomerId, WarehouseId = fixture.WarehouseId,
        InvoiceDateUtc = new DateTimeOffset(2026, 1, 20, 12, 0, 0, TimeSpan.Zero), CurrencyCode = "JOD", ExchangeRate = "1",
        PaymentMethod = PaymentMethod.Credit,
        Lines = [new() { ItemId = fixture.ItemId, UnitId = fixture.UnitId, Quantity = "1", UnitPrice = "10", DisplayOrder = 1 },
                 new() { ItemId = fixture.ItemId, UnitId = fixture.UnitId, Quantity = "2", UnitPrice = "12", DisplayOrder = 2 }],
    };

    private static async Task SetPostingModeAsync(ApiSqlHost host, HttpClient client, string token, string root, DocumentPostingMode mode)
    {
        var current = await SendAsync<CompanyPolicySnapshot>(host, client, HttpMethod.Get, root + "/company-options", token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, current.Status);
        var saved = await SendAsync<CompanyPolicySnapshot>(host, client, HttpMethod.Put, root + "/company-options", token,
            new { Policy = current.Body!.Policy with { DocumentPostingMode = mode }, ExpectedVersion = current.Body.Version, Reason = "اختبار نطاق الحفظ والترحيل" }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, saved.Status);
    }
}
