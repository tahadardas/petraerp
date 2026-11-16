using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Sales;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Sales;
using PetraERP.Infrastructure.Treasury;
using Xunit;

namespace PetraERP.Infrastructure.Tests;

/// <summary>P05 measurement on real SQL Server: SQL command count and P50/P95 of the settlement read and of a sales list page.</summary>
public sealed class SettlementQueriesMeasurementSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Count); return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Count); return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken); }
    }

    private static double Percentile(List<double> sorted, double p) => sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MeasureSettlementReadAndSalesListPage()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            const int invoices = 5000;
            using var seedContext = database.Context();
            var seed = await SeedHelper.MasterAsync(database, seedContext).ConfigureAwait(true);
            var receivable = await seed.Scenario.AccountAsync(seed.Scenario.CompanyA, "1121", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var baseDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var year = await seedContext.FiscalYears.SingleAsync(y => y.CompanyId == seed.Scenario.CompanyA, Cancellation).ConfigureAwait(true);
            var period = await seedContext.AccountingPeriods.Where(p => p.FiscalYearId == year.Id).OrderBy(p => p.RangeStart).FirstAsync(Cancellation).ConfigureAwait(true);
            var random = new Random(5);
            long sequence = 1;
            for (var offset = 0; offset < invoices; offset += 500)
            {
                var journals = new List<JournalEntryRow>(); var rows = new List<SalesInvoiceRow>();
                for (var i = offset; i < offset + 500; i++)
                {
                    var journalId = Guid.NewGuid();
                    var total = random.Next(1, 30) * 10m;
                    var journal = new JournalEntryRow
                    {
                        Id = journalId, CompanyId = seed.Scenario.CompanyA, BranchId = seed.Scenario.BranchA1, AccountingDate = DateOnly.FromDateTime(baseDate.AddDays(i % 300).UtcDateTime),
                        Description = "inv " + i, State = 4, PostingSequence = sequence++, PostingFiscalYearId = year.Id, PostingPeriodId = period.Id, CreatedAtUtc = baseDate, UpdatedAtUtc = baseDate,
                    };
                    journal.Lines.Add(new JournalLineRow
                    {
                        Id = Guid.NewGuid(), CompanyId = seed.Scenario.CompanyA, JournalId = journalId, AccountId = receivable.Id,
                        TransactionCurrencyCode = "USD", TransactionDebit = total, BaseCurrencyCode = "USD", BaseDebit = total,
                        RateSourceCurrencyCode = "USD", RateTargetCurrencyCode = "USD", RateValue = 1m, Description = "ذمم/نقد البيع", CreatedAtUtc = baseDate, UpdatedAtUtc = baseDate,
                    });
                    journals.Add(journal);
                    rows.Add(new SalesInvoiceRow
                    {
                        Id = Guid.NewGuid(), CompanyId = seed.Scenario.CompanyA, BranchId = seed.Scenario.BranchA1, PatternRevisionId = seed.PatternRevisionId,
                        CustomerId = seed.Customers[i % seed.Customers.Length], WarehouseId = seed.Warehouses[0], InvoiceNumber = "INV-" + i.ToString("D6", CultureInfo.InvariantCulture),
                        InvoiceDateUtc = baseDate.AddDays(i % 300), PaymentMethod = 1, CurrencyCode = "USD", Status = 2, GrandTotal = total, JournalEntryId = journalId,
                        CreatedAtUtc = baseDate.AddSeconds(i), UpdatedAtUtc = baseDate,
                    });
                }
                seedContext.JournalEntries.AddRange(journals);
                await seedContext.SaveChangesAsync(Cancellation).ConfigureAwait(true);
                seedContext.SalesInvoices.AddRange(rows);
                await seedContext.SaveChangesAsync(Cancellation).ConfigureAwait(true);
                seedContext.ChangeTracker.Clear();
            }

            var counter = new CommandCounter();
            using var context = new PetraDbContext(new DbContextOptionsBuilder<PetraDbContext>()
                .UseSqlServer(database.ConnectionString, sql => sql.UseCompatibilityLevel(database.CompatibilityLevel)).AddInterceptors(counter).Options);
            var service = new SqlSalesInvoiceService(context, new SqlPatternService(context), TimeProvider.System);

            var lines = new List<string> { "operation;rows;sql_commands;p50_ms;p95_ms;runs" };
            async Task<(int Rows, int Commands, List<double> Samples)> MeasureAsync(Func<Task<int>> action)
            {
                _ = await action().ConfigureAwait(true); // warm-up (plan cache, connection)
                var samples = new List<double>(); var rowsOut = 0; var commands = 0;
                for (var i = 0; i < 20; i++)
                {
                    counter.Count = 0; context.ChangeTracker.Clear();
                    var sw = Stopwatch.StartNew(); rowsOut = await action().ConfigureAwait(true); sw.Stop();
                    samples.Add(sw.Elapsed.TotalMilliseconds); commands = counter.Count;
                }
                samples.Sort();
                return (rowsOut, commands, samples);
            }

            var read = await MeasureAsync(async () => (await SettlementQueries.LoadInvoicesAsync(context, seed.Scenario.CompanyA, seed.Scenario.BranchA1, VoucherKind.Receipt, null, null, null, Cancellation).ConfigureAwait(true)).Count).ConfigureAwait(true);
            Assert.Equal(invoices, read.Rows);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"SettlementQueries.LoadInvoicesAsync(all customers);{read.Rows};{read.Commands};{Percentile(read.Samples, 0.5):F1};{Percentile(read.Samples, 0.95):F1};20"));

            var page = await MeasureAsync(async () => (await service.GetInvoicesAsync(seed.Scenario.CompanyA, seed.Scenario.BranchA1, new SalesInvoiceSearchQuery(Page: 1, PageSize: 100), Cancellation).ConfigureAwait(true)).Items.Count).ConfigureAwait(true);
            Assert.Equal(100, page.Rows);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"SalesInvoices list page (100 rows, 3 credit customers);{page.Rows};{page.Commands};{Percentile(page.Samples, 0.5):F1};{Percentile(page.Samples, 0.95):F1};20"));

            // Bounded-command contract: the number of SQL commands must not depend on the number of invoices.
            Assert.True(read.Commands <= 6, $"settlement read used {read.Commands} commands");
            Assert.True(page.Commands <= 12, $"list page used {page.Commands} commands");
            File.WriteAllLines(Environment.GetEnvironmentVariable("PETRAERP_BENCH_OUT") ?? Path.Combine(Path.GetTempPath(), "petra_sql_bench.csv"), lines);
        }
    }
}

internal static class SeedHelper
{
    internal sealed record Master(LedgerScenario Scenario, Guid PatternRevisionId, Guid[] Customers, Guid[] Warehouses);

    internal static async Task<Master> MasterAsync(SqlTestDatabase database, PetraDbContext context)
    {
        var m = await SalesInvoiceNavigationSqlTests.SeedForMeasurementAsync(database, context).ConfigureAwait(true);
        return new Master(m.Scenario, m.PatternRevisionId, m.Customers, m.Warehouses);
    }
}
