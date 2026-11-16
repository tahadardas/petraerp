using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Sales;

public partial class SalesInvoiceCardView : UserControl
{
    /// <summary>Pattern field behind each line-grid column, by position (null = not a pattern caption).</summary>
    private static readonly string?[] LineFields = ["Item", "Quantity", "UnitPrice", "Notes", "NetTotal", "TaxAmount"];

    public SalesInvoiceCardView()
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
        if (e.PropertyName == nameof(SalesInvoiceCardViewModel.FieldCaptions)) ApplyCaptions();
    }

    private void ApplyCaptions() =>
        PatternGridCaptions.Apply(LinesGrid, (DataContext as SalesInvoiceCardViewModel)?.FieldCaptions, LineFields);

    /// <summary>Close: the shell's normal document close (it asks before discarding unsaved changes).</summary>
    private void CloseClicked(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is DesktopViewModel shell)
            _ = shell.CloseActiveDocumentCommand.ExecuteAsync(null);
    }
}
