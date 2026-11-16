using System.Windows;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Productivity;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// The workspace (search, palette, favorites, recent, preview, grid preferences) is hosted by the single application
/// shell. These tests cover the parity previously provided by the retired WorkspaceWindow.
/// </summary>
public sealed class WorkspaceViewModelTests
{
    private static WorkspaceViewModel Create(WorkspaceFixture fixture) => new(fixture, new LocalizationService("ar"), new TestThemes());
    private static Task ReadyAsync(WorkspaceViewModel model, WorkspaceFixture fixture) => model.SetContextAsync(fixture.A, fixture.Branches[0]);

    [Fact]
    public async Task CommandsRequireAnAuthorizedCompanyAndBranchFromTheShell()
    {
        var fixture = new WorkspaceFixture(); using var model = Create(fixture);
        Assert.False(model.ContextReady);
        await ReadyAsync(model, fixture).ConfigureAwait(true);
        Assert.True(model.ContextReady); await model.SearchNowAsync("cash").ConfigureAwait(true); Assert.NotEmpty(model.PaletteEntities);
        await model.SetContextAsync(fixture.A, fixture.Branches[2]).ConfigureAwait(true); // branch of another company
        Assert.False(model.ContextReady); Assert.Empty(model.PaletteEntities); Assert.Null(model.Company);
        await model.SetContextAsync(fixture.A, null).ConfigureAwait(true);
        Assert.False(model.ContextReady);
    }

    [Fact]
    public async Task LateResponseCannotRepopulateAnotherBranch()
    {
        var fixture = new WorkspaceFixture(); using var model = Create(fixture); await ReadyAsync(model, fixture).ConfigureAwait(true);
        var pending = new TaskCompletionSource<SearchPageResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.PendingSearch = pending;
        var request = model.SearchNowAsync("cash");
        await model.SetContextAsync(fixture.A, fixture.Branches[1]).ConfigureAwait(true);
        pending.SetResult(new SearchPageResponse { Items = [new() { Name = "FORBIDDEN OLD RESPONSE" }], Page = 1, TotalCount = 1 });
        await request.ConfigureAwait(true);
        Assert.DoesNotContain(model.PaletteEntities, hit => hit.Name == "FORBIDDEN OLD RESPONSE");
        await model.SearchNowAsync("cash").ConfigureAwait(true);
        Assert.Equal(fixture.Branches[1].Id.ToString("N"), Assert.Single(model.PaletteEntities).Code);
    }

    [Fact]
    public async Task PaletteFavoritesAndOpeningRespectTheServerCatalog()
    {
        var fixture = new WorkspaceFixture(); using var model = Create(fixture); await ReadyAsync(model, fixture).ConfigureAwait(true);
        Assert.Single(model.Catalog);
        await model.ToggleFavoriteAsync("sales.new").ConfigureAwait(true);
        Assert.Empty(model.Favorites);
        var allowed = Assert.Single(model.Catalog);
        await model.ToggleFavoriteAsync(allowed.Id).ConfigureAwait(true);
        Assert.Equal("accounts.browse", Assert.Single(model.Favorites).Id);
        fixture.AllowAccounts = false;
        await ReadyAsync(model, fixture).ConfigureAwait(true);
        Assert.Empty(model.Catalog); Assert.Empty(model.Favorites);

    }

    [Fact]
    public async Task SortingPagingAndLayoutsGoToServerWithoutChangingData()
    {
        var fixture = new WorkspaceFixture(); var themes = new TestThemes();
        using var model = new WorkspaceViewModel(fixture, new LocalizationService("ar"), themes); await ReadyAsync(model, fixture).ConfigureAwait(true);
        model.SetGrid(new GridPreferenceRequest { Key = "workspace.results", Columns = [new() { Key = "Code", Width = 160, Order = 0, Visible = true }] });
        model.Localization.SetLanguage("en"); themes.Apply(ThemePreference.Dark);
        await model.SavePreferencesAsync().ConfigureAwait(true);
        Assert.Equal("en", fixture.LastSaved!.Preferences.Language); Assert.Equal("Dark", fixture.LastSaved.Preferences.Theme);
        Assert.Equal(160, fixture.LastSaved.Preferences.Grids[0].Columns[0].Width);
        Assert.Equal(FlowDirection.LeftToRight, model.Localization.FlowDirection);
        model.Localization.SetLanguage("ar"); Assert.Equal(FlowDirection.RightToLeft, model.Localization.FlowDirection);
        Assert.Equal("بحث", model.Localization["UX_Search"]);
    }

    [Fact]
    public async Task NewQueryRejectsLateSearchResponses()
    {
        var fixture = new WorkspaceFixture(); using var model = Create(fixture); await ReadyAsync(model, fixture).ConfigureAwait(true);
        var pending = new TaskCompletionSource<SearchPageResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.PendingSearch = pending;
        var first = model.SearchNowAsync("old");
        await model.SearchNowAsync("new").ConfigureAwait(true);
        pending.SetResult(new SearchPageResponse { Items = [new() { Name = "OLD RESPONSE" }] });
        await first.ConfigureAwait(true);
        Assert.DoesNotContain(model.PaletteEntities, hit => hit.Name == "OLD RESPONSE");
    }

    [Fact]
    public async Task LostSessionClearsCachedContextAndNotifiesTheShell()
    {
        var fixture = new WorkspaceFixture(); using var model = Create(fixture); await ReadyAsync(model, fixture).ConfigureAwait(true);
        var notified = false; model.SessionLost += (_, _) => notified = true;
        fixture.SessionUnavailable = true; await model.SearchNowAsync("cash").ConfigureAwait(true);
        Assert.True(notified); Assert.False(model.ContextReady); Assert.Empty(model.PaletteEntities); Assert.Null(model.Company);
    }

    [Fact]
    public async Task ResetImmediatelyClearsContextAndCachedEntities()
    {
        var fixture = new WorkspaceFixture(); using var model = Create(fixture); await ReadyAsync(model, fixture).ConfigureAwait(true);
        await model.SearchNowAsync("cash").ConfigureAwait(true); Assert.NotEmpty(model.PaletteEntities);
        model.Reset();
        Assert.False(model.ContextReady); Assert.Empty(model.PaletteEntities); Assert.Empty(model.Recent); Assert.Empty(model.Catalog); Assert.Null(model.Company);
       
    }

    [Fact]
    public async Task TheSingleShellHostsTheWorkspaceWithItsOwnSessionAndContext()
    {
        var shell = new ShellFixture();
        shell.User.Permissions = [.. shell.User.Permissions, "Ledger.Accounts.View"];
        var workspace = new WorkspaceFixture();
        using var desktop = shell.Create(new AdministrationFixture(), workspace: workspace);
        desktop.UserName = "admin"; desktop.Password = "transient-secret";
        await desktop.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.DoesNotContain(desktop.Navigation.Items, item => item.Id == "workspace");

        await desktop.NavigateCommand.ExecuteAsync("accounts").ConfigureAwait(true);
        Assert.True(desktop.Workspace.ContextReady);
        Assert.Equal(shell.Company.Id, desktop.Workspace.Company!.Id);
        Assert.Equal(shell.Branch.Id, desktop.Workspace.Branch!.Id);
        await desktop.Workspace.SearchNowAsync("cash").ConfigureAwait(true); Assert.NotEmpty(desktop.Workspace.PaletteEntities);

        // Periodic revalidation with the same context keeps the workspace.
        await desktop.RefreshAsync().ConfigureAwait(true);
        Assert.True(desktop.Workspace.ContextReady);

        // Revoked company scope clears it.
        shell.CompanyRevoked = true;
        await desktop.RefreshAsync().ConfigureAwait(true);
        Assert.False(desktop.Workspace.ContextReady);
        Assert.Empty(desktop.Workspace.PaletteEntities);
        shell.CompanyRevoked = false;

        // Signing out through the shell drops every cached workspace entity.
        await desktop.LogoutCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.False(desktop.Workspace.ContextReady);
        Assert.Null(desktop.Workspace.Company);
    }

    [Fact]
    public async Task PendingPasswordChangeKeepsTheWorkspaceUnavailable()
    {
        var shell = new ShellFixture { ForcePasswordChange = true };
        using var desktop = shell.Create(new AdministrationFixture());
        desktop.UserName = "admin"; desktop.Password = "temporary";
        await desktop.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(desktop.PasswordChangeRequired);
        Assert.False(desktop.NavigateCommand.CanExecute("workspace"));
        Assert.False(desktop.Workspace.ContextReady);
    }

    [Fact]
    public async Task WorkspaceSessionLossSignsTheShellOut()
    {
        var shell = new ShellFixture();
        shell.User.Permissions = [.. shell.User.Permissions, "Ledger.Journals.View"];
        var workspace = new WorkspaceFixture();
        using var desktop = shell.Create(new AdministrationFixture(), workspace: workspace);
        desktop.UserName = "admin"; desktop.Password = "transient-secret";
        await desktop.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await desktop.NavigateCommand.ExecuteAsync("accounts").ConfigureAwait(true);
        workspace.SessionUnavailable = true;
        await desktop.Workspace.SearchNowAsync("cash").ConfigureAwait(true);
        Assert.False(desktop.IsAuthenticated);
        Assert.Null(desktop.Company);
        Assert.False(desktop.Workspace.ContextReady);
    }
}
