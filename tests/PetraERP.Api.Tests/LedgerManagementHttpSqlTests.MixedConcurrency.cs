using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.CompanyOptions;
using PetraERP.Contracts.DocumentProcessing;
using PetraERP.Contracts.Purchases;

namespace PetraERP.Api.Tests;

public sealed partial class LedgerManagementHttpSqlTests
{
    /// <summary>
    /// Gate B: purchases and sales posted at the same time by 1/5/10/25/50 users, so inventory, cost layers, numbering and the
    /// ledger are contended across two document types. Asserts the financial invariants, not speed: balanced ledger, no
    /// duplicate numbers, every 2xx is really Posted, and stock on hand equals opening + purchased - sold exactly.
    /// </summary>
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MeasureConcurrentMixedPurchaseAndSalePostingKeepsLedgerAndStockExact()
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

            var lines = new List<string> { "users;requests;posted;not_posted;http_5xx;wall_ms;p50_ms;p95_ms;max_ms;codes" };
            var serverErrors = 0;
            var notPosted = 0;
            foreach (var users in new[] { 1, 5, 10, 25, 50 })
            {
                var outcomes = new ConcurrentBag<ConcurrentOutcome>();
                var wall = Stopwatch.StartNew();
                await Task.WhenAll(Enumerable.Range(0, users).Select(async _ =>
                {
                    outcomes.Add(await PostOnePurchaseAsync(host, client, token, root, fixture).ConfigureAwait(false));
                    outcomes.Add(await PostOneSaleAsync(host, client, token, root, fixture).ConfigureAwait(false));
                })).ConfigureAwait(true);
                wall.Stop();

                var all = outcomes.ToArray();
                var posted = all.Count(o => o.Status is >= 200 and < 300 && o.Code is not null && o.Code.StartsWith("Posted", StringComparison.Ordinal));
                var errors = all.Count(o => o.Status >= 500);
                serverErrors += errors;
                notPosted += all.Length - posted;
                var times = all.Select(o => o.Milliseconds).Order().ToArray();
                lines.Add(FormattableString.Invariant(
                    $"{users};{all.Length};{posted};{all.Length - posted};{errors};{wall.Elapsed.TotalMilliseconds:F0};{times[(times.Length - 1) / 2]:F0};{times[Math.Max(0, (int)Math.Ceiling(times.Length * 0.95) - 1)]:F0};{times[^1]:F0};{string.Join('|', all.Where(o => o.Status is < 200 or >= 300 || o.Code?.StartsWith("Posted", StringComparison.Ordinal) != true).GroupBy(o => $"{o.Status}:{o.Code}").Select(g => $"{g.Key}x{g.Count()}"))}"));
            }

            using var annual = host.AnnualContext();
            var imbalance = Convert.ToDecimal(await ScalarAsync(annual, "SELECT COALESCE(SUM(BaseDebit), 0) - COALESCE(SUM(BaseCredit), 0) FROM ledger.JournalLines").ConfigureAwait(true), CultureInfo.InvariantCulture);
            var duplicateSales = Convert.ToInt64(await ScalarAsync(annual, "SELECT COUNT(InvoiceNumber) - COUNT(DISTINCT InvoiceNumber) FROM sales.Invoices").ConfigureAwait(true), CultureInfo.InvariantCulture);
            var duplicatePurchases = Convert.ToInt64(await ScalarAsync(annual, "SELECT COUNT(InvoiceNumber) - COUNT(DISTINCT InvoiceNumber) FROM purchases.Invoices WHERE InvoiceNumber IS NOT NULL").ConfigureAwait(true), CultureInfo.InvariantCulture);
            var postedPurchases = Convert.ToInt64(await ScalarAsync(annual, "SELECT COUNT(*) FROM purchases.Invoices WHERE Status = 2").ConfigureAwait(true), CultureInfo.InvariantCulture) - 1; // minus the opening-stock purchase
            var postedSales = Convert.ToInt64(await ScalarAsync(annual, "SELECT COUNT(*) FROM sales.Invoices WHERE Status = 2").ConfigureAwait(true), CultureInfo.InvariantCulture);
            var stock = Convert.ToDecimal(await ScalarAsync(annual, "SELECT COALESCE(SUM(BaseQuantity), 0) FROM inventory.StockMovements").ConfigureAwait(true), CultureInfo.InvariantCulture);
            var expectedStock = opening + 3m * postedPurchases - 3m * postedSales;   // each purchase brings 3, each sale takes 1 + 2
            lines.Add(string.Empty);
            lines.Add(FormattableString.Invariant($"integrity: ledger_imbalance={imbalance}; duplicate_sales_numbers={duplicateSales}; duplicate_purchase_numbers={duplicatePurchases}; posted_purchases={postedPurchases}; posted_sales={postedSales}; stock={stock}; expected_stock={expectedStock}"));

            var path = Environment.GetEnvironmentVariable("PETRAERP_BENCH_OUT_MIXED") ?? Path.Combine(Path.GetTempPath(), "petra_mixed_bench.txt");
            await File.WriteAllLinesAsync(path, lines, Cancellation).ConfigureAwait(true);

            Assert.Equal(0m, imbalance);
            Assert.Equal(0L, duplicateSales);
            Assert.Equal(0L, duplicatePurchases);
            Assert.Equal(expectedStock, stock);
            Assert.Equal(0, serverErrors);
            Assert.Equal(0, notPosted);
        }
    }

    private static async Task<ConcurrentOutcome> PostOnePurchaseAsync(ApiSqlHost host, HttpClient client, string token, string root, NumberedSaleFixture fixture)
    {
        var body = new SaveAndProcessPurchaseInvoiceRequest
        {
            Document = new SavePurchaseInvoiceRequest
            {
                PatternRevisionId = fixture.PurchaseRevisionId, SupplierId = fixture.SupplierId, WarehouseId = fixture.WarehouseId,
                InvoiceDateUtc = new DateTimeOffset(2026, 1, 21, 12, 0, 0, TimeSpan.Zero), CurrencyCode = "JOD", ExchangeRate = "1",
                Lines = [new() { ItemId = fixture.ItemId, UnitId = fixture.UnitId, Quantity = "3", UnitCost = "4" }],
            },
            IdempotencyKey = $"mixed-p-{Guid.NewGuid():N}",
        };
        using var request = host.WorkspaceRequest(HttpMethod.Post, root + "/purchases/invoices/save-and-process", token,
            JsonContent.Create(body, body.GetType(), options: ApiJson.Options));
        var timer = Stopwatch.StartNew();
        using var response = await client.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
        timer.Stop();
        string? code = null;
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
