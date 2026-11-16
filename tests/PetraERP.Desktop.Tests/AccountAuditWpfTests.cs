using System.Windows;
using System.Windows.Controls;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Tests;

public sealed partial class WpfFoundationTests
{
    private static void VerifyAccountAuditCard(WpfThemeService themes)
    {
        foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
        foreach (var language in new[] { "ar", "en" })
        {
            themes.Apply(theme);
            var fixture = new ShellFixture(); fixture.User.Permissions = ["Ledger.Accounts.View"];
            var accounting = new AccountingFixture(); var account = Guid.NewGuid();
            accounting.Accounts.Add(new() { Id = account, Code = "1001", NameArabic = "الصندوق الرئيسي", NameEnglish = "Main cash",
                AccountType = AccountType.Assets, IsActive = true, PostingAllowed = false, Version = "AQ==" });
            for (var i = 0; i < 26; i++) accounting.Accounts.Add(new() { Id = Guid.NewGuid(), ParentId = account,
                Code = "1001-" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), NameArabic = "حساب فرعي", NameEnglish = "Child account" });
            var before = new AccountStateResponse { Code = "1001", NameArabic = "الصندوق", NameEnglish = null, AccountType = AccountType.Assets,
                CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly, CostCenterPolicy = CostCenterPolicy.Optional, IsActive = true, PostingAllowed = false };
            var after = new AccountStateResponse { Code = "1001", NameArabic = "الصندوق الرئيسي", NameEnglish = "Main cash", AccountType = AccountType.Assets,
                CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly, CostCenterPolicy = CostCenterPolicy.Required, IsActive = true, PostingAllowed = false };
            accounting.History.Add(new() { EventId = Guid.NewGuid(), AccountId = account, Action = AccountAuditAction.Updated,
                ActorUserId = fixture.User.UserId, ActorNameArabic = "مدير النظام", ActorNameEnglish = "Administrator", Before = before, After = after,
                WorkingBranchId = fixture.Branch.Id, OccurredAtUtc = new DateTimeOffset(2026, 10, 18, 15, 0, 0, TimeSpan.Zero) });
            using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
            shell.Localization.SetLanguage(language);
            var window = new DesktopWindow(shell) { Left = -10000, ShowActivated = false };
            window.Show();
            try
            {
                shell.UserName = "admin"; shell.Password = "test"; shell.LoginCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                // Login applies the user's saved language. Each visual case explicitly chooses its requested language afterwards.
                shell.Localization.SetLanguage(language);
                shell.NavigateToAsync("accounts", new OpenAccountCardRequest(account)).GetAwaiter().GetResult();
                shell.Localization.SetLanguage(language);
                window.UpdateLayout(); Pump();
                var view = Assert.Single(Descendants<AccountCardView>(window));
                var tab = (TabItem)view.FindName("AccountAuditTab"); tab.IsSelected = true; window.UpdateLayout(); Pump();
                window.Width = 1366; window.Height = 768; ArrangeViewport(window); Pump();
                var events = (DataGrid)view.FindName("AccountAuditGrid"); var fields = (DataGrid)view.FindName("AccountAuditFieldsGrid");
                Assert.True(events.IsVisible); Assert.True(events.IsReadOnly); Assert.True(fields.IsReadOnly);
                Assert.Single(events.Items); Assert.Equal(3, fields.Items.Count);
                RenderSystem((FrameworkElement)window.Content, "account-audit", language, theme);
                Assert.Equal(language, shell.Localization.Language);
                var labels = Descendants<TextBlock>(events).Where(text => text.IsVisible).Select(text => text.Text).ToArray();
                Assert.True(labels.Contains(language == "ar" ? "تعديل" : "Updated", StringComparer.Ordinal), language + ": " + string.Join(" / ", labels));
                Assert.Contains(shell.Localization["D_DateTime"], labels);
                Assert.Contains(Descendants<TextBlock>(fields), text => text.IsVisible && text.Text == shell.Localization["D_AuditBefore"]);
                Assert.Contains(Descendants<TextBlock>(fields), text => text.IsVisible && text.Text == shell.Localization["D_AuditAfter"]);
                Assert.DoesNotContain(Descendants<TextBlock>(view), text => text.IsVisible && text.Text.StartsWith("D_", StringComparison.Ordinal));
                var hierarchy = (TabItem)view.FindName("AccountHierarchyTab"); hierarchy.IsSelected = true; window.UpdateLayout(); Pump();
                var children = (DataGrid)view.FindName("ChildrenGrid");
                Assert.True(children.IsVisible); Assert.True(children.IsReadOnly); Assert.Equal(25, children.Items.Count);
                Assert.Contains(Descendants<TextBlock>(children), text => text.IsVisible && text.Text == shell.Localization["D_Code"]);
                Assert.Contains(Descendants<TextBlock>(children), text => text.IsVisible && text.Text == shell.Localization["D_Name"]);
                var card = Assert.IsType<AccountCardViewModel>(shell.CurrentView);
                var next = Assert.Single(Descendants<Button>(view), button => ReferenceEquals(button.Command, card.NextChildrenCommand));
                Assert.True(next.IsEnabled);
                ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)next.Command).ExecuteAsync(null).GetAwaiter().GetResult(); Pump();
                Assert.Single(children.Items); Assert.Equal(2, card.ChildrenPage); Assert.False(next.IsEnabled);
                RenderSystem((FrameworkElement)window.Content, "account-hierarchy", language, theme);
                window.Width = 683; window.Height = 384; ArrangeViewport(window); Pump();
                Assert.InRange(children.ActualHeight, 0, view.ActualHeight);
                tab.IsSelected = true; window.UpdateLayout(); Pump();
                Assert.InRange(events.ActualHeight, 0, view.ActualHeight);
                Assert.InRange(fields.ActualHeight, 0, view.ActualHeight);
            }
            finally { window.Close(); }
        }
    }
}
