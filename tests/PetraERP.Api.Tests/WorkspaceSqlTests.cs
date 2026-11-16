using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Contracts.Productivity;
using PetraERP.Infrastructure.Security;
using PetraERP.Application.Security;

namespace PetraERP.Api.Tests;

/// <summary>Global workspace search, preview and preferences over the independent annual workspace. Financial rows are
/// read exclusively from the provisioned annual database (selected with X-Petra-Workspace); authorization and the
/// per-user preferences remain central. These tests prove the legacy context is never a financial fallback.</summary>
public sealed class WorkspaceSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SearchPagesArabicRecordsAndFiltersPermissionAndScopeChanges()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var company = host.CompanyId; var branch = host.BranchId;
            // Seed the independent annual workspace with an approved journal (two lines) over the provisioned chart. The
            // workspace already carries a default chart of accounts, so assertions target specific entities, not totals.
            var (journal, _) = await LedgerSeed.ApprovedJournalAsync(host, "JOD").ConfigureAwait(true);
            using (var annual = host.AnnualContext())
                await annual.Accounts.Where(account => account.CompanyId == company && account.NameArabic == "الصندوق")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(account => account.NameEnglish, "Workspace Cash Drawer"), Cancellation).ConfigureAwait(true);
            // Legacy-isolation proof: a journal written into the central/legacy ledger must never surface in a workspace
            // search, which now reads exclusively from the annual database.
            var legacyJournal = Guid.NewGuid();
            using (var legacy = host.CreateContext())
                await legacy.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO [ledger].[JournalEntries]([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State]) VALUES({legacyJournal},{company},{branch},{new DateOnly(2026, 1, 20)},N'قيد legacy يجب ألا يظهر',3)",
                    Cancellation).ConfigureAwait(true);

            using var client = host.Factory.CreateClient();
            var token = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            client.DefaultRequestHeaders.Add(ApiSqlHost.WorkspaceHeader, host.WorkspaceId.ToString("D"));
            var path = $"/api/v1/companies/{company}/branches/{branch}/workspace";

            // The seeded journal is visible; the legacy-ledger journal is not, proving search reads the annual database only.
            var all = await client.GetFromJsonAsync<SearchPageResponse>(new Uri(path + "/search?pageSize=200", UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Contains(all!.Items, item => item.Id == journal && item.Category == SearchCategory.JournalEntries);
            Assert.DoesNotContain(all.Items, item => item.Id == legacyJournal);

            // Paging returns a single row while reporting the full authorized count.
            var paged = await client.GetFromJsonAsync<SearchPageResponse>(new Uri(path + "/search?pageSize=1", UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Single(paged!.Items); Assert.True(paged.TotalCount >= 3);

            // The unique English name identifies the seeded cash account across the provisioned chart.
            var typed = new PetraERP.ApiClient.PetraApiClient(client);
            var english = await typed.SearchWorkspaceAsync(company, branch, search: "workspace cash drawer", cancellationToken: Cancellation).ConfigureAwait(true);
            var cash = Assert.Single(english.Items);

            // The Accounts category never returns journals and includes the seeded cash account.
            var accountsOnly = await typed.SearchWorkspaceAsync(company, branch, category: PetraERP.ApiClient.SearchCategory.Accounts, cancellationToken: Cancellation).ConfigureAwait(true);
            Assert.All(accountsOnly.Items, hit => Assert.Equal(PetraERP.ApiClient.SearchCategory.Accounts, hit.Category));
            Assert.Contains(accountsOnly.Items, hit => hit.Id == cash.Id);
            Assert.DoesNotContain(accountsOnly.Items, hit => hit.Id == journal);
            var typedCommands = await typed.ListWorkspaceCommandsAsync(company, branch, Cancellation).ConfigureAwait(true);
            Assert.Contains(typedCommands, command => command.Category == PetraERP.ApiClient.SearchCategory.All);

            // Arabic contains search matches the seeded cash account by its Arabic name.
            var arabic = await client.GetFromJsonAsync<SearchPageResponse>(new Uri(path + "/search?search=" + Uri.EscapeDataString("الصندوق"), UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Contains(arabic!.Items, item => item.Id == cash.Id && item.Name == "الصندوق");

            var detail = await client.GetFromJsonAsync<EntityPreviewResponse>(new Uri(path + $"/entities/JournalEntries/{journal}", UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal("250.5", detail!.BaseDebit); Assert.Equal("250.5", detail.BaseCredit); Assert.Equal(2, detail.Lines.Count);

            // Selecting an authorized financial workspace is mandatory for the financial surface.
            using (var noWorkspace = host.Factory.CreateClient())
            {
                noWorkspace.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                using var missing = await noWorkspace.GetAsync(new Uri(path + "/search", UriKind.Relative), Cancellation).ConfigureAwait(true);
                Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            }

            // Revoking the journals permission removes journals from search and blocks the preview; re-evaluated centrally.
            using (var db = host.CreateContext())
                await db.Database.ExecuteSqlRawAsync("DELETE FROM [security].[RolePermissions] WHERE [PermissionCode] = N'Ledger.Journals.View'", Cancellation).ConfigureAwait(true);
            var filtered = await client.GetFromJsonAsync<SearchPageResponse>(new Uri(path + "/search?pageSize=200", UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.All(filtered!.Items, row => Assert.Equal(SearchCategory.Accounts, row.Category));
            Assert.DoesNotContain(filtered.Items, row => row.Id == journal);
            using var denied = await client.GetAsync(new Uri(path + $"/entities/JournalEntries/{journal}", UriKind.Relative), Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            var commands = await client.GetFromJsonAsync<WorkspaceCommandResponse[]>(new Uri(path + "/commands", UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.DoesNotContain(commands!, command => command.Id == "journals.browse");

            // Central scope enforcement is unchanged: a company outside the caller's grants is forbidden before any workspace binding.
            using var foreignCompany = await client.GetAsync(new Uri($"/api/v1/companies/{Guid.NewGuid()}/branches/{branch}/workspace/search", UriKind.Relative), Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, foreignCompany.StatusCode);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PreferencesAreDurableVersionedAndRecentLabelsAreReauthorized()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var company = host.CompanyId; var branch = host.BranchId;
            var (journal, _) = await LedgerSeed.ApprovedJournalAsync(host, "JOD").ConfigureAwait(true);
            using var client = host.Factory.CreateClient();
            var token = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            client.DefaultRequestHeaders.Add(ApiSqlHost.WorkspaceHeader, host.WorkspaceId.ToString("D"));
            var path = $"/api/v1/companies/{company}/branches/{branch}/workspace";

            var initial = await client.GetFromJsonAsync<WorkspaceStateResponse>(new Uri(path + "/preferences", UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
            var preferences = new WorkspacePreferencesRequest("en", "Dark", ["journals.browse"], [],
                [new("workspace.results", [new("Code", 144, 1, true), new("Name", 240, 0, true)])]);
            using var save = await client.PutAsJsonAsync(new Uri(path + "/preferences", UriKind.Relative), new SaveWorkspaceRequest(preferences, initial!.Version), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);
            using var stale = await client.PutAsJsonAsync(new Uri(path + "/preferences", UriKind.Relative), new SaveWorkspaceRequest(preferences, initial.Version), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            using var recent = await client.PostAsJsonAsync(new Uri(path + "/recent", UriKind.Relative), new RecordRecentRequest(SearchCategory.JournalEntries, journal), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.NoContent, recent.StatusCode);
            var roundtrip = await client.GetFromJsonAsync<WorkspaceStateResponse>(new Uri(path + "/preferences", UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal("Dark", roundtrip!.Preferences.Theme); Assert.Equal(144, roundtrip.Preferences.Grids[0].Columns[0].Width);
            Assert.Single(roundtrip.Recent);
            // Saving returns resolved recent labels, so it needs the same annual binding as GET preferences.
            using var saveWithRecent = await client.PutAsJsonAsync(new Uri(path + "/preferences", UriKind.Relative),
                new SaveWorkspaceRequest(preferences, roundtrip.Version), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, saveWithRecent.StatusCode);
            var savedWithRecent = await saveWithRecent.Content.ReadFromJsonAsync<WorkspaceStateResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal(journal, Assert.Single(savedWithRecent!.Recent).Id);
            using (var unbound = host.Factory.CreateClient())
            {
                unbound.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                using var rejectedSave = await unbound.PutAsJsonAsync(new Uri(path + "/preferences", UriKind.Relative),
                    new SaveWorkspaceRequest(preferences, savedWithRecent.Version), ApiJson.Options, Cancellation).ConfigureAwait(true);
                Assert.Equal(HttpStatusCode.BadRequest, rejectedSave.StatusCode);
            }
            using (var db = host.CreateContext())
                await db.Database.ExecuteSqlRawAsync("DELETE FROM [security].[RolePermissions] WHERE [PermissionCode] = N'Ledger.Journals.View'", Cancellation).ConfigureAwait(true);
            var hidden = await client.GetFromJsonAsync<WorkspaceStateResponse>(new Uri(path + "/preferences", UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Empty(hidden!.Recent); Assert.Empty(hidden.Preferences.FavoriteCommands);
            // Preferences are central and per-user: a colleague with the same company, branch and workspace sees only defaults.
            using (var db = host.CreateContext())
            {
                var userId = Guid.NewGuid();
                var hash = new Pbkdf2PasswordHasher(AuthenticationSettings.Default).Hash("Colleague-Secure-2026!");
                db.Users.Add(new UserRow { Id = userId, UserName = "colleague", NormalizedUserName = "COLLEAGUE", DisplayNameArabic = "موظف آخر",
                    PasswordAlgorithm = hash.Algorithm, PasswordIterations = hash.Iterations, PasswordSalt = hash.Salt.ToArray(), PasswordHash = hash.Hash.ToArray(),
                    PasswordChangedAtUtc = DateTimeOffset.UtcNow, SecurityStamp = Guid.NewGuid(), IsActive = true });
                db.UserCompanyScopes.Add(new UserCompanyScopeRow { UserId = userId, CompanyId = company, CreatedAtUtc = DateTimeOffset.UtcNow });
                db.UserBranchScopes.Add(new UserBranchScopeRow { UserId = userId, CompanyId = company, BranchId = branch, CreatedAtUtc = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            }
            using var otherClient = host.Factory.CreateClient();
            var otherTokens = await ApiSqlHost.LoginAsync(otherClient, "colleague", "Colleague-Secure-2026!").ConfigureAwait(true);
            otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherTokens.AccessToken);
            otherClient.DefaultRequestHeaders.Add(ApiSqlHost.WorkspaceHeader, host.WorkspaceId.ToString("D"));
            var own = await otherClient.GetFromJsonAsync<WorkspaceStateResponse>(new Uri(path + "/preferences", UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Equal(string.Empty, own!.Version); Assert.Equal("ar", own.Preferences.Language); Assert.Empty(own.Preferences.Grids); Assert.Empty(own.Recent);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SearchNeverExposesAnotherCompanyOrFiscalYearOfTheSameUser()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var company = host.CompanyId; var branch = host.BranchId;
            // Primary workspace (Company WS / FY2026): a journal that the primary search must find.
            var (primaryJournal, _) = await LedgerSeed.ApprovedJournalAsync(host, "JOD").ConfigureAwait(true);

            // A SECOND company (independent annual database) with its own journal — must never surface in the primary search.
            var other = await host.ProvisionAdditionalWorkspaceAsync("WSB", 2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).ConfigureAwait(true);
            var otherJournal = Guid.NewGuid();
            using (var db = other.AnnualContext())
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO [ledger].[JournalEntries]([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({otherJournal},{other.CompanyId},{other.BranchId},{new DateOnly(2026, 2, 1)},N'قيد شركة أخرى',3,{DateTimeOffset.UtcNow},{DateTimeOffset.UtcNow})",
                    Cancellation).ConfigureAwait(true);

            // A PRIOR fiscal year of the SAME company (separate annual database) with its own journal — also invisible to FY2026.
            var prior = await host.ProvisionAdditionalYearAsync(2025, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)).ConfigureAwait(true);
            var priorJournal = Guid.NewGuid();
            using (var db = prior.AnnualContext())
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO [ledger].[JournalEntries]([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({priorJournal},{company},{branch},{new DateOnly(2025, 3, 1)},N'قيد سنة سابقة',3,{DateTimeOffset.UtcNow},{DateTimeOffset.UtcNow})",
                    Cancellation).ConfigureAwait(true);

            using var client = host.Factory.CreateClient();
            var token = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

            // Bound to the PRIMARY workspace: the primary journal is visible; neither the other company's nor the prior year's is.
            client.DefaultRequestHeaders.Add(ApiSqlHost.WorkspaceHeader, host.WorkspaceId.ToString("D"));
            var primaryPath = $"/api/v1/companies/{company}/branches/{branch}/workspace/search?pageSize=200";
            var primary = await client.GetFromJsonAsync<SearchPageResponse>(new Uri(primaryPath, UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Contains(primary!.Items, item => item.Id == primaryJournal);
            Assert.DoesNotContain(primary.Items, item => item.Id == otherJournal);
            Assert.DoesNotContain(primary.Items, item => item.Id == priorJournal);

            // Bound to the OTHER company's workspace: its own journal is visible; the primary company's journal is not.
            using var otherClient = host.Factory.CreateClient();
            otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            otherClient.DefaultRequestHeaders.Add(ApiSqlHost.WorkspaceHeader, other.WorkspaceId.ToString("D"));
            var otherPath = $"/api/v1/companies/{other.CompanyId}/branches/{other.BranchId}/workspace/search?pageSize=200";
            var otherResult = await otherClient.GetFromJsonAsync<SearchPageResponse>(new Uri(otherPath, UriKind.Relative), ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.Contains(otherResult!.Items, item => item.Id == otherJournal);
            Assert.DoesNotContain(otherResult.Items, item => item.Id == primaryJournal);
        }
    }
}
