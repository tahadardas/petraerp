using System.ComponentModel;
using System.Windows;
using PetraERP.Desktop.Patterns;

namespace PetraERP.Desktop.Purchases;

public partial class PurchasesView
{
    /// <summary>Pattern field behind each line-grid column, by position (null = not a pattern caption).</summary>
    private static readonly string?[] LineFields =
        ["Item", "Unit", "Quantity", "UnitCost", "Notes", "NetTotal", "TaxAmount", "DiscountAmount"];

    public PurchasesView()
    {
        InitializeComponent();
        DataContextChanged += OnContextChanged;
    }

    private void OnContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= OnViewModelChanged;
        if (e.NewValue is INotifyPropertyChanged current) current.PropertyChanged += OnViewModelChanged;
        ApplyCaptions();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PurchasesViewModel.FieldCaptions)) ApplyCaptions();
    }

    private void ApplyCaptions() =>
        PatternGridCaptions.Apply(PurchaseLinesGrid, (DataContext as PurchasesViewModel)?.FieldCaptions, LineFields);

    private void CloseClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is PurchasesViewModel vm)
        {
            vm.WorkspaceTab = 0;
        }
    }
}
