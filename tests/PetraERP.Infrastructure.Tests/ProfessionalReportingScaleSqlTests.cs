using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports.Professional;
using PetraERP.Infrastructure.Reports;
using PetraERP.Infrastructure.Reports.Professional;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests;

/// <summary>Reporting throughput only. Bulk copies a balanced, genuinely posted seed into an owned disposable database.</summary>
public sealed class ProfessionalReportingScaleSqlTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions EvidenceJson = new() { WriteIndented = true };
    [Theory(Explicit = true)]
    [Trait("Category", "SqlServer")]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public async Task CompleteSyntheticLedgerAtScalePreservesAllRowsAndDecimalBalances(int rowCount)
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using var cleanup = database.ConfigureAwait(true);
        var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
        var debit = await scenario.AccountAsync(scenario.CompanyA, "SCALE-D", AccountTypeCode.Assets, true).ConfigureAwait(true);
        var credit = await scenario.AccountAsync(scenario.CompanyA, "SCALE-C", AccountTypeCode.Revenue, true).ConfigureAwait(true);
        var seed = await scenario.JournalAsync(scenario.CompanyA, scenario.BranchA1, new DateOnly(2026, 1, 1), "USD", true,
            (debit.Id, 1.2345m, 0m), (credit.Id, 0m, 1.2345m)).ConfigureAwait(true);
        using var context = database.Context();
        context.Database.SetCommandTimeout(180);
        // No operational database is used. No claim about production posting follows from bulk report fixtures.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            CREATE TABLE #ReportKeys(Id uniqueidentifier NOT NULL, SequenceNumber bigint NOT NULL);
            INSERT #ReportKeys
            SELECT TOP ({rowCount - 1}) NEWID(), ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) + 1
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            INSERT ledger.JournalEntries
              (Id,CompanyId,BranchId,AccountingDate,Description,State,PostingSequence,PostingFiscalYearId,PostingPeriodId)
            SELECT k.Id,j.CompanyId,j.BranchId,j.AccountingDate,N'حركة صناعية',j.State,k.SequenceNumber,j.PostingFiscalYearId,j.PostingPeriodId
            FROM #ReportKeys k CROSS JOIN ledger.JournalEntries j WHERE j.Id={seed.Id};
            INSERT ledger.JournalLines
              (Id,CompanyId,JournalId,AccountId,CostCenterId,TransactionCurrencyCode,TransactionDebit,TransactionCredit,
               BaseCurrencyCode,BaseDebit,BaseCredit,RateSourceCurrencyCode,RateTargetCurrencyCode,RateValue,Rounding,Description)
            SELECT NEWID(),l.CompanyId,k.Id,l.AccountId,l.CostCenterId,l.TransactionCurrencyCode,l.TransactionDebit,l.TransactionCredit,
               l.BaseCurrencyCode,l.BaseDebit,l.BaseCredit,l.RateSourceCurrencyCode,l.RateTargetCurrencyCode,l.RateValue,l.Rounding,
               CASE WHEN k.SequenceNumber % 2 = 0 THEN N'مطابق %_[' ELSE N'حركة أخرى' END
            FROM #ReportKeys k CROSS JOIN ledger.JournalLines l WHERE l.JournalId={seed.Id};
            DROP TABLE #ReportKeys;
            """, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var year = await context.FiscalYears.SingleAsync(y => y.CompanyId == scenario.CompanyA, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var request = new ProfessionalReportRequest
        {
            Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = year.Id, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31),
            AccountId = debit.Id, Sources = [ProfessionalReportSource.UnclassifiedJournal],
            Fields = [ProfessionalReportField.Date, ProfessionalReportField.Description, ProfessionalReportField.Debit, ProfessionalReportField.ActualBalance],
        };
        var scope = LedgerReportScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor, false);
        var service = new SqlProfessionalReportService(context, new SqlAccessEvaluator(context), new SqlCommercialReportService(context));
        var timer = Stopwatch.StartNew();
        var before = GC.GetTotalAllocatedBytes();
        var all = await service.ExecuteAsync(scope, request, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var fullTime = timer.Elapsed;
        Assert.Equal(rowCount, all.Rows.Count);
        Assert.Equal(rowCount, all.Summary.RowCount);
        Assert.Equal(rowCount * 1.2345m, all.Summary.ActualClosing);
        Assert.Equal(all.Summary.ActualClosing, all.Rows[^1].ActualBalance);
        Assert.Equal(all.Summary.ActualPeriodDebit, all.Summary.FilteredDebit);
        timer.Restart();
        var filtered = await service.ExecuteAsync(scope, request with { DescriptionContains = "%_[" }, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(rowCount / 2, filtered.Rows.Count);
        Assert.Equal(rowCount * 1.2345m, filtered.Summary.ActualClosing);
        Assert.Equal(rowCount / 2 * 1.2345m, filtered.Summary.FilteredDebit);
        Assert.Equal(filtered.Summary.ActualClosing, filtered.Rows[^1].ActualBalance);
        var evidence = new
        {
            rowCount, fullQueryMilliseconds = fullTime.TotalMilliseconds, filteredQueryMilliseconds = timer.Elapsed.TotalMilliseconds,
            allocatedBytes = GC.GetTotalAllocatedBytes() - before, processPeakBytes = Process.GetCurrentProcess().PeakWorkingSet64,
            actualClosing = all.Summary.ActualClosing.ToString(CultureInfo.InvariantCulture),
            filteredDebit = filtered.Summary.FilteredDebit.ToString(CultureInfo.InvariantCulture),
            engine = database.CompatibilityLevel == 170 ? "SQL Server 2025" : "SQL Server 16 compatibility only",
        };
        var json = JsonSerializer.Serialize(evidence, EvidenceJson);
        output.WriteLine(json);
        var artifacts = Environment.GetEnvironmentVariable("PETRA_REPORTING_ARTIFACT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(artifacts))
        {
            Directory.CreateDirectory(artifacts);
            await File.WriteAllTextAsync(Path.Combine(artifacts, $"sql-scale-{rowCount}.json"), json, TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
    }
}
