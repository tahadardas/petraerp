using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Localization;
using Xunit;

namespace PetraERP.Desktop.Tests;

/// <summary>Step 2/3 of the window unification: the shared command contract and the one command bar.</summary>
public sealed class DocumentCommandBarTests
{
    [Theory]
    [InlineData(DocumentLifecycle.None, false, "New")]
    [InlineData(DocumentLifecycle.New, true, "Save,Cancel")]
    [InlineData(DocumentLifecycle.Draft, false, "New,Edit,Approve,Post,Print,Delete")]
    [InlineData(DocumentLifecycle.Draft, true, "Save,Cancel")]
    [InlineData(DocumentLifecycle.Posted, false, "New,Print,Reverse")]
    [InlineData(DocumentLifecycle.Reversed, false, "New,Print")]
    public void EveryCommandIsVisibleOnlyWhenApplicableToTheState(DocumentLifecycle state, bool editing, string expected)
    {
        var screen = new FakeScreen(state, editing) { AllCommands = true };
        Assert.Equal(expected, string.Join(",", DocumentCommandLayout.Visible(screen)));
    }

    [Fact]
    public void UnsupportedCommandsAreHiddenNotDisabled()
    {
        var screen = new FakeScreen(DocumentLifecycle.Draft, false) { AllCommands = false };
        Assert.Empty(DocumentCommandLayout.Visible(screen));
    }

    [Fact]
    public void ReverseAndDeleteAreOnlyUnderMore()
    {
        Assert.False(DocumentCommandLayout.IsPrimary(DocumentCommandId.Reverse));
        Assert.False(DocumentCommandLayout.IsPrimary(DocumentCommandId.Delete));
        Assert.All(new[] { DocumentCommandId.New, DocumentCommandId.Edit, DocumentCommandId.Save, DocumentCommandId.Cancel, DocumentCommandId.Approve, DocumentCommandId.Post, DocumentCommandId.Print },
            id => Assert.True(DocumentCommandLayout.IsPrimary(id)));
    }

    [Fact]
    public void EveryCaptionKeyExistsInBothLanguages()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "PetraERP.Desktop", "Localization"));
        var keys = Enum.GetValues<DocumentCommandId>().Select(DocumentCommandLayout.CaptionKey).Append("D_More");
        foreach (var file in new[] { "Strings.resx", "Strings.ar.resx" })
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            foreach (var key in keys) Assert.Contains($"name=\"{key}\"", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BarShowsVisibleButtonsAndMoreMenuAndFollowsStateChanges()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var screen = new FakeScreen(DocumentLifecycle.Posted, false) { AllCommands = true };
                var bar = new DocumentCommandBar { Screen = screen };
                var panel = (StackPanel)bar.Content;
                var visible = panel.Children.OfType<Button>().Where(b => b.Visibility == Visibility.Visible).Select(b => (string)b.Content).ToArray();
                Assert.Equal(["New", "Print", "More ▾"], visible); // Reverse + extra live under More
                var more = panel.Children.OfType<Button>().Last();
                Assert.Equal(["Reverse", "Journal"], more.ContextMenu!.Items.OfType<MenuItem>().Select(i => (string)i.Header).ToArray());

                screen.Set(DocumentLifecycle.Draft, true);
                visible = panel.Children.OfType<Button>().Where(b => b.Visibility == Visibility.Visible).Select(b => (string)b.Content).ToArray();
                Assert.Equal(["Save", "Cancel"], visible);
                Assert.Equal(Visibility.Collapsed, more.Visibility);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("DocumentCommandBar check failed", failure);
    }

    [Fact]
    public void WarehouseOperationsUsesTheSharedBarAndTheContract()
    {
        Assert.True(typeof(IDocumentScreen).IsAssignableFrom(typeof(InventoryOperationsViewModel)));
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "PetraERP.Desktop", "Inventory"));
        var xaml = File.ReadAllText(Path.Combine(root, "InventoryOperationsView.xaml"));
        Assert.Contains("<ux:DocumentCommandBar Screen=\"{Binding}\"/>", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PageHeader.Commands><WrapPanel", xaml, StringComparison.Ordinal);
        // Post / Reverse / Open journal / New / Edit / Save / Cancel now come only from the shared bar.
        foreach (var command in new[] { "PostCommand", "ReverseCommand", "OpenJournalCommand", "EditCommand", "SaveCommand", "CancelCommand" })
            Assert.DoesNotMatch(new Regex(@"<Button [^>]*Command=""\{Binding " + command + @"\}"""), xaml);
        Assert.Contains("Key=\"N\" Modifiers=\"Control\" Command=\"{Binding NewCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"S\" Modifiers=\"Control\" Command=\"{Binding SaveCommand}\"", xaml, StringComparison.Ordinal);
    }

    public static TheoryData<string, Type, string, string, string> Screens => new()
    {
        { @"Inventory\InventoryOperationsView.xaml", typeof(InventoryOperationsViewModel), "NewCommand", "EditCommand", "SaveCommand" },
        { @"Treasury\VouchersView.xaml", typeof(PetraERP.Desktop.Treasury.VouchersViewModel), "NewReceiptCommand", "EditCommand", "SaveCommand" },
        { @"Purchases\PurchasesView.xaml", typeof(PetraERP.Desktop.Purchases.PurchasesViewModel), "NewCommand", "EditCommand", "SaveCommand" },
        { @"Sales\SalesInvoiceCardView.xaml", typeof(PetraERP.Desktop.Sales.SalesInvoiceCardViewModel), "BeginNewCommand", "BeginEditCommand", "SaveCardCommand" },
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public void EveryDailyDocumentScreenImplementsTheContractUsesTheBarAndKeepsTheShortcuts(string xamlPath, Type viewModel, string newCommand, string editCommand, string saveCommand)
    {
        Assert.True(typeof(IDocumentScreen).IsAssignableFrom(viewModel), viewModel.Name + " must implement IDocumentScreen");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "PetraERP.Desktop"));
        var xaml = File.ReadAllText(Path.Combine(root, xamlPath));
        Assert.Contains("<ux:DocumentCommandBar", xaml, StringComparison.Ordinal);
        Assert.Contains("Screen=\"{Binding}\"", xaml, StringComparison.Ordinal);
        Assert.Contains($"Key=\"S\" Modifiers=\"Control\" Command=\"{{Binding {saveCommand}}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains($"Key=\"N\" Modifiers=\"Control\" Command=\"{{Binding {newCommand}}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains($"Key=\"E\" Modifiers=\"Control\" Command=\"{{Binding {editCommand}}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"F5\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"Escape\"", xaml, StringComparison.Ordinal);
        // The bar owns Post / Approve / Reverse / Delete: no loose button for them may remain next to it.
        foreach (var command in new[] { "PostCommand", "ApproveCommand", "ReverseCommand", "DeleteCommand", "DeleteDraftCommand" })
            Assert.DoesNotMatch(new Regex(@"<Button [^>]*Command=""\{Binding " + command + @"\}"""), xaml);
    }

    [Fact]
    public void VoucherAndPurchaseAndSalesMapEveryContractCommandToTheirOwnCommands()
    {
        // The contract only names existing commands; every non-null contract command must be the screen's real command object.
        var map = new (Type Type, string[] Names)[]
        {
            (typeof(PetraERP.Desktop.Treasury.VouchersViewModel), ["EditCommand", "SaveCommand", "CancelCommand", "DeleteCommand", "ApproveCommand", "PostCommand", "ReverseCommand", "PrintCommand", "FirstVoucherCommand", "PreviousVoucherCommand", "NextVoucherCommand", "LastVoucherCommand"]),
            (typeof(PetraERP.Desktop.Purchases.PurchasesViewModel), ["NewCommand", "EditCommand", "SaveCommand", "CancelCommand", "DeleteCommand", "ApproveCommand", "PostCommand", "ReverseCommand", "PrintCommand", "FirstCommand", "PreviousCommand", "NextCommand", "LastCommand"]),
            (typeof(PetraERP.Desktop.Sales.SalesInvoiceCardViewModel), ["BeginNewCommand", "BeginEditCommand", "SaveCardCommand", "CancelCardCommand", "DeleteDraftCommand", "ApproveCommand", "PostCommand", "ReverseCommand", "PrintCommand", "FirstCommand", "PreviousCommand", "NextCommand", "LastCommand"]),
        };
        foreach (var (type, names) in map)
            foreach (var name in names)
                Assert.NotNull(type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy));
    }

    [Theory]
    [InlineData(@"Sales\SalesInvoiceCardView.xaml")]
    [InlineData(@"Purchases\PurchasesView.xaml")]
    public void SalesAndPurchasesUseTheSharedTotalsBarAndNavigatorWithoutHardCodedValues(string xamlPath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "PetraERP.Desktop"));
        var xaml = File.ReadAllText(Path.Combine(root, xamlPath));
        Assert.Contains("<ux:DocumentTotalsBar", xaml, StringComparison.Ordinal);
        Assert.Contains("<ux:RecordNavigator", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"معتمدة\"", xaml, StringComparison.Ordinal);       // status is the bound badge, never a literal
        Assert.DoesNotContain("FallbackValue=19", xaml, StringComparison.Ordinal);        // no invented document numbers
        Assert.DoesNotContain("FallbackValue=*10", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Style=\"{StaticResource Am.ToolDropdown}\"", xaml, StringComparison.Ordinal); // one "More" menu only
    }

    [Fact]
    public void WarehouseOperationsHidesTheListWhileADocumentIsOpenAndCollapsesTheOptionalCostCenter()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "PetraERP.Desktop", "Inventory"));
        var xaml = File.ReadAllText(Path.Combine(root, "InventoryOperationsView.xaml"));
        Assert.Contains("Visibility=\"{Binding ShowDocumentList, Converter={StaticResource Visible}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding BackToListCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"{Binding IsCostCenterRequired, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FallbackValue=Collapsed", xaml, StringComparison.Ordinal);
    }

    private sealed class FakeScreen : IDocumentScreen
    {
        private static readonly ICommand Noop = new RelayCommand(() => { });
        private readonly FakeLocalization localization = new();
        public FakeScreen(DocumentLifecycle lifecycle, bool editing) { Lifecycle = lifecycle; IsEditing = editing; }
        public bool AllCommands { get; init; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public ILocalizationService Localization => localization;
        public DocumentLifecycle Lifecycle { get; private set; }
        public bool IsEditing { get; private set; }
        public void Set(DocumentLifecycle lifecycle, bool editing)
        {
            Lifecycle = lifecycle; IsEditing = editing;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lifecycle)));
        }
        private ICommand? C => AllCommands ? Noop : null;
        public ICommand? NewCommand => C; public ICommand? EditCommand => C; public ICommand? SaveCommand => C; public ICommand? CancelCommand => C;
        public ICommand? DeleteCommand => C; public ICommand? ApproveCommand => C; public ICommand? PostCommand => C; public ICommand? ReverseCommand => C;
        public ICommand? PrintCommand => C; public ICommand? FirstCommand => null; public ICommand? PreviousCommand => null;
        public ICommand? NextCommand => null; public ICommand? LastCommand => null;
        public IReadOnlyList<DocumentExtraCommand> MoreCommands => AllCommands ? [new("Journal", Noop)] : [];
    }

    private sealed class FakeLocalization : ILocalizationService
    {
        public string this[string key] => key switch { "D_New" => "New", "D_Edit" => "Edit", "D_Save" => "Save", "D_Cancel" => "Cancel", "D_Approve" => "Approve", "D_Post" => "Post",
            "D_Print" => "Print", "D_Reverse" => "Reverse", "D_Delete" => "Delete", "D_More" => "More", _ => key };
        public string Language => "en";
        public bool IsRightToLeft => false;
        public System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.InvariantCulture;
        public event EventHandler? LanguageChanged { add { } remove { } }
        public void SetLanguage(string language) { }
    }
}
