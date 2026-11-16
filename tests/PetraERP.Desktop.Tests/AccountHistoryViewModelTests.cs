using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class AccountHistoryViewModelTests
{
    private static (ShellFixture Shell, AccountingFixture Accounts, Guid Id) Fixture()
    {
        var shell = new ShellFixture(); shell.User.Permissions = ["Ledger.Accounts.View", "Ledger.Accounts.Manage"];
        var accounts = new AccountingFixture(); var id = Guid.NewGuid();
        accounts.Accounts.Add(new() { Id = id, Code = "1001", NameArabic = "الصندوق", AccountType = AccountType.Assets, IsActive = true, PostingAllowed = true, Version = "AQ==" });
        return (shell, accounts, id);
    }

    [Fact]
    public async Task AuditPagesUseTheCurrentScopeAndShowBeforeAfterWithoutChangingAnEditingCardsVersion()
    {
        var (fixture, accounting, id) = Fixture();
        for (var i = 0; i < 26; i++) accounting.History.Add(new()
        {
            EventId = Guid.NewGuid(), AccountId = id, Action = AccountAuditAction.Updated, ActorUserId = fixture.User.UserId,
            ActorNameArabic = "أحمد", Before = new() { NameArabic = "قبل", NameEnglish = null }, After = new() { NameArabic = "بعد", NameEnglish = "" },
        });
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(id)).ConfigureAwait(true);
        var card = Assert.IsType<AccountCardViewModel>(shell.CurrentView);
        Assert.Equal(25, card.AuditEvents.Count); Assert.Equal(26, card.AuditTotalCount); Assert.True(card.NextAuditCommand.CanExecute(null));
        Assert.Equal((fixture.Company.Id, fixture.Branch.Id, id, 1, 25), accounting.HistoryQueries[0]);
        Assert.Contains(card.AuditFields, field => field.Before == "قبل" && field.After == "بعد");
        Assert.Contains(card.AuditFields, field => field.Before == shell.Localization["D_AuditNull"] && field.After == shell.Localization["D_AuditEmpty"]);
        Assert.Equal("أحمد", card.AuditActor);
        await card.NextAuditCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Single(card.AuditEvents); Assert.Equal(2, card.AuditPage); Assert.False(card.NextAuditCommand.CanExecute(null));
        Assert.True(card.PreviousAuditCommand.CanExecute(null));
        card.BeginEditCommand.Execute(null); card.DraftNameArabic = "تعديل غير محفوظ"; var version = card.Account!.Version;
        Assert.True(card.RefreshAuditCommand.CanExecute(null));
        await card.RefreshAuditCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(card.IsEditMode); Assert.Equal("تعديل غير محفوظ", card.DraftNameArabic); Assert.Equal(version, card.Account.Version);
        card.SelectedAudit = new() { AccountId = id, Action = AccountAuditAction.Deleted };
        Assert.True(card.HasLegacyAudit); Assert.Empty(card.AuditFields);
        await card.ActivateAsync(new(fixture.Company, fixture.Branch, new HashSet<string>()), null, CancellationToken.None).ConfigureAwait(true);
        Assert.Empty(card.AuditEvents); Assert.False(card.RefreshAuditCommand.CanExecute(null));
    }

    [Fact]
    public async Task LateAuditResponseCannotRestoreDataAfterTheCardContextIsCleared()
    {
        var (fixture, accounting, id) = Fixture();
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(id)).ConfigureAwait(true);
        var card = Assert.IsType<AccountCardViewModel>(shell.CurrentView);
        accounting.PendingHistory = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = card.RefreshAuditCommand.ExecuteAsync(null);
        card.Clear();
        accounting.PendingHistory.SetResult(new() { Page = 1, PageSize = 25, TotalCount = 1, Items = [new() { AccountId = id }] });
        await pending.ConfigureAwait(true);
        Assert.False(card.HasContext); Assert.Empty(card.AuditEvents); Assert.Empty(card.AuditFields);
    }
}
