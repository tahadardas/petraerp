using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetraERP.Application.Security;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Tests;

/// <summary>HTTP behavior of chart of accounts, calendar, currency context, reports and typed ledger errors (explicit SQL tests).</summary>
public sealed partial class LedgerManagementHttpSqlTests
{
    private const string LimitedPassword = "Limited-Secure-2026-محدود";
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static async Task<(HttpStatusCode Status, T? Body, string? Code)> SendAsync<T>(ApiSqlHost host, HttpClient client, HttpMethod method, string path, string token,
        object? body = null, Guid? idempotencyKey = null, Guid? correlationId = null)
    {
        using var request = host.WorkspaceRequest(method, path, token, body is null ? null : JsonContent.Create(body, body.GetType(), options: ApiJson.Options));
        if (idempotencyKey is { } key) request.Headers.Add("Idempotency-Key", key.ToString("D"));
        if (correlationId is { } correlation) request.Headers.Add("X-Correlation-Id", correlation.ToString("D"));
        using var response = await client.SendAsync(request, Cancellation).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Structured problem-details check (no fragile substring match on error text).
            Assert.DoesNotContain("StackTrace", text, StringComparison.Ordinal);
            Assert.DoesNotContain(" at PetraERP.", text, StringComparison.Ordinal);
            using var problem = JsonDocument.Parse(text);
            Assert.True(problem.RootElement.TryGetProperty("title", out _));
            return (response.StatusCode, default, problem.RootElement.GetProperty("code").GetString());
        }

        return (response.StatusCode, text.Length == 0 ? default : JsonSerializer.Deserialize<T>(text, ApiJson.Options), null);
    }

    /// <summary>A real user with exactly the given permissions on the bootstrap company/branch (no pending password change).</summary>
    private static async Task CreateLimitedUserAsync(ApiSqlHost host, Guid companyId, Guid branchId, params string[] permissions)
    {
        using var scope = host.Factory.Services.CreateScope();
        var hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(LimitedPassword);
        using var context = host.CreateContext();
        var userId = Guid.CreateVersion7();
        var roleId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [security].[Users]([Id],[UserName],[NormalizedUserName],[DisplayNameArabic],[PasswordHash],[PasswordSalt],[PasswordIterations],
                [PasswordAlgorithm],[PasswordChangedAtUtc],[MustChangePassword],[IsActive],[AccessFailedCount],[SecurityStamp],[CreatedAtUtc],[UpdatedAtUtc])
               VALUES({userId},N'limited',N'LIMITED',N'محاسب محدود',{hash.Hash.ToArray()},{hash.Salt.ToArray()},{hash.Iterations},{hash.Algorithm},{now},0,1,0,{Guid.NewGuid()},{now},{now})",
            Cancellation).ConfigureAwait(false);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[Roles]([Id],[Code],[NameArabic],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({roleId},N'LIMITED',N'محدود',{now},{now})", Cancellation).ConfigureAwait(false);
        foreach (var permission in permissions)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) VALUES({roleId},{permission})", Cancellation).ConfigureAwait(false);
        }

        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[UserRoles]([UserId],[RoleId]) VALUES({userId},{roleId})", Cancellation).ConfigureAwait(false);
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[UserCompanyScopes]([UserId],[CompanyId]) VALUES({userId},{companyId})", Cancellation).ConfigureAwait(false);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[UserBranchScopes]([UserId],[CompanyId],[BranchId]) VALUES({userId},{companyId},{branchId})", Cancellation).ConfigureAwait(false);
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ReopeningAPeriodNeedsTheElevatedPermissionAndClosedPeriodsRejectPostingWithAStableCode()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var (companyId, branchId, _) = await LedgerSeed.CalendarAsync(host).ConfigureAwait(true);
            var (journalId, version) = await LedgerSeed.ApprovedJournalAsync(host, "JOD").ConfigureAwait(true);

            await CreateLimitedUserAsync(host, companyId, branchId, PermissionCatalog.PeriodsView, PermissionCatalog.PeriodsManage).ConfigureAwait(true);
            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            var limited = await ApiSqlHost.LoginAsync(client, "limited", LimitedPassword).ConfigureAwait(true);
            var root = $"/api/v1/companies/{companyId}/branches/{branchId}/ledger";

            var years = await SendAsync<List<FiscalYearResponse>>(host, client, HttpMethod.Get, root + "/calendar/fiscal-years", limited.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, years.Status);
            var january = years.Body!.Single().Periods.OrderBy(p => p.RangeStart).First();
            Assert.Equal(AccountingPeriodState.Open, january.State);

            var closed = await SendAsync<AccountingPeriodResponse>(host, client, HttpMethod.Post, root + $"/calendar/periods/{january.Id}/close", limited.AccessToken,
                new PeriodTransitionRequest { ExpectedVersion = january.Version, Reason = "إقفال شهري" }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, closed.Status);
            Assert.Equal(AccountingPeriodState.Closed, closed.Body!.State);

            var denied = await SendAsync<AccountingPeriodResponse>(host, client, HttpMethod.Post, root + $"/calendar/periods/{january.Id}/reopen", limited.AccessToken,
                new PeriodTransitionRequest { ExpectedVersion = closed.Body.Version, Reason = "محاولة غير مصرح بها" }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, denied.Status);
            Assert.Equal("auth.permission_denied", denied.Code);

            var post = await SendAsync<PostingReceiptResponse>(host, client, HttpMethod.Post, root + $"/journals/{journalId}/post", admin.AccessToken,
                new PostJournalRequest { ExpectedVersion = version, Reason = "ترحيل في فترة مقفلة" }, Guid.NewGuid()).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, post.Status);
            Assert.Equal("ledger.period_closed", post.Code);

            var reopened = await SendAsync<AccountingPeriodResponse>(host, client, HttpMethod.Post, root + $"/calendar/periods/{january.Id}/reopen", admin.AccessToken,
                new PeriodTransitionRequest { ExpectedVersion = closed.Body.Version, Reason = "إعادة فتح معتمدة" }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, reopened.Status);
            var audit = await SendAsync<List<PeriodAuditResponse>>(host, client, HttpMethod.Get, root + $"/calendar/periods/{january.Id}/audit", limited.AccessToken).ConfigureAwait(true);
            Assert.Equal(["إقفال شهري", "إعادة فتح معتمدة"], audit.Body!.Select(a => a.Reason));

            var posted = await SendAsync<PostingReceiptResponse>(host, client, HttpMethod.Post, root + $"/journals/{journalId}/post", admin.AccessToken,
                new PostJournalRequest { ExpectedVersion = version, Reason = "ترحيل بعد إعادة الفتح" }, Guid.NewGuid()).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, posted.Status);

            // Transport carries the journal state by name.
            using var raw = host.WorkspaceRequest(HttpMethod.Get, root + $"/journals/{journalId}", admin.AccessToken);
            using var rawResponse = await client.SendAsync(raw, Cancellation).ConfigureAwait(true);
            using var document = JsonDocument.Parse(await rawResponse.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal("Posted", document.RootElement.GetProperty("state").GetString());
            var filtered = await SendAsync<JournalPagedResponse>(host, client, HttpMethod.Get, root + "/journals?state=Posted", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(JournalState.Posted, Assert.Single(filtered.Body!.Items).State);

            var noManage = await SendAsync<AccountResponse>(host, client, HttpMethod.Post, root + "/chart/accounts", limited.AccessToken,
                new AccountRequest { Code = "9", NameArabic = "ممنوع", AccountType = AccountType.Assets, PostingAllowed = false, CostCenterPolicy = CostCenterPolicy.Optional, CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, noManage.Status);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ChartCurrencyContextAndReportsWorkEndToEndWithDecimalStringsAndCompanyIsolation()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var (companyId, branchId, _) = await LedgerSeed.CalendarAsync(host).ConfigureAwait(true);
            var (journalId, version) = await LedgerSeed.ApprovedJournalAsync(host, "JOD").ConfigureAwait(true);

            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            var root = $"/api/v1/companies/{companyId}/branches/{branchId}/ledger";

            var currency = await SendAsync<CompanyCurrencyContextResponse>(host, client, HttpMethod.Get, root + "/currency-context", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal("JOD", currency.Body!.BaseCurrency.Code);
            Assert.Equal("الدينار الأردني", currency.Body.BaseCurrency.NameArabic);

            var parent = await SendAsync<AccountResponse>(host, client, HttpMethod.Post, root + "/chart/accounts", admin.AccessToken,
                new AccountRequest { Code = "19", NameArabic = "الأصول", NameEnglish = "Assets", AccountType = AccountType.Assets, PostingAllowed = false,
                    CostCenterPolicy = CostCenterPolicy.Optional, CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, parent.Status);
            var child = new AccountRequest { Code = "1909", NameArabic = "صندوق فرعي", AccountType = AccountType.Assets, PostingAllowed = true,
                CostCenterPolicy = CostCenterPolicy.Optional, CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly, ParentId = parent.Body!.Id };
            var created = await SendAsync<AccountResponse>(host, client, HttpMethod.Post, root + "/chart/accounts", admin.AccessToken, child).ConfigureAwait(true);
            Assert.Equal(1, created.Body!.Level);
            var duplicate = await SendAsync<AccountResponse>(host, client, HttpMethod.Post, root + "/chart/accounts", admin.AccessToken, child).ConfigureAwait(true);
            Assert.Equal((HttpStatusCode.Conflict, "ledger.account_code_taken"), (duplicate.Status, duplicate.Code));
            var cycle = await SendAsync<AccountResponse>(host, client, HttpMethod.Put, root + $"/chart/accounts/{parent.Body.Id}?expectedVersion={Uri.EscapeDataString(parent.Body.Version)}",
                admin.AccessToken, new AccountRequest { Code = "19", NameArabic = "الأصول", AccountType = AccountType.Assets, PostingAllowed = false,
                    CostCenterPolicy = CostCenterPolicy.Optional, CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly, ParentId = parent.Body.Id }).ConfigureAwait(true);
            Assert.Equal((HttpStatusCode.UnprocessableEntity, "ledger.account_hierarchy_cycle"), (cycle.Status, cycle.Code));
            var stale = await SendAsync<AccountResponse>(host, client, HttpMethod.Post, root + $"/chart/accounts/{created.Body.Id}/activation", admin.AccessToken,
                new SetActiveRequest { ExpectedVersion = parent.Body.Version, IsActive = false }).ConfigureAwait(true);
            Assert.Equal((HttpStatusCode.Conflict, "concurrency.conflict"), (stale.Status, stale.Code));
            var tree = await SendAsync<List<AccountResponse>>(host, client, HttpMethod.Get, root + "/chart/accounts/tree", admin.AccessToken).ConfigureAwait(true);
            Assert.Contains(tree.Body!, a => a.Id == created.Body.Id && a.ParentId == parent.Body.Id);

            // Delete only an unused leaf: a parent is refused, a reason is required, a stale version conflicts.
            var parentNow = await SendAsync<AccountResponse>(host, client, HttpMethod.Get, root + $"/chart/accounts/{parent.Body.Id}", admin.AccessToken).ConfigureAwait(true);
            var deleteParent = await SendAsync<object>(host, client, HttpMethod.Post, root + $"/chart/accounts/{parent.Body.Id}/deletion", admin.AccessToken,
                new DeleteAccountRequest { ExpectedVersion = parentNow.Body!.Version, Reason = "تنظيف" }).ConfigureAwait(true);
            Assert.Equal((HttpStatusCode.UnprocessableEntity, "ledger.account_has_children"), (deleteParent.Status, deleteParent.Code));
            var noReason = await SendAsync<object>(host, client, HttpMethod.Post, root + $"/chart/accounts/{created.Body.Id}/deletion", admin.AccessToken,
                new DeleteAccountRequest { ExpectedVersion = created.Body.Version, Reason = "" }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.BadRequest, noReason.Status);
            var staleDelete = await SendAsync<object>(host, client, HttpMethod.Post, root + $"/chart/accounts/{created.Body.Id}/deletion", admin.AccessToken,
                new DeleteAccountRequest { ExpectedVersion = parentNow.Body.Version, Reason = "تنظيف" }).ConfigureAwait(true);
            Assert.Equal((HttpStatusCode.Conflict, "concurrency.conflict"), (staleDelete.Status, staleDelete.Code));
            var deleted = await SendAsync<object>(host, client, HttpMethod.Post, root + $"/chart/accounts/{created.Body.Id}/deletion", admin.AccessToken,
                new DeleteAccountRequest { ExpectedVersion = created.Body.Version, Reason = "أُنشئ بالخطأ" }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
            var gone = await SendAsync<AccountResponse>(host, client, HttpMethod.Get, root + $"/chart/accounts/{created.Body.Id}", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.NotFound, gone.Status);
            var deletions = await SendAsync<List<AccountAuditResponse>>(host, client, HttpMethod.Get, root + "/chart/accounts/deletions", admin.AccessToken).ConfigureAwait(true);
            var first = await SendAsync<AccountNavigationResponse>(host, client, HttpMethod.Get, root + "/chart/accounts/navigation?direction=First", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, first.Status); Assert.NotNull(first.Body!.Account);
            var afterLast = await SendAsync<AccountNavigationResponse>(host, client, HttpMethod.Get, root + "/chart/accounts/navigation?direction=Last", admin.AccessToken).ConfigureAwait(true);
            var none = await SendAsync<AccountNavigationResponse>(host, client, HttpMethod.Get, root + $"/chart/accounts/navigation?direction=Next&fromAccountId={afterLast.Body!.Account!.Id}", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, none.Status); Assert.Null(none.Body!.Account);
            var missingFrom = await SendAsync<AccountNavigationResponse>(host, client, HttpMethod.Get, root + "/chart/accounts/navigation?direction=Next", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.BadRequest, missingFrom.Status);
            var evidence = Assert.Single(deletions.Body!);
            Assert.Equal((created.Body.Id, "1909", "صندوق فرعي", "أُنشئ بالخطأ", parent.Body.Id), (evidence.AccountId, evidence.Code, evidence.NameArabic, evidence.Reason, evidence.ParentId));

            var center = await SendAsync<CostCenterResponse>(host, client, HttpMethod.Post, root + "/chart/cost-centers", admin.AccessToken,
                new CostCenterRequest { Code = "C1", NameArabic = "مركز عمّان" }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, center.Status);

            var posted = await SendAsync<PostingReceiptResponse>(host, client, HttpMethod.Post, root + $"/journals/{journalId}/post", admin.AccessToken,
                new PostJournalRequest { ExpectedVersion = version, Reason = "ترحيل" }, Guid.NewGuid()).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, posted.Status);

            var tb = await SendAsync<TrialBalanceResponse>(host, client, HttpMethod.Get, root + "/reports/trial-balance?to=2026-12-31", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, tb.Status);
            Assert.True(tb.Body!.IsBalanced);
            Assert.Equal("250.5000", tb.Body.TotalPeriodDebit);
            Assert.Equal(tb.Body.TotalPeriodDebit, tb.Body.TotalPeriodCredit);
            Assert.Equal("JOD", tb.Body.BaseCurrencyCode);
            var journal = await SendAsync<GeneralJournalResponse>(host, client, HttpMethod.Get, root + "/reports/general-journal", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(2, journal.Body!.TotalCount);
            Assert.Equal(journal.Body.TotalBaseDebit, journal.Body.TotalBaseCredit);
            var cashLine = journal.Body.Lines.Single(l => l.BaseDebit != "0.0000");
            var ledger = await SendAsync<AccountLedgerResponse>(host, client, HttpMethod.Get, root + $"/reports/account-ledger/{cashLine.AccountId}?from=2026-01-01", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal("0.0000", ledger.Body!.OpeningBalance);
            Assert.Equal("250.5000", Assert.Single(ledger.Body.Lines).RunningBalance);
            // A cursor is trusted only if the server signed it for this exact request: garbage and forged tokens are client errors, not paging.
            foreach (var bad in new[] { "garbage", "AAAA", new string('A', 5000) })
            {
                var refused = await SendAsync<AccountLedgerResponse>(host, client, HttpMethod.Get,
                    root + $"/reports/account-ledger/{cashLine.AccountId}?from=2026-01-01&cursor={Uri.EscapeDataString(bad)}", admin.AccessToken).ConfigureAwait(true);
                Assert.Equal((HttpStatusCode.BadRequest, "ledger.cursor_invalid"), (refused.Status, refused.Code));
            }

            var usedAccount = await SendAsync<AccountResponse>(host, client, HttpMethod.Get, root + $"/chart/accounts/{cashLine.AccountId}", admin.AccessToken).ConfigureAwait(true);
            var deleteUsed = await SendAsync<object>(host, client, HttpMethod.Post, root + $"/chart/accounts/{cashLine.AccountId}/deletion", admin.AccessToken,
                new DeleteAccountRequest { ExpectedVersion = usedAccount.Body!.Version, Reason = "محاولة حذف حساب مستخدم" }).ConfigureAwait(true);
            Assert.Equal((HttpStatusCode.UnprocessableEntity, "ledger.account_in_use"), (deleteUsed.Status, deleteUsed.Code));
            var badRange = await SendAsync<TrialBalanceResponse>(host, client, HttpMethod.Get, root + "/reports/trial-balance?from=2026-05-01&to=2026-01-01", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal((HttpStatusCode.BadRequest, "ledger.report_range_invalid"), (badRange.Status, badRange.Code));

            var foreign = $"/api/v1/companies/{Guid.NewGuid()}/branches/{branchId}/ledger";
            var otherTree = await SendAsync<List<AccountResponse>>(host, client, HttpMethod.Get, foreign + "/chart/accounts/tree", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, otherTree.Status);
            var otherCurrency = await SendAsync<CompanyCurrencyContextResponse>(host, client, HttpMethod.Get, foreign + "/currency-context", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, otherCurrency.Status);
            var otherBranch = await SendAsync<TrialBalanceResponse>(host, client, HttpMethod.Get,
                $"/api/v1/companies/{companyId}/branches/{Guid.NewGuid()}/ledger/reports/trial-balance?to=2026-12-31", admin.AccessToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, otherBranch.Status);
        }
    }
}
