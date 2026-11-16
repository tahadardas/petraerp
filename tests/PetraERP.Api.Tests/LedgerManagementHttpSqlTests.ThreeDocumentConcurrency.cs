using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PetraERP.Application.CompanyOptions;
using PetraERP.Contracts.DocumentProcessing;
using PetraERP.Contracts.Ledger;
using PetraERP.Contracts.Patterns;
using PetraERP.Contracts.Treasury;

namespace PetraERP.Api.Tests;

public sealed partial class LedgerManagementHttpSqlTests
{
    /// <summary>
    /// Purchase, sale and receipt voucher posted at once by every user (1..50): inventory, numbering, party balances and the
    /// ledger are all contended together. Asserts the financial invariants, not speed.
    /// </summary>
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MeasureConcurrentPurchaseSaleAndReceiptKeepLedgerStockAndNumbersExact()
    {
        const int opening = 5000;
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var fixture = await PrepareNumberedSalesAsync(host, client, token, openingStock: opening.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);

            async Task<T> CreateAsync<T>(string path, object payload)
            {
                var response = await SendAsync<T>(host, client, HttpMethod.Post, root + path, token, payload).ConfigureAwait(true);
                Assert.True(response.Status is HttpStatusCode.OK or HttpStatusCode.Created, $"{path}: {response.Status} / {response.Code}");
                return response.Body!;
            }

            var cash = (await CreateAsync<AccountResponse>("/ledger/chart/accounts", new AccountRequest
            {
                Code = "11001", NameArabic = "صندوق اختبار الضغط", AccountType = AccountType.Assets, PostingAllowed = true,
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
            var receiptRevision = pattern.ActiveRevision!.Id;

            var lines = new List<string> { "users;requests;posted;not_posted;http_5xx;wall_ms;p50_ms;p95_ms;max_ms;codes" };
            var serverErrors = 0;
            var notPosted = 0;
            foreach (var users in new[] { 1, 5, 10, 25, 50 })
            {
                var outcomes = new ConcurrentBag<ConcurrentOutcome>();
                var wall = Stopwatch.StartNew();
                await Task.WhenAll(Enumerable.Range(0, users).Select(_ => Task.WhenAll(
                    Task.Run(async () => outcomes.Add(await PostOnePurchaseAsync(host, client, token, root, fixture).ConfigureAwait(false)), Cancellation),
                    Task.Run(async () => outcomes.Add(await PostOneSaleAsync(host, client, token, root, fixture).ConfigureAwait(false)), Cancellation),
                    Task.Run(async () => outcomes.Add(await PostOneReceiptAsync(host, client, token, root, fixture, receiptRevision).ConfigureAwait(false)), Cancellation)))).ConfigureAwait(true);
                wall.Stop();

                var all = outcomes.ToArray();
                var posted = all.Count(o => o.Status is >= 200 and < 300 && o.Code is not null && o.Code.StartsWith("Posted", StringComparison.Ordinal));
                var errors = all.Count(o => o.Status >= 500);
                serverErrors += errors;
                notPosted += all.Length - posted;
                var times = all.Select(o => o.Milliseconds).Order().ToArray();
                var codes = string.Join('|', all.Where(o => o.Status is < 200 or >= 300 || o.Code?.StartsWith("Posted", StringComparison.Ordinal) != true)
                    .GroupBy(o => $"{o.Status}:{o.Code}").Select(g => $"{g.Key}x{g.Count()}"));
                lines.Add(FormattableString.Invariant(
                    $"{users};{all.Length};{posted};{all.Length - posted};{errors};{wall.Elapsed.TotalMilliseconds:F0};{times[(times.Length - 1) / 2]:F0};{times[Math.Max(0, (int)Math.Ceiling(times.Length * 0.95) - 1)]:F0};{times[^1]:F0};{codes}"));
            }

            using var annual = host.AnnualContext();
            static decimal D(object? v) => Convert.ToDecimal(v, CultureInfo.InvariantCulture);
            var imbalance = D(await ScalarAsync(annual, "SELECT COALESCE(SUM(BaseDebit), 0) - COALESCE(SUM(BaseCredit), 0) FROM ledger.JournalLines").ConfigureAwait(true));
            var dupSales = D(await ScalarAsync(annual, "SELECT COUNT(InvoiceNumber) - COUNT(DISTINCT InvoiceNumber) FROM sales.Invoices").ConfigureAwait(true));
            var dupPurchases = D(await ScalarAsync(annual, "SELECT COUNT(InvoiceNumber) - COUNT(DISTINCT InvoiceNumber) FROM purchases.Invoices WHERE InvoiceNumber IS NOT NULL").ConfigureAwait(true));
            var dupVouchers = D(await ScalarAsync(annual, "SELECT COUNT(VoucherNumber) - COUNT(DISTINCT VoucherNumber) FROM treasury.Vouchers WHERE VoucherNumber IS NOT NULL").ConfigureAwait(true));
            var postedPurchases = D(await ScalarAsync(annual, "SELECT COUNT(*) FROM purchases.Invoices WHERE Status = 2").ConfigureAwait(true)) - 1;
            var postedSales = D(await ScalarAsync(annual, "SELECT COUNT(*) FROM sales.Invoices WHERE Status = 2").ConfigureAwait(true));
            var postedVouchers = D(await ScalarAsync(annual, "SELECT COUNT(*) FROM treasury.Vouchers WHERE Status = 2").ConfigureAwait(true));
            var journalsExpected = 1 + postedPurchases + postedSales + postedVouchers;
            var journals = D(await ScalarAsync(annual, "SELECT COUNT(*) FROM ledger.JournalEntries").ConfigureAwait(true));
            var stock = D(await ScalarAsync(annual, "SELECT COALESCE(SUM(BaseQuantity), 0) FROM inventory.StockMovements").ConfigureAwait(true));
            var expectedStock = opening + 3m * postedPurchases - 3m * postedSales;
            lines.Add(string.Empty);
            lines.Add(FormattableString.Invariant($"integrity: ledger_imbalance={imbalance}; duplicate_numbers(sales/purchases/vouchers)={dupSales}/{dupPurchases}/{dupVouchers}; posted(p/s/v)={postedPurchases}/{postedSales}/{postedVouchers}; journals={journals} expected={journalsExpected}; stock={stock} expected={expectedStock}"));

            var path = Environment.GetEnvironmentVariable("PETRAERP_BENCH_OUT_MIXED3") ?? Path.Combine(Path.GetTempPath(), "petra_mixed3_bench.txt");
            await File.WriteAllLinesAsync(path, lines, Cancellation).ConfigureAwait(true);

            Assert.Equal(0m, imbalance);
            Assert.Equal(0m, dupSales + dupPurchases + dupVouchers);
            Assert.Equal(expectedStock, stock);
            Assert.Equal(journalsExpected, journals);
            Assert.Equal(0, serverErrors);
            Assert.Equal(0, notPosted);
        }
    }

    private static async Task<ConcurrentOutcome> PostOneReceiptAsync(ApiSqlHost host, HttpClient client, string token, string root, NumberedSaleFixture fixture, Guid revisionId)
    {
        var body = new SaveAndProcessVoucherRequest
        {
            Document = new SaveVoucherRequest
            {
                Kind = VoucherKind.Receipt, PatternRevisionId = revisionId, PartyId = fixture.CustomerId, SettlementMethod = SettlementMethod.Cash,
                VoucherDateUtc = new DateTimeOffset(2026, 1, 25, 12, 0, 0, TimeSpan.Zero), CurrencyCode = "JOD", ExchangeRate = "1", Amount = "10",
            },
            IdempotencyKey = $"mixed-v-{Guid.NewGuid():N}",
        };
        using var request = host.WorkspaceRequest(HttpMethod.Post, root + "/treasury/vouchers/save-and-process", token,
            JsonContent.Create(body, body.GetType(), options: ApiJson.Options));
        var timer = Stopwatch.StartNew();
        using var response = await client.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
        timer.Stop();
        string? code;
        try
        {
            using var parsed = JsonDocument.Parse(text);
            code = response.IsSuccessStatusCode
                ? (parsed.RootElement.TryGetProperty("outcome", out var o) ? o.GetString() : null) + "/" + (parsed.RootElement.TryGetProperty("failureCode", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null)
                : parsed.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
        }
        catch (JsonException)
        {
            code = "non-json";
        }

        return new ConcurrentOutcome((int)response.StatusCode, code, timer.Elapsed.TotalMilliseconds);
    }
}
