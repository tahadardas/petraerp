using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using PetraERP.Contracts.Administration;
using PetraERP.Contracts.Audit;
using PetraERP.Contracts.Authentication;
using PetraERP.Contracts.Context;

namespace PetraERP.Api.Tests;

/// <summary>End-to-end HTTP tests against a real SQL Server database (explicit; require PETRAERP_SQL_TEST_SERVER).</summary>
public sealed class ApiSecuritySqlTests
{
    private static readonly string[] IdentifierOnlyClaims = ["aud", "exp", "iat", "iss", "jti", "nbf", "sid", "sub"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task LoginProfileContextRefreshAndLogoutWorkEndToEnd()
    {
        var host = await ApiSqlHost.CreateAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var tokens = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            Assert.Equal("Bearer", tokens.TokenType);
            Assert.False(tokens.MustChangePassword);
            Assert.True(tokens.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow);
            Assert.True(tokens.SessionExpiresAtUtc >= tokens.RefreshTokenExpiresAtUtc);

            // The access token carries identifiers only: no roles, scopes, names or secrets.
            var jwt = new JsonWebToken(tokens.AccessToken);
            var claimTypes = jwt.Claims.Select(static claim => claim.Type).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(IdentifierOnlyClaims, claimTypes);
            Assert.Equal("HS256", jwt.Alg);
            Assert.DoesNotContain(ApiSqlHost.AdminPassword, jwt.EncodedPayload, StringComparison.Ordinal);

            using (var me = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/me", tokens.AccessToken))
            using (var meResponse = await client.SendAsync(me, Cancellation).ConfigureAwait(true))
            {
                Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
                var body = await meResponse.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(true);
                Assert.DoesNotContain(ApiSqlHost.AdminPassword, body, StringComparison.Ordinal);
                Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("securityStamp", body, StringComparison.OrdinalIgnoreCase);
                var profile = JsonSerializer.Deserialize<CurrentUserResponse>(body, ApiJson.Options)!;
                Assert.Equal("admin", profile.UserName);
                Assert.Equal("مدير النظام", profile.DisplayNameArabic);
                Assert.Contains("System.Users.Manage", profile.Permissions);
                Assert.Equal(tokens.SessionId, profile.SessionId);
            }

            CompanyContextResponse company;
            using (var companies = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/me/companies", tokens.AccessToken))
            using (var companiesResponse = await client.SendAsync(companies, Cancellation).ConfigureAwait(true))
            {
                var list = await companiesResponse.Content.ReadFromJsonAsync<CompanyContextListResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
                company = Assert.Single(list!.Items);
                Assert.Equal("شركة البتراء للتجارة", company.NameArabic);
                Assert.Equal("JOD", company.BaseCurrencyCode);
            }

            using (var branches = ApiSqlHost.Authorized(HttpMethod.Get, $"/api/v1/me/companies/{company.Id}/branches", tokens.AccessToken))
            using (var branchesResponse = await client.SendAsync(branches, Cancellation).ConfigureAwait(true))
            {
                var list = await branchesResponse.Content.ReadFromJsonAsync<BranchContextListResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
                Assert.Equal("المقر الرئيسي", Assert.Single(list!.Items).NameArabic);
            }

            using var refresh = await client.PostAsJsonAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative),
                new RefreshSessionRequest { RefreshToken = tokens.RefreshToken }, ApiJson.Options, Cancellation).ConfigureAwait(true);
            var rotated = (await refresh.Content.ReadFromJsonAsync<TokenResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true))!;
            Assert.Equal(tokens.SessionId, rotated.SessionId);
            Assert.NotEqual(tokens.RefreshToken, rotated.RefreshToken);

            using var reuse = await client.PostAsJsonAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative),
                new RefreshSessionRequest { RefreshToken = tokens.RefreshToken }, ApiJson.Options, Cancellation).ConfigureAwait(true);
            await ProblemAssertions.AssertProblemAsync(reuse, HttpStatusCode.Unauthorized, "auth.refresh_token_reused").ConfigureAwait(true);

            // Reuse revoked the session: even the still-unexpired access token is now refused.
            using var afterReuse = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/me", rotated.AccessToken);
            using var afterReuseResponse = await client.SendAsync(afterReuse, Cancellation).ConfigureAwait(true);
            await ProblemAssertions.AssertProblemAsync(afterReuseResponse, HttpStatusCode.Unauthorized, "auth.session_revoked")
                .ConfigureAwait(true);

            var second = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            using (var logout = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/auth/logout", second.AccessToken))
            using (var logoutResponse = await client.SendAsync(logout, Cancellation).ConfigureAwait(true))
            {
                Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
            }

            using var afterLogout = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/me", second.AccessToken);
            using var afterLogoutResponse = await client.SendAsync(afterLogout, Cancellation).ConfigureAwait(true);
            await ProblemAssertions.AssertProblemAsync(afterLogoutResponse, HttpStatusCode.Unauthorized, "auth.session_revoked")
                .ConfigureAwait(true);

            var third = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            var fourth = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            using (var logoutAll = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/auth/logout-all", third.AccessToken))
            using (var logoutAllResponse = await client.SendAsync(logoutAll, Cancellation).ConfigureAwait(true))
            {
                var result = await logoutAllResponse.Content.ReadFromJsonAsync<LogoutAllResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
                Assert.Equal(2, result!.RevokedSessions);
            }

            using var afterAll = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/me", fourth.AccessToken);
            using var afterAllResponse = await client.SendAsync(afterAll, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Unauthorized, afterAllResponse.StatusCode);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task InvalidLockedAndDisabledSignInsAreDistinguishedSafely()
    {
        var host = await ApiSqlHost.CreateAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            using (var unknown = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
                       new LoginRequest { UserName = "nobody", Password = "whatever-password" }, ApiJson.Options, Cancellation).ConfigureAwait(true))
            {
                await ProblemAssertions.AssertProblemAsync(unknown, HttpStatusCode.Unauthorized, "auth.invalid_credentials").ConfigureAwait(true);
            }

            HttpResponseMessage? last = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                last?.Dispose();
                last = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
                    new LoginRequest { UserName = ApiSqlHost.AdminUser, Password = "wrong-password-1" }, ApiJson.Options, Cancellation).ConfigureAwait(true);
            }

            using (last)
            {
                var problem = await ProblemAssertions.AssertProblemAsync(last!, (HttpStatusCode)423, "auth.locked_out").ConfigureAwait(true);
                Assert.True(last!.Headers.RetryAfter is not null);
                Assert.Equal("urn:petraerp:problem:auth.locked_out", problem.GetProperty("type").GetString());
            }

            using var arabic = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/login", UriKind.Relative))
            {
                Content = JsonContent.Create(new LoginRequest { UserName = ApiSqlHost.AdminUser, Password = ApiSqlHost.AdminPassword }, options: ApiJson.Options),
            };
            arabic.Headers.AcceptLanguage.ParseAdd("ar-SY");
            using var lockedResponse = await client.SendAsync(arabic, Cancellation).ConfigureAwait(true);
            var lockedProblem = await ProblemAssertions.AssertProblemAsync(lockedResponse, (HttpStatusCode)423, "auth.locked_out")
                .ConfigureAwait(true);
            Assert.Contains("مقفل", lockedProblem.GetProperty("title").GetString(), StringComparison.Ordinal);

            var audit = await AdminAuditCountAsync(host, "AccountLockedOut").ConfigureAwait(true);
            Assert.Equal(1, audit);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PermissionsAndCrossCompanyAndCrossBranchAccessAreEnforcedByTheServer()
    {
        var host = await ApiSqlHost.CreateAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            Guid petraCompany;
            Guid petraBranch;
            var otherCompany = Guid.CreateVersion7();
            var otherBranch = Guid.CreateVersion7();
            using (var context = host.CreateContext())
            {
                petraCompany = (await context.Companies.AsNoTracking().SingleAsync(Cancellation).ConfigureAwait(true)).Id;
                petraBranch = (await context.Branches.AsNoTracking().SingleAsync(Cancellation).ConfigureAwait(true)).Id;
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO [system].[Companies]([Id],[Code],[NameArabic],[BaseCurrencyCode],[IsActive]) VALUES({otherCompany},N'RIVAL',N'شركة منافسة',N'JOD',1)",
                    Cancellation).ConfigureAwait(true);
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO [system].[Branches]([Id],[CompanyId],[Code],[NameArabic],[IsActive]) VALUES({otherBranch},{otherCompany},N'R1',N'فرع منافس',1)",
                    Cancellation).ConfigureAwait(true);
            }

            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);

            // The admin is scoped only to the company that existed at bootstrap.
            using (var foreign = ApiSqlHost.Authorized(HttpMethod.Get, $"/api/v1/me/companies/{otherCompany}/branches", admin.AccessToken))
            using (var foreignResponse = await client.SendAsync(foreign, Cancellation).ConfigureAwait(true))
            {
                await ProblemAssertions.AssertProblemAsync(foreignResponse, HttpStatusCode.Forbidden, "auth.company_scope_denied")
                    .ConfigureAwait(true);
            }

            using (var crossBranch = ApiSqlHost.Authorized(HttpMethod.Post,
                       $"/api/v1/companies/{petraCompany}/branches/{otherBranch}/ledger/journals/{Guid.NewGuid()}/post", admin.AccessToken,
                       JsonContent.Create(new { expectedVersion = "AAAAAAAAB9E=", reason = "محاولة" }, options: ApiJson.Options)))
            {
                crossBranch.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
                using var crossBranchResponse = await client.SendAsync(crossBranch, Cancellation).ConfigureAwait(true);
                await ProblemAssertions.AssertProblemAsync(crossBranchResponse, HttpStatusCode.Forbidden, "auth.branch_scope_denied")
                    .ConfigureAwait(true);
            }

            using (var crossCompany = ApiSqlHost.Authorized(HttpMethod.Post,
                       $"/api/v1/companies/{otherCompany}/branches/{otherBranch}/ledger/journals/{Guid.NewGuid()}/post", admin.AccessToken,
                       JsonContent.Create(new { expectedVersion = "AAAAAAAAB9E=", reason = "محاولة" }, options: ApiJson.Options)))
            {
                crossCompany.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
                using var crossCompanyResponse = await client.SendAsync(crossCompany, Cancellation).ConfigureAwait(true);
                await ProblemAssertions.AssertProblemAsync(crossCompanyResponse, HttpStatusCode.Forbidden, "auth.company_scope_denied")
                    .ConfigureAwait(true);
            }

            // Adversarial matrix: master data and reports across foreign company are strictly denied with 403
            foreach (var route in new[]
            {
                $"/api/v1/companies/{otherCompany}/branches/{otherBranch}/ledger/chart/accounts/tree",
                $"/api/v1/companies/{otherCompany}/branches/{otherBranch}/ledger/chart/cost-centers",
                $"/api/v1/companies/{otherCompany}/branches/{otherBranch}/ledger/calendar/fiscal-years",
                $"/api/v1/companies/{otherCompany}/branches/{otherBranch}/ledger/reports/general-journal",
                $"/api/v1/companies/{otherCompany}/branches/{otherBranch}/ledger/reports/trial-balance",
            })
            {
                using var advReq = ApiSqlHost.Authorized(HttpMethod.Get, route, admin.AccessToken);
                using var advRes = await client.SendAsync(advReq, Cancellation).ConfigureAwait(true);
                await ProblemAssertions.AssertProblemAsync(advRes, HttpStatusCode.Forbidden, "auth.company_scope_denied").ConfigureAwait(true);
            }

            // Adversarial matrix: foreign branch with legitimate company route is strictly denied with 403
            foreach (var route in new[]
            {
                $"/api/v1/companies/{petraCompany}/branches/{otherBranch}/ledger/chart/accounts/tree",
                $"/api/v1/companies/{petraCompany}/branches/{otherBranch}/ledger/reports/general-journal",
            })
            {
                using var advReq = ApiSqlHost.Authorized(HttpMethod.Get, route, admin.AccessToken);
                using var advRes = await client.SendAsync(advReq, Cancellation).ConfigureAwait(true);
                await ProblemAssertions.AssertProblemAsync(advRes, HttpStatusCode.Forbidden, "auth.branch_scope_denied").ConfigureAwait(true);
            }

            // A clerk with no roles: authenticated, but every protected operation is denied.
            var clerk = await CreateUserAsync(client, admin.AccessToken, "clerk", "Teller-Initial-2026").ConfigureAwait(true);
            var clerkTokens = await ApiSqlHost.LoginAsync(client, "clerk", "Teller-Initial-2026").ConfigureAwait(true);
            Assert.True(clerkTokens.MustChangePassword);
            using (var pending = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/me/companies", clerkTokens.AccessToken))
            using (var pendingResponse = await client.SendAsync(pending, Cancellation).ConfigureAwait(true))
            {
                await ProblemAssertions.AssertProblemAsync(pendingResponse, HttpStatusCode.Forbidden, "auth.password_change_required")
                    .ConfigureAwait(true);
            }

            using var change = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/auth/change-password", clerkTokens.AccessToken,
                JsonContent.Create(new ChangePasswordRequest { CurrentPassword = "Teller-Initial-2026", NewPassword = "Teller-Chosen-2026!" }, options: ApiJson.Options));
            using var changeResponse = await client.SendAsync(change, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);
            var clerkSession = (await changeResponse.Content.ReadFromJsonAsync<TokenResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true))!;
            Assert.False(clerkSession.MustChangePassword);

            using (var adminList = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/admin/users", clerkSession.AccessToken))
            using (var adminListResponse = await client.SendAsync(adminList, Cancellation).ConfigureAwait(true))
            {
                await ProblemAssertions.AssertProblemAsync(adminListResponse, HttpStatusCode.Forbidden, "auth.permission_denied")
                    .ConfigureAwait(true);
            }

            using (var companies = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/me/companies", clerkSession.AccessToken))
            using (var companiesResponse = await client.SendAsync(companies, Cancellation).ConfigureAwait(true))
            {
                var list = await companiesResponse.Content.ReadFromJsonAsync<CompanyContextListResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
                Assert.Empty(list!.Items);
            }

            using (var post = ApiSqlHost.Authorized(HttpMethod.Post,
                       $"/api/v1/companies/{petraCompany}/branches/{petraBranch}/ledger/journals/{Guid.NewGuid()}/post", clerkSession.AccessToken,
                       JsonContent.Create(new { expectedVersion = "AAAAAAAAB9E=", reason = "محاولة" }, options: ApiJson.Options)))
            {
                post.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
                using var postResponse = await client.SendAsync(post, Cancellation).ConfigureAwait(true);
                await ProblemAssertions.AssertProblemAsync(postResponse, HttpStatusCode.Forbidden, "auth.permission_denied")
                    .ConfigureAwait(true);
            }

            Assert.True(await AdminAuditCountAsync(host, "AuthorizationDenied").ConfigureAwait(true) >= 11);
            Assert.NotEqual(Guid.Empty, clerk.Id);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AdministrationApisAreAuditedAndRejectStaleVersionsWith409()
    {
        var host = await ApiSqlHost.CreateAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            var user = await CreateUserAsync(client, admin.AccessToken, "cashier", "Drawer-Initial-2026").ConfigureAwait(true);
            Assert.True(user.MustChangePassword);
            Assert.Equal(12, user.Version.Length);

            using var update = ApiSqlHost.Authorized(HttpMethod.Put, $"/api/v1/admin/users/{user.Id}/profile", admin.AccessToken,
                JsonContent.Create(new UpdateUserProfileRequest { DisplayNameArabic = "أمين الصندوق", Version = user.Version }, options: ApiJson.Options));
            using var updateResponse = await client.SendAsync(update, Cancellation).ConfigureAwait(true);
            var updated = (await updateResponse.Content.ReadFromJsonAsync<UserDetailsResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true))!;
            Assert.Equal("أمين الصندوق", updated.DisplayNameArabic);
            Assert.NotEqual(user.Version, updated.Version);

            using var stale = ApiSqlHost.Authorized(HttpMethod.Put, $"/api/v1/admin/users/{user.Id}/profile", admin.AccessToken,
                JsonContent.Create(new UpdateUserProfileRequest { DisplayNameArabic = "اسم قديم", Version = user.Version }, options: ApiJson.Options));
            using var staleResponse = await client.SendAsync(stale, Cancellation).ConfigureAwait(true);
            await ProblemAssertions.AssertProblemAsync(staleResponse, HttpStatusCode.Conflict, "concurrency.conflict").ConfigureAwait(true);

            using var malformedVersion = ApiSqlHost.Authorized(HttpMethod.Put, $"/api/v1/admin/users/{user.Id}/profile", admin.AccessToken,
                JsonContent.Create(new UpdateUserProfileRequest { DisplayNameArabic = "اسم", Version = "not-a-versio" }, options: ApiJson.Options));
            using var malformedResponse = await client.SendAsync(malformedVersion, Cancellation).ConfigureAwait(true);
            await ProblemAssertions.AssertProblemAsync(malformedResponse, HttpStatusCode.BadRequest, "request.version_invalid")
                .ConfigureAwait(true);

            using var duplicate = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/admin/users", admin.AccessToken,
                JsonContent.Create(new CreateUserRequest { UserName = "CASHIER", DisplayNameArabic = "مكرر", InitialPassword = "Another-Pass-2026" }, options: ApiJson.Options));
            using var duplicateResponse = await client.SendAsync(duplicate, Cancellation).ConfigureAwait(true);
            await ProblemAssertions.AssertProblemAsync(duplicateResponse, HttpStatusCode.Conflict, "security.user_name_taken").ConfigureAwait(true);

            using var weak = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/admin/users", admin.AccessToken,
                JsonContent.Create(new CreateUserRequest { UserName = "weak.user", DisplayNameArabic = "ضعيف", InitialPassword = "weak.user-123" }, options: ApiJson.Options));
            using var weakResponse = await client.SendAsync(weak, Cancellation).ConfigureAwait(true);
            await ProblemAssertions.AssertProblemAsync(weakResponse, HttpStatusCode.BadRequest, "security.password_policy.contains_user_name")
                .ConfigureAwait(true);

            using var audit = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/audit/security-events?eventType=UserCreated", admin.AccessToken);
            using var auditResponse = await client.SendAsync(audit, Cancellation).ConfigureAwait(true);
            var page = (await auditResponse.Content.ReadFromJsonAsync<SecurityAuditPageResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true))!;
            var created = Assert.Single(page.Items);
            Assert.Equal(user.Id, created.UserId);
            Assert.Equal(host.Bootstrap.AdministratorUserId, created.ActorUserId);
            Assert.DoesNotContain("Drawer-Initial-2026", created.Details ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AuthenticationEndpointsAreRateLimited()
    {
        var host = await ApiSqlHost.CreateAsync(authenticationPermitsPerMinute: 3).ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var statuses = new List<HttpStatusCode>();
            for (var attempt = 0; attempt < 5; attempt++)
            {
                using var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
                    new LoginRequest { UserName = "nobody", Password = "whatever-password" }, ApiJson.Options, Cancellation).ConfigureAwait(true);
                statuses.Add(response.StatusCode);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await ProblemAssertions.AssertProblemAsync(response, HttpStatusCode.TooManyRequests, "request.rate_limited")
                        .ConfigureAwait(true);
                }
            }

            Assert.Equal(3, statuses.Count(static status => status == HttpStatusCode.Unauthorized));
            Assert.Equal(2, statuses.Count(static status => status == HttpStatusCode.TooManyRequests));
        }
    }

    private static async Task<UserDetailsResponse> CreateUserAsync(HttpClient client, string accessToken, string userName, string password)
    {
        using var request = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/admin/users", accessToken,
            JsonContent.Create(new CreateUserRequest { UserName = userName, DisplayNameArabic = "مستخدم " + userName, InitialPassword = password }, options: ApiJson.Options));
        using var response = await client.SendAsync(request, Cancellation).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserDetailsResponse>(ApiJson.Options, Cancellation).ConfigureAwait(false))!;
    }

    private static async Task<int> AdminAuditCountAsync(ApiSqlHost host, string eventType)
    {
        var type = (byte)Enum.Parse<SecurityAuditEventType>(eventType);
        using var context = host.CreateContext();
        return await context.SecurityAuditEvents.CountAsync(row => row.EventType == type, Cancellation).ConfigureAwait(false);
    }
}
