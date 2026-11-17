using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// Code/name type-ahead picker. Typing filters the complete source with <see cref="LookupFilter"/>; Up/Down move,
/// Enter or Tab commits, Escape restores the committed value. Supports asynchronous server-side search beyond preloaded limits.
/// </summary>
[TemplatePart(Name = "PART_Text", Type = typeof(TextBox))]
[TemplatePart(Name = "PART_Popup", Type = typeof(Popup))]
[TemplatePart(Name = "PART_List", Type = typeof(ListBox))]
public class LookupPicker : Control, IDisposable
{
    private static readonly DependencyPropertyKey ResultsKey = DependencyProperty.RegisterReadOnly(nameof(Results), typeof(IReadOnlyList<LookupItem>), typeof(LookupPicker),
        new PropertyMetadata(Array.Empty<LookupItem>()));
    public static readonly DependencyProperty ResultsProperty = ResultsKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsSearchingKey = DependencyProperty.RegisterReadOnly(nameof(IsSearching), typeof(bool), typeof(LookupPicker),
        new PropertyMetadata(false));
    public static readonly DependencyProperty IsSearchingProperty = IsSearchingKey.DependencyProperty;

    private static readonly DependencyPropertyKey HasSearchErrorKey = DependencyProperty.RegisterReadOnly(nameof(HasSearchError), typeof(bool), typeof(LookupPicker),
        new PropertyMetadata(false));
    public static readonly DependencyProperty HasSearchErrorProperty = HasSearchErrorKey.DependencyProperty;

    private static readonly DependencyPropertyKey SearchErrorMessageKey = DependencyProperty.RegisterReadOnly(nameof(SearchErrorMessage), typeof(string), typeof(LookupPicker),
        new PropertyMetadata(null));
    public static readonly DependencyProperty SearchErrorMessageProperty = SearchErrorMessageKey.DependencyProperty;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(LookupPicker),
        new PropertyMetadata(null, static (d, _) =>
        {
            var p = (LookupPicker)d;
            p.CancelActiveSearch();
            p.SetValue(ResultsKey, Array.Empty<LookupItem>());
            p.SyncText();
        }));
    public static readonly DependencyProperty SelectedIdProperty = DependencyProperty.Register(nameof(SelectedId), typeof(Guid?), typeof(LookupPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, static (d, _) => ((LookupPicker)d).SyncText()));
    public static readonly DependencyProperty IsArabicProperty = DependencyProperty.Register(nameof(IsArabic), typeof(bool), typeof(LookupPicker),
        new PropertyMetadata(true, static (d, _) => ((LookupPicker)d).SyncText()));
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(LookupPicker));
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(LookupPicker));
    public static readonly DependencyProperty AsyncSearchProviderProperty = DependencyProperty.Register(
        nameof(AsyncSearchProvider), typeof(Func<string, CancellationToken, Task<IReadOnlyList<LookupItem>>>), typeof(LookupPicker));

    private TextBox? text;
    private Popup? popup;
    private ListBox? list;
    private Button? open;
    private bool syncing;
    private LookupItem? committedItem;
    private CancellationTokenSource? searchCts;

    private int searchGeneration;

    static LookupPicker() => DefaultStyleKeyProperty.OverrideMetadata(typeof(LookupPicker), new FrameworkPropertyMetadata(typeof(LookupPicker)));

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public Guid? SelectedId { get => (Guid?)GetValue(SelectedIdProperty); set => SetValue(SelectedIdProperty, value); }
    public bool IsArabic { get => (bool)GetValue(IsArabicProperty); set => SetValue(IsArabicProperty, value); }
    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public string? Placeholder { get => (string?)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public Func<string, CancellationToken, Task<IReadOnlyList<LookupItem>>>? AsyncSearchProvider
    {
        get => (Func<string, CancellationToken, Task<IReadOnlyList<LookupItem>>>?)GetValue(AsyncSearchProviderProperty);
        set => SetValue(AsyncSearchProviderProperty, value);
    }

    public IReadOnlyList<LookupItem> Results => (IReadOnlyList<LookupItem>)GetValue(ResultsProperty);
    public bool IsSearching => (bool)GetValue(IsSearchingProperty);
    public bool HasSearchError => (bool)GetValue(HasSearchErrorProperty);
    public string? SearchErrorMessage => (string?)GetValue(SearchErrorMessageProperty);
    public bool IsPopupOpen => popup?.IsOpen == true;

    private IEnumerable<LookupItem> Source => ItemsSource?.OfType<LookupItem>() ?? [];

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (text is not null)
        {
            text.TextChanged -= OnTextChanged;
            text.PreviewKeyDown -= OnKeyDown;
            text.LostKeyboardFocus -= OnLostFocus;
        }

        if (open is not null) open.Click -= OnOpen;
        if (list is not null) list.PreviewMouseLeftButtonUp -= OnListClick;
        if (popup is not null) popup.Closed -= OnPopupClosed;

        text = GetTemplateChild("PART_Text") as TextBox;
        popup = GetTemplateChild("PART_Popup") as Popup;
        if (popup is not null) popup.Closed += OnPopupClosed;
        list = GetTemplateChild("PART_List") as ListBox;
        open = GetTemplateChild("PART_Open") as Button;
        if (open is not null) open.Click += OnOpen;
        if (text is not null)
        {
            text.TextChanged += OnTextChanged;
            text.PreviewKeyDown += OnKeyDown;
            text.LostKeyboardFocus += OnLostFocus;
        }

        if (list is not null)
        {
            list.PreviewMouseLeftButtonUp += OnListClick;
        }

        SyncText();
    }

    private void OnOpen(object sender, RoutedEventArgs e) => ToggleLookup();
    private void OnListClick(object sender, MouseButtonEventArgs e) => Commit(list?.SelectedItem as LookupItem);

    /// <summary>Moves focus into the search editor and selects its current text.</summary>
    public bool FocusSearch()
    {
        if (IsReadOnly || text is null) return false;
        var focused = text.Focus();
        text.SelectAll();
        return focused;
    }

    /// <summary>Expands or collapses the quick search popup (called by click or F2).</summary>
    public void ToggleLookup()
    {
        if (IsReadOnly) return;
        if (popup?.IsOpen == true) { popup.IsOpen = false; return; }
        text?.Focus(); text?.SelectAll(); Search(string.Empty);
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        CancelActiveSearch();
        SetValue(IsSearchingKey, false);
    }

    private void CancelActiveSearch()
    {
        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;
        Interlocked.Increment(ref searchGeneration);
        SetValue(IsSearchingKey, false);
    }

    /// <summary>Applies a typed query exactly as the keyboard path does (also used by tests).</summary>
    public void Search(string query)
    {
        if (IsReadOnly) return;
        CancelActiveSearch();
        SetValue(HasSearchErrorKey, false);
        SetValue(SearchErrorMessageKey, null);

        var filtered = LookupFilter.Filter(Source, query);
        SetValue(ResultsKey, filtered);
        if (list is not null) list.SelectedIndex = Results.Count > 0 ? 0 : -1;
        if (popup is not null) popup.IsOpen = (Results.Count > 0 || (AsyncSearchProvider is not null && !string.IsNullOrWhiteSpace(query))) && !IsReadOnly && text?.IsKeyboardFocusWithin == true;

        if (AsyncSearchProvider is not null && !string.IsNullOrWhiteSpace(query))
        {
            searchCts = new CancellationTokenSource();
            var token = searchCts.Token;
            _ = TriggerAsyncSearch(query, token);
        }
        else
        {
            SetValue(IsSearchingKey, false);
        }
    }

    private async Task TriggerAsyncSearch(string query, CancellationToken token)
    {
        var provider = AsyncSearchProvider;
        if (provider is null || token.IsCancellationRequested) return;
        var generation = Interlocked.Increment(ref searchGeneration);
        SetValue(IsSearchingKey, true);
        SetValue(HasSearchErrorKey, false);
        SetValue(SearchErrorMessageKey, null);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(180), token).ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != searchGeneration) return;

            var remoteResults = await provider(query, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != searchGeneration || remoteResults is null) return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || generation != searchGeneration) return;
                if (text is not null && (!text.IsKeyboardFocusWithin || !string.Equals(text.Text, query, StringComparison.Ordinal))) return;

                var combined = Source.Concat(remoteResults).DistinctBy(i => i.Id);
                var filtered = LookupFilter.Filter(combined, query);
                SetValue(ResultsKey, filtered);
                if (list is not null) list.SelectedIndex = Results.Count > 0 ? 0 : -1;
                if (popup is not null) popup.IsOpen = !IsReadOnly && text?.IsKeyboardFocusWithin == true;
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (generation == searchGeneration)
                {
                    SetValue(HasSearchErrorKey, true);
                    SetValue(SearchErrorMessageKey, exception.Message);
                    if (popup is not null) popup.IsOpen = !IsReadOnly && text?.IsKeyboardFocusWithin == true;
                }
            });
        }
        finally
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (generation == searchGeneration)
                {
                    SetValue(IsSearchingKey, false);
                }
            });
        }
    }

    /// <summary>Commits the highlighted (or first) result, or an exact match for typed text.</summary>
    public bool CommitHighlighted()
    {
        if (IsReadOnly) return false;
        var item = list?.SelectedItem as LookupItem ?? (Results.Count > 0 ? Results[0] : null);
        if (item is null && text is not null && !string.IsNullOrWhiteSpace(text.Text))
        {
            var query = text.Text.Trim();
            item = Source.FirstOrDefault(i =>
                string.Equals(LookupText.Normalize(i.Code), LookupText.Normalize(query), StringComparison.Ordinal) ||
                (!string.IsNullOrEmpty(i.Barcode) && string.Equals(LookupText.Normalize(i.Barcode), LookupText.Normalize(query), StringComparison.Ordinal)));
            if (item is null)
            {
                var filtered = LookupFilter.Filter(Source, query, 1);
                item = filtered.Count > 0 ? filtered[0] : null;
            }
        }
        if (item is null) return false;
        Commit(item);
        if (popup is not null) popup.IsOpen = false;
        return true;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (syncing || IsReadOnly || text is null || !text.IsKeyboardFocusWithin) return;
        Search(text.Text);
    }

    /// <summary>
    /// True when Space should open the list rather than type a space: nothing has been typed (empty) or the whole committed
    /// value is selected. Inside free search text Space stays an ordinary character, so Arabic phrases with spaces still work.
    /// </summary>
    public bool CanOpenWithSpace =>
        !IsReadOnly && text is not null && (text.Text.Length == 0 || (text.SelectionLength == text.Text.Length && !IsSearching));

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (IsReadOnly) return;
        switch (e.Key)
        {
            case Key.F2 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.F3 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.Multiply when Keyboard.Modifiers == ModifierKeys.None && CanOpenWithSpace:
            case Key.Down when Keyboard.Modifiers == ModifierKeys.Alt:
            case Key.System when e.SystemKey == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt:
            case Key.Space when Keyboard.Modifiers == ModifierKeys.None && CanOpenWithSpace:
                ToggleLookup();
                e.Handled = true;
                break;
            case Key.Down when popup?.IsOpen != true:
                Search(text?.Text ?? string.Empty);
                e.Handled = true;
                break;
            case Key.Down or Key.Up when list is not null && Results.Count > 0:
                list.SelectedIndex = Math.Clamp(list.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Results.Count - 1);
                list.ScrollIntoView(list.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter or Key.Tab when string.IsNullOrWhiteSpace(text?.Text):
                committedItem = null;
                SetCurrentValue(SelectedIdProperty, null);
                if (popup is not null) popup.IsOpen = false;
                e.Handled = e.Key == Key.Enter;
                if (e.Key == Key.Enter && GridEntry.FindParent<DataGrid>(this) is null) MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                break;
            case Key.Enter or Key.Tab:
                var committed = CommitHighlighted();
                e.Handled = e.Key == Key.Enter;
                if (committed && e.Key == Key.Enter && GridEntry.FindParent<DataGrid>(this) is null) MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                break;
            case Key.Escape when popup?.IsOpen == true:
                CancelActiveSearch();
                SetValue(IsSearchingKey, false);
                if (popup is not null) popup.IsOpen = false;
                SyncText();
                e.Handled = true;
                break;
        }
    }

    private void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (list?.IsKeyboardFocusWithin == true) return;
        CancelActiveSearch();
        SetValue(IsSearchingKey, false);
        if (popup is not null) popup.IsOpen = false;
        SyncText();
    }

    private void Commit(LookupItem? item)
    {
        if (item is null || IsReadOnly) return;
        CancelActiveSearch();
        SetValue(IsSearchingKey, false);
        committedItem = item;
        SetCurrentValue(SelectedIdProperty, item.Id);
        if (popup is not null) popup.IsOpen = false;
        SyncText();
    }

    private void SyncText()
    {
        if (text is null) return;
        syncing = true;
        try
        {
            var selected = SelectedId is { } id
                ? (Source.FirstOrDefault(item => item.Id == id) ?? (committedItem?.Id == id ? committedItem : null) ?? Results.FirstOrDefault(item => item.Id == id))
                : null;
            text.Text = selected?.Display(IsArabic) ?? string.Empty;
        }
        finally
        {
            syncing = false;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            searchCts?.Cancel();
            searchCts?.Dispose();
            searchCts = null;
        }
    }
}

/// <summary>Design-system name for the code/name type-ahead picker; behavior is exactly <see cref="LookupPicker"/>.</summary>
public class PetraLookupPicker : LookupPicker
{
}

/// <summary>
/// Account picker: the same type-ahead over the accounts the page loaded (pages bind posting accounts where only posting
/// accounts are valid). Codes stay left to right; the server still validates the selected account.
/// </summary>
public sealed class PetraAccountPicker : PetraLookupPicker
{

}
