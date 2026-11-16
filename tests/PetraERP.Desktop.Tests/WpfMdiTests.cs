using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed partial class WpfFoundationTests
{
    /// <summary>
    /// Real WPF: the classic MDI mode renders documents as internal windows (never twice), lays them out inside the container,
    /// minimizes / maximizes / closes them through their own title-bar buttons and gives the tab host back when the mode changes.
    /// </summary>
    private static void VerifyClassicMdi()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = PetraERP.Application.Security.PermissionCatalog.All.Select(permission => permission.Code).ToArray();
        var directory = Path.Combine(Path.GetTempPath(), "petra-mdi-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var shell = fixture.Create(new AdministrationFixture(), accounting: new AccountingFixture(),
                preferences: localization => new UserPreferencesService(new UserPreferenceStore(directory), fixture, localization));
            var window = new DesktopWindow(shell) { Width = 1366, Height = 768, Left = -10000, ShowActivated = false };
            window.Show();
            try
            {
                shell.UserName = "admin"; shell.Password = "test"; shell.LoginCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                // Classic MDI is the default presentation; the tab host is the alternative.
                Assert.True(shell.IsClassicMdi); Assert.Equal(Visibility.Visible, window.MdiHost.Visibility);
                Assert.Equal(Visibility.Collapsed, window.DocumentTabsStrip.Visibility);
                Assert.Equal(ScrollBarVisibility.Disabled, window.WorkspaceViewport.VerticalScrollBarVisibility);
                Assert.Equal(0d, window.WorkspaceViewport.MinimumContentHeight);
                shell.Preferences!.SetWindowMode(WindowPresentationMode.Tabbed); window.UpdateLayout(); Pump();
                Assert.False(shell.IsClassicMdi); Assert.Equal(Visibility.Collapsed, window.MdiHost.Visibility);
                Assert.Equal(Visibility.Visible, window.DocumentTabsStrip.Visibility);
                Assert.Equal(ScrollBarVisibility.Auto, window.WorkspaceViewport.VerticalScrollBarVisibility);
                shell.Preferences!.SetWindowMode(WindowPresentationMode.ClassicMdi); window.UpdateLayout(); Pump();
                Assert.True(shell.IsClassicMdi); Assert.Equal(Visibility.Visible, window.MdiHost.Visibility);
                Assert.Equal(Visibility.Collapsed, window.DocumentTabsStrip.Visibility);

                foreach (var route in new[] { "accounts", "cost-centers", "periods" }) shell.NavigateToAsync(route, null).GetAwaiter().GetResult();
                window.UpdateLayout(); Pump();
                var host = window.MdiHost;
                Assert.Equal(3, host.WindowCount);
                Assert.Null(window.PageHost.Content);
                Assert.Equal(Visibility.Collapsed, window.PageSurface.Visibility);
                Assert.Single(host.Windows, child => child.Document.IsActive);
                host.ContextCaption = "الفرع الرئيسي · 2026";
                Assert.All(host.Windows, child => Assert.Equal("الفرع الرئيسي · 2026", child.ContextCaption));
                Assert.All(host.Windows, child => Assert.Equal("الفرع الرئيسي · 2026", Descendants<TextBlock>(child).First(block => block.ToolTip is string).ToolTip));
                Assert.All(host.Windows, child => AssertInsideMdi(host, child));

                shell.TileHorizontallyCommand.Execute(null); window.UpdateLayout(); Pump();
                var rows = host.Windows.OrderBy(Canvas.GetTop).ToArray();
                Assert.All(rows, child => AssertInsideMdi(host, child));
                for (var index = 0; index < rows.Length - 1; index++) Assert.True(Canvas.GetTop(rows[index]) + rows[index].Height <= Canvas.GetTop(rows[index + 1]) + 1.0, "tiled rows overlap");
                // The 2-DIP busy bar above the workspace may appear or vanish while pages load, so the container can differ by that much.
                Assert.InRange(rows.Sum(child => child.Height), host.ActualHeight - 4, host.ActualHeight + 4);

                shell.TileVerticallyCommand.Execute(null); window.UpdateLayout(); Pump();
                var columns = host.Windows.OrderBy(Canvas.GetLeft).ToArray();
                Assert.All(columns, child => AssertInsideMdi(host, child));
                for (var index = 0; index < columns.Length - 1; index++) Assert.True(Canvas.GetLeft(columns[index]) + columns[index].Width <= Canvas.GetLeft(columns[index + 1]) + 1.0, "tiled columns overlap");

                shell.CascadeWindowsCommand.Execute(null); window.UpdateLayout(); Pump();
                Assert.All(host.Windows, child => AssertInsideMdi(host, child));
                SaveMdiScreenshot(window, "mdi-cascade-ar-1366");

                var active = host.Windows.Single(child => child.Document.IsActive);
                shell.MinimizeActiveDocumentCommand.Execute(null); window.UpdateLayout(); Pump();
                Assert.Equal(MdiWindowState.Minimized, active.State);
                Assert.True(active.Height < 40, "a minimized window collapses to its title bar");
                Assert.Equal(host.ActualHeight, Canvas.GetTop(active) + active.Height, 1);
                shell.Documents.Activate(active.Document); window.UpdateLayout(); Pump();
                Assert.Equal(MdiWindowState.Normal, active.State);

                var maximize = Descendants<Button>(active).First(button => Equals(button.ToolTip, shell.Localization["UX_Maximize"]));
                maximize.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); window.UpdateLayout(); Pump();
                Assert.Equal(MdiWindowState.Maximized, active.State);
                Assert.Equal(host.ActualWidth, active.Width, 1); Assert.Equal(host.ActualHeight, active.Height, 1);
                Descendants<Button>(active).First(button => Equals(button.ToolTip, shell.Localization["D_RestoreAll"]))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); window.UpdateLayout(); Pump();
                Assert.Equal(MdiWindowState.Normal, active.State);
                AssertInsideMdi(host, active);

                var closing = active.Document;
                Descendants<Button>(active).First(button => Equals(button.ToolTip, shell.Localization["D_CloseTab"]))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                for (var attempt = 0; attempt < 100 && host.WindowCount != 2; attempt++) { Thread.Sleep(20); Pump(); }
                Assert.Equal(2, host.WindowCount); Assert.DoesNotContain(closing, shell.Documents.Documents);

                // Switching back releases every internal window and gives the page host its document again.
                shell.Preferences!.SetWindowMode(WindowPresentationMode.Tabbed); window.UpdateLayout(); Pump();
                Assert.Equal(0, host.WindowCount); Assert.Equal(Visibility.Collapsed, host.Visibility);
                Assert.NotNull(window.PageHost.Content); Assert.Same(shell.CurrentView, window.PageHost.Content);
            }
            finally { window.Close(); }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void AssertInsideMdi(MdiWorkspaceHost host, MdiChildWindow child)
    {
        const double tolerance = 1.0;
        Assert.True(Canvas.GetLeft(child) >= -tolerance && Canvas.GetTop(child) >= -tolerance, $"{child.Document.Title} starts outside the MDI container");
        Assert.True(Canvas.GetLeft(child) + child.Width <= host.ActualWidth + tolerance, $"{child.Document.Title} exceeds the MDI width");
        Assert.True(Canvas.GetTop(child) + child.Height <= host.ActualHeight + tolerance, $"{child.Document.Title} exceeds the MDI height");
    }

    private static void SaveMdiScreenshot(Window window, string name)
    {
        window.UpdateLayout(); Pump();
        var bitmap = new RenderTargetBitmap(1366, 768, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var output = Path.Combine(FindRoot(), ".artifacts", "viewport-acceptance", "visual"); Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, name + ".png"));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
    }
}
