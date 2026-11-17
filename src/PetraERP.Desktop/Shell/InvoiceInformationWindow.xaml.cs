using System.Windows;
using System.Windows.Input;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// Al-Ameen 10 Independent Invoice Information Window:
/// Displays real-time profitability, customer/supplier credit health, audit lifecycle, and operational attribution.
/// </summary>
public partial class InvoiceInformationWindow : Window
{
    public InvoiceInformationWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (sender, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
