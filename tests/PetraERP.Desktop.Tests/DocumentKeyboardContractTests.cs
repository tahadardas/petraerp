using System.IO;
using System.Text.RegularExpressions;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// Keyboard and input rules the UI spec asks of every card, invoice and voucher window, checked on the XAML itself:
/// Ctrl+S saves, F5 refreshes, Esc leaves an edit (the command asks before discarding changes), and no field takes a raw identifier.
/// </summary>
public sealed class DocumentKeyboardContractTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PetraERP.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "PetraERP.Desktop");
    }

    public static TheoryData<string, string> EditableWindows => new()
    {
        { "Sales/SalesInvoiceCardView.xaml", "CancelCardCommand" },
        { "Parties/PartyCardView.xaml", "CancelCardCommand" },
        { "Inventory/ItemCardView.xaml", "CancelCardCommand" },
        { "Inventory/ItemGroupCardView.xaml", "CancelCardCommand" },
        { "Patterns/PatternCardView.xaml", "CancelCardCommand" },
        { "Accounting/AccountCardView.xaml", "CancelCardCommand" },
        { "Treasury/VouchersView.xaml", "CancelCommand" },
        { "Inventory/InventoryOperationsView.xaml", "CancelCommand" },
    };

    [Theory]
    [MemberData(nameof(EditableWindows))]
    public void EscapeLeavesAnEditThroughTheWindowsOwnCancelCommand(string view, string command)
    {
        var xaml = File.ReadAllText(Path.Combine(Root(), view));
        Assert.Matches($"<KeyBinding Key=\"Escape\" Command=\"\\{{Binding {command}\\}}\"/>", xaml);
    }

    [Theory]
    [InlineData("Sales/SalesInvoiceCardView.xaml")]
    [InlineData("Parties/PartyCardView.xaml")]
    [InlineData("Inventory/ItemCardView.xaml")]
    [InlineData("Treasury/VouchersView.xaml")]
    [InlineData("Purchases/PurchasesView.xaml")]
    public void WindowsRefreshWithF5AndSaveWithControlS(string view)
    {
        var xaml = File.ReadAllText(Path.Combine(Root(), view));
        Assert.Contains("Key=\"F5\"", xaml, StringComparison.Ordinal);
        Assert.Matches("Key=\"S\" Modifiers=\"Control\"", xaml);
    }

    [Fact]
    public void NoTextBoxTakesARawIdentifier()
    {
        var pattern = new Regex("<TextBox[^>]*Text=\"\\{Binding [A-Za-z.]*(Id|Guid)[ ,}]", RegexOptions.None, TimeSpan.FromSeconds(5));
        foreach (var file in Directory.EnumerateFiles(Root(), "*.xaml", SearchOption.AllDirectories).Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            Assert.False(pattern.IsMatch(File.ReadAllText(file)), $"{Path.GetFileName(file)} has a text box bound to an identifier");
    }
}
