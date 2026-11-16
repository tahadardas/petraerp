using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Tests;

public sealed partial class WpfFoundationTests
{
    private static void VerifyCostCenterHistory(WpfThemeService themes)
    {
        foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
        foreach (var language in new[] { "ar", "en" })
        {
            themes.Apply(theme);
            var fixture = new ShellFixture(); fixture.User.Permissions = ["Ledger.CostCenters.View"];
            var accounting = new AccountingFixture(); var id = Guid.NewGuid();
            accounting.CostCenters.Add(new() { Id = id, Code = "HQ", NameArabic = "الإدارة الرئيسية", NameEnglish = "Head office", IsActive = true, Version = "AQ==" });
            for (var i = 0; i < 26; i++) accounting.CenterHistory.Add(new()
            {
                EventId = Guid.NewGuid(), CostCenterId = id, Action = CostCenterAuditAction.Updated, ActorUserId = fixture.User.UserId,
                ActorNameArabic = "مدير النظام", ActorNameEnglish = "Administrator", WorkingBranchId = fixture.Branch.Id,
                OccurredAtUtc = new(2026, 10, 18, 17, 0, 0, TimeSpan.Zero),
                Before = new() { Code = "HQ", NameArabic = "الإدارة", NameEnglish = null, IsActive = true },
                After = new() { Code = "HQ", NameArabic = "الإدارة الرئيسية", NameEnglish = "Head office", IsActive = true },
            });
            using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
            var window = new DesktopWindow(shell) { Left = -10000, ShowActivated = false };
            window.Show();
            try
            {
                shell.UserName = "admin"; shell.Password = "test"; shell.LoginCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                shell.NavigateToAsync("cost-centers", null).GetAwaiter().GetResult(); shell.Localization.SetLanguage(language);
                var centers = Assert.IsType<CostCentersViewModel>(shell.CurrentView); centers.Selected = centers.Items[0];
                window.Width = 1366; window.Height = 768; ArrangeViewport(window); Pump();
                var view = Assert.Single(Descendants<CostCentersView>(window));
                ((TabItem)view.FindName("CostCenterHistoryTab")).IsSelected = true; window.UpdateLayout(); Pump();
                var historyView = Assert.Single(Descendants<CostCenterHistoryView>(view));
                var refresh = Assert.Single(Descendants<Button>(historyView), button => ReferenceEquals(button.Command, centers.RefreshHistoryCommand));
                Assert.True(refresh.IsEnabled); ((IAsyncRelayCommand)refresh.Command).ExecuteAsync(null).GetAwaiter().GetResult(); Pump();
                var grid = (DataGrid)historyView.FindName("CostCenterHistoryGrid");
                var fields = (DataGrid)historyView.FindName("CostCenterHistoryFieldsGrid");
                Assert.True(grid.IsVisible); Assert.True(grid.IsReadOnly); Assert.True(fields.IsReadOnly); Assert.Equal(25, grid.Items.Count); Assert.Equal(2, fields.Items.Count);
                Assert.Contains(Descendants<TextBlock>(grid), text => text.IsVisible && text.Text == shell.Localization["D_DateTime"]);
                Assert.Contains(Descendants<TextBlock>(fields), text => text.IsVisible && text.Text == shell.Localization["D_AuditBefore"]);
                Assert.DoesNotContain(Descendants<TextBlock>(historyView), text => text.IsVisible && text.Text.StartsWith("D_", StringComparison.Ordinal));
                var next = Assert.Single(Descendants<Button>(historyView), button => ReferenceEquals(button.Command, centers.NextHistoryCommand));
                ((IAsyncRelayCommand)next.Command).ExecuteAsync(null).GetAwaiter().GetResult(); Pump();
                Assert.Single(grid.Items); Assert.False(next.IsEnabled); Assert.False(centers.CanManage);
                RenderSystem((FrameworkElement)window.Content, "cost-center-audit", language, theme);
                window.Width = 683; window.Height = 384; ArrangeViewport(window); Pump();
                Assert.InRange(grid.ActualHeight, 0, view.ActualHeight); Assert.InRange(fields.ActualHeight, 0, view.ActualHeight);
            }
            finally { window.Close(); }
        }
    }
}
