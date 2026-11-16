using System.Net.Http;
using PetraERP.ApiClient;
using PetraERP.Desktop.Administration;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Tests;

public sealed class DesktopShellTests
{
    [Fact]
    public async Task LoginContextNavigationAndLogoutUseAuthorizedServerValues()
    {
        var fixture = new ShellFixture { Saved = new(Guid.NewGuid(), Guid.NewGuid()) };
        using var model = fixture.Create(new AdministrationFixture());
        Assert.False(model.LoginCommand.CanExecute(null));
        model.UserName = "admin"; model.Password = "secret";
        await model.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(model.IsAuthenticated); Assert.Empty(model.Password); Assert.Equal(fixture.Company.Id, model.Company!.Id); Assert.Equal(fixture.Branch.Id, model.Branch!.Id);
        Assert.Contains(model.Navigation.Items, item => item.Id == "users"); Assert.DoesNotContain(model.Navigation.Items, item => item.Id == "ledger");
        await model.SelectBranchCommand.ExecuteAsync(new BranchContextResponse { Id = Guid.NewGuid(), CompanyId = fixture.Company.Id }).ConfigureAwait(true);
        Assert.Null(model.Branch);
        await model.SelectBranchCommand.ExecuteAsync(fixture.Branch).ConfigureAwait(true); Assert.Equal(fixture.Branch.Id, fixture.Saved!.BranchId);
        model.Navigation.Query = "roles"; Assert.Equal("roles", Assert.Single(model.Navigation.VisibleItems).Id);
        model.ToggleLanguageCommand.Execute(null); Assert.False(model.Localization.IsRightToLeft); Assert.Contains("Administrator", model.UserDisplay);
        model.ToggleThemeCommand.Execute(null); Assert.Equal(ThemePreference.Dark, model.Themes.Preference);
        fixture.Confirm = false; await model.LogoutAllCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.True(model.IsAuthenticated);
        fixture.Confirm = true; await model.LogoutAllCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.True(fixture.LogoutAll); Assert.False(model.IsAuthenticated); Assert.Empty(model.Companies); Assert.Empty(model.Navigation.Items);
    }
    [Fact]
    public async Task OpeningAWindowNeverWaitsForAnIdentityRoundTripAndRevocationStillReloads()
    {
        var fixture = new ShellFixture();
        using var model = fixture.Create(new AdministrationFixture());
        model.UserName = "admin"; model.Password = "secret";
        await model.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        var validatedAtLogin = fixture.ProfileCalls;
        Assert.True(validatedAtLogin > 0);

        // Two clicks that open windows must not each re-fetch the profile — that wait is what read as a freeze.
        await model.NavigateToAsync("users", null).ConfigureAwait(true);
        await model.NavigateToAsync("roles", null).ConfigureAwait(true);
        Assert.Equal(validatedAtLogin, fixture.ProfileCalls);
        Assert.Equal(validatedAtLogin, fixture.CompanyScopeCalls);

        // Permissions withdrawn on the server still reach the shell on the next quiet check.
        fixture.User.Permissions = ["System.Users.View", "System.Users.Manage"];
        await model.RevalidateSessionAsync().ConfigureAwait(true);
        Assert.True(fixture.ProfileCalls > validatedAtLogin);
        Assert.DoesNotContain(model.Navigation.Items, item => item.Id == "roles");
    }
    [Fact]
    public async Task LoginLoadingStatePreventsDuplicateSubmission()
    {
        var fixture = new ShellFixture { LoginGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var model = fixture.Create(new AdministrationFixture()); model.UserName = "admin"; model.Password = "secret";
        var pending = model.LoginCommand.ExecuteAsync(null);
        Assert.True(model.Operation.IsBusy); Assert.False(model.LoginCommand.CanExecute(null)); Assert.False(model.LogoutCommand.CanExecute(null));
        fixture.LoginGate.SetResult(false); await pending.ConfigureAwait(true);
        Assert.Equal(1, fixture.LoginCount); Assert.True(model.IsAuthenticated); Assert.False(model.Operation.IsBusy);
    }
    [Theory]
    [InlineData(401, "auth.invalid_credentials", "InvalidCredentials")]
    [InlineData(423, "auth.locked_out", "AccountLocked")]
    [InlineData(403, "auth.account_disabled", "AccountDisabled")]
    public async Task LoginFailuresAreLocalizedWithoutRawDetails(int status, string code, string expected)
    {
        var fixture = new ShellFixture { LoginFailure = new PetraApiException("private server exception", status, "{\"code\":\"" + code + "\",\"detail\":\"do not expose\"}", null!, null) };
        using var model = fixture.Create(new AdministrationFixture()); model.UserName = "admin"; model.Password = "secret";
        await model.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.False(model.IsAuthenticated); Assert.Equal(model.Localization["D_" + expected], model.Operation.Error); Assert.DoesNotContain("expose", model.Operation.Error); Assert.Empty(model.Password);
    }
    [Theory]
    [InlineData(401, "auth.invalid_credentials", "InvalidCredentials")]
    [InlineData(423, "auth.locked_out", "AccountLocked")]
    [InlineData(403, "auth.account_disabled", "AccountDisabled")]
    [InlineData(401, "auth.session_revoked", "SessionEnded")]
    public async Task StreamedLoginProblemsKeepTheirSpecificLocalizedMessage(int status, string code, string expected)
    {
        var fixture = new ShellFixture
        {
            LoginFailure = new PetraApiException<ProblemDetails>("private server exception", status, string.Empty, null!,
                new ProblemDetails { Code = code, Detail = "do not expose" }, null),
        };
        using var model = fixture.Create(new AdministrationFixture()); model.UserName = "admin"; model.Password = "secret";
        await model.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.False(model.IsAuthenticated); Assert.Equal(expected, model.Operation.ErrorKey);
        Assert.Equal(model.Localization["D_" + expected], model.Operation.Error);
        Assert.DoesNotContain("expose", model.Operation.Error); Assert.Empty(model.Password);
    }
    [Fact]
    public async Task PasswordChangeBlocksShellUntilSuccessfulAndClearsSecrets()
    {
        var fixture = new ShellFixture { ForcePasswordChange = true };
        using var model = fixture.Create(new AdministrationFixture()); model.UserName = "admin"; model.Password = "secret";
        await model.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(model.PasswordChangeRequired); Assert.False(model.IsAuthenticated); Assert.Empty(model.Navigation.Items);
        model.CurrentPassword = "secret"; model.NewPassword = "Different!234"; model.ConfirmPassword = "wrong";
        Assert.False(model.ChangePasswordCommand.CanExecute(null)); model.ConfirmPassword = model.NewPassword;
        await model.ChangePasswordCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(model.IsAuthenticated); Assert.False(model.PasswordChangeRequired); Assert.Empty(model.NewPassword); Assert.Empty(model.CurrentPassword); Assert.Empty(model.ConfirmPassword);
    }
    [Fact]
    public async Task NetworkFailurePermissionChangesRevokedCompanyAndRevokedSessionNeverRetainAdminAccess()
    {
        var fixture = new ShellFixture(); using var model = fixture.Create(new AdministrationFixture()); model.UserName = "admin"; model.Password = "secret";
        await model.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        fixture.ProfileFailure = new HttpRequestException("private network diagnostics"); await model.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.False(model.Operation.IsOnline); Assert.Empty(model.Navigation.Items); Assert.Null(model.Company); Assert.False(model.Administration.NewUserCommand.CanExecute(null));
        fixture.ProfileFailure = null; fixture.User.Permissions = []; fixture.CompanyRevoked = true;
        await model.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.True(model.IsAuthenticated); Assert.Null(model.Company); Assert.Equal("home", Assert.Single(model.Navigation.Items).Id);
        fixture.ProfileFailure = new SessionUnavailableException(); await model.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.False(model.IsAuthenticated); Assert.Null(model.Profile); Assert.Empty(model.Branches);
    }
    [Fact]
    public void TlsAndNetworkAndStorageFailuresHaveSeparateSafeMessages()
    {
        Assert.Equal("TlsFailure", DesktopErrors.Key(new HttpRequestException(HttpRequestError.SecureConnectionError, "private", null)));
        Assert.Equal("ServerUnavailable", DesktopErrors.Key(new HttpRequestException("private")));
        Assert.Equal("StorageFailure", DesktopErrors.Key(new System.IO.IOException("private")));
    }
}
