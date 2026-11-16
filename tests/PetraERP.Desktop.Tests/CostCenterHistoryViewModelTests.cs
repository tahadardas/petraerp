using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;

namespace PetraERP.Desktop.Tests;

public sealed class CostCenterHistoryViewModelTests
{
    [Fact]
    public async Task ReadOnlyHistoryPagesPreserveTheDraftAndClearOnNewSelectionRevocationAndLateResponse()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = ["Ledger.CostCenters.View", "Ledger.CostCenters.Manage"];
        var accounting = new AccountingFixture(); var id = Guid.NewGuid();
        accounting.CostCenters.Add(new() { Id = id, Code = "HQ", NameArabic = "الإدارة", Version = "AQ==", IsActive = true });
        accounting.CostCenters.Add(new() { Id = Guid.NewGuid(), Code = "BR", NameArabic = "الفرع", Version = "AQ==", IsActive = true });
        for (var i = 0; i < 26; i++) accounting.CenterHistory.Add(new() { EventId = Guid.NewGuid(), CostCenterId = id,
            Action = CostCenterAuditAction.Updated, ActorNameArabic = "أحمد", Before = new() { NameArabic = "قبل", NameEnglish = null }, After = new() { NameArabic = "بعد", NameEnglish = "" } });
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("cost-centers", null).ConfigureAwait(true);
        var centers = Assert.IsType<CostCentersViewModel>(shell.CurrentView); centers.Selected = centers.Items[0]; centers.NameArabic = "مسودة";
        await centers.RefreshHistoryCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal((fixture.Company.Id, fixture.Branch.Id, id, 1, 25), accounting.CenterHistoryQueries[0]);
        Assert.Equal(25, centers.History.Count); Assert.Equal(26, centers.HistoryTotalCount); Assert.Equal("أحمد", centers.HistoryActor);
        Assert.Equal("مسودة", centers.NameArabic); Assert.Equal("AQ==", centers.Selected.Version); Assert.True(centers.HasUnsavedChanges);
        Assert.Contains(centers.HistoryFields, field => field.Before == shell.Localization["D_AuditNull"] && field.After == shell.Localization["D_AuditEmpty"]);
        await centers.NextHistoryCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Single(centers.History);
        Assert.False(centers.NextHistoryCommand.CanExecute(null)); Assert.True(centers.PreviousHistoryCommand.CanExecute(null));
        await centers.PreviousHistoryCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Equal(1, centers.HistoryPage);
        centers.Selected = centers.Items[1]; Assert.Empty(centers.History);
        centers.NewCommand.Execute(null); Assert.False(centers.RefreshHistoryCommand.CanExecute(null));
        centers.Selected = centers.Items[0]; accounting.PendingCenterHistory = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = centers.RefreshHistoryCommand.ExecuteAsync(null); centers.Clear();
        accounting.PendingCenterHistory.SetResult(new() { Items = [accounting.CenterHistory[0]], TotalCount = 26, Page = 1, PageSize = 25 });
        await pending.ConfigureAwait(true);
        Assert.Empty(centers.History); Assert.Empty(centers.HistoryFields); Assert.False(centers.RefreshHistoryCommand.CanExecute(null));
        await centers.ActivateAsync(new(fixture.Company, fixture.Branch, new HashSet<string>()), null, CancellationToken.None).ConfigureAwait(true);
        Assert.False(centers.HasContext);
    }
}
