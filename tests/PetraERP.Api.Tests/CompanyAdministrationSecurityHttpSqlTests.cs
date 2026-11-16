using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Contracts.Administration;
using PetraERP.Contracts.Authentication;
using PetraERP.Contracts.Workspaces;

namespace PetraERP.Api.Tests;

public sealed class CompanyAdministrationSecurityHttpSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CompanyCreationEnforcesDatabaseManageAndMandatoryAdminPassword()
    {
        var host = await ApiSqlHost.CreateAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);

            // 1. Anonymous company creation -> 401
            using var anonReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/workspaces/companies")
            {
                Content = JsonContent.Create(ValidCompanyPayload("ANON", password: ApiSqlHost.AdminPassword), options: ApiJson.Options)
            };
            using var anonResp = await client.SendAsync(anonReq, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Unauthorized, anonResp.StatusCode);

            // 2. Ordinary user's company creation (no DatabaseManage) -> 403
            using var createUser = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/admin/users", admin.AccessToken,
                JsonContent.Create(new CreateUserRequest { UserName = "clerk.security", DisplayNameArabic = "موظف", InitialPassword = "Initial-Clerk-2026!" }, options: ApiJson.Options));
            using var userResp = await client.SendAsync(createUser, Cancellation).ConfigureAwait(true);
            userResp.EnsureSuccessStatusCode();

            var clerkLogin = await ApiSqlHost.LoginAsync(client, "clerk.security", "Initial-Clerk-2026!").ConfigureAwait(true);
            // Change password so clerk credential is fully active
            using var changePwd = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/auth/change-password", clerkLogin.AccessToken,
                JsonContent.Create(new ChangePasswordRequest { CurrentPassword = "Initial-Clerk-2026!", NewPassword = "Chosen-Clerk-2026!" }, options: ApiJson.Options));
            using var changedResp = await client.SendAsync(changePwd, Cancellation).ConfigureAwait(true);
            changedResp.EnsureSuccessStatusCode();
            var activeClerk = (await changedResp.Content.ReadFromJsonAsync<TokenResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true))!;

            using var clerkReq = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/workspaces/companies", activeClerk.AccessToken,
                JsonContent.Create(ValidCompanyPayload("CLERK", password: "Chosen-Clerk-2026!"), options: ApiJson.Options));
            using var clerkResp = await client.SendAsync(clerkReq, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, clerkResp.StatusCode);

            // 3. Authorized admin without password -> 400
            using var noPwdReq = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/workspaces/companies", admin.AccessToken,
                JsonContent.Create(ValidCompanyPayload("NOPWD", password: null), options: ApiJson.Options));
            using var noPwdResp = await client.SendAsync(noPwdReq, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.BadRequest, noPwdResp.StatusCode);
            using var noPwdProblem = JsonDocument.Parse(await noPwdResp.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal("administrator.password_required", noPwdProblem.RootElement.GetProperty("code").GetString());

            // 3b. Authorized admin with whitespace password -> 400
            using var wsPwdReq = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/workspaces/companies", admin.AccessToken,
                JsonContent.Create(ValidCompanyPayload("WSPWD", password: "   "), options: ApiJson.Options));
            using var wsPwdResp = await client.SendAsync(wsPwdReq, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.BadRequest, wsPwdResp.StatusCode);

            // 4. Authorized admin with wrong password -> 422
            using var wrongPwdReq = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/workspaces/companies", admin.AccessToken,
                JsonContent.Create(ValidCompanyPayload("WRONG", password: "WrongPassword!"), options: ApiJson.Options));
            using var wrongPwdResp = await client.SendAsync(wrongPwdReq, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongPwdResp.StatusCode);
            using var wrongProblem = JsonDocument.Parse(await wrongPwdResp.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal("administrator.confirmation_failed", wrongProblem.RootElement.GetProperty("code").GetString());

            // 5. Authorized admin with correct password and valid database name -> 201
            var dbName = $"{host.Database.DatabaseName}_C01";
            using var okReq = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/workspaces/companies", admin.AccessToken,
                JsonContent.Create(ValidCompanyPayload("C01", password: ApiSqlHost.AdminPassword, dbName: dbName), options: ApiJson.Options));
            using var okResp = await client.SendAsync(okReq, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, okResp.StatusCode);
            var okResult = await okResp.Content.ReadFromJsonAsync<ProvisioningOperationResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.NotNull(okResult);
            await host.Database.RegisterAnnualDatabaseAsync(okResult.TargetDatabaseName).ConfigureAwait(true);

            // 6. Disabled admin -> 403
            using (var db = host.CreateContext())
            {
                var user = await db.Users.SingleAsync(u => u.Id == host.Bootstrap.AdministratorUserId, Cancellation).ConfigureAwait(true);
                user.IsActive = false;
                await db.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            }
            using var disabledReq = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/workspaces/companies", admin.AccessToken,
                JsonContent.Create(ValidCompanyPayload("DIS", password: ApiSqlHost.AdminPassword), options: ApiJson.Options));
            using var disabledResp = await client.SendAsync(disabledReq, Cancellation).ConfigureAwait(true);
            Assert.True(disabledResp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized);

            // Re-enable admin for next test
            using (var db = host.CreateContext())
            {
                var user = await db.Users.SingleAsync(u => u.Id == host.Bootstrap.AdministratorUserId, Cancellation).ConfigureAwait(true);
                user.IsActive = true;
                user.MustChangePassword = true;
                await db.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            }

            // 7. MustChangePassword admin -> 403
            using var mustChangeReq = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/workspaces/companies", admin.AccessToken,
                JsonContent.Create(ValidCompanyPayload("CHG", password: ApiSqlHost.AdminPassword), options: ApiJson.Options));
            using var mustChangeResp = await client.SendAsync(mustChangeReq, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, mustChangeResp.StatusCode);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task FiscalYearAndMigrationEndpointsEnforceDatabaseManageAndCompanyScope()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);

            // Create clerk with no DatabaseManage
            using var createUser = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/admin/users", admin.AccessToken,
                JsonContent.Create(new CreateUserRequest { UserName = "clerk.ops", DisplayNameArabic = "موظف عمليات", InitialPassword = "Initial-Clerk-2026!" }, options: ApiJson.Options));
            using var userResp = await client.SendAsync(createUser, Cancellation).ConfigureAwait(true);
            userResp.EnsureSuccessStatusCode();

            var clerkLogin = await ApiSqlHost.LoginAsync(client, "clerk.ops", "Initial-Clerk-2026!").ConfigureAwait(true);
            using var changePwd = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/auth/change-password", clerkLogin.AccessToken,
                JsonContent.Create(new ChangePasswordRequest { CurrentPassword = "Initial-Clerk-2026!", NewPassword = "Chosen-Clerk-2026!" }, options: ApiJson.Options));
            using var changedResp = await client.SendAsync(changePwd, Cancellation).ConfigureAwait(true);
            changedResp.EnsureSuccessStatusCode();
            var activeClerk = (await changedResp.Content.ReadFromJsonAsync<TokenResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true))!;

            // 8. Unauthorized fiscal-year creation -> 403
            var fyPayload = new CreateFiscalYearWorkspaceRequest
            {
                CompanyId = host.CompanyId,
                YearNumber = 2027,
                YearCode = "FY2027",
                StartDate = new DateOnly(2027, 1, 1),
                EndDate = new DateOnly(2027, 12, 31),
            };
            using var unauthFy = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/workspaces/fiscal-years", activeClerk.AccessToken,
                JsonContent.Create(fyPayload, options: ApiJson.Options));
            using var unauthFyResp = await client.SendAsync(unauthFy, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, unauthFyResp.StatusCode);

            // 9. Unauthorized migration plan -> 403
            using var planReq = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/workspaces/migration/plan", activeClerk.AccessToken);
            using var planResp = await client.SendAsync(planReq, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, planResp.StatusCode);

            // 10. Unauthorized migration execution -> 403
            using var execReq = ApiSqlHost.Authorized(HttpMethod.Post, $"/api/v1/workspaces/migration/execute?companyId={host.CompanyId}&fiscalYearId={host.FiscalYearId}&dryRun=true", activeClerk.AccessToken);
            using var execResp = await client.SendAsync(execReq, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, execResp.StatusCode);

            // 12. Attempt to reuse an existing database name -> rejected
            using var dupDbReq = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/workspaces/companies", admin.AccessToken,
                JsonContent.Create(ValidCompanyPayload("DUP", password: ApiSqlHost.AdminPassword, dbName: host.Database.DatabaseName), options: ApiJson.Options));
            using var dupDbResp = await client.SendAsync(dupDbReq, Cancellation).ConfigureAwait(true);
            Assert.False(dupDbResp.IsSuccessStatusCode);
            Assert.Contains(dupDbResp.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity });
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task DefaultFiscalYearReturnsExactValuePerCompanyInWorkspaceList()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);

            // Company A has FY2026 (created via host). Now provision FY2027 for Company A.
            var fy2027 = await host.ProvisionAdditionalYearAsync(2027, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31)).ConfigureAwait(true);

            // In central registry, verify FY2026 is default and FY2027 is NOT default (or vice versa according to registry)
            using (var db = host.CreateContext())
            {
                var reg2026 = await db.FiscalDatabases.SingleAsync(f => f.FiscalYearId == host.FiscalYearId, Cancellation).ConfigureAwait(true);
                var reg2027 = await db.FiscalDatabases.SingleAsync(f => f.FiscalYearId == fy2027.FiscalYearId, Cancellation).ConfigureAwait(true);

                // Set FY2026 as IsDefault = false and FY2027 as IsDefault = true explicitly.
                // Save sequentially to avoid violating UX_FiscalDatabases_CompanyDefault filtered unique index.
                reg2026.IsDefault = false;
                await db.SaveChangesAsync(Cancellation).ConfigureAwait(true);

                reg2027.IsDefault = true;
                await db.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            }

            // Create Company B with FY2026 (default)
            var coB = await host.ProvisionAdditionalWorkspaceAsync("COB", 2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).ConfigureAwait(true);

            // Query GET /api/v1/me/workspaces
            using var listReq = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/me/workspaces", admin.AccessToken);
            using var listResp = await client.SendAsync(listReq, Cancellation).ConfigureAwait(true);
            listResp.EnsureSuccessStatusCode();

            var workspaces = await listResp.Content.ReadFromJsonAsync<WorkspaceListResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.NotNull(workspaces);

            // Check Company A
            var w2026 = Assert.Single(workspaces.Items, w => w.FiscalYearId == host.FiscalYearId);
            Assert.False(w2026.IsDefault, "FY2026 must have IsDefault=false matching registry");

            var w2027 = Assert.Single(workspaces.Items, w => w.FiscalYearId == fy2027.FiscalYearId);
            Assert.True(w2027.IsDefault, "FY2027 must have IsDefault=true matching registry");

            // Check Company B
            var wB = Assert.Single(workspaces.Items, w => w.CompanyId == coB.CompanyId);
            Assert.True(wB.IsDefault, "Company B must have its own IsDefault=true");
        }
    }

    private static CreateCompanyWorkspaceRequest ValidCompanyPayload(string code, string? password, string? dbName = null) => new()
    {
        Code = code,
        NameArabic = $"شركة {code}",
        NameEnglish = $"Company {code}",
        BaseCurrencyCode = "JOD",
        InitialYear = 2026,
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2026, 12, 31),
        AdministratorPassword = password,
        DatabaseName = dbName
    };
}
