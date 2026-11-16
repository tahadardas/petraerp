using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PetraERP.Desktop.Reporting;

/// <summary>Explicit server search prevents a large account directory from being downloaded or filtered locally.</summary>
public sealed partial class ProfessionalLookupViewModel : ObservableObject
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<ProfessionalLookupItem>>> search;
    private readonly Func<string, int, CancellationToken, Task<ProfessionalLookupPage>>? searchPage;
    private readonly Func<Guid, CancellationToken, Task<ProfessionalLookupItem>>? resolve;
    private readonly Func<CancellationToken> token;
    private readonly Func<string, string, string> text;
    private CancellationTokenSource? searchCancellation;
    private long generation;
    private bool settingSelectionText;
    private int currentPage;
    [ObservableProperty] private bool hasMore;
    [ObservableProperty] private string query = string.Empty;
    [ObservableProperty] private ProfessionalLookupItem? selected;
    [ObservableProperty] private string error = string.Empty;
    [ObservableProperty] private string status = string.Empty;
    [ObservableProperty] private bool isSearching;
    [ObservableProperty] private bool isResultsOpen;

    public ProfessionalLookupViewModel(Func<string, CancellationToken, Task<IReadOnlyList<ProfessionalLookupItem>>> search,
        Func<CancellationToken> token, Func<string, string, string>? text = null,
        Func<Guid, CancellationToken, Task<ProfessionalLookupItem>>? resolve = null,
        Func<string, int, CancellationToken, Task<ProfessionalLookupPage>>? searchPage = null)
    {
        this.search = search; this.token = token; this.text = text ?? ((arabic, english) => arabic + " / " + english); this.resolve = resolve; this.searchPage = searchPage;
        SearchCommand = new AsyncRelayCommand(SearchAsync);
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync, () => HasMore && !IsSearching);
        ClearCommand = new RelayCommand(() =>
        {
            ++generation;
            searchCancellation?.Cancel();
            searchCancellation = null;
            IsSearching = false;
            HasMore = false; currentPage = 0;
            Selected = null;
            Query = string.Empty;
            Items.Clear();
            Error = string.Empty;
            Status = string.Empty;
            IsResultsOpen = false;
        });
    }

    public ObservableCollection<ProfessionalLookupItem> Items { get; } = [];
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand LoadMoreCommand { get; }
    public RelayCommand ClearCommand { get; }

    private Task SearchAsync() => SearchPageAsync(1);
    private Task LoadMoreAsync() => SearchPageAsync(currentPage + 1);

    private async Task SearchPageAsync(int page)
    {
        // A paged lookup opens with a bounded first page for an empty query and loads further pages as the list is scrolled;
        // only the legacy non-paged search still requires text, so a huge directory is never downloaded in one request.
        if (string.IsNullOrWhiteSpace(Query) && searchPage is null)
        {
            Items.Clear(); HasMore = false; currentPage = 0; IsResultsOpen = false;
            Status = text("أدخل جزءاً من الرمز أو الاسم للبحث.", "Enter part of a code or name to search.");
            return;
        }
        searchCancellation?.Cancel();
        var current = ++generation;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token());
        searchCancellation = cancellation;
        Error = string.Empty;
        Status = text("جارٍ البحث…", "Searching…");
        IsSearching = true;
        IsResultsOpen = false;
        try
        {
            var found = searchPage is null
                ? new ProfessionalLookupPage(await search(Query.Trim(), cancellation.Token).ConfigureAwait(true), null, false, false)
                : await searchPage(Query.Trim(), page, cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            if (current != generation) return;
            if (page == 1) Items.Clear();
            var existing = Items.Select(item => item.Id).ToHashSet();
            foreach (var item in found.Items.Where(item => existing.Add(item.Id))) Items.Add(item);
            currentPage = page; HasMore = found.HasMore;
            Status = Items.Count == 0
                ? text("لا توجد نتائج مطابقة. جرّب الرمز أو جزءاً من الاسم.", "No matching results. Try a code or part of the name.")
                : found.Truncated
                    ? text($"عُرضت {Items.Count} نتيجة كحد أقصى؛ قد توجد نتائج أخرى. ضيّق البحث.", $"Showing up to {Items.Count} results; more may exist. Refine the search.")
                    : text($"عُرض {Items.Count} من {found.Total?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "؟"} — اختر من القائمة.", $"Showing {Items.Count} of {found.Total?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} — select a result.");
            IsResultsOpen = Items.Count > 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (current != generation) return;
            Error = text("تعذر البحث: ", "Lookup unavailable: ") + Connection.DesktopErrors.Key(exception);
            Status = string.Empty;
        }
        finally
        {
            if (current == generation) { IsSearching = false; searchCancellation = null; }
        }
    }

    partial void OnQueryChanged(string value)
    {
        if (settingSelectionText) return;
        ++generation;
        searchCancellation?.Cancel();
        searchCancellation = null;
        IsSearching = false;
        HasMore = false; currentPage = 0;
        IsResultsOpen = false;
        if (Selected is not null && !string.Equals(value, Selected.Label, StringComparison.Ordinal))
            Selected = null;
        Items.Clear();
        Error = string.Empty;
        Status = string.Empty;
    }

    partial void OnSelectedChanged(ProfessionalLookupItem? value)
    {
        if (value is null) return;
        settingSelectionText = true;
        try { Query = value.Label; }
        finally { settingSelectionText = false; }
        ++generation;
        searchCancellation?.Cancel();
        searchCancellation = null;
        IsSearching = false;
        HasMore = false;
        IsResultsOpen = false;
        Status = value.Label;
    }

    partial void OnHasMoreChanged(bool value) => LoadMoreCommand.NotifyCanExecuteChanged();
    partial void OnIsSearchingChanged(bool value) => LoadMoreCommand.NotifyCanExecuteChanged();

    public async Task RestoreAsync(Guid? id)
    {
        ClearCommand.Execute(null);
        if (id is null) return;
        var current = ++generation;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token());
        searchCancellation = cancellation;
        IsSearching = true;
        try
        {
            if (resolve is null) throw new InvalidOperationException("lookup.unavailable");
            var item = await resolve(id.Value, cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            if (current != generation) return;
            if (item.Id != id.Value) throw new InvalidOperationException("lookup.invalid_identity");
            Items.Add(item);
            Selected = item;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (current == generation)
                Error = text("تعذر استعادة السجل: قد يكون غير نشط أو غير متاح في هذا النطاق أو لا تملك صلاحية عرضه. اختره مجدداً.",
                    "The saved record is inactive, unavailable in this workspace, or you lack permission. Select it again.");
        }
        finally
        {
            if (current == generation) { IsSearching = false; searchCancellation = null; }
        }
    }

    public async Task EnsureSelectedAsync()
    {
        if (Selected is not null) return;
        if (string.IsNullOrWhiteSpace(Query)) return;
        if (Items.Count == 0)
        {
            await SearchPageAsync(1).ConfigureAwait(true);
        }
        var trimmed = Query.Trim();
        var match = Items.FirstOrDefault(i => string.Equals(i.Code, trimmed, StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(i.Label, trimmed, StringComparison.OrdinalIgnoreCase))
                    ?? Items.FirstOrDefault(i => i.Code.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ||
                                                 i.Label.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                    ?? Items.FirstOrDefault();
        if (match is not null)
        {
            Selected = match;
        }
    }
}
