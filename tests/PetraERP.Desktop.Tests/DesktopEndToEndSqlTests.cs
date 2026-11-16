using System.IO;
using PetraERP.Api.Tests;
using PetraERP.Desktop.Administration;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Security;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class DesktopEndToEndSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task RealDesktopModelsAndGeneratedClientMaintainSecurityAgainstRealApiAndSql()
    {
        var host = await ApiSqlHost.CreateBootstrapWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var directory = Path.Combine(Path.GetTempPath(), "PetraERP_Desktop_E2E_" + Guid.NewGuid().ToString("N"));
            var endpoint = new Uri("https://localhost/");
            var store = new WindowsSessionStore(endpoint, directory);
            try
            {
                using var session = new DesktopSession(endpoint, host.Factory.Server.CreateHandler(), host.Factory.Server.CreateHandler(), store);
                var fixture = new ShellFixture();
                var localization = new PetraERP.Desktop.Localization.LocalizationService("ar");
                var operation = new PetraERP.Desktop.Shell.DesktopOperation(localization);
                using var admin = new AdministrationViewModel(new AdministrationClient(session), fixture, operation, localization);
                using var ledger = new GeneralLedgerViewModel(new GeneralLedgerClient(session), fixture, operation, localization);
                var navigator = new ShellNavigator();
                var pages = TestPages.Create(admin, ledger, new GeneralLedgerClient(session), fixture, operation, localization, navigator,
                    new ChartOfAccountsClient(session), new FiscalCalendarClient(session), new LedgerReportsClient(session));
                using var shell = new PetraERP.Desktop.Shell.DesktopViewModel(new DesktopClient(session), fixture, fixture, localization, fixture,
                    new PetraERP.Desktop.Shell.NavigationService(localization), operation, pages,
                    new PetraERP.Desktop.Productivity.WorkspaceViewModel(new WorkspaceClient(session), localization, fixture), navigator, new NotificationCenter(localization), TimeProvider.System);
                shell.UserName = ApiSqlHost.AdminUser; shell.Password = ApiSqlHost.AdminPassword;
                await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
                Assert.True(shell.IsAuthenticated, operation.Error); Assert.NotNull(shell.Profile); Assert.True(shell.ContextReady); Assert.NotNull(store.Read());
                await shell.NavigateCommand.ExecuteAsync("accounts").ConfigureAwait(true); Assert.Empty(operation.Error);
                Assert.True(shell.Workspace.ContextReady, shell.Workspace.Error); Assert.Empty(shell.Workspace.Error);
                Assert.Equal(shell.Company!.Id, shell.Workspace.Company!.Id);
                await shell.NavigateCommand.ExecuteAsync("journals").ConfigureAwait(true); Assert.Empty(operation.Error);
                Assert.Equal(shell.Company.BaseCurrencyCode, ledger.BaseCurrencyCode);
                Assert.Equal(shell.Company.BaseCurrencyCode, ledger.Currencies[0]);
                await shell.NavigateCommand.ExecuteAsync("roles").ConfigureAwait(true); Assert.Empty(operation.Error);
                admin.NewRoleCommand.Execute(null); admin.RoleCode = "DESKTOP_READER"; admin.RoleArabic = "قراءة المستخدمين"; admin.RoleEnglish = "User reader";
                Assert.Equal(PetraERP.Application.Security.PermissionCatalog.All.Count, admin.RolePermissions.Count);
                admin.RolePermissions.Single(option => option.Key == "System.Users.View").IsSelected = true;
                await admin.SaveRoleCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Empty(operation.Error);
                var roleId = Assert.Single(admin.Roles, role => role.Code == "DESKTOP_READER").Id;
                admin.RoleArabic = "قراءة أسماء المستخدمين"; await admin.SaveRoleCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Empty(operation.Error);
                await shell.NavigateCommand.ExecuteAsync("users").ConfigureAwait(true); Assert.Empty(operation.Error);
                admin.NewUserCommand.Execute(null); admin.LoginName = "desktop.reader"; admin.DisplayArabic = "مستخدم واجهة حقيقي"; admin.DisplayEnglish = "Desktop reader"; admin.TemporaryPassword = "Temporary-Desktop-2026!";
                await admin.SaveProfileCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Empty(operation.Error); Assert.Empty(admin.TemporaryPassword);
                var userId = Assert.Single(admin.Users, user => user.UserName == "desktop.reader").Id;
                admin.DisplayArabic = "مستخدم معدّل بالعربية"; await admin.SaveProfileCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Empty(operation.Error);
                admin.UserRoles.Single(option => option.Key == roleId.ToString("D")).IsSelected = true;
                await admin.SaveUserRolesCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Empty(operation.Error);
                var grant = Assert.Single(admin.Scopes); grant.IsSelected = true; Assert.Single(grant.Branches).IsSelected = true;
                await admin.SaveScopesCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Empty(operation.Error);
                using var child = new DesktopSession(endpoint, host.Factory.Server.CreateHandler(), host.Factory.Server.CreateHandler());
                Assert.True(await child.LoginAsync("desktop.reader", "Temporary-Desktop-2026!", Cancellation).ConfigureAwait(true));
                await child.ChangePasswordAsync("Temporary-Desktop-2026!", "Updated-Desktop-2026!", Cancellation).ConfigureAwait(true);
                var childProfile = await child.Api.GetCurrentUserAsync(Cancellation).ConfigureAwait(true);
                Assert.Equal("مستخدم معدّل بالعربية", childProfile.DisplayNameArabic); Assert.Equal("System.Users.View", Assert.Single(childProfile.Permissions));
                Assert.Equal(shell.Company!.Id, Assert.Single((await child.Api.ListMyCompaniesAsync(Cancellation).ConfigureAwait(true)).Items).Id);
                Assert.Equal(shell.Branch!.Id, Assert.Single((await child.Api.ListMyBranchesAsync(shell.Company.Id, Cancellation).ConfigureAwait(true)).Items).Id);
                var scoped = await session.Api.GetUserAsync(userId, Cancellation).ConfigureAwait(true);
                Assert.Equal(shell.Branch.Id, Assert.Single(Assert.Single(scoped.Scopes).BranchIds));
                admin.Reason = "تعطيل اختباري موثق"; await admin.SetStatusCommand.ExecuteAsync(null).ConfigureAwait(true);
                // The user logged in/changed their password after this editor loaded. A stale write must fail.
                Assert.Equal(localization["D_Conflict"], operation.Error);
                admin.SelectedUser = admin.Users.Single(item => item.Id == userId); await admin.OpenUserCommand.ExecuteAsync(null).ConfigureAwait(true);
                admin.Reason = "تعطيل بعد مراجعة النسخة الحالية"; await admin.SetStatusCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Empty(operation.Error);
                await Assert.ThrowsAsync<SessionUnavailableException>(() => child.Api.GetCurrentUserAsync(Cancellation)).ConfigureAwait(true);
                admin.Reason = "إعادة التفعيل"; await admin.SetStatusCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Empty(operation.Error);
                await admin.UnlockCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Empty(operation.Error);
                admin.TemporaryPassword = "Reset-Desktop-2026!"; admin.Reason = "إعادة تعيين موثقة";
                await admin.ResetPasswordCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Empty(operation.Error); Assert.Empty(admin.TemporaryPassword);
                admin.Reason = "إنهاء الجلسات"; await admin.RevokeCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Empty(operation.Error);
                await shell.NavigateCommand.ExecuteAsync("audit").ConfigureAwait(true); Assert.Empty(operation.Error);
                Assert.Contains(admin.Audit, item => item.UserId == userId && item.EventType == PetraERP.ApiClient.SecurityAuditEventType.PasswordReset);
                await shell.LogoutAllCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.False(shell.IsAuthenticated); Assert.Null(store.Read());
            }
            finally { store.Delete(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ProtectedResumeAndServerRevocationReturnTheShellToLogin()
    {
        var host = await ApiSqlHost.CreateBootstrapWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var directory = Path.Combine(Path.GetTempPath(), "PetraERP_Resume_E2E_" + Guid.NewGuid().ToString("N"));
            var endpoint = new Uri("https://localhost/"); var store = new WindowsSessionStore(endpoint, directory);
            try
            {
                using (var initial = new DesktopSession(endpoint, host.Factory.Server.CreateHandler(), host.Factory.Server.CreateHandler(), store))
                    await initial.LoginAsync(ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword, Cancellation).ConfigureAwait(true);
                Assert.NotNull(store.Read());
                using var session = new DesktopSession(endpoint, host.Factory.Server.CreateHandler(), host.Factory.Server.CreateHandler(), store);
                var fixture = new ShellFixture(); var localization = new PetraERP.Desktop.Localization.LocalizationService("en");
                var operation = new PetraERP.Desktop.Shell.DesktopOperation(localization);
                using var admin = new AdministrationViewModel(new AdministrationClient(session), fixture, operation, localization);
                using var ledger = new GeneralLedgerViewModel(new GeneralLedgerClient(session), fixture, operation, localization);
                var navigator = new ShellNavigator();
                var pages = TestPages.Create(admin, ledger, new GeneralLedgerClient(session), fixture, operation, localization, navigator,
                    new ChartOfAccountsClient(session), new FiscalCalendarClient(session), new LedgerReportsClient(session));
                using var shell = new PetraERP.Desktop.Shell.DesktopViewModel(new DesktopClient(session), fixture, fixture, localization, fixture,
                    new PetraERP.Desktop.Shell.NavigationService(localization), operation, pages,
                    new PetraERP.Desktop.Productivity.WorkspaceViewModel(new WorkspaceClient(session), localization, fixture), navigator, new NotificationCenter(localization), TimeProvider.System);
                string diagnostic = string.Empty; operation.Failed += (_, error) => { diagnostic = error.GetType().Name + ":" + (error is PetraERP.Desktop.Connection.SessionUnavailableException sessionError && sessionError.InnerException is PetraERP.ApiClient.PetraApiException apiError ? apiError.StatusCode + ":" + DesktopErrors.Code(apiError) : string.Empty); };
                await shell.InitializeAsync().ConfigureAwait(true); Assert.True(shell.IsAuthenticated, diagnostic + " " + operation.Error); Assert.True(shell.ContextReady);
                using var controller = new DesktopSession(endpoint, host.Factory.Server.CreateHandler(), host.Factory.Server.CreateHandler());
                await controller.LoginAsync(ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword, Cancellation).ConfigureAwait(true);
                await controller.Api.RevokeUserSessionsAsync(shell.Profile!.UserId, new PetraERP.ApiClient.RevokeUserSessionsRequest { Reason = "اختبار إلغاء فعلي" }, Cancellation).ConfigureAwait(true);
                await shell.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
                Assert.False(shell.IsAuthenticated); Assert.Null(shell.Profile); Assert.Empty(shell.Navigation.Items); Assert.Null(store.Read());
                Assert.Equal(localization["D_SessionEnded"], operation.Error);
            }
            finally { store.Delete(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task InvalidPasswordUsesTypedProblemCodeAndCorrectRetryAuthenticatesTheRealDesktopShell()
    {
        var host = await ApiSqlHost.CreateBootstrapWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var session = new DesktopSession(new Uri("https://localhost/"), host.Factory.Server.CreateHandler(), host.Factory.Server.CreateHandler());
            var fixture = new ShellFixture();
            var localization = new PetraERP.Desktop.Localization.LocalizationService("ar");
            var operation = new DesktopOperation(localization);
            using var admin = new AdministrationViewModel(new AdministrationClient(session), fixture, operation, localization);
            using var ledger = new GeneralLedgerViewModel(new GeneralLedgerClient(session), fixture, operation, localization);
            var navigator = new ShellNavigator();
            var pages = TestPages.Create(admin, ledger, new GeneralLedgerClient(session), fixture, operation, localization, navigator,
                new ChartOfAccountsClient(session), new FiscalCalendarClient(session), new LedgerReportsClient(session));
            using var shell = new DesktopViewModel(new DesktopClient(session), fixture, fixture, localization, fixture,
                new NavigationService(localization), operation, pages,
                new PetraERP.Desktop.Productivity.WorkspaceViewModel(new WorkspaceClient(session), localization, fixture),
                navigator, new NotificationCenter(localization), TimeProvider.System);
            PetraERP.ApiClient.PetraApiException<PetraERP.ApiClient.ProblemDetails>? invalid = null;
            operation.Failed += (_, exception) => invalid = exception as PetraERP.ApiClient.PetraApiException<PetraERP.ApiClient.ProblemDetails>;
            shell.UserName = ApiSqlHost.AdminUser;
            shell.Password = "Incorrect-Desktop-Password!";
            await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.False(shell.IsAuthenticated);
            Assert.True(shell.ShowLogin);
            Assert.Empty(shell.Password);
            Assert.NotNull(invalid);
            Assert.Equal(401, invalid.StatusCode);
            Assert.Equal("auth.invalid_credentials", invalid.Result.Code);
            // Streaming generated clients keep the structured result instead of a raw JSON response body.
            Assert.True(string.IsNullOrEmpty(invalid.Response));
            Assert.Equal("InvalidCredentials", operation.ErrorKey);
            Assert.Equal(localization["D_InvalidCredentials"], operation.Error);
            shell.Password = ApiSqlHost.AdminPassword;
            Assert.True(shell.LoginCommand.CanExecute(null));
            await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.True(shell.IsAuthenticated, operation.Error);
            Assert.NotNull(shell.Profile);
            Assert.Equal(ApiSqlHost.AdminUser, shell.Profile.UserName);
            Assert.True(shell.ContextReady);
            Assert.Empty(shell.Password);
            Assert.Empty(operation.Error);
            var current = await session.Api.GetCurrentUserAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(shell.Profile.UserId, current.UserId);
        }
    }

    [Theory(Explicit = true)]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "SqlServer")]
    public async Task FirstLoginWithTwoCompaniesLoadsRegisteredWorkspacesAfterTheLoginOperationCompletes(bool rememberedUnavailableContext)
    {
        var host = await ApiSqlHost.CreateBootstrapWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var second = await host.ProvisionAdditionalWorkspaceAsync("DESKTOP-SECOND", 2026,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).ConfigureAwait(true);
            using var session = new DesktopSession(new Uri("https://localhost/"), host.Factory.Server.CreateHandler(), host.Factory.Server.CreateHandler());
            var fixture = new ShellFixture();
            if (rememberedUnavailableContext)
            {
                // Only local remembered identifiers are stale; authorization and workspace discovery use the real API.
                fixture.Saved = new ContextPreference(Guid.NewGuid(), Guid.NewGuid(), FiscalYearId: Guid.NewGuid());
                Assert.NotEqual(host.CompanyId, fixture.Saved.CompanyId);
                Assert.NotEqual(second.CompanyId, fixture.Saved.CompanyId);
            }
            else
            {
                Assert.Null(fixture.Saved);
            }
            var localization = new PetraERP.Desktop.Localization.LocalizationService("ar");
            var operation = new DesktopOperation(localization);
            using var admin = new AdministrationViewModel(new AdministrationClient(session), fixture, operation, localization);
            using var ledger = new GeneralLedgerViewModel(new GeneralLedgerClient(session), fixture, operation, localization);
            var navigator = new ShellNavigator();
            var pages = TestPages.Create(admin, ledger, new GeneralLedgerClient(session), fixture, operation, localization, navigator,
                new ChartOfAccountsClient(session), new FiscalCalendarClient(session), new LedgerReportsClient(session));
            using var shell = new DesktopViewModel(new DesktopClient(session), fixture, fixture, localization, fixture,
                new NavigationService(localization), operation, pages,
                new PetraERP.Desktop.Productivity.WorkspaceViewModel(new WorkspaceClient(session), localization, fixture),
                navigator, new NotificationCenter(localization), TimeProvider.System);
            shell.UserName = ApiSqlHost.AdminUser;
            shell.Password = ApiSqlHost.AdminPassword;
            await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.True(shell.IsAuthenticated, operation.Error);
            Assert.False(operation.IsBusy);
            Assert.Empty(operation.Error);
            Assert.Equal(2, shell.Companies.Count);
            Assert.True(shell.ShowWorkspaceSelection);
            Assert.False(shell.WorkspaceSelection.HasNoWorkspaces);
            Assert.Equal(2, shell.WorkspaceSelection.AllWorkspaces.Count);
            Assert.Contains(shell.WorkspaceSelection.AllWorkspaces, workspace => workspace.WorkspaceId == host.WorkspaceId
                && workspace.CompanyId == host.CompanyId && workspace.FiscalYearId == host.FiscalYearId && workspace.State == "Active");
            Assert.Contains(shell.WorkspaceSelection.AllWorkspaces, workspace => workspace.WorkspaceId == second.WorkspaceId
                && workspace.CompanyId == second.CompanyId && workspace.FiscalYearId == second.FiscalYearId && workspace.State == "Active");
            var authorized = await session.Api.ListMyWorkspacesAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(authorized.Items.Select(workspace => workspace.WorkspaceId).OrderBy(id => id),
                shell.WorkspaceSelection.AllWorkspaces.Select(workspace => workspace.WorkspaceId).OrderBy(id => id));
        }
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
}
