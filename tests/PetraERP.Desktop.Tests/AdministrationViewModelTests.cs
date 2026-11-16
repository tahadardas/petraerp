using PetraERP.ApiClient;

namespace PetraERP.Desktop.Tests;

public sealed class AdministrationViewModelTests
{
    [Fact]
    public async Task UserPagingAndExplicitScopeReplacementUseServerContractAndVersion()
    {
        var fixture = new ShellFixture(); var api = new AdministrationFixture(); using var shell = fixture.Create(api);
        shell.UserName = "admin"; shell.Password = "secret"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateCommand.ExecuteAsync("users").ConfigureAwait(true); var model = shell.Administration;
        model.UserSearch = "موظف"; await model.NextUsersCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(2, api.RequestedPage); Assert.Equal("موظف", api.RequestedSearch);
        model.SelectedUser = model.Users[0]; await model.OpenUserCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(model.IsExistingUser); Assert.False(Assert.Single(model.Scopes).IsSelected);
        model.Scopes[0].IsSelected = true; model.Scopes[0].Branches[0].IsSelected = true;
        await model.SaveScopesCommand.ExecuteAsync(null).ConfigureAwait(true);
        var grant = Assert.Single(api.SavedScopes!.Scopes); Assert.Equal(api.Company.Id, grant.CompanyId); Assert.Equal(api.Company.Branches[0].Id, Assert.Single(grant.BranchIds));
        Assert.Equal("AQIDBAUGBwg=", api.SavedScopes.Version);
        fixture.Confirm = false; var before = api.Writes; await model.SaveScopesCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Equal(before, api.Writes);
        model.Configure(["System.Users.View"]); Assert.False(model.SaveScopesCommand.CanExecute(null));
        await model.SaveScopesCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Equal(before, api.Writes);
    }
    [Fact]
    public async Task ClearingOptionalNameUsesNullAndDiscardingOtherSectionsNeedsExplicitConfirmation()
    {
        var fixture = new ShellFixture(); var api = new AdministrationFixture(); using var shell = fixture.Create(api);
        shell.UserName = "admin"; shell.Password = "secret"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateCommand.ExecuteAsync("users").ConfigureAwait(true);
        var model = shell.Administration; model.SelectedUser = model.Users[0]; await model.OpenUserCommand.ExecuteAsync(null).ConfigureAwait(true);
        model.DisplayEnglish = string.Empty; await model.SaveProfileCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Null(api.SavedProfile!.DisplayNameEnglish);
        model.DisplayArabic = "تعديل غير محفوظ"; model.Scopes[0].IsSelected = true;
        fixture.Confirm = false; var writes = api.Writes; await model.SaveScopesCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("D_ConfirmDiscardOtherEdits", fixture.LastConfirmation); Assert.Equal(writes, api.Writes); Assert.Equal("تعديل غير محفوظ", model.DisplayArabic);
    }
    [Fact]
    public async Task ConflictPreservesDraftAndDoesNotSilentlyRetryOrReplaceTheVersion()
    {
        var fixture = new ShellFixture(); var api = new AdministrationFixture(); using var shell = fixture.Create(api);
        shell.UserName = "admin"; shell.Password = "secret"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateCommand.ExecuteAsync("users").ConfigureAwait(true);
        var model = shell.Administration; model.SelectedUser = model.Users[0]; await model.OpenUserCommand.ExecuteAsync(null).ConfigureAwait(true);
        model.DisplayArabic = "اسم معدّل"; api.WriteFailure = new PetraApiException("private", 409, "{}", null!, null);
        await model.SaveProfileCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(1, api.Writes); Assert.Equal("اسم معدّل", model.DisplayArabic); Assert.Equal("AQIDBAUGBwg=", model.UserVersion);
        Assert.Equal(shell.Localization["D_Conflict"], shell.Operation.Error);
    }
}
