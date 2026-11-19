using System.Security.Cryptography;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Tests;

public sealed class LedgerKeysetPagingSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private sealed class TestSigner : ILedgerCursorSigner
    {
        private readonly byte[] key = RandomNumberGenerator.GetBytes(32);

        public string Protect(ReadOnlySpan<byte> payload) =>
            Convert.ToBase64String(payload) + "." + Convert.ToBase64String(HMACSHA256.HashData(key, payload));

        public bool TryUnprotect(string token, out byte[] payload)
        {
            payload = [];
            var parts = token.Split('.');
            if (parts.Length != 2) return false;
            try
            {
                var body = Convert.FromBase64String(parts[0]);
                if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, body), Convert.FromBase64String(parts[1]))) return false;
                payload = body;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CursorChainReproducesTheFullLedgerIsStableUnderNewPostingsAndRejectsForgery()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var sales = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            // Several journals on the same date so the (date, sequence, journal, line) tie-breaks are exercised.
            for (var i = 0; i < 12; i++)
            {
                await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 1, 1 + (i / 4)), "USD", true,
                    (cash.Id, 100m + i, 0m), (sales.Id, 0m, 100m + i)).ConfigureAwait(true);
            }

            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var signer = new TestSigner();
            using var context = database.Context();
            var reports = new SqlLedgerQueryService(context, signer);

            var whole = await reports.GetAccountLedgerAsync(scope,
                new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(1, 100)), Cancellation).ConfigureAwait(true);
            Assert.Equal(12, whole.Lines.Count);
            Assert.Null(whole.NextCursor);

            var walked = new List<AccountLedgerLineDto>();
            AccountLedgerDto page = await reports.GetAccountLedgerAsync(scope,
                new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(1, 5)), Cancellation).ConfigureAwait(true);
            walked.AddRange(page.Lines);
            var firstCursor = page.NextCursor;
            Assert.NotNull(firstCursor);

            // A journal posted after the first page must not enter the chain (snapshot watermark).
            await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 1, 2), "USD", true,
                (cash.Id, 7777m, 0m), (sales.Id, 0m, 7777m)).ConfigureAwait(true);

            while (page.NextCursor is { } cursor)
            {
                page = await reports.GetAccountLedgerAsync(scope,
                    new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(1, 5), cursor), Cancellation).ConfigureAwait(true);
                Assert.Equal(whole.SnapshotPostingSequence, page.SnapshotPostingSequence);
                Assert.Equal(whole.ClosingBalance, page.ClosingBalance);
                walked.AddRange(page.Lines);
            }

            Assert.Equal(whole.Lines.Select(l => l.LineId), walked.Select(l => l.LineId));
            Assert.Equal(whole.Lines.Select(l => l.RunningBalance), walked.Select(l => l.RunningBalance));
            Assert.Equal(whole.ClosingBalance, walked[^1].RunningBalance);

            // Tampered, foreign and mismatched cursors are client errors, never silently accepted.
            var tampered = firstCursor[..^2] + (firstCursor[^2] == 'A' ? "B=" : "A=");
            await Assert.ThrowsAsync<RequestValidationException>(() => reports.GetAccountLedgerAsync(scope,
                new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(1, 5), tampered), Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<RequestValidationException>(() => reports.GetAccountLedgerAsync(scope,
                new AccountLedgerQuery(cash.Id, new DateOnly(2026, 1, 2), null, PageRequest.Create(1, 5), firstCursor), Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<RequestValidationException>(() => reports.GetAccountLedgerAsync(scope,
                new AccountLedgerQuery(sales.Id, null, null, PageRequest.Create(1, 5), firstCursor), Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SourceFilterRestrictsLinesOpeningAndBindsTheCursor()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var sales = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            for (var i = 0; i < 6; i++)
            {
                await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 1, 1 + i), "USD", true,
                    (cash.Id, 100m, 0m), (sales.Id, 0m, 100m)).ConfigureAwait(true);
            }

            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            using var context = database.Context();
            var reports = new SqlLedgerQueryService(context, new TestSigner());

            // Manual journals are the JOURNAL source: they are visible under it and under "all", never under another source.
            var manual = await reports.GetAccountLedgerAsync(scope, new AccountLedgerQuery(cash.Id, new DateOnly(2026, 1, 3), null, PageRequest.Create(1, 100))
                { Sources = [TrialBalanceSources.Journal] }, Cancellation).ConfigureAwait(true);
            var all = await reports.GetAccountLedgerAsync(scope, new AccountLedgerQuery(cash.Id, new DateOnly(2026, 1, 3), null, PageRequest.Create(1, 100)), Cancellation).ConfigureAwait(true);
            Assert.Equal(4, manual.Lines.Count);
            Assert.Equal(all.OpeningBalance, manual.OpeningBalance);
            Assert.Equal(all.ClosingBalance, manual.ClosingBalance);

            var sold = await reports.GetAccountLedgerAsync(scope, new AccountLedgerQuery(cash.Id, new DateOnly(2026, 1, 3), null, PageRequest.Create(1, 100))
                { Sources = [TrialBalanceSources.SalesInvoice] }, Cancellation).ConfigureAwait(true);
            Assert.Empty(sold.Lines);
            Assert.Equal(0m, sold.OpeningBalance);
            Assert.Equal(0m, sold.ClosingBalance);

            // A cursor issued under one source filter is refused under another.
            var firstPage = await reports.GetAccountLedgerAsync(scope, new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(1, 2))
                { Sources = [TrialBalanceSources.Journal] }, Cancellation).ConfigureAwait(true);
            Assert.NotNull(firstPage.NextCursor);
            await Assert.ThrowsAsync<RequestValidationException>(() => reports.GetAccountLedgerAsync(scope,
                new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(1, 2), firstPage.NextCursor)
                    { Sources = [TrialBalanceSources.SalesInvoice] }, Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServerBenchmark")]
    public async Task MeasureDeepPageByNumberVersusByCursor()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var sales = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            const int journals = 1000;
            const int pageSize = 50;
            for (var i = 0; i < journals; i++)
            {
                await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 1, 1).AddDays(i % 300), "USD", true,
                    (cash.Id, 10m + (i % 7), 0m), (sales.Id, 0m, 10m + (i % 7))).ConfigureAwait(true);
            }

            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var signer = new TestSigner();
            var lastPage = journals / pageSize;
            var byNumber = new List<double>();
            var byCursor = new List<double>();
            for (var run = 0; run < 5; run++)
            {
                using (var context = database.Context())
                {
                    var reports = new SqlLedgerQueryService(context, signer);
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    var deep = await reports.GetAccountLedgerAsync(scope,
                        new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(lastPage, pageSize)), Cancellation).ConfigureAwait(true);
                    byNumber.Add(timer.Elapsed.TotalMilliseconds);
                    Assert.Equal(pageSize, deep.Lines.Count);
                }

                using (var context = database.Context())
                {
                    var reports = new SqlLedgerQueryService(context, signer);
                    var page = await reports.GetAccountLedgerAsync(scope,
                        new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(1, pageSize)), Cancellation).ConfigureAwait(true);
                    for (var n = 2; n < lastPage; n++)
                    {
                        page = await reports.GetAccountLedgerAsync(scope,
                            new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(n, pageSize), page.NextCursor), Cancellation).ConfigureAwait(true);
                    }

                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    var deep = await reports.GetAccountLedgerAsync(scope,
                        new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(lastPage, pageSize), page.NextCursor), Cancellation).ConfigureAwait(true);
                    byCursor.Add(timer.Elapsed.TotalMilliseconds);
                    Assert.Equal(pageSize, deep.Lines.Count);
                }
            }

            byNumber.Sort();
            byCursor.Sort();
            Console.WriteLine($"LEDGER_BENCH lines={journals} page={lastPage} byNumber p50={byNumber[2]:F1}ms (min {byNumber[0]:F1}) byCursor p50={byCursor[2]:F1}ms (min {byCursor[0]:F1})");
        }
    }
}
