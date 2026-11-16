using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class AccountCardContextViewModelTests
{
    [Fact]
    public async Task CardPagesImmediateChildrenWithoutTreeReadsOrReplacingItsEditingSnapshot()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = ["Ledger.Accounts.View", "Ledger.Accounts.Manage"];
        var accounting = new AccountingFixture(); var id = Guid.NewGuid();
        accounting.Accounts.Add(new() { Id = id, Code = "1000", NameArabic = "الأصول", IsActive = true, PostingAllowed = false, Version = "AQ==" });
        for (var i = 0; i < 56; i++) accounting.Accounts.Add(new() { Id = Guid.NewGuid(), ParentId = id, Code = "1000-" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), NameArabic = "حساب فرعي" });
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        var trees = accounting.TreeCalls;
        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(id)).ConfigureAwait(true);
        var card = Assert.IsType<AccountCardViewModel>(shell.CurrentView);
        Assert.Equal(trees, accounting.TreeCalls); Assert.Single(card.Path); Assert.Null(card.Parent);
        Assert.Equal(56, card.ChildrenTotalCount); Assert.Equal(25, card.Children.Count);
        Assert.False(card.PreviousChildrenCommand.CanExecute(null)); Assert.True(card.NextChildrenCommand.CanExecute(null));
        card.BeginEditCommand.Execute(null); card.DraftNameArabic = "مسودة محلية";
        var original = card.Account;
        accounting.Accounts[0] = new() { Id = id, Code = "1000", NameArabic = "نسخة أحدث", IsActive = true, Version = "Ag==" };
        await card.NextChildrenCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(2, card.ChildrenPage); Assert.Equal("1000-025", card.Children[0].Code);
        Assert.Same(original, card.Account); Assert.Equal("AQ==", card.Account!.Version); Assert.Equal("مسودة محلية", card.DraftNameArabic);
        await card.NextChildrenCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(6, card.Children.Count); Assert.False(card.NextChildrenCommand.CanExecute(null));
        await card.PreviousChildrenCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Equal(2, card.ChildrenPage);
        Assert.All(accounting.ContextQueries, query => Assert.Equal((fixture.Company.Id, fixture.Branch.Id, id, 25), (query.Company, query.Branch, query.Account, query.Size)));
        accounting.PendingContext = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = card.RefreshChildrenCommand.ExecuteAsync(null); card.Clear();
        accounting.PendingContext.SetResult(new() { Path = [accounting.Accounts[0]], Children = new() { Items = [accounting.Accounts[1]], TotalCount = 56, Page = 1, PageSize = 25 } });
        await pending.ConfigureAwait(true);
        Assert.False(card.HasContext); Assert.Empty(card.Children); Assert.Equal(0, card.ChildrenTotalCount); Assert.False(card.RefreshChildrenCommand.CanExecute(null));
    }
}
