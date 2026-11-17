using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Reporting;

/// <summary>
/// Virtualized presentation of one immutable report section. Sorting only changes row order; it never
/// recalculates or replaces server-authoritative measures held by <see cref="ReportDocument"/>.
/// </summary>
public partial class PetraInteractiveReportGrid : UserControl
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(ReportDocument), typeof(PetraInteractiveReportGrid),
        new FrameworkPropertyMetadata(null, DocumentChanged));

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> sourceRows = [];
    private IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = [];
    private readonly Dictionary<DataGridColumn, ReportColumn> columnDefinitions = [];
    private readonly List<string> groupingKeys = [];
    private readonly List<PetraConditionalFormatRule> conditionalRules = [];

    public PetraInteractiveReportGrid()
    {
        InitializeComponent();
        Results.LoadingRow += ResultsLoadingRow;
        // Report display options (font, row height, grid lines, alternating rows) are followed while the grid is loaded.
        Loaded += (_, _) =>
        {
            ReportDisplayOptionsHub.Changed -= OnDisplayOptionsChanged;
            ReportDisplayOptionsHub.Changed += OnDisplayOptionsChanged;
            ReportGridAppearance.Apply(Results, ReportDisplayOptionsHub.Current);
        };
        Unloaded += (_, _) => ReportDisplayOptionsHub.Changed -= OnDisplayOptionsChanged;
    }

    private void OnDisplayOptionsChanged(object? sender, EventArgs args) => ReportGridAppearance.Apply(Results, ReportDisplayOptionsHub.Current);

    public ReportDocument? Document
    {
        get => (ReportDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public int SelectedIndex => Results.SelectedIndex;
    public int SelectedDocumentRowIndex
    {
        get
        {
            if (Document is null || Results.SelectedItem is not IReadOnlyDictionary<string, object?> selected) return -1;
            for (var index = 0; index < Document.Rows.Count; index++)
                if (ReferenceEquals(Document.Rows[index], selected)) return index;
            return -1;
        }
    }
    public int VisibleRowCount => rows.Count;
    public event EventHandler? RowActivated;
    public event EventHandler? SelectionChanged;
    public event EventHandler? VisibleRowsChanged;
    internal event EventHandler<ReportColumnFilterRequest>? HeaderFilterRequested;
    internal event EventHandler<ReportColumnAutoFilterRequest>? AutoFilterRequested;
    /// <summary>The user chose a value set for one column. The viewer owns the filter state and re-applies the whole model.</summary>
    internal event EventHandler<PetraReportFilterCondition?>? HeaderFilterConditionApplied;
    /// <summary>The user cleared (or selected everything in) ONE column's header filter; other filters must stay.</summary>
    internal event EventHandler<string>? HeaderFilterCleared;
    /// <summary>Raw values currently selected by the viewer's header filter for a column, or null when that column is unfiltered.</summary>
    internal Func<string, IReadOnlyCollection<string>?>? HeaderSelectionProvider { get; set; }

    internal DataGrid DataGrid => Results;

    /// <summary>True while the grid shows its "no rows" message instead of an unexplained empty box.</summary>
    internal bool ShowsEmptyState => EmptyState.Visibility == Visibility.Visible;

    internal IReadOnlyList<ReportColumn> AvailableColumns => Document?.Columns ?? [];
    internal IReadOnlyList<string> GroupingKeys => groupingKeys.AsReadOnly();
    internal IReadOnlyList<IReadOnlyDictionary<string, object?>> VisibleRowsSnapshot => rows;
    internal IReadOnlyList<IReadOnlyDictionary<string, object?>> SelectedRowsSnapshot => Results.SelectedItems
        .OfType<IReadOnlyDictionary<string, object?>>().ToArray();

    public void ScrollToSelected()
    {
        if (Results.SelectedItem is { } item) Results.ScrollIntoView(item);
    }

    internal Task<IReadOnlyList<PetraReportSearchMatch>> SearchAsync(PetraReportSearchOptions options, CancellationToken cancellationToken)
    {
        if (Document is not { } document) return Task.FromResult<IReadOnlyList<PetraReportSearchMatch>>([]);
        var currentRows = rows;
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = PetraReportFilterEngine.Search(document, currentRows, options);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }, cancellationToken);
    }

    // ---- Unified, generation-guarded view pipeline: filter -> sort -> bind ---------------------------------------
    // Filter and sort are two inputs of ONE recomputation. A newer request supersedes an older one and carries
    // the other input with it, so concurrent filter/sort requests can never discard each other's state.
    private PetraReportFilterNode? committedFilter;
    private PetraReportFilterNode? pendingFilter;
    private ViewSort? committedSort;
    private ViewSort? pendingSort;
    private IReadOnlyList<IReadOnlyDictionary<string, object?>> filteredRows = [];
    private int viewGeneration;
    private CancellationTokenSource? viewWork;

    private readonly record struct ViewSort(string Key, ListSortDirection Direction);

    /// <summary>Monotonic token bumped by every document load, filter/sort request and cancellation; a result is applied only while its token is current.</summary>
    internal int ViewGeneration => viewGeneration;

    /// <summary>A background filter/sort failed (not cancelled); the viewer reports it instead of letting an async-void handler crash the app.</summary>
    internal event EventHandler<Exception>? ViewOperationFailed;

    /// <summary>Rejects every in-flight filter/sort result (window closing, report section changing).</summary>
    internal void CancelPendingViewWork()
    {
        viewWork?.Cancel();
        viewGeneration++;
        pendingFilter = committedFilter;
        pendingSort = committedSort;
    }

    private ReportColumn? DefinitionFor(ViewSort? sort) =>
        sort is { } value ? columnDefinitions.Values.FirstOrDefault(column => string.Equals(column.Key, value.Key, StringComparison.Ordinal)) : null;

    internal PetraReportFilterValidation ApplyFilter(PetraReportFilterNode? filter)
    {
        if (Document is not { } document) return PetraReportFilterValidation.Success;
        var validation = PetraReportFilterEngine.Validate(document, filter);
        if (!validation.IsValid) return validation;
        CancelPendingViewWork();
        var filtered = PetraReportFilterEngine.Apply(document, sourceRows, filter);
        var sort = committedSort;
        var column = DefinitionFor(sort);
        var ordered = column is null ? filtered : PetraReportRowSorter.Sort(filtered, column, sort!.Value.Direction, CancellationToken.None);
        CommitView(filter, sort, filtered, ordered);
        return validation;
    }

    internal Task<PetraReportFilterValidation> ApplyFilterAsync(PetraReportFilterNode? filter, CancellationToken cancellationToken) =>
        RefreshViewAsync(changeFilter: true, filter, changeSort: false, null, cancellationToken);

    /// <summary>Sorts the CURRENT filtered rows by raw values on a worker thread. Returns false when the column is unknown.</summary>
    internal async Task<bool> ApplySortAsync(string key, ListSortDirection direction, CancellationToken cancellationToken)
    {
        var column = Results.Columns.FirstOrDefault(candidate => string.Equals(candidate.SortMemberPath, key, StringComparison.Ordinal));
        if (column is null || !columnDefinitions.ContainsKey(column)) return false;
        await RefreshViewAsync(changeFilter: false, null, changeSort: true, new ViewSort(key, direction), cancellationToken).ConfigureAwait(true);
        return true;
    }

    private async Task<PetraReportFilterValidation> RefreshViewAsync(
        bool changeFilter, PetraReportFilterNode? filter, bool changeSort, ViewSort? sort, CancellationToken cancellationToken)
    {
        if (Document is not { } document) return PetraReportFilterValidation.Success;
        var validation = PetraReportFilterValidation.Success;
        if (changeFilter)
        {
            validation = PetraReportFilterEngine.Validate(document, filter);
            if (!validation.IsValid) return validation;
        }
        viewWork?.Cancel();
        var generation = ++viewGeneration;
        if (changeFilter) pendingFilter = filter;
        if (changeSort) pendingSort = sort;
        var runFilter = pendingFilter;
        var runSort = pendingSort;
        var sortColumn = DefinitionFor(runSort);
        // A sort-only request reuses the already filtered rows instead of filtering 100k rows again.
        var cached = ReferenceEquals(runFilter, committedFilter) ? filteredRows : null;
        var source = sourceRows;
        var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        viewWork = work;
        var token = work.Token;
        try
        {
            var (filtered, ordered) = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var matching = cached ?? PetraReportFilterEngine.Apply(document, source, runFilter);
                token.ThrowIfCancellationRequested();
                var sorted = sortColumn is null ? matching : PetraReportRowSorter.Sort(matching, sortColumn, runSort!.Value.Direction, token);
                return (matching, sorted);
            }, token).ConfigureAwait(true);
            // Verify the report reference and generation BEFORE touching rows or the DataGrid.
            if (token.IsCancellationRequested || generation != viewGeneration || !ReferenceEquals(Document, document))
                throw new OperationCanceledException(token);
            CommitView(runFilter, runSort, filtered, ordered);
            return validation;
        }
        catch (Exception) when (generation == viewGeneration)
        {
            pendingFilter = committedFilter;
            pendingSort = committedSort;
            throw;
        }
        finally
        {
            if (ReferenceEquals(viewWork, work)) viewWork = null;
            work.Dispose();
        }
    }

    /// <summary>The only place that mutates the visible rows after a filter/sort. Selection is resolved against the NEW rows by reference.</summary>
    private void CommitView(
        PetraReportFilterNode? filter,
        ViewSort? sort,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> filtered,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> ordered)
    {
        var selected = Results.SelectedItem as IReadOnlyDictionary<string, object?>;
        committedFilter = pendingFilter = filter;
        committedSort = pendingSort = sort;
        filteredRows = filtered;
        rows = ordered;
        foreach (var column in Results.Columns)
            column.SortDirection = sort is { } active && string.Equals(column.SortMemberPath, active.Key, StringComparison.Ordinal)
                ? active.Direction
                : null;
        BindRows();
        if (selected is not null && ordered.Any(row => ReferenceEquals(row, selected)))
        {
            Results.SelectedItem = selected;
            Results.ScrollIntoView(selected);
        }
        VisibleRowsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SelectMatch(PetraReportSearchMatch match)
    {
        Results.SelectedItem = match.Row;
        var column = Results.Columns.FirstOrDefault(candidate =>
            string.Equals(candidate.SortMemberPath, match.ColumnKey, StringComparison.Ordinal));
        if (column is not null) Results.CurrentCell = new DataGridCellInfo(match.Row, column);
        Results.ScrollIntoView(match.Row, column);
        Results.Focus();
    }

    internal bool SetColumnVisibility(string key, bool visible)
    {
        var column = Results.Columns.FirstOrDefault(candidate => string.Equals(candidate.SortMemberPath, key, StringComparison.Ordinal));
        if (column is null) return false;
        if (!visible && Results.Columns.Count(candidate => candidate.Visibility == Visibility.Visible) <= 1) return false;
        column.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        return true;
    }

    internal bool MoveColumn(string key, int offset)
    {
        var column = Results.Columns.FirstOrDefault(candidate => string.Equals(candidate.SortMemberPath, key, StringComparison.Ordinal));
        if (column is null) return false;
        var target = Math.Clamp(column.DisplayIndex + offset, 0, Results.Columns.Count - 1);
        if (target == column.DisplayIndex) return false;
        column.DisplayIndex = target;
        return true;
    }

    internal bool FreezeThrough(string key)
    {
        var column = Results.Columns.FirstOrDefault(candidate => string.Equals(candidate.SortMemberPath, key, StringComparison.Ordinal));
        if (column is null || column.Visibility != Visibility.Visible) return false;
        Results.FrozenColumnCount = column.DisplayIndex + 1;
        return true;
    }

    internal void ResetColumnLayout()
    {
        Results.FrozenColumnCount = 0;
        for (var index = 0; index < Results.Columns.Count; index++)
        {
            var column = Results.Columns[index];
            column.Visibility = Visibility.Visible;
            column.DisplayIndex = index;
            if (columnDefinitions.TryGetValue(column, out var definition))
                column.Width = definition.Kind == ReportValueKind.Text
                    ? new DataGridLength(1, DataGridLengthUnitType.Star)
                    : DataGridLength.Auto;
        }
    }

    internal IReadOnlyList<PetraReportColumnState> CaptureColumnLayout() => Results.Columns
        .OrderBy(column => column.DisplayIndex)
        .Select(column => new PetraReportColumnState(
            column.SortMemberPath,
            column.DisplayIndex,
            Math.Clamp(column.ActualWidth, 48, 2000),
            column.Visibility == Visibility.Visible,
            column.DisplayIndex < Results.FrozenColumnCount))
        .ToArray();

    internal IReadOnlyList<string> ApplyColumnLayout(IEnumerable<PetraReportColumnState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        var warnings = new List<string>();
        var byKey = Results.Columns.ToDictionary(column => column.SortMemberPath, StringComparer.Ordinal);
        var supplied = states.ToArray();
        var valid = supplied.Where(state => !string.IsNullOrWhiteSpace(state.Key) && byKey.ContainsKey(state.Key))
            .GroupBy(state => state.Key, StringComparer.Ordinal).Select(group => group.First()).OrderBy(state => state.DisplayIndex).ToArray();
        foreach (var state in supplied)
            if (!byKey.ContainsKey(state.Key)) warnings.Add("Unavailable field ignored: " + state.Key);
        for (var index = 0; index < valid.Length; index++)
        {
            var state = valid[index];
            var column = byKey[state.Key];
            column.DisplayIndex = Math.Clamp(index, 0, Results.Columns.Count - 1);
            column.Width = new DataGridLength(Math.Clamp(state.Width, 48, 2000));
            column.Visibility = state.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        if (Results.Columns.All(column => column.Visibility != Visibility.Visible))
        {
            Results.Columns[0].Visibility = Visibility.Visible;
            warnings.Add("At least one field was restored because the workspace hid every available field.");
        }
        Results.FrozenColumnCount = Math.Clamp(valid.Count(state => state.IsFrozen), 0, Results.Columns.Count);
        return warnings.AsReadOnly();
    }

    internal IReadOnlyList<VisibleReportSummary> VisibleSummaries()
    {
        if (Document is null) return [];
        var summaries = new List<VisibleReportSummary>();
        foreach (var column in Document.Columns.Where(column => column.Kind is ReportValueKind.Amount or ReportValueKind.Quantity))
        {
            decimal sum = 0;
            foreach (var row in rows)
                if (row.TryGetValue(column.Key, out var value) && value is not null) sum += ReportDocument.DecimalValue(value);
            summaries.Add(new VisibleReportSummary(column.Key, column.Heading, sum));
        }
        return summaries.AsReadOnly();
    }

    internal bool AddGrouping(string key)
    {
        if (Document is null || groupingKeys.Contains(key, StringComparer.Ordinal) ||
            Document.Columns.All(column => !string.Equals(column.Key, key, StringComparison.Ordinal)) || groupingKeys.Count >= 4) return false;
        groupingKeys.Add(key);
        BindRows();
        return true;
    }

    internal void ClearGrouping()
    {
        groupingKeys.Clear();
        BindRows();
    }

    internal IReadOnlyList<PetraConditionalFormatRule> ActiveConditionalRules => conditionalRules.AsReadOnly();

    internal void ApplyConditionalFormatting(IEnumerable<PetraConditionalFormatRule> rules)
    {
        conditionalRules.Clear();
        conditionalRules.AddRange(rules);
        Results.Items.Refresh();
    }

    internal void ClearConditionalFormatting()
    {
        conditionalRules.Clear();
        Results.Items.Refresh();
    }

    private void ResultsLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is not IReadOnlyDictionary<string, object?> row) return;

        foreach (var rule in conditionalRules)
        {
            var column = Document?.Columns.FirstOrDefault(c => string.Equals(c.Key, rule.ColumnKey, StringComparison.Ordinal));
            if (column is not null && rule.Matches(row, column))
            {
                e.Row.Background = rule.BackgroundBrush;
                e.Row.Foreground = rule.ForegroundBrush;
                return;
            }
        }

        e.Row.ClearValue(DataGridRow.BackgroundProperty);
        e.Row.ClearValue(DataGridRow.ForegroundProperty);
    }

    private void BindRows()
    {
        if (groupingKeys.Count == 0)
        {
            Results.ItemsSource = rows;
        }
        else
        {
            var view = new ListCollectionView(new ArrayList(rows.ToArray()));
            foreach (var key in groupingKeys)
                view.GroupDescriptions.Add(new PropertyGroupDescription(null, new DictionaryGroupConverter(key)));
            Results.ItemsSource = view;
        }
        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void DocumentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((PetraInteractiveReportGrid)sender).LoadDocument(args.NewValue as ReportDocument);

    private void LoadDocument(ReportDocument? document)
    {
        CancelPendingViewWork();
        distinctLoad?.Cancel();
        distinctSearch?.Cancel();
        HeaderFilterPopup.IsOpen = false;
        activePopupColumn = null;
        allDistinctValues = [];
        matchingDistinctValues = [];
        Results.Columns.Clear();
        columnDefinitions.Clear();
        groupingKeys.Clear();
        sourceRows = document?.Rows ?? [];
        rows = sourceRows;
        filteredRows = sourceRows;
        committedFilter = pendingFilter = null;
        committedSort = pendingSort = null;
        BindRows();
        if (document is null) return;

        foreach (var definition in document.Columns)
        {
            var column = new DataGridTextColumn
            {
                Header = CreateColumnHeader(definition),
                Binding = new Binding($"[{definition.Key}]")
                {
                    Mode = BindingMode.OneWay,
                    Converter = ReportGridCellConverter.Instance,
                    ConverterParameter = definition,
                },
                SortMemberPath = definition.Key,
                Width = definition.Kind == ReportValueKind.Text ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
                MinWidth = definition.Kind == ReportValueKind.Text ? 130 : 96,
            };
            if (definition.Kind is ReportValueKind.Amount or ReportValueKind.Quantity or ReportValueKind.Integer)
                column.ElementStyle = TryFindResource("Text.Numeric") as Style;
            Results.Columns.Add(column);
            columnDefinitions.Add(column, definition);
        }
        VisibleRowsChanged?.Invoke(this, EventArgs.Empty);
    }

    private StackPanel CreateColumnHeader(ReportColumn definition)
    {
        var heading = new TextBlock { Text = definition.Heading, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var filter = new Button
        {
            Content = "⌄",
            Width = 22,
            Height = 20,
            Padding = new Thickness(0),
            ToolTip = "تصفية العمود / Filter column",
            Focusable = false,
        };
        filter.Click += (_, args) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                HeaderFilterRequested?.Invoke(this, new ReportColumnFilterRequest(definition));
            else
                OpenHeaderFilterPopup(filter, definition);
            args.Handled = true;
        };
        var title = new DockPanel();
        DockPanel.SetDock(filter, Dock.Right);
        title.Children.Add(filter);
        title.Children.Add(heading);
        var input = new TextBox
        {
            MinWidth = definition.Kind == ReportValueKind.Text ? 110 : 82,
            Height = 24,
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 3, 0, 0),
            FlowDirection = FlowDirection.LeftToRight,
            ToolTip = "تصفية سريعة؛ Enter للتطبيق / Quick filter; Enter to apply",
        };
        input.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            AutoFilterRequested?.Invoke(this, new ReportColumnAutoFilterRequest(definition, input.Text));
            args.Handled = true;
        };
        return new StackPanel { Children = { title, input } };
    }

    private ReportColumn? activePopupColumn;
    private IReadOnlyList<DistinctFilterItem> allDistinctValues = [];
    private IReadOnlyList<DistinctFilterItem> matchingDistinctValues = [];
    private CancellationTokenSource? distinctLoad;
    private CancellationTokenSource? distinctSearch;

    internal void OpenHeaderFilterPopup(Button anchor, ReportColumn definition)
    {
        distinctLoad?.Cancel();
        distinctSearch?.Cancel();
        activePopupColumn = definition;
        HeaderPopupTitle.Text = "تصفية: " + definition.Heading;
        allDistinctValues = [];
        matchingDistinctValues = [];
        DistinctValuesList.ItemsSource = null;
        DistinctValuesNote.Text = "جارٍ تحميل القيم… / Loading values…";
        HeaderPopupSearchBox.Clear();
        HeaderFilterPopup.PlacementTarget = anchor;
        HeaderFilterPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        HeaderFilterPopup.IsOpen = true;
        HeaderPopupSearchBox.Focus();
        _ = LoadDistinctValuesAsync(definition);
    }

    /// <summary>
    /// Builds EVERY distinct value of the column (grouped by raw value) from the immutable snapshot on a worker thread.
    /// Only a bounded slice is bound to the popup; search and select-all always cover the complete set.
    /// </summary>
    private async Task LoadDistinctValuesAsync(ReportColumn definition)
    {
        distinctLoad?.Cancel();
        var source = new CancellationTokenSource();
        distinctLoad = source;
        if (Document is not { } document) return;
        var preselected = HeaderSelectionProvider?.Invoke(definition.Key);
        try
        {
            var items = await Task.Run(
                () => PetraReportDistinctValues.Build(document, definition, preselected, source.Token), source.Token).ConfigureAwait(true);
            if (source.IsCancellationRequested || !ReferenceEquals(distinctLoad, source) ||
                !ReferenceEquals(activePopupColumn, definition) || !ReferenceEquals(Document, document)) return;
            allDistinctValues = items;
            await RefreshDistinctViewAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (ReferenceEquals(activePopupColumn, definition)) DistinctValuesNote.Text = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(distinctLoad, source)) distinctLoad = null;
            source.Dispose();
        }
    }

    /// <summary>Re-runs the value search over ALL distinct values off the UI thread and binds only the bounded slice.</summary>
    private async Task RefreshDistinctViewAsync()
    {
        distinctSearch?.Cancel();
        var source = new CancellationTokenSource();
        distinctSearch = source;
        var all = allDistinctValues;
        var column = activePopupColumn;
        var query = HeaderPopupSearchBox.Text;
        try
        {
            var result = string.IsNullOrWhiteSpace(query)
                ? PetraReportDistinctValues.Search(all, query, PetraReportDistinctValues.VisibleLimit, source.Token)
                : await Task.Run(() => PetraReportDistinctValues.Search(all, query, PetraReportDistinctValues.VisibleLimit, source.Token), source.Token)
                    .ConfigureAwait(true);
            if (source.IsCancellationRequested || !ReferenceEquals(distinctSearch, source) ||
                !ReferenceEquals(all, allDistinctValues) || !ReferenceEquals(column, activePopupColumn)) return;
            matchingDistinctValues = result.Matches;
            DistinctValuesList.ItemsSource = result.Shown;
            DistinctValuesNote.Text = result.Matches.Count > result.Shown.Count
                ? $"عرض {result.Shown.Count.ToString("N0", CultureInfo.InvariantCulture)} من {result.Matches.Count.ToString("N0", CultureInfo.InvariantCulture)} / Showing {result.Shown.Count.ToString("N0", CultureInfo.InvariantCulture)} of {result.Matches.Count.ToString("N0", CultureInfo.InvariantCulture)} - refine the search"
                : $"{result.Matches.Count.ToString("N0", CultureInfo.InvariantCulture)} قيمة / values";
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(distinctSearch, source)) distinctSearch = null;
            source.Dispose();
        }
    }

    private async void HeaderPopupSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (allDistinctValues.Count == 0) return;
        await RefreshDistinctViewAsync().ConfigureAwait(true);
    }

    /// <summary>Select all / clear all act on every value matching the current search, not only on the bounded slice shown.</summary>
    private void SelectAllValuesClicked(object sender, RoutedEventArgs e)
    {
        foreach (var item in matchingDistinctValues) item.IsSelected = true;
    }

    private void ClearAllValuesClicked(object sender, RoutedEventArgs e)
    {
        foreach (var item in matchingDistinctValues) item.IsSelected = false;
    }

    private void ApplyHeaderPopupClicked(object sender, RoutedEventArgs e)
    {
        if (activePopupColumn is null) { HeaderFilterPopup.IsOpen = false; return; }
        if (allDistinctValues.Count == 0) return; // values are still loading: nothing to apply yet
        HeaderFilterPopup.IsOpen = false;
        var selected = allDistinctValues.Where(i => i.IsSelected).Select(i => i.RawValue).ToArray();
        if (selected.Length == allDistinctValues.Count)
            HeaderFilterCleared?.Invoke(this, activePopupColumn.Key);
        else
            HeaderFilterConditionApplied?.Invoke(this, new PetraReportFilterCondition(activePopupColumn.Key, PetraReportFilterOperator.In, Values: selected));
    }

    private void ClearHeaderPopupClicked(object sender, RoutedEventArgs e)
    {
        HeaderFilterPopup.IsOpen = false;
        if (activePopupColumn is null) return;
        HeaderFilterCleared?.Invoke(this, activePopupColumn.Key);
    }

    private void CloseHeaderPopupClicked(object sender, RoutedEventArgs e)
    {
        HeaderFilterPopup.IsOpen = false;
    }

    private async void ResultsSorting(object sender, DataGridSortingEventArgs args)
    {
        if (!columnDefinitions.TryGetValue(args.Column, out var definition)) return;
        args.Handled = true;
        var direction = pendingSort is { } current && string.Equals(current.Key, definition.Key, StringComparison.Ordinal) &&
                        current.Direction == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        try { await RefreshViewAsync(changeFilter: false, null, changeSort: true, new ViewSort(definition.Key, direction), CancellationToken.None).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException) { ViewOperationFailed?.Invoke(this, exception); }
    }

    private void ResultsSelectionChanged(object sender, SelectionChangedEventArgs args) => SelectionChanged?.Invoke(this, EventArgs.Empty);
    private void ResultsMouseDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (Results.SelectedIndex < 0) return;
        RowActivated?.Invoke(this, EventArgs.Empty);
        args.Handled = true;
    }

    private void ResultsPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || Results.SelectedIndex < 0) return;
        RowActivated?.Invoke(this, EventArgs.Empty);
        args.Handled = true;
    }

    private sealed class DictionaryGroupConverter(string key) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is IReadOnlyDictionary<string, object?> row && row.TryGetValue(key, out var group) && group is not null
                ? System.Convert.ToString(group, CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
    }
}

internal sealed class ReportColumnFilterRequest(ReportColumn column) : EventArgs
{
    internal ReportColumn Column { get; } = column;
}

internal sealed class ReportColumnAutoFilterRequest(ReportColumn column, string value) : EventArgs
{
    internal ReportColumn Column { get; } = column;
    internal string Value { get; } = value;
}

internal sealed record VisibleReportSummary(string Key, string Label, decimal Value);
internal sealed record PetraReportColumnState(string Key, int DisplayIndex, double Width, bool IsVisible, bool IsFrozen);

internal sealed class ReportGridCellConverter : IValueConverter
{
    internal static readonly ReportGridCellConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is not ReportColumn column) return string.Empty;
        return Format(value, column);
    }

    internal static string Format(object? value, ReportColumn column)
    {
        if (value is null) return string.Empty;
        return column.Kind switch
        {
            ReportValueKind.Amount => ReportDocument.DecimalValue(value).ToString("N" + column.DecimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            ReportValueKind.Quantity => ReportDocument.DecimalValue(value).ToString("0.########", CultureInfo.InvariantCulture),
            ReportValueKind.Integer => ReportDocument.DecimalValue(value).ToString("0", CultureInfo.InvariantCulture),
            ReportValueKind.Date => ReportDocument.DateValue(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class DistinctFilterItem : INotifyPropertyChanged
{
    private bool isSelected = true;
    public string RawValue { get; }
    public string FormattedValue { get; }
    public int Count { get; }
    /// <summary>Lower-cased, Arabic-normalised text of display value + raw value; precomputed on the worker so a keystroke search stays cheap.</summary>
    internal string SearchKey { get; }
    /// <summary>Display text; when several raw values render identically the raw value is appended so they stay distinguishable.</summary>
    public string DisplayLabel { get; }

    public DistinctFilterItem(string rawValue, string formattedValue, int count, bool selected = true, bool showRaw = false)
    {
        RawValue = rawValue;
        FormattedValue = string.IsNullOrWhiteSpace(formattedValue) ? "(فارغ)" : formattedValue;
        Count = count;
        isSelected = selected;
        SearchKey = PetraReportFilterEngine.NormalizeArabic(FormattedValue + " " + rawValue);
        DisplayLabel = (showRaw ? $"{FormattedValue} [{rawValue}]" : FormattedValue) + $" ({Count.ToString(CultureInfo.InvariantCulture)})";
    }

    public bool IsSelected
    {
        get => isSelected;
        set { if (isSelected != value) { isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
