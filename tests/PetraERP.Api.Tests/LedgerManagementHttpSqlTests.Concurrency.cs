using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.CompanyOptions;
using PetraERP.Contracts.DocumentProcessing;
using PetraERP.Contracts.Sales;

namespace PetraERP.Api.Tests;

/// <summary>
/// Explicit multi-user and stability measurements on a disposable SQL Server database through the real HTTP pipeline.
/// They assert accounting integrity (balanced ledger, no duplicate invoice numbers, no server errors) and record
/// throughput, latency and memory so changes can be compared. They are measurements, not performance gates.
/// </summary>
public sealed partial class LedgerManagementHttpSqlTests
{
    private sealed record ConcurrentOutcome(int Status, string? Code, double Milliseconds);

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MeasureConcurrentSalesPostingAndLedgerIntegrity()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            // Enough opening stock that the benchmark exercises the real posting path instead of the negative-stock block. A long soak needs far more
            // than the default: once stock runs out every request is a cheap rejection and the run no longer tests posting.
            var fixture = await PrepareNumberedSalesAsync(host, client, token, openingStock: Environment.GetEnvironmentVariable("PETRAERP_SOAK_OPENING_STOCK") ?? "5000").ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);

            var lines = new List<string> { "users;requests;ok;controlled_rejections;server_errors;wall_ms;requests_per_second;p50_ms;p95_ms;max_ms;rejection_codes" };
            var serverErrors = 0;
            foreach (var users in new[] { 1, 5, 10, 25, 50 })
            {
                var outcomes = new ConcurrentBag<ConcurrentOutcome>();
                var wall = Stopwatch.StartNew();
                await Task.WhenAll(Enumerable.Range(0, users).Select(async _ =>
                {
                    for (var index = 0; index < 2; index++)
                    {
                        outcomes.Add(await PostOneSaleAsync(host, client, token, root, fixture).ConfigureAwait(false));
                    }
                })).ConfigureAwait(true);
                wall.Stop();

                var all = outcomes.ToArray();
                var ok = all.Count(o => o.Status is >= 200 and < 300);
                var errors = all.Count(o => o.Status >= 500);
                serverErrors += errors;
                var times = all.Select(o => o.Milliseconds).Order().ToArray();
                lines.Add("  ok outcomes: " + string.Join('|', all.Where(o => o.Status is >= 200 and < 300).GroupBy(o => o.Code).Select(g => $"{g.Key}x{g.Count()}")));
                var codes = string.Join('|', all.Where(o => o.Status is < 200 or >= 300).GroupBy(o => $"{o.Status}:{o.Code}").Select(g => $"{g.Key}x{g.Count()}"));
                lines.Add(FormattableString.Invariant(
                    $"{users};{all.Length};{ok};{all.Length - ok - errors};{errors};{wall.Elapsed.TotalMilliseconds:F0};{all.Length / wall.Elapsed.TotalSeconds:F2};{times[(times.Length - 1) / 2]:F0};{times[Math.Max(0, (int)Math.Ceiling(0.95 * times.Length) - 1)]:F0};{times[^1]:F0};{codes}"));
            }

            using var annual = host.AnnualContext();
            var imbalance = await ScalarAsync(annual, "SELECT COALESCE(SUM(BaseDebit), 0) - COALESCE(SUM(BaseCredit), 0) FROM ledger.JournalLines").ConfigureAwait(true);
            var duplicateNumbers = await ScalarAsync(annual, "SELECT COUNT(InvoiceNumber) - COUNT(DISTINCT InvoiceNumber) FROM sales.Invoices").ConfigureAwait(true);
            var invoices = await ScalarAsync(annual, "SELECT COUNT(*) FROM sales.Invoices").ConfigureAwait(true);
            var journals = await ScalarAsync(annual, "SELECT COUNT(*) FROM ledger.JournalEntries").ConfigureAwait(true);
            lines.Add(string.Empty);
            lines.Add("invoice status distribution (status;count;with_journal): " + string.Join(" | ", await QueryRowsAsync(annual,
                "SELECT CONCAT(CAST(Status AS int), ';', COUNT(*), ';', SUM(CASE WHEN JournalEntryId IS NULL THEN 0 ELSE 1 END)) FROM sales.Invoices GROUP BY Status ORDER BY Status").ConfigureAwait(true)));
            lines.Add(string.Empty);
            lines.Add(FormattableString.Invariant($"integrity: ledger_imbalance={imbalance}; duplicate_invoice_numbers={duplicateNumbers}; invoices={invoices}; journals={journals}"));
            lines.Add(string.Empty);
            lines.Add("missing index suggestions (annual database, small data volume -> advisory only): impact;seeks;table;equality;inequality;included");
            lines.AddRange(await QueryRowsAsync(annual, """
                SELECT TOP (10) CAST(migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans) AS bigint),
                  migs.user_seeks, mid.statement, mid.equality_columns, mid.inequality_columns, mid.included_columns
                FROM sys.dm_db_missing_index_group_stats migs
                JOIN sys.dm_db_missing_index_groups mig ON migs.group_handle = mig.index_group_handle
                JOIN sys.dm_db_missing_index_details mid ON mig.index_handle = mid.index_handle
                WHERE mid.database_id = DB_ID()
                ORDER BY 1 DESC
                """).ConfigureAwait(true));

            var path = Environment.GetEnvironmentVariable("PETRAERP_BENCH_OUT_CONCURRENCY") ?? Path.Combine(Path.GetTempPath(), "petra_concurrency_bench.txt");
            await File.WriteAllLinesAsync(path, lines, Cancellation).ConfigureAwait(true);

            Assert.Equal(0m, Convert.ToDecimal(imbalance, CultureInfo.InvariantCulture));
            Assert.Equal(0L, Convert.ToInt64(duplicateNumbers, CultureInfo.InvariantCulture));
            Assert.Equal(0, serverErrors);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MeasureSoakMemoryStability()
    {
        var seconds = int.Parse(Environment.GetEnvironmentVariable("PETRAERP_SOAK_SECONDS") ?? "120", CultureInfo.InvariantCulture);
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            // Enough opening stock that the benchmark exercises the real posting path instead of the negative-stock block. A long soak needs far more
            // than the default: once stock runs out every request is a cheap rejection and the run no longer tests posting.
            var fixture = await PrepareNumberedSalesAsync(host, client, token, openingStock: Environment.GetEnvironmentVariable("PETRAERP_SOAK_OPENING_STOCK") ?? "5000").ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);

            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            // Access tokens expire (about ten minutes). A soak that keeps using one token turns into an hour of 401s and rate-limit
            // rejections, which is not a test of posting at all; renew the token well inside its lifetime.
            var currentToken = token;
            var requests = 0;
            var failures = 0;
            var outcomes = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();
            var renewer = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    try { await Task.Delay(TimeSpan.FromMinutes(4), stop.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    Volatile.Write(ref currentToken, (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(false)).AccessToken);
                }
            }, Cancellation);
            var workers = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    var outcome = await PostOneSaleAsync(host, client, Volatile.Read(ref currentToken), root, fixture).ConfigureAwait(false);
                    Interlocked.Increment(ref requests);
                    // Count every distinct outcome: a soak that quietly turns into fast rejections must be visible, not a "pass".
                    outcomes.AddOrUpdate($"{outcome.Status}:{outcome.Code}", 1, static (_, count) => count + 1);
                    if (outcome.Status >= 500)
                    {
                        Interlocked.Increment(ref failures);
                    }
                }
            }, Cancellation)).ToArray();

            var lines = new List<string> { "elapsed_s;requests;live_heap_mb_after_gc;working_set_mb;gen2_collections;threads" };
            var started = Stopwatch.StartNew();
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, seconds / 8)), Cancellation).ConfigureAwait(true);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                using var process = Process.GetCurrentProcess();
                lines.Add(FormattableString.Invariant(
                    $"{started.Elapsed.TotalSeconds:F0};{Volatile.Read(ref requests)};{GC.GetTotalMemory(true) / 1048576.0:F1};{process.WorkingSet64 / 1048576.0:F0};{GC.CollectionCount(2)};{process.Threads.Count}"));
            }

            await Task.WhenAll(workers).ConfigureAwait(true);
            lines.Add(FormattableString.Invariant($"total_requests={requests}; server_errors={failures}; duration_s={seconds}"));
            lines.Add("outcomes: " + string.Join(", ", outcomes.OrderByDescending(o => o.Value).Select(o => $"{o.Key}={o.Value}")));
            var path = Environment.GetEnvironmentVariable("PETRAERP_BENCH_OUT_SOAK") ?? Path.Combine(Path.GetTempPath(), "petra_soak_bench.txt");
            await File.WriteAllLinesAsync(path, lines, Cancellation).ConfigureAwait(true);
            var postedCount = outcomes.TryGetValue("201:Posted/", out var posted) ? posted : 0;
            Assert.True(requests > 0 && postedCount >= requests * 0.95, $"The soak must post, not reject: {postedCount} of {requests} posted ({string.Join(", ", outcomes.Select(o => o.Key + "=" + o.Value))}).");
            Assert.Equal(0, failures);
        }
    }

    private static async Task<ConcurrentOutcome> PostOneSaleAsync(ApiSqlHost host, HttpClient client, string token, string root, NumberedSaleFixture fixture)
    {
        var body = new SaveAndProcessSalesInvoiceRequest { Document = TwoLineSale(fixture), IdempotencyKey = $"load-{Guid.NewGuid():N}" };
        using var request = host.WorkspaceRequest(HttpMethod.Post, root + "/sales/invoices/save-and-process", token,
            JsonContent.Create(body, body.GetType(), options: ApiJson.Options));
        var timer = Stopwatch.StartNew();
        using var response = await client.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
        timer.Stop();
        string? code = null;
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                using var problem = JsonDocument.Parse(text);
                code = problem.RootElement.TryGetProperty("code", out var value) ? value.GetString() : null;
            }
            catch (JsonException)
            {
                code = "non-json";
            }
        }

        else
        {
            // A 2xx is not "posted": record what the document processing really did (Posted, PostingFailed, ...).
            try
            {
                using var result = JsonDocument.Parse(text);
                var outcome = result.RootElement.TryGetProperty("outcome", out var o) ? o.GetString() : null;
                var failure = result.RootElement.TryGetProperty("failureCode", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
                code = $"{outcome}/{failure}";
            }
            catch (JsonException)
            {
                code = "non-json";
            }
        }

        return new ConcurrentOutcome((int)response.StatusCode, code, timer.Elapsed.TotalMilliseconds);
    }

    private static async Task<object?> ScalarAsync(DbContext context, string sql)
    {
        await context.Database.OpenConnectionAsync(Cancellation).ConfigureAwait(false);
        var command = context.Database.GetDbConnection().CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = sql;
            return await command.ExecuteScalarAsync(Cancellation).ConfigureAwait(false);
        }
    }

    private static async Task<List<string>> QueryRowsAsync(DbContext context, string sql)
    {
        var rows = new List<string>();
        await context.Database.OpenConnectionAsync(Cancellation).ConfigureAwait(false);
        var command = context.Database.GetDbConnection().CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = sql;
            var reader = await command.ExecuteReaderAsync(Cancellation).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(Cancellation).ConfigureAwait(false))
                {
                    var values = new string[reader.FieldCount];
                    for (var column = 0; column < values.Length; column++)
                    {
                        values[column] = Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture) ?? string.Empty;
                    }

                    rows.Add(string.Join(';', values));
                }
            }
        }

        return rows;
    }
}
