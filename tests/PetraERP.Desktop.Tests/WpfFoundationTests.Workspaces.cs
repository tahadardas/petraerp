using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PetraERP.Desktop.Theming;
using PetraERP.Desktop.Workspaces;

namespace PetraERP.Desktop.Tests;

public sealed partial class WpfFoundationTests
{
    /// <summary>The dialog owns its selection VM, but visibility must follow the ancestor shell's selection state.</summary>
    private static void VerifyWorkspaceSelectionVisibility()
    {
        var fixture = new ShellFixture();
        using var shell = fixture.Create(new AdministrationFixture());
        var overlay = new PetraERP.Desktop.Shell.ShellOverlays { DataContext = shell };
        var window = new Window
        {
            Content = overlay, DataContext = shell, Width = 1000, Height = 720, ShowActivated = false,
            Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.Show();
        try
        {
            window.UpdateLayout(); Pump();
            var selection = Assert.Single(Descendants<WorkspaceSelectionView>(overlay));
            Assert.Same(shell.WorkspaceSelection, selection.DataContext);
            Assert.False(shell.ShowWorkspaceSelection);
            Assert.Equal(Visibility.Collapsed, selection.Visibility);
            Assert.False(selection.IsVisible);
            shell.ShowWorkspaceSelection = true;
            window.UpdateLayout(); Pump();
            Assert.Same(shell.WorkspaceSelection, selection.DataContext);
            Assert.Equal(Visibility.Visible, selection.Visibility);
            Assert.True(selection.IsVisible);
            shell.ShowWorkspaceSelection = false;
            window.UpdateLayout(); Pump();
            Assert.Equal(Visibility.Collapsed, selection.Visibility);
            Assert.False(selection.IsVisible);
            Assert.Same(shell.WorkspaceSelection, selection.DataContext);
            Assert.Null(System.Windows.Data.BindingOperations.GetBindingExpression(selection, UIElement.VisibilityProperty)!.ValidationError);
        }
        finally { window.Close(); }
    }

    /// <summary>Company files and each wizard step render in both themes (Arabic RTL); images go to .artifacts/viewport-acceptance/workspaces.</summary>
    private static void VerifyWorkspaceWizard(WpfThemeService themes)
    {
        VerifyWorkspaceSelectionVisibility();
        var output = Path.Combine(FindRoot(), ".artifacts", "viewport-acceptance", "workspaces"); Directory.CreateDirectory(output);
        foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
        {
            themes.Apply(theme);
            var model = WorkspaceWizardTests.Model(new WorkspaceWizardTests.Client(), out _);
            var view = new WorkspaceSelectionView { DataContext = model, FlowDirection = FlowDirection.RightToLeft };
            var window = new Window { Content = view, Width = 1366, Height = 768, ShowActivated = false, Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual };
            window.Show();
            try
            {
                _ = model.LoadWorkspacesAsync(); Pump();
                Assert.Single(model.FilteredWorkspaces);
                Save(window, Path.Combine(output, $"files-{theme}.png"));
                _ = model.StartCreateCompanyCommand.ExecuteAsync(null); Pump();
                Assert.True(model.ShowCompanyInfo);
                model.NewCompanyCode = "PETRA2"; model.NewCompanyNameArabic = "شركة البتراء"; model.NewCompanyNameEnglish = "Petra"; Pump();
                Save(window, Path.Combine(output, $"wizard-1-{theme}.png"));

                // Step 1: SQL Database
                model.NextCommand.Execute(null); Pump();
                Assert.True(model.ShowCompanyDatabase);
                Save(window, Path.Combine(output, $"wizard-2-db-{theme}.png"));

                // Step 2: Currency
                model.NextCommand.Execute(null); model.NewCompanyCurrency = model.Currencies[0]; Pump();
                Assert.True(model.ShowCompanyCurrency);
                Save(window, Path.Combine(output, $"wizard-3-currency-{theme}.png"));

                // Step 3: Admin confirmation
                model.NextCommand.Execute(null); model.AdministratorPassword = "AdminPassword123!"; Pump();
                Assert.True(model.ShowAdministratorConfirmation);
                Save(window, Path.Combine(output, $"wizard-4-admin-{theme}.png"));

                // Step 4: Account template
                model.NextCommand.Execute(null); Pump();
                Assert.True(model.ShowAccountTemplate);
                Save(window, Path.Combine(output, $"wizard-5-template-{theme}.png"));

                // Step 5: Fiscal year
                model.NextCommand.Execute(null); Pump();
                Assert.True(model.ShowCompanyYear);
                Save(window, Path.Combine(output, $"wizard-6-year-{theme}.png"));

                // Step 6: Review
                model.NextCommand.Execute(null); Pump();
                Assert.True(model.ShowReview);
                Save(window, Path.Combine(output, $"wizard-7-review-{theme}.png"));
            }
            finally { window.Close(); }
        }
        themes.Apply(ThemePreference.Light);

        static void Save(Window window, string path)
        {
            window.UpdateLayout(); Pump();
            var bitmap = new RenderTargetBitmap(1366, 768, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            using var stream = File.Create(path);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
        }
    }
}
