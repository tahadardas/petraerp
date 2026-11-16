using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.CompanyOptions;
using PetraERP.Contracts.DocumentProcessing;
using PetraERP.Contracts.Sales;

namespace PetraERP.Api.Tests;

/// <summary>
/// Explicit performance measurement on a real disposable SQL Server database through the real HTTP pipeline.
/// Not a pass/fail gate: it records warm latency percentiles, the server-reported SQL time and command count
/// (Server-Timing) and the statements executed most often, so regressions and N+1 patterns can be compared.
/// </summary>
public sealed partial class LedgerManagementHttpSqlTests
{
    [GeneratedRegex(@"total;dur=(?<total>[\d.]+), db;dur=(?<db>[\d.]+);desc=""(?<cmds>\d+) cmd""", RegexOptions.CultureInvariant)]
    private static partial Regex ServerTimingPattern();

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MeasureWarmSalesSaveAndPostLatencyAndSqlCommandCount()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var fixture = await PrepareNumberedSalesAsync(host, client, token).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);

            const int Warmup = 3;
            const int Runs = 20;
            var wall = new List<double>();
            var total = new List<double>();
            var sql = new List<double>();
            var commands = new List<double>();
            for (var index = 0; index < Warmup + Runs; index++)
            {
                var body = new SaveAndProcessSalesInvoiceRequest { Document = TwoLineSale(fixture), IdempotencyKey = $"perf-{Guid.NewGuid():N}" };
                using var request = host.WorkspaceRequest(HttpMethod.Post, root + "/sales/invoices/save-and-process", token,
                    JsonContent.Create(body, body.GetType(), options: ApiJson.Options));
                var timer = Stopwatch.StartNew();
                using var response = await client.SendAsync(request, Cancellation).ConfigureAwait(true);
                _ = await response.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(true);
                timer.Stop();
                Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}");
                if (index < Warmup)
                {
                    continue;
                }

                var match = ServerTimingPattern().Match(string.Join(", ", response.Headers.GetValues("Server-Timing")));
                Assert.True(match.Success, "Server-Timing header missing or malformed.");
                wall.Add(timer.Elapsed.TotalMilliseconds);
                total.Add(double.Parse(match.Groups["total"].Value, CultureInfo.InvariantCulture));
                sql.Add(double.Parse(match.Groups["db"].Value, CultureInfo.InvariantCulture));
                commands.Add(double.Parse(match.Groups["cmds"].Value, CultureInfo.InvariantCulture));
            }

            var lines = new List<string>
            {
                "metric;p50;p95;max;runs",
                Summarize("client_wall_ms", wall),
                Summarize("server_total_ms", total),
                Summarize("server_sql_ms", sql),
                Summarize("sql_commands", commands),
                string.Empty,
                "most executed statements (annual database): executions;total_ms;text",
            };
            using (var annual = host.AnnualContext())
            {
                lines.AddRange(await TopStatementsAsync(annual, "qs.execution_count").ConfigureAwait(true));
                lines.Add(string.Empty);
                lines.Add("most expensive statements by total elapsed time (annual database): executions;total_ms;text");
                lines.AddRange(await TopStatementsAsync(annual, "qs.total_elapsed_time").ConfigureAwait(true));
            }

            var path = Environment.GetEnvironmentVariable("PETRAERP_BENCH_OUT_HTTP") ?? Path.Combine(Path.GetTempPath(), "petra_http_bench.txt");
            await File.WriteAllLinesAsync(path, lines, Cancellation).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MeasureColdAndWarmLoginLatency()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var samples = new List<double>();
            for (var index = 0; index < 13; index++)
            {
                var timer = Stopwatch.StartNew();
                _ = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
                samples.Add(timer.Elapsed.TotalMilliseconds);
            }

            var warm = samples.Skip(3).ToList();
            var lines = new List<string>
            {
                "metric;p50;p95;max;runs",
                FormattableString.Invariant($"first_login_cold_ms;{samples[0]:F1};{samples[0]:F1};{samples[0]:F1};1"),
                Summarize("warm_login_ms", warm),
            };
            var path = Environment.GetEnvironmentVariable("PETRAERP_BENCH_OUT_LOGIN") ?? Path.Combine(Path.GetTempPath(), "petra_login_bench.txt");
            await File.WriteAllLinesAsync(path, lines, Cancellation).ConfigureAwait(true);
        }
    }

    private static string Summarize(string name, List<double> values)
    {
        var sorted = values.Order().ToArray();
        double Percentile(double p) => sorted[Math.Max(0, (int)Math.Ceiling(p * sorted.Length) - 1)];
        return FormattableString.Invariant($"{name};{Percentile(0.5):F1};{Percentile(0.95):F1};{sorted[^1]:F1};{sorted.Length}");
    }

    private static async Task<List<string>> TopStatementsAsync(DbContext context, string orderBy)
    {
        var result = new List<string>();
        await context.Database.OpenConnectionAsync(Cancellation).ConfigureAwait(false);
        var command = context.Database.GetDbConnection().CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = """
                SELECT TOP (15) qs.execution_count, qs.total_elapsed_time / 1000.0,
                  LEFT(REPLACE(REPLACE(SUBSTRING(st.text, qs.statement_start_offset / 2 + 1,
                    (CASE WHEN qs.statement_end_offset = -1 THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END
                     - qs.statement_start_offset) / 2 + 1), CHAR(13), N' '), CHAR(10), N' '), 170)
                FROM sys.dm_exec_query_stats qs
                CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
                CROSS APPLY sys.dm_exec_plan_attributes(qs.plan_handle) pa
                WHERE pa.attribute = N'dbid' AND CAST(pa.value AS int) = DB_ID()
                """ + $"\nORDER BY {orderBy} DESC";
            var reader = await command.ExecuteReaderAsync(Cancellation).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(Cancellation).ConfigureAwait(false))
                {
                    result.Add(FormattableString.Invariant($"{reader.GetInt64(0)};{Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture):F1};{reader.GetString(2)}"));
                }
            }
        }

        return result;
    }
}
