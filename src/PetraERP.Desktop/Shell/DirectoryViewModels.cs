using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Localization;

namespace PetraERP.Desktop.Shell;

/// <summary>One line of a directory: a group (tree node) or a card (list row).</summary>
public sealed partial class DirectoryEntry : ObservableObject
{
    [ObservableProperty] private bool isExpanded = true;
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public Guid? ParentId { get; init; }
    public bool IsGroup { get; init; }
    public bool IsActive { get; init; } = true;
    public ObservableCollection<DirectoryEntry> Children { get; } = [];
    public string Caption => Code + "-" + Name;
}

/// <summary>
/// Al-Ameen directory window ("دليل الحسابات" / "دليل المواد"): a search box on top, the group tree on one side and the cards of the
/// selected group on the other. A double click opens the card. Read-only; the cards stay the only place that changes data.
/// </summary>
public abstract partial class DirectoryViewModelBase : PageViewModel
{
    private const int RowLimit = 300;
    private readonly ShellNavigator navigator;
    private long refreshGeneration;

    [ObservableProperty] private DirectoryEntry? selectedNode;
    [ObservableProperty] private DirectoryEntry? selectedRow;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string statusText = string.Empty;

    protected DirectoryViewModelBase(ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization) : base(operation, localization)
    {
        this.navigator = navigator;
        OpenCommand = new AsyncRelayCommand(OpenSelectedAsync, () => HasContext && SelectedRow is not null);
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(ReloadAsync), () => HasContext && Idle);
    }

    public ObservableCollection<DirectoryEntry> Roots { get; } = [];
    public ObservableCollection<DirectoryEntry> Rows { get; } = [];
    public AsyncRelayCommand OpenCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public abstract string Title { get; }
    public string SearchHint => Localization["D_DirectorySearchHint"];
    public string RowsCaption => Localization["D_DirectoryCards"];

    protected abstract Task LoadTreeAsync(CancellationToken cancellationToken);
    protected abstract Task<IReadOnlyList<DirectoryEntry>> LoadRowsAsync(DirectoryEntry? group, string search, CancellationToken cancellationToken);
    protected abstract Task OpenAsync(ShellNavigator navigator, DirectoryEntry entry);

    public void Localize()
    {
        OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(SearchHint)); OnPropertyChanged(nameof(RowsCaption));
    }

    protected string PickName(string? arabic, string? english) =>
        !Localization.IsRightToLeft && !string.IsNullOrWhiteSpace(english) ? english! : arabic ?? string.Empty;

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        Roots.Clear();
        await LoadTreeAsync(cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        await RefreshRowsAsync(cancellationToken).ConfigureAwait(true);
        NotifyCommands();
    }

    private CancellationTokenSource? searchDebounceCts;

    private async Task RefreshRowsAsync(CancellationToken cancellationToken)
    {
        var generation = Interlocked.Increment(ref refreshGeneration);
        var rows = await LoadRowsAsync(SelectedNode, SearchText.Trim(), cancellationToken).ConfigureAwait(true);
        if (generation != Interlocked.Read(ref refreshGeneration) || cancellationToken.IsCancellationRequested) return;
        var limitRows = rows.Take(RowLimit).ToList();
        if (Rows.Count == limitRows.Count && Rows.Select(r => r.Id).SequenceEqual(limitRows.Select(r => r.Id))) return;
        Rows.Clear();
        foreach (var row in limitRows) Rows.Add(row);
        StatusText = rows.Count > RowLimit ? string.Format(Localization.Culture, Localization["D_DirectoryTruncated"], RowLimit) : string.Empty;
    }

    partial void OnSelectedNodeChanged(DirectoryEntry? value) => _ = SafeRefreshAsync();

    partial void OnSearchTextChanged(string value) => _ = DebouncedRefreshAsync();

    private async Task DebouncedRefreshAsync()
    {
        if (!HasContext) return;
        searchDebounceCts?.Cancel();
        searchDebounceCts?.Dispose();
        var cts = searchDebounceCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(80, cts.Token).ConfigureAwait(true);
            if (!cts.IsCancellationRequested)
            {
                await RefreshRowsAsync(cts.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException) { StatusText = exception.Message; }
    }

    partial void OnSelectedRowChanged(DirectoryEntry? value) => OpenCommand.NotifyCanExecuteChanged();

    private async Task SafeRefreshAsync()
    {
        if (!HasContext) return;
        try { await RefreshRowsAsync(Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException) { StatusText = exception.Message; }
    }

    private Task OpenSelectedAsync() => SelectedRow is { } row ? OpenAsync(navigator, row) : Task.CompletedTask;

    protected override void ClearPage()
    {
        Roots.Clear(); Rows.Clear(); SelectedNode = null; SelectedRow = null; SearchText = string.Empty; StatusText = string.Empty;
    }

    protected override void NotifyCommands()
    {
        OpenCommand?.NotifyCanExecuteChanged(); RefreshCommand?.NotifyCanExecuteChanged();
    }
}

/// <summary>"دليل الحسابات": the chart as a tree of control accounts; the list shows the accounts under the selected one.</summary>
public sealed class AccountDirectoryViewModel(IChartOfAccountsClient client, ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization)
    : DirectoryViewModelBase(navigator, operation, localization)
{
    private List<AccountResponse> accounts = [];

    protected override string RequiredPermission => "Ledger.Accounts.View";
    public override string Title => Localization["D_AccountDirectory"];

    protected override async Task LoadTreeAsync(CancellationToken cancellationToken)
    {
        accounts = [.. await client.GetAccountTreeAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true)];
        var lookup = accounts.Where(a => a.ParentId is not null).ToLookup(a => a.ParentId!.Value);
        DirectoryEntry Build(AccountResponse account)
        {
            var node = ToEntry(account);
            foreach (var child in lookup[account.Id].Where(c => !c.PostingAllowed).OrderBy(c => c.Code, StringComparer.Ordinal)) node.Children.Add(Build(child));
            return node;
        }

        foreach (var root in accounts.Where(a => a.ParentId is null).OrderBy(a => a.Code, StringComparer.Ordinal)) Roots.Add(Build(root));
    }

    protected override Task<IReadOnlyList<DirectoryEntry>> LoadRowsAsync(DirectoryEntry? group, string search, CancellationToken cancellationToken)
    {
        IEnumerable<AccountResponse> query = accounts;
        if (search.Length > 0) query = query.Where(a => Controls.LookupText.Matches(search, a.Code, a.NameArabic, a.NameEnglish));
        else if (group is not null) query = query.Where(a => a.ParentId == group.Id);
        else query = query.Where(a => a.ParentId is null);
        IReadOnlyList<DirectoryEntry> rows = [.. query.OrderBy(a => a.Code, StringComparer.Ordinal).Select(ToEntry)];
        return Task.FromResult(rows);
    }

    protected override Task OpenAsync(ShellNavigator navigator, DirectoryEntry entry) => navigator.NavigateAsync("accounts", new OpenAccountCardRequest(entry.Id));

    private DirectoryEntry ToEntry(AccountResponse account) => new()
    {
        Id = account.Id, Code = account.Code, Name = PickName(account.NameArabic, account.NameEnglish), ParentId = account.ParentId,
        IsGroup = !account.PostingAllowed, IsActive = account.IsActive,
    };
}

/// <summary>"دليل المواد": the item groups as a tree; the list shows the items of the selected group (or the search result).</summary>
public sealed class ItemDirectoryViewModel(IInventoryClient client, ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization)
    : DirectoryViewModelBase(navigator, operation, localization)
{
    protected override string RequiredPermission => "Inventory.Items.View";
    public override string Title => Localization["D_ItemDirectory"];

    protected override async Task LoadTreeAsync(CancellationToken cancellationToken)
    {
        var groups = await client.ListItemGroupsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
        var lookup = groups.Where(g => g.ParentId is not null).ToLookup(g => g.ParentId!.Value);
        DirectoryEntry Build(ItemGroupDto group)
        {
            var node = new DirectoryEntry
            {
                Id = group.Id ?? Guid.Empty, Code = group.Code ?? string.Empty, Name = PickName(group.NameArabic, group.NameEnglish),
                ParentId = group.ParentId, IsGroup = true, IsActive = group.IsActive != false,
            };
            foreach (var child in lookup[group.Id ?? Guid.Empty].OrderBy(c => c.Code, StringComparer.Ordinal)) node.Children.Add(Build(child));
            return node;
        }

        foreach (var root in groups.Where(g => g.ParentId is null).OrderBy(g => g.Code, StringComparer.Ordinal)) Roots.Add(Build(root));
    }

    protected override async Task<IReadOnlyList<DirectoryEntry>> LoadRowsAsync(DirectoryEntry? group, string search, CancellationToken cancellationToken)
    {
        var page = await client.ListItemsAsync(CompanyId, BranchId, 1, 300, search.Length == 0 ? null : search, search.Length == 0 ? group?.Id : null, null, null, cancellationToken)
            .ConfigureAwait(true);
        return [.. (page.Items ?? []).Select(item => new DirectoryEntry
        {
            Id = item.Id ?? Guid.Empty, Code = item.Code ?? string.Empty, Name = PickName(item.NameArabic, item.NameEnglish), ParentId = item.GroupId,
            IsGroup = false, IsActive = item.IsActive != false,
        })];
    }

    protected override Task OpenAsync(ShellNavigator navigator, DirectoryEntry entry) => navigator.NavigateAsync("item-card", new OpenItemCardRequest(entry.Id));
}
