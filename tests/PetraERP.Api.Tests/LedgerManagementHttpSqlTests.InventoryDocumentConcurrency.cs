using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using PetraERP.Contracts.Inventory;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Tests;

public sealed partial class LedgerManagementHttpSqlTests
{
    private sealed record InventoryOutcome(string Kind, HttpStatusCode Status, string? Code, bool Posted, double Milliseconds);

    /// <summary>
    /// Internal issues and warehouse transfers drawing on the same stock, posted by 1..25 users at once. Asserts invariants only:
    /// the ledger balances, every unit that left the source is accounted for in the target or in an issue, and nothing fails with 5xx.
    /// </summary>
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MeasureConcurrentInventoryIssuesAndTransfersKeepStockAndLedgerExact()
    {
        const int opening = 5000;
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var fixture = await PrepareNumberedSalesAsync(host, client, token, openingStock: opening.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";

            async Task<T> CreateAsync<T>(string path, object payload)
            {
                var response = await SendAsync<T>(host, client, HttpMethod.Post, root + path, token, payload).ConfigureAwait(true);
                Assert.True(response.Status is HttpStatusCode.OK or HttpStatusCode.Created, $"{path}: {response.Status} / {response.Code}");
                return response.Body!;
            }

            var inventoryAccount = (await CreateAsync<AccountResponse>("/ledger/chart/accounts", new AccountRequest
            {
                Code = "13001", NameArabic = "مخزون اختبار الضغط", AccountType = AccountType.Assets, PostingAllowed = true,
                CostCenterPolicy = CostCenterPolicy.Optional, CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly,
            }).ConfigureAwait(true)).Id;
            var offsetAccount = (await CreateAsync<AccountResponse>("/ledger/chart/accounts", new AccountRequest
            {
                Code = "59101", NameArabic = "تكلفة صرف داخلي", AccountType = AccountType.Expenses, PostingAllowed = true,
                CostCenterPolicy = CostCenterPolicy.Optional, CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly,
            }).ConfigureAwait(true)).Id;
            var center = (await CreateAsync<CostCenterResponse>("/ledger/chart/cost-centers", new CostCenterRequest { Code = "LOADCC", NameArabic = "مركز اختبار الضغط" }).ConfigureAwait(true)).Id;
            var target = await CreateAsync<WarehouseDto>("/inventory/warehouses", new CreateWarehouseRequest { BranchId = host.BranchId, Code = "LOAD-WH2", NameArabic = "مستودع الهدف" }).ConfigureAwait(true);

            async Task<InventoryOutcome> OneAsync(InventoryDocumentKind kind)
            {
                var timer = Stopwatch.StartNew();
                var create = await SendAsync<InventoryDocumentDto>(host, client, HttpMethod.Post, root + "/inventory/documents", token, new SaveInventoryDocumentRequest
                {
                    CreationKey = Guid.NewGuid().ToString("N"), Kind = kind, WarehouseId = fixture.WarehouseId,
                    TargetWarehouseId = kind == InventoryDocumentKind.Transfer ? target.Id : null, DateUtc = DateTimeOffset.UtcNow, Reason = "اختبار الضغط",
                    // An intra-branch transfer preserves value and accepts no accounts; an internal issue needs both accounts and a cost center.
                    CostCenterId = kind == InventoryDocumentKind.InternalIssue ? center : null,
                    InventoryAccountId = kind == InventoryDocumentKind.InternalIssue ? inventoryAccount : null,
                    OffsetAccountId = kind == InventoryDocumentKind.InternalIssue ? offsetAccount : null,
                    Lines = [new InventoryDocumentLineInputDto { ItemId = fixture.ItemId, UnitId = fixture.UnitId, Quantity = "1.0000" }],
                }).ConfigureAwait(false);
                if (create.Body is null) return new(kind.ToString(), create.Status, create.Code, false, timer.Elapsed.TotalMilliseconds);
                var post = await SendAsync<InventoryDocumentDto>(host, client, HttpMethod.Post, root + $"/inventory/documents/{create.Body.Id}/post", token,
                    new InventoryDocumentActionRequest { ExpectedVersion = create.Body.Version, IdempotencyKey = Guid.NewGuid().ToString("N"), Reason = "ترحيل اختبار الضغط" }).ConfigureAwait(false);
                return new(kind.ToString(), post.Status, post.Code, post.Body?.State == InventoryDocumentState.Posted, timer.Elapsed.TotalMilliseconds);
            }

            var lines = new List<string> { "users;requests;posted;not_posted;http_5xx;wall_ms;p50_ms;max_ms;codes" };
            var serverErrors = 0;
            var notPosted = 0;
            // PETRAERP_LOAD_USERS ("1,5") and PETRAERP_LOAD_KINDS ("issue"|"transfer") narrow a run, e.g. to trace the SQL of a single document.
            var userSteps = (Environment.GetEnvironmentVariable("PETRAERP_LOAD_USERS") ?? "1,5,10,25").Split(',').Select(int.Parse).ToArray();
            var kinds = Environment.GetEnvironmentVariable("PETRAERP_LOAD_KINDS");
            foreach (var users in userSteps)
            {
                var outcomes = new ConcurrentBag<InventoryOutcome>();
                var wall = Stopwatch.StartNew();
                await Task.WhenAll(Enumerable.Range(0, users).Select(_ => Task.WhenAll(
                    kinds == "transfer" ? Task.CompletedTask : Task.Run(async () => outcomes.Add(await OneAsync(InventoryDocumentKind.InternalIssue).ConfigureAwait(false)), Cancellation),
                    kinds == "issue" ? Task.CompletedTask : Task.Run(async () => outcomes.Add(await OneAsync(InventoryDocumentKind.Transfer).ConfigureAwait(false)), Cancellation)))).ConfigureAwait(true);
                wall.Stop();
                var all = outcomes.ToArray();
                var posted = all.Count(o => o.Posted);
                var errors = all.Count(o => (int)o.Status >= 500);
                serverErrors += errors;
                notPosted += all.Length - posted;
                var times = all.Select(o => o.Milliseconds).Order().ToArray();
                var codes = string.Join('|', all.Where(o => !o.Posted).GroupBy(o => $"{(int)o.Status}:{o.Code}").Select(g => $"{g.Key}x{g.Count()}"));
                lines.Add(FormattableString.Invariant($"{users};{all.Length};{posted};{all.Length - posted};{errors};{wall.Elapsed.TotalMilliseconds:F0};{times[(times.Length - 1) / 2]:F0};{times[^1]:F0};{codes}"));
            }

            using var annual = host.AnnualContext();
            static decimal D(object? v) => Convert.ToDecimal(v, CultureInfo.InvariantCulture);
            var imbalance = D(await ScalarAsync(annual, "SELECT COALESCE(SUM(BaseDebit), 0) - COALESCE(SUM(BaseCredit), 0) FROM ledger.JournalLines").ConfigureAwait(true));
            var issues = D(await ScalarAsync(annual, "SELECT COUNT(*) FROM inventory.Documents WHERE Kind = 3 AND State = 1").ConfigureAwait(true));
            var transfers = D(await ScalarAsync(annual, "SELECT COUNT(*) FROM inventory.Documents WHERE Kind = 1 AND State = 1").ConfigureAwait(true));
            var total = D(await ScalarAsync(annual, "SELECT COALESCE(SUM(BaseQuantity), 0) FROM inventory.StockMovements").ConfigureAwait(true));
            var targetStock = D(await ScalarAsync(annual, FormattableString.Invariant($"SELECT COALESCE(SUM(BaseQuantity), 0) FROM inventory.StockMovements WHERE WarehouseId = '{target.Id}'")).ConfigureAwait(true));
            var duplicateNumbers = D(await ScalarAsync(annual, "SELECT COUNT(Number) - COUNT(DISTINCT Number) FROM inventory.Documents WHERE Number IS NOT NULL").ConfigureAwait(true));
            lines.Add(string.Empty);
            lines.Add(FormattableString.Invariant($"integrity: ledger_imbalance={imbalance}; posted_issues={issues}; posted_transfers={transfers}; total_stock={total}; target_stock={targetStock}; duplicate_numbers={duplicateNumbers}"));
            var path = Environment.GetEnvironmentVariable("PETRAERP_BENCH_OUT_INVENTORY") ?? Path.Combine(Path.GetTempPath(), "petra_inventory_bench.txt");
            await File.WriteAllLinesAsync(path, lines, Cancellation).ConfigureAwait(true);

            Assert.Equal(0m, imbalance);
            Assert.Equal(0m, duplicateNumbers);
            Assert.Equal(opening - issues, total);          // transfers move stock between warehouses; only issues leave the company
            Assert.Equal(transfers, targetStock);           // every posted transfer arrived in the target exactly once
            Assert.Equal(0, serverErrors);
            Assert.Equal(0, notPosted);
        }
    }
}
