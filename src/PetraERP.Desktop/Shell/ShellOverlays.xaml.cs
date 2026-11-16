using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Shell;

/// <summary>Visual mechanics only: focus handling and keyboard routing for the palette and dialog overlays.</summary>
public partial class ShellOverlays : UserControl
{
    private IInputElement? priorFocus;
    private DesktopViewModel? model;

    public ShellOverlays()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (model is not null) model.Palette.PropertyChanged -= PaletteChanged;
            model = args.NewValue as DesktopViewModel;
            if (model is not null) model.Palette.PropertyChanged += PaletteChanged;
        };
    }

    private void PaletteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CommandPaletteViewModel.IsOpen) || model is null) return;
        if (model.Palette.IsOpen)
        {
            priorFocus = Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(() => { PaletteBox.Focus(); PaletteBox.SelectAll(); });
        }
        else
        {
            priorFocus?.Focus();
        }
    }

    private void OverlayKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && model?.Dialogs?.Current is { } dialog) { dialog.CancelCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Escape && model?.Palette.IsOpen == true) { model.Palette.IsOpen = false; e.Handled = true; }
        else if (e.Key == Key.Escape && model?.Notifications.IsPanelOpen == true) { model.Notifications.IsPanelOpen = false; e.Handled = true; }
    }
    private void PaletteKeyDown(object sender, KeyEventArgs e)
    {
        if (model is null) return;
        switch (e.Key)
        {
            case Key.Down: model.Palette.MoveCommand.Execute("down"); PaletteList.ScrollIntoView(model.Palette.Selected); e.Handled = true; break;
            case Key.Up: model.Palette.MoveCommand.Execute("up"); PaletteList.ScrollIntoView(model.Palette.Selected); e.Handled = true; break;
            case Key.Enter: _ = model.Palette.InvokeAsync(model.Palette.Selected); e.Handled = true; break;
            case Key.Escape: model.Palette.IsOpen = false; e.Handled = true; break;
        }
    }

    private void PaletteInvoked(object sender, MouseButtonEventArgs e)
    {
        if (model is not null) _ = model.Palette.InvokeAsync(model.Palette.Selected);
    }

    private void PaletteScrimClicked(object sender, MouseButtonEventArgs e)
    {
        if (model is not null) model.Palette.IsOpen = false;
    }

    private void StopClick(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
