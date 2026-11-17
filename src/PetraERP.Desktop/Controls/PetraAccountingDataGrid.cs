using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// The design system's accounting grid: the dense, virtualized DataGrid style plus shared behaviors:
/// <list type="bullet">
/// <item>Excel-style clipboard and undo (<see cref="GridClipboard"/>), row-under-pointer context actions (<see cref="GridRowContext"/>).</item>
/// <item><see cref="EntryMode"/>: Al-Ameen high-speed entry keys (<see cref="GridEntry"/>: Enter/Shift+Enter, Tab/Shift+Tab, F2 lookup, F4/Ctrl+D fill, Esc).</item>
/// <item><see cref="ShowRowNumbers"/>: view-ordered row numbers (<see cref="GridRowNumbers"/>).</item>
/// <item><see cref="AutoGrowCommand"/>: automatic row growth when the user reaches the end of the entry buffer.</item>
/// </list>
/// </summary>
public sealed class PetraAccountingDataGrid : DataGrid
{
    public static readonly DependencyProperty EntryModeProperty = DependencyProperty.Register(
        nameof(EntryMode), typeof(bool), typeof(PetraAccountingDataGrid),
        new PropertyMetadata(false, static (d, e) => GridEntry.SetEnabled(d, (bool)e.NewValue)));

    public static readonly DependencyProperty ShowRowNumbersProperty = DependencyProperty.Register(
        nameof(ShowRowNumbers), typeof(bool), typeof(PetraAccountingDataGrid),
        new PropertyMetadata(false, static (d, e) => GridRowNumbers.SetEnabled(d, (bool)e.NewValue)));

    public static readonly DependencyProperty AutoGrowCommandProperty = DependencyProperty.Register(
        nameof(AutoGrowCommand), typeof(ICommand), typeof(PetraAccountingDataGrid),
        new PropertyMetadata(null));

    public PetraAccountingDataGrid()
    {
        GridClipboard.SetEnabled(this, true);
        GridRowContext.SetEnabled(this, true);

        // Row height comes from the active density token; local values would make theme switching ineffective.
        EnableRowVirtualization = true;
        EnableColumnVirtualization = false;
        VirtualizingPanel.SetIsVirtualizing(this, true);
        VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Standard);
        ScrollViewer.SetCanContentScroll(this, true);
        CanUserAddRows = false;
        AutoGenerateColumns = false;
        CanUserSortColumns = false;
        SelectionUnit = DataGridSelectionUnit.CellOrRowHeader;
        BorderThickness = new Thickness(1);
    }

    public bool EntryMode
    {
        get => (bool)GetValue(EntryModeProperty);
        set => SetValue(EntryModeProperty, value);
    }

    public bool ShowRowNumbers
    {
        get => (bool)GetValue(ShowRowNumbersProperty);
        set => SetValue(ShowRowNumbersProperty, value);
    }

    public ICommand? AutoGrowCommand
    {
        get => (ICommand?)GetValue(AutoGrowCommandProperty);
        set => SetValue(AutoGrowCommandProperty, value);
    }

    public Action? AutoGrowAction { get; set; }

    /// <summary>
    /// Invoked when keyboard navigation reaches the last row and column of the entry buffer.
    /// Appends additional blank entry rows so keyboard data entry continues uninterrupted.
    /// </summary>
    public void RequestAutoGrow()
    {
        AutoGrowAction?.Invoke();
        if (AutoGrowCommand is { } cmd && cmd.CanExecute(null))
        {
            cmd.Execute(null);
        }
    }
}
