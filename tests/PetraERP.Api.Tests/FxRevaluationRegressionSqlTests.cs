using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Contracts.Currencies;

namespace PetraERP.Api.Tests;

/// <summary>Foreign-currency revaluation regression suite (explicit SQL). Proves the four accounting defects stay fixed:
/// (B) carrying value includes prior base-currency revaluation adjustments so a repeated revaluation never double counts;
/// (C) every balance read is isolated to the selected company AND branch; (D) idempotency is validated by canonical
/// content, not the key alone; (E) execution is a durable, recoverable state machine that never posts twice nor orphans a
/// financial effect. All data lives in the disposable annual workspace; authorization is exercised over the real HTTP API.</summary>
public sealed partial class FxRevaluationRegressionSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static readonly DateOnly Oct = new(2026, 10, 24);
    private static readonly DateOnly Nov = new(2026, 11, 20);
    private static readonly DateOnly Dec = new(2026, 12, 20);

    private static HttpClient Workspace(ApiSqlHost host, string token)
    {
        var client = host.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add(ApiSqlHost.WorkspaceHeader, host.WorkspaceId.ToString("D"));
        return client;
    }

    private static Task<string> AdminTokenAsync(ApiSqlHost host) => AdminTokenCoreAsync(host);
    private static async Task<string> AdminTokenCoreAsync(ApiSqlHost host)
        => (await ApiSqlHost.LoginAsync(host.Factory.CreateClient(), ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;

    private sealed record Accounts(Guid Fx, Guid Gain, Guid Loss, Guid Counter);

    // Four accounts and the USD currency in the annual catalog. No rate, no opening balance.
    private static async Task<Accounts> SeedAccountsAsync(ApiSqlHost host)
    {
        using var annual = host.AnnualContext();
        var now = DateTimeOffset.UtcNow;
        var fx = Guid.CreateVersion7(); var gain = Guid.CreateVersion7(); var loss = Guid.CreateVersion7(); var counter = Guid.CreateVersion7();
        async Task AccountAsync(Guid id, string code, byte type, string name) =>
            await annual.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[Accounts]([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({id},{host.CompanyId},{code},{name},{type},1,1,1,1,{now},{now})",
                Cancellation).ConfigureAwait(false);
        await AccountAsync(fx, "1500-FX", 1, "رصيد بعملة أجنبية").ConfigureAwait(false);
        await AccountAsync(gain, "4900-FXG", 4, "أرباح فروقات صرف").ConfigureAwait(false);
        await AccountAsync(loss, "5900-FXL", 5, "خسائر فروقات صرف").ConfigureAwait(false);
        await AccountAsync(counter, "2000-FXC", 2, "حساب مقابل").ConfigureAwait(false);
        await EnsureCurrencyAsync(annual, "USD", "دولار", "US Dollar").ConfigureAwait(false);
        return new Accounts(fx, gain, loss, counter);
    }

    private static async Task EnsureCurrencyAsync(Microsoft.EntityFrameworkCore.DbContext annual, string code, string ar, string en)
    {
        var now = DateTimeOffset.UtcNow;
        await annual.Database.ExecuteSqlInterpolatedAsync(
            $"IF NOT EXISTS (SELECT 1 FROM [system].[Currencies] WHERE [Code]={code}) INSERT INTO [system].[Currencies]([Code],[NameArabic],[NameEnglish],[IsActive],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({code},{ar},{en},1,{now},{now})",
            Cancellation).ConfigureAwait(false);
    }

    private static async Task SeedRateAsync(ApiSqlHost host, string currency, decimal rate, DateOnly effective)
    {
        using var annual = host.AnnualContext();
        var now = DateTimeOffset.UtcNow;
        await annual.Database.ExecuteSqlInterpolatedAsync(
            $"IF NOT EXISTS (SELECT 1 FROM [audit].[ActorReferences] WHERE [Id]={host.UserId}) INSERT INTO [audit].[ActorReferences]([Id],[CompanyId],[DisplayNameArabic],[DisplayNameEnglish],[FirstVerifiedAtUtc]) VALUES({host.UserId},{host.CompanyId},N'مدير النظام','Administrator',{now})",
            Cancellation).ConfigureAwait(false);
        // A precision-typed parameter: a plain EF decimal parameter defaults to decimal(18,2) and would silently truncate a
        // three-plus-decimal rate (0.709 -> 0.71), corrupting the revaluation amount.
        var rateParam = new Microsoft.Data.SqlClient.SqlParameter("@rate", System.Data.SqlDbType.Decimal) { Precision = 19, Scale = 8, Value = rate };
        await annual.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [system].[ExchangeRates]([Id],[CompanyId],[CurrencyCode],[BaseCurrencyCode],[Rate],[EffectiveDate],[Source],[Reason],[CreatedByUserId],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({Guid.CreateVersion7()},{host.CompanyId},{currency},'JOD',{rateParam},{effective},N'Seed',N'اختبار',{host.UserId},{now},{now})",
            Cancellation).ConfigureAwait(false);
    }

    // Registers a second branch in BOTH the central registry and the annual snapshot (same id): workspace readiness rejects
    // any annual branch that is not centrally registered for the company, so a second branch must exist in both.
    private static async Task<Guid> SeedBranchAsync(ApiSqlHost host, string code)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.CreateVersion7();
        const string sql = "INSERT INTO [system].[Branches]([Id],[CompanyId],[Code],[NameArabic],[NameEnglish],[IsActive],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({0},{1},{2},N'فرع ثانٍ','Second branch',1,{3},{3})";
        using (var central = host.CreateContext())
            await central.Database.ExecuteSqlRawAsync(sql, new object[] { id, host.CompanyId, code, now }, Cancellation).ConfigureAwait(false);
        using (var annual = host.AnnualContext())
            await annual.Database.ExecuteSqlRawAsync(sql, new object[] { id, host.CompanyId, code, now }, Cancellation).ConfigureAwait(false);
        return id;
    }

    // Posts a balanced two-line journal directly into the annual database (state 4). Raw SQL because no domain service can
    // construct an already-posted historical journal in one step; posting sequence >= 5000 keeps seeded history distinct
    // from journals the real posting service creates (which start at small sequences).
    private static async Task SeedPostedForeignLineAsync(ApiSqlHost host, Guid branchId, Accounts a, string currency,
        decimal foreignAmount, decimal baseAmount, DateOnly date, long sequence, bool debitExposure = true)
    {
        using var annual = host.AnnualContext();
        var now = DateTimeOffset.UtcNow;
        var period = await annual.Database.SqlQuery<Guid>(
            $"SELECT TOP 1 [Id] AS [Value] FROM [ledger].[AccountingPeriods] WHERE [CompanyId]={host.CompanyId} AND [FiscalYearId]={host.FiscalYearId} ORDER BY [RangeStart]")
            .FirstAsync(Cancellation).ConfigureAwait(false);
        var journal = Guid.CreateVersion7();
        await annual.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[JournalEntries]([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State],[PostingSequence],[PostingFiscalYearId],[PostingPeriodId],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({journal},{host.CompanyId},{branchId},{date},N'حركة بعملة أجنبية',4,{sequence},{host.FiscalYearId},{period},{now},{now})",
            Cancellation).ConfigureAwait(false);
        async Task LineAsync(Guid acct, string txnCur, decimal td, decimal tc, decimal bd, decimal bc) =>
            await annual.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [ledger].[JournalLines]([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],[BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description],[CreatedAtUtc],[UpdatedAtUtc])
                   VALUES({Guid.CreateVersion7()},{host.CompanyId},{journal},{acct},{txnCur},{td},{tc},'JOD',{bd},{bc},{txnCur},'JOD',1.0,0,N'سطر',{now},{now})",
                Cancellation).ConfigureAwait(false);
        if (debitExposure)
        {
            await LineAsync(a.Fx, currency, foreignAmount, 0m, baseAmount, 0m).ConfigureAwait(false);
            await LineAsync(a.Counter, "JOD", 0m, baseAmount, 0m, baseAmount).ConfigureAwait(false);
        }
        else
        {
            // Settlement: reduce the foreign exposure (credit the FX account) at the given base value.
            await LineAsync(a.Fx, currency, 0m, foreignAmount, 0m, baseAmount).ConfigureAwait(false);
            await LineAsync(a.Counter, "JOD", baseAmount, 0m, baseAmount, 0m).ConfigureAwait(false);
        }
    }

    private static string Root(ApiSqlHost host, Guid? branch = null)
        => $"/api/v1/companies/{host.CompanyId}/branches/{branch ?? host.BranchId}/currencies/fx-revaluation";

    private static async Task<(HttpStatusCode Status, FxRevaluationPreviewResponse? Body, string Code)> PreviewAsync(
        HttpClient client, ApiSqlHost host, DateOnly asOf, string? currency = null, string? overrideRate = null, Guid? branch = null)
    {
        using var resp = await client.PostAsJsonAsync(new Uri(Root(host, branch) + "/preview", UriKind.Relative),
            new FxRevaluationPreviewRequest { AsOfDate = asOf, CurrencyCode = currency, OverrideClosingRate = overrideRate }, ApiJson.Options, Cancellation).ConfigureAwait(false);
        if (resp.IsSuccessStatusCode)
            return (resp.StatusCode, await resp.Content.ReadFromJsonAsync<FxRevaluationPreviewResponse>(ApiJson.Options, Cancellation).ConfigureAwait(false), string.Empty);
        var code = string.Empty;
        try
        {
            using var problem = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(false));
            if (problem.RootElement.TryGetProperty("code", out var c)) code = c.GetString() ?? string.Empty;
        }
        catch (JsonException) { }
        return (resp.StatusCode, null, code);
    }

    private static async Task<(HttpStatusCode Status, FxRevaluationExecutionResponse? Body, string Code)> ExecuteAsync(
        HttpClient client, ApiSqlHost host, ExecuteFxRevaluationRequest request, Guid? branch = null)
    {
        using var resp = await client.PostAsJsonAsync(new Uri(Root(host, branch) + "/execute", UriKind.Relative), request, ApiJson.Options, Cancellation).ConfigureAwait(false);
        if (resp.IsSuccessStatusCode)
            return (resp.StatusCode, await resp.Content.ReadFromJsonAsync<FxRevaluationExecutionResponse>(ApiJson.Options, Cancellation).ConfigureAwait(false), string.Empty);
        var code = string.Empty;
        try
        {
            using var problem = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(false));
            if (problem.RootElement.TryGetProperty("code", out var c)) code = c.GetString() ?? string.Empty;
        }
        catch (JsonException) { }
        return (resp.StatusCode, null, code);
    }

    private static async Task<int> PostedFxJournalCountAsync(ApiSqlHost host)
    {
        using var annual = host.AnnualContext();
        return await annual.JournalEntries.AsNoTracking().CountAsync(j => j.PostingSequence < 5000, Cancellation).ConfigureAwait(false);
    }

    // Posted journals only (state 4): an interrupted/concurrent attempt may leave an inert unposted draft, which must never
    // count toward a ledger balance.
    private static async Task<decimal> GainBalanceAsync(ApiSqlHost host, Guid gain)
    {
        using var annual = host.AnnualContext();
        var posted = annual.JournalEntries.Where(j => j.State == 4).Select(j => j.Id);
        return await annual.JournalLines.AsNoTracking().Where(l => l.AccountId == gain && posted.Contains(l.JournalId)).SumAsync(l => l.BaseCredit, Cancellation).ConfigureAwait(false);
    }


    private static ExecuteFxRevaluationRequest Exec(Accounts a, DateOnly asOf, string key, string? description = null, string? currency = null)
        => new() { AsOfDate = asOf, GainAccountId = a.Gain, LossAccountId = a.Loss, IdempotencyKey = key, Description = description, CurrencyCode = currency };

    // ---------------------------------------------------------------- Preview correctness

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PreviewIsZeroWhenRevaluedEqualsCarryingValue()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.70m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            var (status, body, _) = await PreviewAsync(client, host, Oct).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Empty(body!.Items);
            Assert.Equal("0.0000", body.NetDifference);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PreviewReportsUnrealizedLoss()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.69m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            var (status, body, _) = await PreviewAsync(client, host, Oct).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("10.0000", body!.TotalUnrealizedLosses);
            Assert.Contains(body.Items, i => i.AccountId == a.Fx && !i.IsGain && i.UnrealizedDifference == "-10.0000");
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PreviewWithCurrencyFilterIsolatesTheRequestedCurrency()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            var euroAccount = Guid.CreateVersion7();
            using (var annual = host.AnnualContext())
            {
                await EnsureCurrencyAsync(annual, "EUR", "يورو", "Euro").ConfigureAwait(true);
                var now = DateTimeOffset.UtcNow;
                await annual.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO [ledger].[Accounts]([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({euroAccount},{host.CompanyId},N'1501-EUR',N'رصيد باليورو',1,1,1,1,1,{now},{now})",
                    Cancellation).ConfigureAwait(true);
            }
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedRateAsync(host, "EUR", 0.80m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a with { Fx = euroAccount }, "EUR", 500m, 380m, new DateOnly(2026, 2, 1), 5001).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            var (status, body, _) = await PreviewAsync(client, host, Oct, currency: "USD").ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            var usd = Assert.Single(body!.Items);
            Assert.Equal("USD", usd.CurrencyCode);
            Assert.Equal("700.0000", usd.BookBaseBalance);
            Assert.Equal("9.0000", usd.UnrealizedDifference);
            var (allStatus, all, _) = await PreviewAsync(client, host, Oct).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, allStatus);
            Assert.Equal(2, all!.Items.Count);
            Assert.Equal("29.0000", all.TotalUnrealizedGains);
        }
    }

    // ---------------------------------------------------------------- Execution correctness

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ExecutePostsABalancedLossJournal()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.69m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            var (status, body, _) = await ExecuteAsync(client, host, Exec(a, Oct, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("10.0000", body!.TotalLossAmount);
            using var annual = host.AnnualContext();
            var lines = await annual.JournalLines.AsNoTracking().Where(l => l.JournalId == body.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
            Assert.Equal(10m, lines.Where(l => l.AccountId == a.Loss).Sum(l => l.BaseDebit));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ExecutionResultEqualsThePreview()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            var (_, preview, _) = await PreviewAsync(client, host, Oct).ConfigureAwait(true);
            var (status, exec, _) = await ExecuteAsync(client, host, Exec(a, Oct, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(preview!.TotalUnrealizedGains, exec!.TotalGainAmount);
            Assert.Equal(preview.NetDifference, exec.NetRevaluationAmount);
        }
    }

    // ---------------------------------------------------------------- TASK B: no double counting

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SecondRevaluationAtTheSameRateRecognizesNothing()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            var (first, firstBody, _) = await ExecuteAsync(client, host, Exec(a, Oct, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, first);
            Assert.Equal("9.0000", firstBody!.TotalGainAmount);

            // Same market rate, a later date, a fresh key: the carrying value already reflects the +9, so there is nothing
            // left to recognize. A broken carrying value would re-recognize the whole 9 again.
            var (second, _, code) = await ExecuteAsync(client, host, Exec(a, Nov, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, second);
            Assert.Equal("fx.no_items", code);
            Assert.Equal(1, await PostedFxJournalCountAsync(host).ConfigureAwait(true));
            Assert.Equal(9m, await GainBalanceAsync(host, a.Gain).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SecondRevaluationAtANewRateRecognizesOnlyTheIncrement()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.72m, Nov).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            await ExecuteAsync(client, host, Exec(a, Oct, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);   // +9, carrying -> 709
            var (second, body, _) = await ExecuteAsync(client, host, Exec(a, Nov, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);

            // 1000 * 0.72 = 720 vs carrying 709 => +11 only (never +20).
            Assert.Equal(HttpStatusCode.OK, second);
            Assert.Equal("11.0000", body!.TotalGainAmount);
            Assert.Equal(2, await PostedFxJournalCountAsync(host).ConfigureAwait(true));
            Assert.Equal(20m, await GainBalanceAsync(host, a.Gain).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task RevaluationAfterPartialSettlementRevaluesOnlyRemainingExposure()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.71m, Oct).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.72m, Dec).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            // Revalue @0.71: 710 vs 700 => +10, carrying -> 710.
            var (_, first, _) = await ExecuteAsync(client, host, Exec(a, Oct, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);
            Assert.Equal("10.0000", first!.TotalGainAmount);

            // Settle 500 USD at 0.71 (base 355): foreign -> 500, carrying -> 355.
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 500m, 355m, Nov, 5001, debitExposure: false).ConfigureAwait(true);

            // Revalue @0.72: remaining 500 * 0.72 = 360 vs carrying 355 => +5.
            var (status, second, _) = await ExecuteAsync(client, host, Exec(a, Dec, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("5.0000", second!.TotalGainAmount);
        }
    }

    // ---------------------------------------------------------------- TASK D: content-validated idempotency

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SameKeyAndSameContentReplaysTheOriginalJournal()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            var key = Guid.NewGuid().ToString("N");
            var (_, first, _) = await ExecuteAsync(client, host, Exec(a, Oct, key)).ConfigureAwait(true);
            var (status, second, _) = await ExecuteAsync(client, host, Exec(a, Oct, key)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(first!.JournalEntryId, second!.JournalEntryId);
            Assert.Equal(1, await PostedFxJournalCountAsync(host).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SameKeyWithDifferentContentIsRejectedAsAConflict()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.72m, Nov).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            var key = Guid.NewGuid().ToString("N");
            var (first, _, _) = await ExecuteAsync(client, host, Exec(a, Oct, key)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, first);

            // Reusing the key with a different as-of date is a different economic request.
            var (status, _, code) = await ExecuteAsync(client, host, Exec(a, Nov, key)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
            Assert.Equal("fx.idempotency_conflict", code);
            Assert.Equal(1, await PostedFxJournalCountAsync(host).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task IdempotencyConflictLeavesTheOriginalJournalIntact()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.72m, Nov).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            var key = Guid.NewGuid().ToString("N");
            var (_, first, _) = await ExecuteAsync(client, host, Exec(a, Oct, key)).ConfigureAwait(true);
            await ExecuteAsync(client, host, Exec(a, Nov, key)).ConfigureAwait(true); // conflict

            Assert.Equal(9m, await GainBalanceAsync(host, a.Gain).ConfigureAwait(true));
            using var annual = host.AnnualContext();
            var posted = await annual.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == first!.JournalEntryId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)4, posted.State);
        }
    }

    // ---------------------------------------------------------------- TASK C: branch isolation

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PreviewExcludesForeignExposureFromOtherBranches()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            var branchB = await SeedBranchAsync(host, "BR2").ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, branchB, a, "USD", 5000m, 3500m, new DateOnly(2026, 2, 1), 5001).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            // Branch A alone: 1000 * 0.709 = 709 vs 700 carrying (branch A only) => +9, never +54 for the combined 6000.
            var (status, body, code) = await PreviewAsync(client, host, Oct).ConfigureAwait(true);
            Assert.True(status == HttpStatusCode.OK, $"preview code={code}");
            Assert.Equal("9.0000", body!.TotalUnrealizedGains);
            var item = Assert.Single(body.Items, i => i.AccountId == a.Fx);
            Assert.Equal("1000.0000", item.ForeignBalance);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ExecuteRevaluesOnlyTheSelectedBranchExposure()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            var branchB = await SeedBranchAsync(host, "BR2").ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, branchB, a, "USD", 5000m, 3500m, new DateOnly(2026, 2, 1), 5001).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            var (status, body, _) = await ExecuteAsync(client, host, Exec(a, Oct, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("9.0000", body!.TotalGainAmount);
        }
    }

    // ---------------------------------------------------------------- TASK E: durable, recoverable execution

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ResumesAnInterruptedRequestThatPostedButNeverCompleted()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            var key = Guid.NewGuid().ToString("N");
            var (_, first, _) = await ExecuteAsync(client, host, Exec(a, Oct, key)).ConfigureAwait(true);

            // Simulate a crash AFTER the journal posted but BEFORE the request row was marked complete.
            using (var annual = host.AnnualContext())
                await annual.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE [ledger].[FxRevaluationRequests] SET [Status]=1,[OutcomeJournalId]=NULL,[CompletedAtUtc]=NULL WHERE [IdempotencyKey]={key}",
                    Cancellation).ConfigureAwait(true);

            // Retrying the identical request resumes to completion against the SAME posted journal — no second posting.
            var (status, resumed, _) = await ExecuteAsync(client, host, Exec(a, Oct, key)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(first!.JournalEntryId, resumed!.JournalEntryId);
            Assert.Equal(1, await PostedFxJournalCountAsync(host).ConfigureAwait(true));
            Assert.Equal(9m, await GainBalanceAsync(host, a.Gain).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ConcurrentExecutionsWithTheSameKeyPostExactlyOneJournal()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            var token = await AdminTokenAsync(host).ConfigureAwait(true);
            var key = Guid.NewGuid().ToString("N");

            using var c1 = Workspace(host, token);
            using var c2 = Workspace(host, token);
            var t1 = ExecuteAsync(c1, host, Exec(a, Oct, key));
            var t2 = ExecuteAsync(c2, host, Exec(a, Oct, key));
            var results = await Task.WhenAll(t1, t2).ConfigureAwait(true);

            var successes = results.Where(r => r.Status == HttpStatusCode.OK && r.Body is not null).Select(r => r.Body!).ToArray();
            Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));
            Assert.Equal(2, successes.Length);
            Assert.All(successes, r => Assert.Equal(successes[0].JournalEntryId, r.JournalEntryId));
            using var annual = host.AnnualContext();
            Assert.Equal(1, await annual.JournalEntries.CountAsync(j => j.PostingSequence == null || j.PostingSequence < 5000, Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await PostedFxJournalCountAsync(host).ConfigureAwait(true));
            Assert.Equal(9m, await GainBalanceAsync(host, a.Gain).ConfigureAwait(true));
        }
    }

    // ---------------------------------------------------------------- Validation & guards

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ExecutionWithoutForeignExposureYieldsNoItems()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            var (status, _, code) = await ExecuteAsync(client, host, Exec(a, Oct, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
            Assert.Equal("fx.no_items", code);
            Assert.Equal(0, await PostedFxJournalCountAsync(host).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task GainAndLossAccountsAreRequired()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            await SeedAccountsAsync(host).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));
            var request = new ExecuteFxRevaluationRequest { AsOfDate = Oct, GainAccountId = Guid.Empty, LossAccountId = Guid.Empty, IdempotencyKey = Guid.NewGuid().ToString("N") };
            var (status, _, _) = await ExecuteAsync(client, host, request).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task IdempotencyKeyIsRequired()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));
            var request = new ExecuteFxRevaluationRequest { AsOfDate = Oct, GainAccountId = a.Gain, LossAccountId = a.Loss, IdempotencyKey = string.Empty };
            var (status, _, _) = await ExecuteAsync(client, host, request).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.BadRequest, status);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ValidationFailureLeavesNoReservationRow()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));
            var request = new ExecuteFxRevaluationRequest { AsOfDate = Oct, GainAccountId = Guid.Empty, LossAccountId = a.Loss, IdempotencyKey = Guid.NewGuid().ToString("N") };
            await ExecuteAsync(client, host, request).ConfigureAwait(true);
            using var annual = host.AnnualContext();
            var reservations = await annual.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [ledger].[FxRevaluationRequests]").SingleAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(0, reservations);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task OverrideClosingRateIsUsedWhenNoMarketRateExists()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            // No market rate seeded — only the override drives the revaluation.
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            var request = Exec(a, Oct, Guid.NewGuid().ToString("N")) with { OverrideClosingRate = "0.72" };
            var (status, body, _) = await ExecuteAsync(client, host, request).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("20.0000", body!.TotalGainAmount); // 1000 * 0.72 = 720 vs 700 => +20
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PreviewAndExecuteAgreeThenPreviewIsFlatAfterExecution()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            await ExecuteAsync(client, host, Exec(a, Oct, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);

            // After execution at the same rate, a fresh preview shows no remaining difference (fully revalued).
            var (status, body, _) = await PreviewAsync(client, host, Oct).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Empty(body!.Items);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SelectedAccountFilterRestrictsTheRevaluationToChosenAccounts()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            // Selecting an unrelated account id excludes the only exposure, so there is nothing to post.
            var request = Exec(a, Oct, Guid.NewGuid().ToString("N")) with { SelectedAccountIds = new[] { Guid.NewGuid() } };
            var (status, _, code) = await ExecuteAsync(client, host, request).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
            Assert.Equal("fx.no_items", code);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task DescriptionIsPartOfTheIdempotencyFingerprint()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));

            var key = Guid.NewGuid().ToString("N");
            var (first, _, _) = await ExecuteAsync(client, host, Exec(a, Oct, key, description: "قيد أول")).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, first);
            var (status, _, code) = await ExecuteAsync(client, host, Exec(a, Oct, key, description: "قيد مختلف")).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
            Assert.Equal("fx.idempotency_conflict", code);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ExecutionIsRejectedWithoutASelectedWorkspace()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, new DateOnly(2026, 2, 1), 5000).ConfigureAwait(true);
            var token = await AdminTokenAsync(host).ConfigureAwait(true);
            using var noWorkspace = host.Factory.CreateClient();
            noWorkspace.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await noWorkspace.PostAsJsonAsync(new Uri(Root(host) + "/execute", UriKind.Relative),
                Exec(a, Oct, Guid.NewGuid().ToString("N")), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Equal(0, await PostedFxJournalCountAsync(host).ConfigureAwait(true));
        }
    }
}
