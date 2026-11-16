using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using PetraERP.Api.Security;
using PetraERP.Application.Security;
using PetraERP.Contracts.Administration;
using PetraERP.Contracts.Authentication;
using PetraERP.Contracts.Workspaces;
using PermissionScope = PetraERP.Application.Security.PermissionScope;

namespace PetraERP.Api.Tests;

public sealed class PhaseOneWorkspaceAuthorizationTests
{
    [Fact]
    public async Task DatabasePermissionIsInstallationScopedAndRequiresAnActiveCredential()
    {
        Assert.Equal(PermissionScope.Installation, PermissionCatalog.Get(PermissionCatalog.DatabaseManage).Scope);
        using var factory = new PetraApiFactory();
        var options = factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        var policy = options.GetPolicy(PetraPolicies.Permission(PermissionCatalog.DatabaseManage))!;
        Assert.Contains(policy.Requirements, r => r is ActiveCredentialRequirement);
        Assert.Contains(options.DefaultPolicy.Requirements, r => r is ActiveCredentialRequirement);
        var maintenance = options.GetPolicy(PetraPolicies.CredentialMaintenance)!;
        Assert.DoesNotContain(maintenance.Requirements, r => r is ActiveCredentialRequirement);
        Assert.Contains(maintenance.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
        Assert.Contains(policy.Requirements, r => r is PermissionRequirement p && p.Permission.Code == PermissionCatalog.DatabaseManage);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(PetraClaims.PasswordChangeRequired, "true") }, "test"));
        var requirement = new ActiveCredentialRequirement();
        var authorization = new AuthorizationHandlerContext(new[] { requirement }, principal, null);
        await new ActiveCredentialHandler().HandleAsync(authorization).ConfigureAwait(true);
        Assert.True(authorization.HasFailed);
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task OrdinaryAndPendingCredentialUsersCannotManageDatabases()
    {
        var host = await ApiSqlHost.CreateAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = TestContext.Current.CancellationToken;
            var admin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            using var createUser = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/admin/users", admin.AccessToken,
                JsonContent.Create(new CreateUserRequest { UserName = "workspace.clerk", DisplayNameArabic = "موظف", InitialPassword = "Initial-Clerk-2026!" }, options: ApiJson.Options));
            using var userResponse = await client.SendAsync(createUser, token).ConfigureAwait(true);
            userResponse.EnsureSuccessStatusCode();
            var clerk = await ApiSqlHost.LoginAsync(client, "workspace.clerk", "Initial-Clerk-2026!").ConfigureAwait(true);
            foreach (var (method, path) in AdministrativeRoutes)
            {
                using var request = ApiSqlHost.Authorized(method, path, clerk.AccessToken, CompanyRequest());
                using var response = await client.SendAsync(request, token).ConfigureAwait(true);
                await ProblemAssertions.AssertProblemAsync(response, HttpStatusCode.Forbidden, "auth.password_change_required").ConfigureAwait(true);
            }
            using var change = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/auth/change-password", clerk.AccessToken,
                JsonContent.Create(new ChangePasswordRequest { CurrentPassword = "Initial-Clerk-2026!", NewPassword = "Chosen-Clerk-2026!" }, options: ApiJson.Options));
            using var changed = await client.SendAsync(change, token).ConfigureAwait(true);
            changed.EnsureSuccessStatusCode();
            var active = (await changed.Content.ReadFromJsonAsync<TokenResponse>(ApiJson.Options, token).ConfigureAwait(true))!;
            foreach (var (method, path) in AdministrativeRoutes)
            {
                using var request = ApiSqlHost.Authorized(method, path, active.AccessToken, CompanyRequest());
                using var response = await client.SendAsync(request, token).ConfigureAwait(true);
                await ProblemAssertions.AssertProblemAsync(response, HttpStatusCode.Forbidden, "auth.permission_denied").ConfigureAwait(true);
            }
            // A pending administrator must also be denied despite holding DatabaseManage.
            using (var context = host.CreateContext())
            {
                var user = await context.Users.SingleAsync(u => u.Id == host.Bootstrap.AdministratorUserId, token).ConfigureAwait(true);
                user.MustChangePassword = true;
                await context.SaveChangesAsync(token).ConfigureAwait(true);
            }
            var pendingAdmin = await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            foreach (var (method, path) in AdministrativeRoutes)
            {
                using var request = ApiSqlHost.Authorized(method, path, pendingAdmin.AccessToken, CompanyRequest());
                using var response = await client.SendAsync(request, token).ConfigureAwait(true);
                await ProblemAssertions.AssertProblemAsync(response, HttpStatusCode.Forbidden, "auth.password_change_required").ConfigureAwait(true);
            }
            using var currencies = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/workspaces/currencies", pendingAdmin.AccessToken);
            using var currenciesResponse = await client.SendAsync(currencies, token).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, currenciesResponse.StatusCode);
            using var anonymousCurrencies = await client.GetAsync("/api/v1/workspaces/currencies", token).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousCurrencies.StatusCode);
        }
    }

    private static readonly (HttpMethod Method, string Path)[] AdministrativeRoutes =
    [
        (HttpMethod.Post, "/api/v1/workspaces/companies"),
        (HttpMethod.Post, "/api/v1/workspaces/fiscal-years"),
        (HttpMethod.Get, "/api/v1/workspaces/migration/plan"),
        (HttpMethod.Post, "/api/v1/workspaces/migration/execute?companyId=00000000-0000-0000-0000-000000000001&fiscalYearId=00000000-0000-0000-0000-000000000002&dryRun=false"),
    ];

    private static JsonContent CompanyRequest() => JsonContent.Create(new CreateCompanyWorkspaceRequest
    {
        Code = "DENIED", NameArabic = "شركة محظورة", BaseCurrencyCode = "USD", InitialYear = 2026,
        StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31),
    }, options: ApiJson.Options);
}
