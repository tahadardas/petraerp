using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetraERP.Application.Security;
using PetraERP.Contracts.Currencies;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Api.Tests;

/// <summary>Foreign-currency revaluation over the independent annual workspace: authorization, rate validation,
/// balanced posting, preview/execution agreement and idempotent retry (explicit SQL tests).</summary>
public sealed class FxRevaluationHttpSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static readonly DateOnly AsOf = new(2026, 10, 24);

    private static HttpClient Workspace(ApiSqlHost host, string token)
    {
        var client = host.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add(ApiSqlHost.WorkspaceHeader, host.WorkspaceId.ToString("D"));
        return client;
    }

    // Seeds a posted foreign-currency (USD) balance of 1000 booked at base 700 (rate 0.70) plus gain/loss/counter accounts.
    // Raw SQL into the disposable annual database only: no domain service can construct an already-posted historical journal
    // in one step, and application authorization is still exercised by executing the real HTTP endpoint.
    private static async Task<(Guid Fx, Guid Gain, Guid Loss)> SeedForeignBalanceAsync(ApiSqlHost host, bool withRate)
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

        // USD in the annual catalog (FK target for the rate and the foreign line).
        await annual.Database.ExecuteSqlInterpolatedAsync(
            $"IF NOT EXISTS (SELECT 1 FROM [system].[Currencies] WHERE [Code]='USD') INSERT INTO [system].[Currencies]([Code],[NameArabic],[NameEnglish],[IsActive],[CreatedAtUtc],[UpdatedAtUtc]) VALUES('USD',N'دولار','US Dollar',1,{now},{now})",
            Cancellation).ConfigureAwait(false);

        if (withRate)
        {
            // An actor reference is required by the ExchangeRates FK; the admin is a verified central identity.
            await annual.Database.ExecuteSqlInterpolatedAsync(
                $"IF NOT EXISTS (SELECT 1 FROM [audit].[ActorReferences] WHERE [Id]={host.UserId}) INSERT INTO [audit].[ActorReferences]([Id],[CompanyId],[DisplayNameArabic],[DisplayNameEnglish],[FirstVerifiedAtUtc]) VALUES({host.UserId},{host.CompanyId},N'مدير النظام','Administrator',{now})",
                Cancellation).ConfigureAwait(false);
            await annual.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [system].[ExchangeRates]([Id],[CompanyId],[CurrencyCode],[BaseCurrencyCode],[Rate],[EffectiveDate],[Source],[Reason],[CreatedByUserId],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({Guid.CreateVersion7()},{host.CompanyId},'USD','JOD',0.709,{AsOf},N'Seed',N'اختبار',{host.UserId},{now},{now})",
                Cancellation).ConfigureAwait(false);
        }

        // Posted (state 4) balanced journal: USD 1000 (base 700) debit on the FX account, 700 base credit on the counter.
        var period = await annual.Database.SqlQuery<Guid>(
            $"SELECT TOP 1 [Id] AS [Value] FROM [ledger].[AccountingPeriods] WHERE [CompanyId]={host.CompanyId} AND [FiscalYearId]={host.FiscalYearId} ORDER BY [RangeStart]")
            .FirstAsync(Cancellation).ConfigureAwait(false);
        var journal = Guid.CreateVersion7();
        await annual.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[JournalEntries]([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State],[PostingSequence],[PostingFiscalYearId],[PostingPeriodId],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({journal},{host.CompanyId},{host.BranchId},{new DateOnly(2026,2,1)},N'رصيد افتتاحي بالدولار',4,5000,{host.FiscalYearId},{period},{now},{now})",
            Cancellation).ConfigureAwait(false);
        async Task LineAsync(Guid acct, string txnCur, decimal td, decimal tc, decimal bd, decimal bc) =>
            await annual.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [ledger].[JournalLines]([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],[BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description],[CreatedAtUtc],[UpdatedAtUtc])
                   VALUES({Guid.CreateVersion7()},{host.CompanyId},{journal},{acct},{txnCur},{td},{tc},'JOD',{bd},{bc},{txnCur},'JOD',1.0,0,N'سطر',{now},{now})",
                Cancellation).ConfigureAwait(false);
        await LineAsync(fx, "USD", 1000m, 0m, 700m, 0m).ConfigureAwait(false);
        await LineAsync(counter, "JOD", 0m, 700m, 0m, 700m).ConfigureAwait(false);
        return (fx, gain, loss);
    }

    private static async Task<Guid> SeedFxViewerAsync(ApiSqlHost host, string password)
    {
        using var scope = host.Factory.Services.CreateScope();
        var hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password);
        using var db = host.CreateContext();
        var userId = Guid.CreateVersion7(); var roleId = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [security].[Users]([Id],[UserName],[NormalizedUserName],[DisplayNameArabic],[PasswordHash],[PasswordSalt],[PasswordIterations],[PasswordAlgorithm],[PasswordChangedAtUtc],[MustChangePassword],[IsActive],[AccessFailedCount],[SecurityStamp],[CreatedAtUtc],[UpdatedAtUtc])
               VALUES({userId},N'fxviewer',N'FXVIEWER',N'مطلع فقط',{hash.Hash.ToArray()},{hash.Salt.ToArray()},{hash.Iterations},{hash.Algorithm},{now},0,1,0,{Guid.NewGuid()},{now},{now})", Cancellation).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[Roles]([Id],[Code],[NameArabic],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({roleId},N'FXVIEWER',N'مطلع',{now},{now})", Cancellation).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) VALUES({roleId},N'Currencies.FxRevaluation.View')", Cancellation).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[UserRoles]([UserId],[RoleId]) VALUES({userId},{roleId})", Cancellation).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[UserCompanyScopes]([UserId],[CompanyId]) VALUES({userId},{host.CompanyId})", Cancellation).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[UserBranchScopes]([UserId],[CompanyId],[BranchId]) VALUES({userId},{host.CompanyId},{host.BranchId})", Cancellation).ConfigureAwait(false);
        return userId;
    }

    private static string Root(ApiSqlHost host) => $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}/currencies/fx-revaluation";

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AuthorizedExecutionPostsABalancedJournalMatchesPreviewAndReplaysOnRetry()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var (fx, gain, loss) = await SeedForeignBalanceAsync(host, withRate: true).ConfigureAwait(true);
            var admin = await ApiSqlHost.LoginAsync(host.Factory.CreateClient(), ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            using var client = Workspace(host, admin.AccessToken);

            // Preview: 1000 USD * 0.709 = 709 base vs 700 booked => +9 unrealized gain.
            using var previewResp = await client.PostAsJsonAsync(new Uri(Root(host) + "/preview", UriKind.Relative),
                new FxRevaluationPreviewRequest { AsOfDate = AsOf }, ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, previewResp.StatusCode);
            var preview = await previewResp.Content.ReadFromJsonAsync<FxRevaluationPreviewResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal("9.0000", preview!.TotalUnrealizedGains);
            Assert.Contains(preview.Items, i => i.AccountId == fx && i.IsGain && i.UnrealizedDifference == "9.0000");

            // Execute: posts a balanced journal; result matches the preview.
            var key = Guid.NewGuid().ToString("N");
            var request = new ExecuteFxRevaluationRequest { AsOfDate = AsOf, GainAccountId = gain, LossAccountId = loss, IdempotencyKey = key };
            using var execResp = await client.PostAsJsonAsync(new Uri(Root(host) + "/execute", UriKind.Relative), request, ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, execResp.StatusCode);
            var exec = await execResp.Content.ReadFromJsonAsync<FxRevaluationExecutionResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal("9.0000", exec!.TotalGainAmount);
            Assert.Equal("9.0000", exec.NetRevaluationAmount);

            // The posted FX journal is balanced and the gain account carries exactly the net gain, in the annual database.
            using (var annual = host.AnnualContext())
            {
                var posted = await annual.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == exec.JournalEntryId, Cancellation).ConfigureAwait(true);
                Assert.Equal((byte)4, posted.State);
                var lines = await annual.JournalLines.AsNoTracking().Where(l => l.JournalId == exec.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
                Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
                Assert.Equal(9m, lines.Where(l => l.AccountId == gain).Sum(l => l.BaseCredit));
            }

            // Retry with the SAME idempotency key returns the SAME journal and never posts a second one.
            using var retryResp = await client.PostAsJsonAsync(new Uri(Root(host) + "/execute", UriKind.Relative), request, ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, retryResp.StatusCode);
            var retry = await retryResp.Content.ReadFromJsonAsync<FxRevaluationExecutionResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal(exec.JournalEntryId, retry!.JournalEntryId);
            using (var annual = host.AnnualContext())
                Assert.Equal(1, await annual.JournalLines.AsNoTracking().CountAsync(l => l.AccountId == gain, Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MissingClosingRateIsRejectedWithoutPosting()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var (_, gain, loss) = await SeedForeignBalanceAsync(host, withRate: false).ConfigureAwait(true);
            var admin = await ApiSqlHost.LoginAsync(host.Factory.CreateClient(), ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            using var client = Workspace(host, admin.AccessToken);
            using var resp = await client.PostAsJsonAsync(new Uri(Root(host) + "/execute", UriKind.Relative),
                new ExecuteFxRevaluationRequest { AsOfDate = AsOf, GainAccountId = gain, LossAccountId = loss, IdempotencyKey = Guid.NewGuid().ToString("N") },
                ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.False(resp.IsSuccessStatusCode);
            using var problem = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal("fx.closing_rate_unavailable", problem.RootElement.GetProperty("code").GetString());
            using var annual = host.AnnualContext();
            Assert.Equal(0, await annual.JournalEntries.AsNoTracking().CountAsync(j => j.PostingSequence < 5000, Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task InvalidOverrideRateIsRejected()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var admin = await ApiSqlHost.LoginAsync(host.Factory.CreateClient(), ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            using var client = Workspace(host, admin.AccessToken);
            foreach (var bad in new[] { "abc", "-1", "0" })
            {
                using var resp = await client.PostAsJsonAsync(new Uri(Root(host) + "/preview", UriKind.Relative),
                    new FxRevaluationPreviewRequest { AsOfDate = AsOf, OverrideClosingRate = bad }, ApiJson.Options, Cancellation).ConfigureAwait(true);
                Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
                using var problem = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal("fx.override_rate_invalid", problem.RootElement.GetProperty("code").GetString());
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ExecutionRequiresTheExecutePermissionAndASelectedWorkspace()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var (_, gain, loss) = await SeedForeignBalanceAsync(host, withRate: true).ConfigureAwait(true);
            await SeedFxViewerAsync(host, "FxViewer-Secure-2026!").ConfigureAwait(true);
            var request = new ExecuteFxRevaluationRequest { AsOfDate = AsOf, GainAccountId = gain, LossAccountId = loss, IdempotencyKey = Guid.NewGuid().ToString("N") };

            // A user holding only the View permission cannot execute.
            var viewer = await ApiSqlHost.LoginAsync(host.Factory.CreateClient(), "fxviewer", "FxViewer-Secure-2026!").ConfigureAwait(true);
            using (var viewerClient = Workspace(host, viewer.AccessToken))
            using (var forbidden = await viewerClient.PostAsJsonAsync(new Uri(Root(host) + "/execute", UriKind.Relative), request, ApiJson.Options, Cancellation).ConfigureAwait(true))
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            // The admin can execute, but only with a selected annual workspace.
            var admin = await ApiSqlHost.LoginAsync(host.Factory.CreateClient(), ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            using (var noWorkspace = host.Factory.CreateClient())
            {
                noWorkspace.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
                using var missing = await noWorkspace.PostAsJsonAsync(new Uri(Root(host) + "/execute", UriKind.Relative), request, ApiJson.Options, Cancellation).ConfigureAwait(true);
                Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            }
        }
    }
}
