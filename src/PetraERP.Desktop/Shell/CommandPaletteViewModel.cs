using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Productivity;

namespace PetraERP.Desktop.Shell;

public enum PaletteEntryKind
{
    Navigate,
    Action,
    Entity,
    /// <summary>An open workspace tab (activating it never reloads or discards input).</summary>
    Tab,
    /// <summary>A local shell command (theme, language, preferences); no server mutation.</summary>
    Command,
    /// <summary>A saved report filter: opens its route with the saved criteria.</summary>
    Filter,
}

/// <summary>
/// One palette row. Navigate/Action/Entity/Filter rows open a permitted route; Tab and Command rows run <see cref="Run"/>,
/// which the shell supplies (activate a tab, toggle the theme, ...). <see cref="IsDirty"/> marks a tab with unsaved input.
/// </summary>
public sealed record PaletteEntry(PaletteEntryKind Kind, string Id, string Label, string Detail, string Glyph, string Route, object? Parameter, SearchHitResponse? Entity = null,
    Func<Task>? Run = null, bool IsDirty = false);

/// <summary>
/// Ctrl+K palette: permitted navigation routes, real permitted actions and authorized entity search (accounts and
/// journal entries via the workspace search API). Nothing is listed without an implemented backend workflow.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject, IDisposable
{
    private readonly NavigationService navigation;
    private readonly WorkspaceViewModel workspace;
    private readonly ShellNavigator navigator;
    private readonly ILocalizationService localization;

    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private PaletteEntry? selected;

    public CommandPaletteViewModel(NavigationService navigation, WorkspaceViewModel workspace, ShellNavigator navigator, ILocalizationService localization)
    {
        this.navigation = navigation; this.workspace = workspace; this.navigator = navigator; this.localization = localization;
        OpenCommand = new RelayCommand(Open, () => CanOpen);
        CloseCommand = new RelayCommand(() => IsOpen = false);
        InvokeCommand = new AsyncRelayCommand<PaletteEntry>(InvokeAsync);
        MoveCommand = new RelayCommand<string>(Move);
        workspace.PropertyChanged += WorkspaceChanged; navigation.PropertyChanged += NavigationChanged;
    }

    private bool CanOpen => workspace.ContextReady || navigation.Items.Any(item => IsContextFree(item.Id));
    private static bool IsContextFree(string route) => route is "users" or "roles" or "audit" or "preferences";
    public ObservableCollection<PaletteEntry> Entries { get; } = [];
    /// <summary>
    /// Shell-provided rows (open tabs, quick actions, local commands, saved filters). The shell builds them from its own
    /// permitted state; route rows are still re-checked against navigation when invoked.
    /// </summary>
    public Func<IEnumerable<PaletteEntry>>? ShellEntries { get; set; }
    public string Query { get => workspace.PaletteQuery; set { workspace.PaletteQuery = value; OnPropertyChanged(); Rebuild(); } }
    public RelayCommand OpenCommand { get; }
    public RelayCommand CloseCommand { get; }
    public AsyncRelayCommand<PaletteEntry> InvokeCommand { get; }
    public RelayCommand<string> MoveCommand { get; }

    public void Open()
    {
        if (!CanOpen) return;
        IsOpen = true;
        Rebuild();
    }

    public void Rebuild()
    {
        var term = workspace.PaletteQuery.Trim();
        Entries.Clear();
        foreach (var item in navigation.Items.Where(item => item.Id != "home" && (workspace.ContextReady || IsContextFree(item.Id)) && Matches(term, item.Label, item.Id)).OrderByDescending(item => workspace.IsFavorite(CommandId(item.Id))))
        {
            Entries.Add(new PaletteEntry(PaletteEntryKind.Navigate, item.Id, item.Label, localization["D_NavGroup_" + item.Group], item.Glyph, item.Id, null));
        }

        if (workspace.ContextReady && navigation.Allows("journals") && navigation.Has("Ledger.Journals.Manage") && Matches(term, localization["D_NewJournal"], "journal.new"))
        {
            Entries.Add(new PaletteEntry(PaletteEntryKind.Action, "journal.new", localization["D_NewJournal"], localization["D_Journals"], "", "journals", NewJournalRequest.Instance));
        }

        foreach (var entry in ShellEntries?.Invoke() ?? [])
        {
            if (Entries.Any(existing => existing.Kind == entry.Kind && existing.Id == entry.Id)) continue;
            if (entry.Kind is PaletteEntryKind.Tab or PaletteEntryKind.Command ? entry.Run is null : !navigation.Allows(entry.Route)) continue;
            if (Matches(term, entry.Label, entry.Detail, entry.Id)) Entries.Add(entry);
        }

        if (term.Length == 0)
        {
            foreach (var hit in workspace.Recent)
            {
                var (route, parameter, glyph) = Target(hit);
                if (route is null) continue;
                Entries.Add(new PaletteEntry(PaletteEntryKind.Entity, "recent:" + hit.Id.ToString("N"), hit.Code + " — " + hit.Name,
                    localization["UX_Recent"], glyph, route, parameter, hit));
            }
        }

        foreach (var hit in workspace.PaletteEntities)
        {
            var (route, parameter, glyph) = Target(hit);
            if (route is null) continue;
            Entries.Add(new PaletteEntry(PaletteEntryKind.Entity, hit.Id.ToString("N"), hit.Code + " — " + hit.Name,
                localization["UX_Category_" + hit.Category], glyph, route, parameter, hit));
        }

        Selected = Entries.FirstOrDefault();
    }

    /// <summary>Entity → screen, only when the user can open that screen.</summary>
    public (string? Route, object? Parameter, string Glyph) Target(SearchHitResponse hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
                return hit.Category switch
        {
            SearchCategory.Accounts when navigation.Allows("accounts") => ("accounts", new OpenAccountRequest(hit.Id), ""),
            SearchCategory.JournalEntries when navigation.Allows("journals") => ("journals", new OpenJournalRequest(hit.Id), ""),
            SearchCategory.Items when navigation.Allows("items") => ("item-card", new OpenItemCardRequest(hit.Id), ""),
            SearchCategory.Parties when navigation.Allows("parties") => ("party-card", new OpenPartyCardRequest(hit.Id), ""),
            SearchCategory.Invoices when navigation.Allows("sales-invoices") => ("sales-invoice-card", new OpenSalesInvoiceCardRequest(hit.Id), ""),
            _ => (null, null, string.Empty),
        };
    }

    public async Task InvokeAsync(PaletteEntry? entry)
    {
        entry ??= Selected;
        if (entry is null || !Entries.Contains(entry)) return;
        if (entry.Kind is PaletteEntryKind.Tab or PaletteEntryKind.Command)
        {
            IsOpen = false;
            if (entry.Run is not null) await entry.Run().ConfigureAwait(true);
            return;
        }

        if (!CanOpen || (!workspace.ContextReady && !IsContextFree(entry.Route)) || !navigation.Allows(entry.Route)) return;
        IsOpen = false;
        await navigator.NavigateAsync(entry.Route, entry.Parameter).ConfigureAwait(true);
    }

    private void Move(string? direction)
    {
        if (Entries.Count == 0) return;
        var index = Selected is null ? -1 : Entries.IndexOf(Selected);
        index = Math.Clamp(index + (direction == "up" ? -1 : 1), 0, Entries.Count - 1);
        Selected = Entries[index];
    }

    private void NavigationChanged(object? sender, PropertyChangedEventArgs args)
    {
        OpenCommand.NotifyCanExecuteChanged();
        if (!CanOpen) IsOpen = false; else if (IsOpen) Rebuild();
    }
    private void WorkspaceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkspaceViewModel.PaletteEntities) && IsOpen) Rebuild();
        if (args.PropertyName == nameof(WorkspaceViewModel.ContextReady)) { OpenCommand.NotifyCanExecuteChanged(); if (!CanOpen) IsOpen = false; }
    }

    public static string CommandId(string route) => route switch
    {
        "accounts" => "accounts.browse", "journals" => "journals.browse",
        "general-journal" or "account-ledger" or "trial-balance" => route + ".report", _ => route,
    };
    public static string? RouteFor(string commandId) => commandId switch
    {
        "accounts.browse" => "accounts", "journals.browse" => "journals", "workspace.search" => null,
        "general-journal.report" => "general-journal", "account-ledger.report" => "account-ledger", "trial-balance.report" => "trial-balance", _ => null,
    };
    public void Dispose() { workspace.PropertyChanged -= WorkspaceChanged; navigation.PropertyChanged -= NavigationChanged; GC.SuppressFinalize(this); }
    private static bool Matches(string term, params string[] values) =>
        term.Length == 0 || values.Any(value => value.Contains(term, StringComparison.CurrentCultureIgnoreCase));
}

/// <summary>Drill-down parameter: open the journal screen on a new draft.</summary>
public sealed record NewJournalRequest
{
    public static NewJournalRequest Instance { get; } = new();
}
