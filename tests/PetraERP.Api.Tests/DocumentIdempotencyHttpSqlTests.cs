using System.Net;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.CompanyOptions;
using PetraERP.Contracts.DocumentProcessing;
using PetraERP.Contracts.Purchases;
using PetraERP.Contracts.Patterns;
using PetraERP.Contracts.Sales;
using PetraERP.Contracts.Treasury;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Tests;

/// <summary>
/// P01 acceptance on real SQL Server through the real API: the request key is durably bound to the hash of the complete
/// normalized input. An exact retry resolves to the stored result; any material change conflicts explicitly and creates
/// nothing; a Posted document is never "AlreadyPosted" for an unrelated payload.
/// </summary>
public sealed partial class LedgerManagementHttpSqlTests
{
    private const string RequestKeyConflict = "document.request_key_conflict";
    private const string PostedImmutable = "document.posted_immutable";

    /// <summary>Two lines of the SAME item (duplicated same-item lines) with explicit discount and tax amounts.</summary>
    private static CreateSalesInvoiceRequest DuplicatedItemSale(NumberedSaleFixture fixture) => new()
    {
        PatternRevisionId = fixture.RevisionId, CustomerId = fixture.CustomerId, WarehouseId = fixture.WarehouseId,
        InvoiceDateUtc = new DateTimeOffset(2026, 1, 20, 12, 0, 0, TimeSpan.Zero), CurrencyCode = "JOD", ExchangeRate = "1",
        PaymentMethod = PaymentMethod.Credit,
        Lines = [new() { ItemId = fixture.ItemId, UnitId = fixture.UnitId, Quantity = "1", UnitPrice = "10", DisplayOrder = 1 },
                 new() { ItemId = fixture.ItemId, UnitId = fixture.UnitId, Quantity = "2", UnitPrice = "10", DisplayOrder = 2 }],
    };

    private static CreateSalesInvoiceRequest Mutate(CreateSalesInvoiceRequest document, string mutation)
    {
        var first = document.Lines[0];
        return mutation switch
        {
            "one-cent-price" => document with { Lines = [first with { UnitPrice = "10.01" }, .. document.Lines.Skip(1)] },
            "last-fx-digit" => document with { ExchangeRate = "1.00000001" },
            "discount-amount" => document with { Lines = [first with { DiscountAmount = "0.01" }, .. document.Lines.Skip(1)] },
            "tax-percentage" => document with { Lines = [first with { TaxPercentage = "0.0001" }, .. document.Lines.Skip(1)] },
            "document-date" => document with { InvoiceDateUtc = document.InvoiceDateUtc!.Value.AddDays(1) },
            "quantity" => document with { Lines = [first with { Quantity = "1.5" }, .. document.Lines.Skip(1)] },
            // Same item, same totals (3 x 10), different request: the duplicated lines were merged.
            "merged-duplicate-lines" => document with { Lines = [first with { Quantity = "3" }] },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
    }

    [Theory(Explicit = true)]
    [InlineData("one-cent-price")]
    [InlineData("last-fx-digit")]
    [InlineData("discount-amount")]
    [InlineData("tax-percentage")]
    [InlineData("document-date")]
    [InlineData("quantity")]
    [InlineData("merged-duplicate-lines")]
    [Trait("Category", "SqlServer")]
    public async Task SalesExactRetryAfterPostResolvesToStoredResultAndAnyMaterialChangeConflictsWithoutNewRows(string mutation)
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var fixture = await PrepareNumberedSalesAsync(host, client, token).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);
            var key = $"idem-{mutation}-{Guid.NewGuid():N}";
            var original = DuplicatedItemSale(fixture);

            var first = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, root + "/sales/invoices/save-and-process", token,
                new SaveAndProcessSalesInvoiceRequest { Document = original, IdempotencyKey = key }).ConfigureAwait(true);
            Assert.True(first.Status == HttpStatusCode.Created, $"HTTP {(int)first.Status} / {first.Code}");
            Assert.Equal("Posted", first.Body!.Outcome);
            var (invoices, journals) = await CountsAsync(host).ConfigureAwait(true);

            // Exact retry of an already-posted request: same document, same journal, no new financial effect.
            var retry = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, root + "/sales/invoices/save-and-process", token,
                new SaveAndProcessSalesInvoiceRequest { Document = original, IdempotencyKey = key }).ConfigureAwait(true);
            Assert.True(retry.Status is HttpStatusCode.Created or HttpStatusCode.OK, $"retry: HTTP {(int)retry.Status} / {retry.Code}");
            Assert.Equal(first.Body.DocumentId, retry.Body!.DocumentId);
            Assert.Equal(first.Body.JournalEntryId, retry.Body.JournalEntryId);
            Assert.Equal("AlreadyPosted", retry.Body.Outcome);

            // The same key with a materially different request is an explicit conflict.
            var changed = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, root + "/sales/invoices/save-and-process", token,
                new SaveAndProcessSalesInvoiceRequest { Document = Mutate(original, mutation), IdempotencyKey = key }).ConfigureAwait(true);
            Assert.False(changed.Status is HttpStatusCode.Created or HttpStatusCode.OK, $"{mutation}: a changed request was accepted ({(int)changed.Status}).");
            Assert.Equal(RequestKeyConflict, changed.Code);
            Assert.Equal((invoices, journals), await CountsAsync(host).ConfigureAwait(true));

            // The durable binding exists exactly once for the key.
            using var annual = host.AnnualContext();
            var bindings = await annual.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [audit].[DocumentRequestBindings] WHERE [IdempotencyKey]={key}")
                .SingleAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(1, bindings);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SalesUpdateOfPostedInvoiceDistinguishesExactRetryFromChangedPayload()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var fixture = await PrepareNumberedSalesAsync(host, client, token).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);
            var key = $"posted-update-{Guid.NewGuid():N}";
            var original = DuplicatedItemSale(fixture);
            var created = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, root + "/sales/invoices/save-and-process", token,
                new SaveAndProcessSalesInvoiceRequest { Document = original, IdempotencyKey = key }).ConfigureAwait(true);
            Assert.Equal("Posted", created.Body!.Outcome);
            var id = created.Body.DocumentId;
            var (invoices, journals) = await CountsAsync(host).ConfigureAwait(true);
            var put = root + $"/sales/invoices/{id}/save-and-process";

            SaveAndProcessSalesInvoiceRequest Request(CreateSalesInvoiceRequest document, string requestKey) => new()
            { Document = document, InvoiceId = id, ExpectedVersion = created.Body.Version, IdempotencyKey = requestKey };

            // Exact retry of the command that was already applied.
            var retry = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Put, put, token, Request(original, key)).ConfigureAwait(true);
            Assert.True(retry.Status == HttpStatusCode.OK, $"HTTP {(int)retry.Status} / {retry.Code}");
            Assert.Equal("AlreadyPosted", retry.Body!.Outcome);

            // A different payload under a NEW key is an unauthorized edit of a Posted document - never AlreadyPosted.
            var edit = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Put, put, token,
                Request(Mutate(original, "one-cent-price"), $"edit-{Guid.NewGuid():N}")).ConfigureAwait(true);
            Assert.False(edit.Status == HttpStatusCode.OK);
            Assert.Equal(PostedImmutable, edit.Code);

            // A different payload under the ORIGINAL key is an explicit key conflict.
            var reused = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Put, put, token,
                Request(Mutate(original, "one-cent-price"), key)).ConfigureAwait(true);
            Assert.False(reused.Status == HttpStatusCode.OK);
            Assert.Equal(RequestKeyConflict, reused.Code);
            Assert.Equal((invoices, journals), await CountsAsync(host).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SimultaneousIdenticalSalesSavesProduceExactlyOneDocument()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var fixture = await PrepareNumberedSalesAsync(host, client, token).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, DocumentPostingMode.ApproveOnly).ConfigureAwait(true);
            var (invoicesBefore, _) = await CountsAsync(host).ConfigureAwait(true);
            var request = new SaveAndProcessSalesInvoiceRequest { Document = DuplicatedItemSale(fixture), IdempotencyKey = $"race-{Guid.NewGuid():N}" };

            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => SendAsync<DocumentProcessingResultDto>(host, client,
                HttpMethod.Post, root + "/sales/invoices/save-and-process", token, request))).ConfigureAwait(true);

            Assert.All(results, r => Assert.True(r.Status is HttpStatusCode.Created or HttpStatusCode.OK, $"HTTP {(int)r.Status} / {r.Code}"));
            Assert.Single(results.Select(r => r.Body!.DocumentId).Distinct());
            Assert.Equal(invoicesBefore + 1, (await CountsAsync(host).ConfigureAwait(true)).Invoices);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PurchaseExactRetryAfterPostResolvesAndOneCentChangeConflicts()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            await PrepareNumberedSalesAsync(host, client, token).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);
            using var annual = host.AnnualContext();
            var source = await annual.PurchaseInvoices.AsNoTracking().Include(i => i.Lines).SingleAsync(Cancellation).ConfigureAwait(true);
            var line = source.Lines.Single();
            SavePurchaseInvoiceRequest Document(string cost) => new()
            {
                PatternRevisionId = source.PatternRevisionId, SupplierId = source.SupplierId, WarehouseId = source.WarehouseId,
                InvoiceDateUtc = source.InvoiceDateUtc, CurrencyCode = "JOD", ExchangeRate = "1",
                Lines = [new() { ItemId = line.ItemId, UnitId = line.UnitId, Quantity = "1", UnitCost = cost },
                         new() { ItemId = line.ItemId, UnitId = line.UnitId, Quantity = "2", UnitCost = cost }],
            };
            var key = $"pidem-{Guid.NewGuid():N}";
            var path = root + "/purchases/invoices/save-and-process";
            var first = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, path, token,
                new SaveAndProcessPurchaseInvoiceRequest { Document = Document("4.00"), IdempotencyKey = key }).ConfigureAwait(true);
            Assert.True(first.Status == HttpStatusCode.Created, $"HTTP {(int)first.Status} / {first.Code}");
            Assert.Equal("Posted", first.Body!.Outcome);
            var (_, journals) = await CountsAsync(host).ConfigureAwait(true);

            // "4.00" and "4" are the same value; the retry must resolve to the stored posted result.
            var retry = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, path, token,
                new SaveAndProcessPurchaseInvoiceRequest { Document = Document("4"), IdempotencyKey = key }).ConfigureAwait(true);
            Assert.True(retry.Status is HttpStatusCode.Created or HttpStatusCode.OK, $"HTTP {(int)retry.Status} / {retry.Code}");
            Assert.Equal(first.Body.DocumentId, retry.Body!.DocumentId);
            Assert.Equal("AlreadyPosted", retry.Body.Outcome);

            var changed = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, path, token,
                new SaveAndProcessPurchaseInvoiceRequest { Document = Document("4.01"), IdempotencyKey = key }).ConfigureAwait(true);
            Assert.Equal(RequestKeyConflict, changed.Code);
            Assert.Equal(journals, (await CountsAsync(host).ConfigureAwait(true)).Journals);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task VoucherRetryAfterPostingAndAfterExtraSettlementBatchNeverDuplicatesAndChangesConflict()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var fixture = await PrepareNumberedSalesAsync(host, client, token).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);
            var sale = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, root + "/sales/invoices/save-and-process", token,
                new SaveAndProcessSalesInvoiceRequest { Document = DuplicatedItemSale(fixture), IdempotencyKey = $"vsale-{Guid.NewGuid():N}" }).ConfigureAwait(true);
            Assert.Equal("Posted", sale.Body!.Outcome);
            var invoiceId = sale.Body.DocumentId;

            async Task<T> CreateAsync<T>(string path, object payload)
            {
                var response = await SendAsync<T>(host, client, HttpMethod.Post, root + path, token, payload).ConfigureAwait(true);
                Assert.True(response.Status is HttpStatusCode.OK or HttpStatusCode.Created, $"{path}: {response.Status} / {response.Code}");
                return response.Body!;
            }
            var cash = (await CreateAsync<AccountResponse>("/ledger/chart/accounts", new AccountRequest
            {
                Code = "11001", NameArabic = "صندوق اختبار السندات", AccountType = AccountType.Assets, PostingAllowed = true,
                CostCenterPolicy = CostCenterPolicy.Optional, CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly,
            }).ConfigureAwait(true)).Id;
            var pattern = await CreateAsync<DocumentPatternResponse>("/patterns", new CreateDocumentPatternRequest
            {
                Code = "RC", NameArabic = "نمط سند قبض", ShortCode = "RC", OperationKind = BusinessOperationKind.CashReceipt, BranchIds = [host.BranchId],
                InitialConfiguration = new PatternConfigurationDto
                {
                    AccountMappings = [new() { Role = "Cash", AccountId = cash }, new() { Role = "Bank", AccountId = cash }],
                    NumberingPolicy = new() { Prefix = "RC-", MinimumDigits = 4, Scope = PatternNumberingScope.BranchAndFiscalYear },
                    ApprovalPolicy = new() { RequiresApproval = false },
                },
            }).ConfigureAwait(true);
            pattern = await CreateAsync<DocumentPatternResponse>($"/patterns/{pattern.Id}/publish", new PublishPatternRevisionRequest
            { ExpectedVersion = pattern.Version, RevisionId = pattern.ActiveRevision!.Id, Reason = "نشر نمط سند القبض" }).ConfigureAwait(true);

            SaveVoucherRequest Receipt(string amount, string settled) => new()
            {
                Kind = VoucherKind.Receipt, PatternRevisionId = pattern.ActiveRevision!.Id, PartyId = fixture.CustomerId, SettlementMethod = SettlementMethod.Cash,
                VoucherDateUtc = new DateTimeOffset(2026, 1, 25, 12, 0, 0, TimeSpan.Zero), CurrencyCode = "JOD", ExchangeRate = "1", Amount = amount,
                InvoiceAllocations = [new() { InvoiceId = invoiceId, InvoiceAmount = settled, SettlementAmount = settled, CrossRate = "1.00000000" }],
            };
            var vouchers = root + "/treasury/vouchers";
            var key = $"videm-{Guid.NewGuid():N}";
            var original = Receipt("20", "10");
            var first = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, vouchers + "/save-and-process", token,
                new SaveAndProcessVoucherRequest { Document = original, IdempotencyKey = key }).ConfigureAwait(true);
            Assert.True(first.Status == HttpStatusCode.Created, $"HTTP {(int)first.Status} / {first.Code}");
            Assert.True(first.Body!.Outcome == "Posted", $"{first.Body.Outcome} [{first.Body.FailureCode}] {first.Body.Message}");
            var before = await VoucherCountsAsync(host).ConfigureAwait(true);

            async Task AssertRetryResolvesAsync(string stage)
            {
                var retry = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, vouchers + "/save-and-process", token,
                    new SaveAndProcessVoucherRequest { Document = original, IdempotencyKey = key }).ConfigureAwait(true);
                Assert.True(retry.Status is HttpStatusCode.Created or HttpStatusCode.OK, $"{stage}: HTTP {(int)retry.Status} / {retry.Code}");
                Assert.Equal(first.Body.DocumentId, retry.Body!.DocumentId);
                Assert.Equal(first.Body.JournalEntryId, retry.Body.JournalEntryId);
                Assert.Equal("AlreadyPosted", retry.Body.Outcome);
            }

            await AssertRetryResolvesAsync("after first posting").ConfigureAwait(true);
            Assert.Equal(before, await VoucherCountsAsync(host).ConfigureAwait(true));

            // An additional settlement batch (the 10 JOD advance applied to the same invoice) changes the settlement
            // representation; the original request must still resolve to the stored result.
            var current = await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Get, vouchers + $"/{first.Body.DocumentId}", token).ConfigureAwait(true);
            var advance = await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Post, vouchers + $"/{first.Body.DocumentId}/apply-advance", token,
                new ApplyAdvanceRequest
                {
                    ExpectedVersion = current.Body!.Version, IdempotencyKey = $"vadv-{Guid.NewGuid():N}", Reason = "تسوية الدفعة المقدمة",
                    DateUtc = new DateTimeOffset(2026, 1, 26, 12, 0, 0, TimeSpan.Zero),
                    Lines = [new() { InvoiceId = invoiceId, InvoiceAmount = "10", SettlementAmount = "10", CrossRate = "1.00000000" }],
                }).ConfigureAwait(true);
            Assert.True(advance.Status == HttpStatusCode.OK, $"apply-advance: HTTP {(int)advance.Status} / {advance.Code}");
            var afterAdvance = await VoucherCountsAsync(host).ConfigureAwait(true);
            Assert.Equal(before.Vouchers, afterAdvance.Vouchers);
            Assert.Equal(before.Applications + 1, afterAdvance.Applications);
            await AssertRetryResolvesAsync("after extra settlement batch").ConfigureAwait(true);
            Assert.Equal(afterAdvance, await VoucherCountsAsync(host).ConfigureAwait(true));

            // Any material change under the same key conflicts: amount (one cent) and allocation amount/destination.
            foreach (var changed in new[] { Receipt("20.01", "10"), Receipt("20", "9.99") })
            {
                var conflict = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, vouchers + "/save-and-process", token,
                    new SaveAndProcessVoucherRequest { Document = changed, IdempotencyKey = key }).ConfigureAwait(true);
                Assert.Equal(RequestKeyConflict, conflict.Code);
            }
            Assert.Equal(afterAdvance, await VoucherCountsAsync(host).ConfigureAwait(true));

            // Editing a Posted voucher with a different payload is an unauthorized edit, not AlreadyPosted.
            var edit = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Put, vouchers + $"/{first.Body.DocumentId}/save-and-process", token,
                new SaveAndProcessVoucherRequest { Document = Receipt("20.01", "10"), VoucherId = first.Body.DocumentId, ExpectedVersion = current.Body.Version, IdempotencyKey = $"vedit-{Guid.NewGuid():N}" }).ConfigureAwait(true);
            Assert.Equal(PostedImmutable, edit.Code);
            Assert.Equal(afterAdvance, await VoucherCountsAsync(host).ConfigureAwait(true));
        }
    }

    private static async Task<(int Vouchers, int Journals, int Applications)> VoucherCountsAsync(ApiSqlHost host)
    {
        using var annual = host.AnnualContext();
        return (await annual.Vouchers.CountAsync(Cancellation).ConfigureAwait(true), await annual.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true),
            await annual.Set<PetraERP.Infrastructure.Treasury.SettlementApplicationRow>().CountAsync(Cancellation).ConfigureAwait(true));
    }

    private static async Task<(int Invoices, int Journals)> CountsAsync(ApiSqlHost host)
    {
        using var annual = host.AnnualContext();
        return (await annual.SalesInvoices.CountAsync(Cancellation).ConfigureAwait(true), await annual.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
    }
}
