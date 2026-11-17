using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Patterns;

/// <summary>Opens the patterns page already narrowed to one family of documents (all, invoices, vouchers, cash book or stock movements).</summary>
public sealed record OpenPatternsRequest(string FilterTab);

/// <summary>One family of document patterns offered by the pattern manager.</summary>
public sealed record PatternCategory(string Id, string Title, string Description, string FilterTab);

/// <summary>
/// Al-Ameen "إدارة الأنماط": a list of document families; the chosen family explains what its patterns control and opens the pattern list
/// narrowed to that family. Only families Petra really has are offered (invoices and their returns, vouchers and journals, cash-book
/// vouchers, stock movements); the patterns themselves are still created and published through the existing pattern pages.
/// </summary>
public sealed partial class PatternManagerViewModel : PageViewModel
{
    private readonly ShellNavigator navigator;

    [ObservableProperty] private PatternCategory? selectedCategory;

    public PatternManagerViewModel(ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization) : base(operation, localization)
    {
        this.navigator = navigator;
        EditCommand = new AsyncRelayCommand(EditAsync, () => HasContext && SelectedCategory is not null);
        NewCommand = new AsyncRelayCommand(() =>
        {
            var kind = SelectedCategory?.Id switch
            {
                "invoices" => BusinessOperationKind.SalesInvoice,
                "vouchers" => BusinessOperationKind.ManualJournal,
                "cash" => BusinessOperationKind.CashJournal,
                "inventory" => BusinessOperationKind.InventoryReceipt,
                "assembly" => BusinessOperationKind.Assembly,
                _ => (BusinessOperationKind?)null,
            };
            return navigator.NavigateAsync("patterns", new NewPatternCardRequest(InitialOperationKind: kind));
        }, () => HasContext && Can(DocumentPatternsViewModel.ManagePermission));
        BuildCategories();
    }

    protected override string RequiredPermission => DocumentPatternsViewModel.ViewPermission;
    public ObservableCollection<PatternCategory> Categories { get; } = [];
    public AsyncRelayCommand EditCommand { get; }
    public AsyncRelayCommand NewCommand { get; }
    public bool CanCreate => Can(DocumentPatternsViewModel.ManagePermission);

    public void Localize()
    {
        var selected = SelectedCategory?.Id;
        BuildCategories();
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == selected) ?? Categories.FirstOrDefault();
    }

    private void BuildCategories()
    {
        Categories.Clear();
        Categories.Add(new("invoices", Localization["D_PM_Invoices"], Localization["D_PM_InvoicesHelp"], "invoices"));
        Categories.Add(new("vouchers", Localization["D_PM_Vouchers"], Localization["D_PM_VouchersHelp"], "vouchers"));
        Categories.Add(new("cash", Localization["D_PM_Cash"], Localization["D_PM_CashHelp"], "cash"));
        Categories.Add(new("inventory", Localization["D_PM_Inventory"], Localization["D_PM_InventoryHelp"], "inventory"));
        Categories.Add(new("assembly", Localization["D_PM_Assembly"], Localization["D_PM_AssemblyHelp"], "assembly"));
    }

    private Task EditAsync() =>
        SelectedCategory is { } category ? navigator.NavigateAsync("patterns", new OpenPatternsRequest(category.FilterTab)) : Task.CompletedTask;

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        SelectedCategory ??= Categories.FirstOrDefault();
        OnPropertyChanged(nameof(CanCreate));
        NotifyCommands();
        return Task.CompletedTask;
    }

    partial void OnSelectedCategoryChanged(PatternCategory? value) => EditCommand?.NotifyCanExecuteChanged();

    protected override void ClearPage() => SelectedCategory = Categories.FirstOrDefault();

    protected override void NotifyCommands()
    {
        EditCommand?.NotifyCanExecuteChanged(); NewCommand?.NotifyCanExecuteChanged();
    }
}
