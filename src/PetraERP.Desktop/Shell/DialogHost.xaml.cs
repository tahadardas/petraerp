using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Shell;

/// <summary>Visual mechanics only: focuses Cancel when a request opens and maps Escape to Cancel.</summary>
public partial class DialogHost : UserControl
{
    private ShellDialogService? dialogs;

    public DialogHost()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (dialogs is not null) dialogs.PropertyChanged -= DialogChanged;
            dialogs = (args.NewValue as DesktopViewModel)?.Dialogs;
            if (dialogs is not null) dialogs.PropertyChanged += DialogChanged;
        };
        Unloaded += (_, _) => { if (dialogs is not null) dialogs.PropertyChanged -= DialogChanged; dialogs = null; };
    }

    private void DialogChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellDialogService.Current) && dialogs?.Current is not null && IsVisible)
            Dispatcher.BeginInvoke(() => DialogCancel.Focus());
    }

    private void DialogKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && dialogs?.Current is { } dialog) { dialog.CancelCommand.Execute(null); e.Handled = true; }
    }
}
