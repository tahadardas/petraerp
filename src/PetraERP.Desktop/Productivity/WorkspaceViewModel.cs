using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Productivity;

/// <summary>
/// The shell's workspace service: authorized entity search for the command palette, recent items, favorite commands
/// and per-user preferences (language, theme, saved grid layouts) through the existing workspace API. The shell
/// supplies company/branch via <see cref="SetContextAsync"/>; every context change invalidates late responses.
/// </summary>
public sealed class WorkspaceViewModel : ObservableObject, IGridLayoutStore, IDisposable
{
    private readonly IWorkspaceClient client;
    private readonly IThemeService themes;
    private CancellationTokenSource contextLifetime = new();
    private CancellationTokenSource? debounce;
    private long generation;
    private string error = string.Empty, paletteQuery = string.Empty;
    private CompanyContextResponse? company;
    private BranchContextResponse? branch;
    private WorkspaceStateResponse? preferences;
    private IReadOnlyList<WorkspaceCommandResponse> catalog = [];
    private IReadOnlyList<SearchHitResponse> paletteEntities = [];

    public WorkspaceViewModel(IWorkspaceClient client, LocalizationService localization, IThemeService themes)
    {
        this.client = client; Localization = localization; this.themes = themes;
    }

    /// <summary>Raised when the server reports the session ended; the shell owns sign-out and re-authentication.</summary>
    public event EventHandler? SessionLost;

    public LocalizationService Localization { get; }
    public ObservableCollection<SearchHitResponse> Recent { get; } = [];
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public CompanyContextResponse? Company { get => company; private set { SetProperty(ref company, value); OnPropertyChanged(nameof(ContextReady)); } }
    public BranchContextResponse? Branch { get => branch; private set { SetProperty(ref branch, value); OnPropertyChanged(nameof(ContextReady)); } }
    public bool ContextReady => Company is not null && Branch is not null;
    /// <summary>Server-authorized command catalog (the server filters by permission and scope).</summary>
    public IReadOnlyList<WorkspaceCommandResponse> Catalog => catalog;
    /// <summary>Entity hits for the current palette query (accounts and journal entries the user may read).</summary>
    public IReadOnlyList<SearchHitResponse> PaletteEntities => paletteEntities;
    public IReadOnlyList<WorkspaceCommandResponse> Favorites => preferences is null ? [] : catalog.Where(command => (preferences.Preferences.FavoriteCommands.Contains(command.Id) || preferences.Preferences.FavoriteReports.Contains(command.Id))).ToArray();
    public IReadOnlyList<GridPreferenceRequest> Grids => preferences?.Preferences.Grids ?? [];

    public string PaletteQuery
    {
        get => paletteQuery;
        set
        {
            if (!SetProperty(ref paletteQuery, value)) return;
            paletteEntities = [];
            OnPropertyChanged(nameof(PaletteEntities));
            _ = DebouncePaletteAsync(value);
        }
    }

    public async Task SetContextAsync(CompanyContextResponse? selectedCompany, BranchContextResponse? selectedBranch)
    {
        if (selectedCompany?.Id == Company?.Id && selectedBranch?.Id == Branch?.Id && ContextReady)
        {
            var refreshStamp = generation;
            await RunAsync(async token =>
            {
                var commands = await client.CommandsAsync(selectedCompany!.Id, selectedBranch!.Id, token).ConfigureAwait(true);
                var state = await client.PreferencesAsync(selectedCompany!.Id, selectedBranch!.Id, token).ConfigureAwait(true);
                if (refreshStamp != generation) return;
                catalog = commands; ApplyPreferences(state, false);
                paletteEntities = paletteEntities.Where(hit => catalog.Any(command => command.Category == hit.Category)).ToArray();
                OnPropertyChanged(nameof(Catalog)); OnPropertyChanged(nameof(Favorites)); OnPropertyChanged(nameof(PaletteEntities));
            }).ConfigureAwait(true);
            return;
        }
        ClearContext();
        if (selectedCompany is null || selectedBranch is null || selectedBranch.CompanyId != selectedCompany.Id) return;
        Company = selectedCompany; Branch = selectedBranch;
        var stamp = generation;
        await RunAsync(async token =>
        {
            var commands = await client.CommandsAsync(selectedCompany.Id, selectedBranch.Id, token).ConfigureAwait(true);
            var state = await client.PreferencesAsync(selectedCompany.Id, selectedBranch.Id, token).ConfigureAwait(true);
            if (stamp != generation) return;
            catalog = commands; ApplyPreferences(state);
        }).ConfigureAwait(true);
    }

    /// <summary>Drops all cached context (sign-out, session loss, company change). Nothing survives into the next user.</summary>
    public void Reset() { ClearContext(); Error = string.Empty; }

    /// <summary>Immediate (non-debounced) palette search, used by Enter and tests.</summary>
    public Task SearchNowAsync(string text)
    {
        debounce?.Cancel();
        paletteQuery = text; OnPropertyChanged(nameof(PaletteQuery));
        return RunAsync(token => SearchCoreAsync(text, token));
    }

    public Task RecordRecentAsync(SearchCategory category, Guid entityId) => RunAsync(async token =>
    {
        if (!ContextReady) return;
        var stamp = generation; var companyId = Company!.Id; var branchId = Branch!.Id;
        await client.RecordRecentAsync(companyId, branchId, new RecordRecentRequest { Category = category, EntityId = entityId }, token).ConfigureAwait(true);
        var state = await client.PreferencesAsync(companyId, branchId, token).ConfigureAwait(true);
        if (stamp == generation) ApplyPreferences(state);
    });

    public string Label(string key) => Localization[key];

    public bool IsFavorite(string commandId) => preferences is not null && (preferences.Preferences.FavoriteCommands.Contains(commandId) || preferences.Preferences.FavoriteReports.Contains(commandId));

    public Task ToggleFavoriteAsync(string commandId) => RunAsync(async token =>
    {
        if (preferences is null || !catalog.Any(command => command.Id == commandId)) return;
        var report = commandId.EndsWith(".report", StringComparison.Ordinal);
        var list = (report ? preferences.Preferences.FavoriteReports : preferences.Preferences.FavoriteCommands).ToList();
        if (!list.Remove(commandId)) list.Add(commandId);
        if (report) preferences.Preferences.FavoriteReports = list; else preferences.Preferences.FavoriteCommands = list;
        await SavePreferencesCoreAsync(token).ConfigureAwait(true);
    });

    public void SetGrid(GridPreferenceRequest layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (preferences is null) return;
        preferences.Preferences.Grids = preferences.Preferences.Grids.Where(grid => grid.Key != layout.Key).Append(layout).ToArray();
        OnPropertyChanged(nameof(Grids));
    }

    public void RemoveGrid(string key)
    {
        if (preferences is null) return;
        preferences.Preferences.Grids = preferences.Preferences.Grids.Where(grid => grid.Key != key).ToArray();
        OnPropertyChanged(nameof(Grids));
    }

    /// <summary>Removes every saved grid layout of this user in the current company/branch (Preferences → Reset layouts).</summary>
    public Task ResetGridsAsync() => RunAsync(async token =>
    {
        if (preferences is null) return;
        preferences.Preferences.Grids = [];
        OnPropertyChanged(nameof(Grids));
        await SavePreferencesCoreAsync(token).ConfigureAwait(true);
    });

    public Task SavePreferencesAsync() => RunAsync(SavePreferencesCoreAsync);

    private async Task SavePreferencesCoreAsync(CancellationToken token)
    {
        if (preferences is null || !ContextReady) return;
        preferences.Preferences.Language = Localization.Language;
        preferences.Preferences.Theme = themes.Preference.ToString();
        var stamp = generation;
        var result = await client.SavePreferencesAsync(Company!.Id, Branch!.Id, new SaveWorkspaceRequest
            { Preferences = preferences.Preferences, Version = preferences.Version }, token).ConfigureAwait(true);
        if (stamp == generation) ApplyPreferences(result);
    }

    private async Task SearchCoreAsync(string text, CancellationToken token)
    {
        if (!ContextReady) return;
        var stamp = generation;
        var result = await client.SearchAsync(Company!.Id, Branch!.Id, text, null, SearchOrder.Code, false, 1, token).ConfigureAwait(true);
        if (stamp != generation || !string.Equals(text, paletteQuery, StringComparison.Ordinal)) return;
        paletteEntities = result.Items.Where(hit => catalog.Any(command => command.Category == hit.Category)).ToArray();
        OnPropertyChanged(nameof(PaletteEntities));
    }

    private async Task DebouncePaletteAsync(string text)
    {
        debounce?.Cancel(); debounce?.Dispose(); debounce = new CancellationTokenSource();
        var cancellationToken = debounce.Token;
        try
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(text)) return;
            await RunAsync(token => SearchCoreAsync(text, token)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query.
        }
    }

    private void ApplyPreferences(WorkspaceStateResponse state, bool applyAppearance = true)
    {
        preferences = state;
        if (applyAppearance)
        {
            Localization.SetLanguage(state.Preferences.Language);
            if (Enum.TryParse<ThemePreference>(state.Preferences.Theme, out var theme)) themes.Apply(theme);
        }
        Recent.Clear(); foreach (var hit in state.Recent) Recent.Add(hit);
        OnPropertyChanged(nameof(Favorites)); OnPropertyChanged(nameof(Grids)); OnPropertyChanged(nameof(Catalog));
    }

    private void ClearContext()
    {
        generation++; debounce?.Cancel(); contextLifetime.Cancel(); contextLifetime.Dispose(); contextLifetime = new CancellationTokenSource();
        paletteQuery = string.Empty; OnPropertyChanged(nameof(PaletteQuery));
        Company = null; Branch = null; Recent.Clear(); catalog = []; preferences = null; paletteEntities = [];
        OnPropertyChanged(nameof(PaletteEntities)); OnPropertyChanged(nameof(Favorites)); OnPropertyChanged(nameof(Catalog)); OnPropertyChanged(nameof(Grids));
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        Error = string.Empty;
        try { await operation(contextLifetime.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (SessionUnavailableException) { LoseSession(); }
        catch (PetraApiException exception)
        {
            if (exception.StatusCode == 401) { LoseSession(); return; }
            if (exception.StatusCode == 403) ClearContext();
            Error = Localization[exception.StatusCode == 409 ? "UX_Conflict" : "UX_RequestFailed"];
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TimeoutException)
        { Error = Localization["UX_RequestFailed"]; }
    }

    private void LoseSession()
    {
        Reset(); Error = Localization["UX_RequestFailed"];
        SessionLost?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        contextLifetime.Cancel(); contextLifetime.Dispose(); debounce?.Cancel(); debounce?.Dispose();
    }
}
