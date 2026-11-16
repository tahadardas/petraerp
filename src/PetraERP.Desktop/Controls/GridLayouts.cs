using System.Windows;
using System.Windows.Controls;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Controls;

/// <summary>Per-user layout storage (server workspace preferences). Layout is presentation metadata only.</summary>
public interface IGridLayoutStore
{
    IReadOnlyList<GridPreferenceRequest> Grids { get; }
    void SetGrid(GridPreferenceRequest layout);
    /// <summary>Forgets a saved layout so the grid's designed default applies again (saved with the next save).</summary>
    void RemoveGrid(string key) { }
    Task SavePreferencesAsync();
    string Label(string key);
}

/// <summary>Layout is UI metadata only: no formulas, accounting data, filters, expressions or executable markup.</summary>
public static class GridLayouts
{
    /// <summary>Stable server-whitelisted grid key (see WorkspaceRules.GridColumns). Column keys are SortMemberPath values.</summary>
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached("Key", typeof(string), typeof(GridLayouts), new PropertyMetadata(null, Attach));
    public static readonly DependencyProperty StoreProperty = DependencyProperty.RegisterAttached("Store", typeof(IGridLayoutStore), typeof(GridLayouts), new PropertyMetadata(null, Attach));

    public static string? GetKey(DependencyObject element) { ArgumentNullException.ThrowIfNull(element); return (string?)element.GetValue(KeyProperty); }
    public static void SetKey(DependencyObject element, string? value) { ArgumentNullException.ThrowIfNull(element); element.SetValue(KeyProperty, value); }
    public static IGridLayoutStore? GetStore(DependencyObject element) { ArgumentNullException.ThrowIfNull(element); return (IGridLayoutStore?)element.GetValue(StoreProperty); }
    public static void SetStore(DependencyObject element, IGridLayoutStore? value) { ArgumentNullException.ThrowIfNull(element); element.SetValue(StoreProperty, value); }

    public static GridPreferenceRequest Capture(DataGrid grid, string key)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return new GridPreferenceRequest { Key = key, Columns = grid.Columns.Where(column => !string.IsNullOrEmpty(column.SortMemberPath)).Select(column => new GridColumnPreferenceRequest
        {
            Key = column.SortMemberPath, Width = (int)Math.Clamp(column.ActualWidth, 40, 1000), Order = column.DisplayIndex,
            Visible = column.Visibility == Visibility.Visible,
        }).ToArray() };
    }

    /// <summary>The designed column layout of each configured grid, captured before any saved layout is applied.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGrid, GridPreferenceRequest> Defaults = new();
    private static readonly List<WeakReference<DataGrid>> Configured = [];

    /// <summary>Restores a grid's designed layout (columns, widths, order, visibility). Returns false for an unknown grid.</summary>
    public static bool ResetToDefault(DataGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!Defaults.TryGetValue(grid, out var original)) return false;
        foreach (var saved in original.Columns)
        {
            var column = grid.Columns.SingleOrDefault(item => item.SortMemberPath == saved.Key);
            if (column is null) continue;
            column.Visibility = saved.Visible ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var saved in original.Columns.OrderBy(column => column.Order))
        {
            var column = grid.Columns.SingleOrDefault(item => item.SortMemberPath == saved.Key);
            if (column is not null && saved.Order < grid.Columns.Count) column.DisplayIndex = saved.Order;
        }

        foreach (var column in grid.Columns) if (DefaultWidths.TryGetValue(column, out var width)) column.Width = width.Value;
        if (grid.ContextMenu is { } menu) SyncChecks(menu, grid);
        return true;
    }

    /// <summary>Resets every open configured grid (Preferences → Reset layouts); saved layouts are removed by the caller.</summary>
    public static void ResetAllOpen()
    {
        lock (Configured)
        {
            Configured.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in Configured) if (reference.TryGetTarget(out var grid)) ResetToDefault(grid);
        }
    }

    /// <summary>Original DataGridLength per column (star and auto widths are restored exactly, not as pixels).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGridColumn, StrongBox<DataGridLength>> DefaultWidths = new();

    private sealed class StrongBox<T>(T value) where T : struct
    {
        public T Value { get; } = value;
    }

    private static void RememberDefault(DataGrid grid, string key)
    {
        if (Defaults.TryGetValue(grid, out _)) return;
        Defaults.AddOrUpdate(grid, new GridPreferenceRequest { Key = key, Columns = grid.Columns.Where(column => !string.IsNullOrEmpty(column.SortMemberPath)).Select(column => new GridColumnPreferenceRequest
        {
            Key = column.SortMemberPath, Width = 40, Order = column.DisplayIndex, Visible = column.Visibility == Visibility.Visible,
        }).ToArray() });
        foreach (var column in grid.Columns) DefaultWidths.AddOrUpdate(column, new StrongBox<DataGridLength>(column.Width));
        lock (Configured) Configured.Add(new WeakReference<DataGrid>(grid));
    }

    private static void SyncChecks(ContextMenu menu, DataGrid grid)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is DataGridColumn column) item.IsChecked = column.Visibility == Visibility.Visible;
            if (item.Items.Count > 0) foreach (var child in item.Items.OfType<MenuItem>()) if (child.Tag is DataGridColumn inner) child.IsChecked = inner.Visibility == Visibility.Visible;
        }
    }

    public static void Apply(DataGrid grid, GridPreferenceRequest? layout)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (layout is null) return;
        foreach (var saved in layout.Columns.OrderBy(column => column.Order))
        {
            var column = grid.Columns.SingleOrDefault(item => item.SortMemberPath == saved.Key);
            if (column is null || saved.Width is < 40 or > 1000 || saved.Order < 0 || saved.Order >= grid.Columns.Count) continue;
            column.Width = new DataGridLength(saved.Width); column.DisplayIndex = saved.Order;
            column.Visibility = saved.Visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Marks the menu or items this class adds, so a page's own context menu is kept and refreshed safely.</summary>
    private static readonly object ColumnMenuTag = new();

    /// <summary>
    /// Adds the column chooser. A grid without a menu gets it as its context menu; a grid whose page defines its own menu
    /// (for example the account tree) keeps that menu and gets the chooser as a "Columns" submenu at the end.
    /// </summary>
    public static void AddColumnMenu(DataGrid grid, Func<string, string>? label = null, Action? save = null, string? saveLabel = null, string? columnsLabel = null,
        Action? reset = null, string? resetLabel = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var menu = new ContextMenu { Tag = ColumnMenuTag };
        foreach (var column in grid.Columns.Where(column => !string.IsNullOrEmpty(column.SortMemberPath)))
        {
            var item = new MenuItem { Header = label?.Invoke(column.SortMemberPath) ?? column.SortMemberPath, IsCheckable = true, IsChecked = column.Visibility == Visibility.Visible, Tag = column };
            item.Click += (_, _) =>
            {
                if (!item.IsChecked && grid.Columns.Count(other => other.Visibility == Visibility.Visible) <= 1) { item.IsChecked = true; return; }
                column.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            };
            menu.Items.Add(item);
        }

        if (save is not null)
        {
            menu.Items.Add(new Separator());
            var saveItem = new MenuItem { Header = saveLabel ?? "Save layout" };
            saveItem.Click += (_, _) => save();
            menu.Items.Add(saveItem);
        }

        if (reset is not null)
        {
            var resetItem = new MenuItem { Header = resetLabel ?? "Reset layout" };
            resetItem.Click += (_, _) => reset();
            menu.Items.Add(resetItem);
        }

        if (grid.ContextMenu is { } own && !ReferenceEquals(own.Tag, ColumnMenuTag))
        {
            foreach (var previous in own.Items.OfType<Control>().Where(item => ReferenceEquals(item.Tag, ColumnMenuTag)).ToArray()) own.Items.Remove(previous);
            var columns = new MenuItem { Header = columnsLabel ?? "Columns", Tag = ColumnMenuTag };
            foreach (var item in menu.Items.OfType<Control>().ToArray()) { menu.Items.Remove(item); columns.Items.Add(item); }
            own.Items.Add(new Separator { Tag = ColumnMenuTag });
            own.Items.Add(columns);
            return;
        }

        grid.ContextMenu = menu;
    }

    private static void Attach(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not DataGrid grid) return;
        grid.Loaded -= OnLoaded;
        grid.Loaded += OnLoaded;
        if (grid.IsLoaded) Configure(grid);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Configure((DataGrid)sender);

    private static void Configure(DataGrid grid)
    {
        var key = GetKey(grid);
        var store = GetStore(grid);
        if (key is null || store is null) return;
        RememberDefault(grid, key);
        Apply(grid, store.Grids.SingleOrDefault(layout => layout.Key == key));
        AddColumnMenu(grid, column => store.Label("D_Col_" + column), async () =>
        {
            store.SetGrid(Capture(grid, key));
            await store.SavePreferencesAsync().ConfigureAwait(true);
        }, store.Label("D_SaveLayout"), store.Label("D_Columns"), async () =>
        {
            ResetToDefault(grid);
            store.RemoveGrid(key);
            await store.SavePreferencesAsync().ConfigureAwait(true);
        }, store.Label("UI_ResetLayout"));
    }
}
