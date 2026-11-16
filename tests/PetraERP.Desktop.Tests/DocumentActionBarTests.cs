using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Purchases;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;
using Xunit;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// The invoice and voucher windows are built from many buttons and menu items bound by name. A binding to a command the view model
/// does not have fails silently at run time (the button looks alive and does nothing), so every command binding in these windows is
/// checked against the real view model, and every dropdown button must open its menu on an ordinary click.
/// </summary>
public sealed class DocumentActionBarTests
{
    private static readonly Regex CommandBinding = new(@"Command=""\{Binding (?<name>[A-Za-z_][A-Za-z0-9_]*)\}""", RegexOptions.Compiled);

    public static TheoryData<string, Type> Windows => new()
    {
        { @"Sales\SalesInvoiceCardView.xaml", typeof(SalesInvoiceCardViewModel) },
        { @"Purchases\PurchasesView.xaml", typeof(PurchasesViewModel) },
        { @"Shell\DailyVoucherView.xaml", typeof(DailyVoucherViewModel) },
    };

    [Theory]
    [MemberData(nameof(Windows))]
    public void EveryCommandBindingResolvesOnTheViewModel(string xamlPath, Type viewModel)
    {
        var xaml = File.ReadAllText(Path.Combine(DesktopSourceRoot(), xamlPath));
        var missing = CommandBinding.Matches(xaml)
            .Select(m => m.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(name => viewModel.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy) is null)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(missing.Length == 0, $"{xamlPath}: commands without a view-model property: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData(@"Sales\SalesInvoiceCardView.xaml")]
    [InlineData(@"Purchases\PurchasesView.xaml")]
    public void DropdownButtonStyleOpensItsMenuOnClick(string xamlPath)
    {
        var root = DesktopSourceRoot();
        var viewXaml = File.ReadAllText(Path.Combine(root, xamlPath));
        Assert.DoesNotContain(@"x:Key=""Am.ToolDropdown""", viewXaml, StringComparison.Ordinal);

        // The shared Am.* styles live in Themes/Components.xaml (single definition).
        var xaml = File.ReadAllText(Path.Combine(root, @"Themes\Components.xaml"));
        var style = Regex.Match(xaml, @"x:Key=""Am\.ToolDropdown""(?<body>.*?)</Style>", RegexOptions.Singleline);
        Assert.True(style.Success, "Am.ToolDropdown style not found in Themes/Components.xaml");
        Assert.Contains("ux:MenuButton.OpensMenu", style.Groups["body"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void MenuButtonOpensTheContextMenuOnLeftClickAndTogglesItClosed()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var item = new MenuItem { Header = "x" };
                var menu = new ContextMenu();
                menu.Items.Add(item);
                var button = new Button { ContextMenu = menu, DataContext = "context" };
                MenuButton.SetOpensMenu(button, true);

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(menu.IsOpen);
                Assert.Same(button, menu.PlacementTarget);
                Assert.Equal("context", menu.DataContext);

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(menu.IsOpen);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("MenuButton check failed", failure);
    }

    private static string DesktopSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "PetraERP.Desktop");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("src/PetraERP.Desktop was not found above the test output directory.");
    }
}
