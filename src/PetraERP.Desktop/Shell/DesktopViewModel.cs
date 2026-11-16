using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Administration;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Productivity;
using PetraERP.Desktop.Theming;
using PetraERP.Desktop.Workspaces;

namespace PetraERP.Desktop.Shell;

/// <summary>A permitted quick action on Home: a route plus optional drill-down parameter.</summary>
public sealed record QuickAction(string Id, string Label, string Glyph, string Route, object? Parameter);

/// <summary>
/// The single application shell: sign-in and session, company/branch/period context, permission-driven navigation,
/// the command palette, notifications and in-shell dialogs. Pages receive the authorized context; they never select it.
/// </summary>
public sealed partial class DesktopViewModel : ObservableObject, IDisposable
{
    private readonly IDesktopClient client;
    private readonly IContextPreferenceStore preferences;
    private readonly IUserConfirmation confirmation;

    /// <summary>True when the program may close: the "confirm before closing" option is off, or the user answered yes.</summary>
    public async Task<bool> ConfirmExitAsync(EntryOptions? options = null) =>
        !(options ?? EntryOptionsHub.Current).ConfirmOnExit || await confirmation.ConfirmAsync("D_ConfirmExit").ConfigureAwait(true);
    private readonly ShellNavigator navigator;
    private readonly TimeProvider time;
    private readonly CancellationTokenSource lifetime = new();
    private HashSet<string> validatedPermissions = new(StringComparer.Ordinal);
    private CurrentUserResponse? profile;
    private CompanyContextResponse? company;
    private BranchContextResponse? branch;
    private bool authenticated, passwordChangeRequired;
    private string userName = string.Empty, password = string.Empty, currentPassword = string.Empty, newPassword = string.Empty, confirmPassword = string.Empty;
    private AccountingPeriodResponse? currentPeriod;
    private ServiceStatusResponse? serverStatus;
    private bool userMenuOpen;
    private bool showWorkspaceSelection;
    /// <summary>Journal documents hidden by an outage while their posting outcome is unknown (key, view model, scope).</summary>
    private readonly Dictionary<string, RetainedJournal> retainedJournals = new(StringComparer.Ordinal);
    /// <summary>The current document of each journal view model (kept while a journal is retained offline).</summary>
    private readonly Dictionary<GeneralLedgerViewModel, WorkspaceDocument> journalDocuments = [];
    private long draftSequence;
    private bool restoringWorkspace;

    public DesktopViewModel(IDesktopClient client, IContextPreferenceStore preferences, IUserConfirmation confirmation,
        LocalizationService localization, IThemeService themes, NavigationService navigation, DesktopOperation operation,
        ShellPages pages, WorkspaceViewModel workspace, ShellNavigator navigator, NotificationCenter notifications, TimeProvider time,
        UserPreferencesService? preferencesService = null, DashboardViewModel? dashboard = null)
    {
        this.client = client; this.preferences = preferences; this.confirmation = confirmation; this.navigator = navigator; this.time = time;
        pages.Journals.Navigator = navigator;
        if (pages.Purchases is { } purchases) purchases.PrintRequested += OnPrintRequested;
        if (pages.Vouchers is { } vouchers) vouchers.PrintRequested += OnPrintRequested;
        Localization = localization; Themes = themes; Navigation = navigation; Operation = operation; Pages = pages;
        Workspace = workspace; Notifications = notifications;
        Dialogs = confirmation as ShellDialogService ?? new ShellDialogService(localization);
        Documents = new WorkspaceDocumentManager(localization["D_Home"]);
        Documents.ActiveChanged += ActiveDocumentChanged;
        Palette = new CommandPaletteViewModel(navigation, workspace, navigator, localization);
        Workspace.SessionLost += WorkspaceSessionLost;
        navigator.Requested += OnNavigationRequested;
        WorkspaceSelection = new WorkspaceSelectionViewModel(client, localization, operation, async workspaceSummary =>
        {
            await SwitchWorkspaceAsync(workspaceSummary).ConfigureAwait(true);
        }, () => ShowWorkspaceSelection = false);
        OpenWorkspaceDialogCommand = new AsyncRelayCommand(OpenWorkspaceDialogAsync, () => !Operation.IsBusy && IsAuthenticated && Operation.IsOnline);
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !Operation.IsBusy && !IsAuthenticated && !PasswordChangeRequired && UserName.Trim().Length is >= 3 and <= 64 && Password.Length is > 0 and <= 128);
        ChangePasswordCommand = new AsyncRelayCommand(ChangePasswordAsync, () => !Operation.IsBusy && PasswordChangeRequired && CurrentPassword.Length > 0 && NewPassword.Length is >= 10 and <= 128 && NewPassword == ConfirmPassword);
        LogoutCommand = new AsyncRelayCommand(() => LogoutAsync(false), () => !Operation.IsBusy && (IsAuthenticated || PasswordChangeRequired));
        LogoutAllCommand = new AsyncRelayCommand(() => LogoutAsync(true), () => !Operation.IsBusy && (IsAuthenticated || PasswordChangeRequired));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !Operation.IsBusy && IsAuthenticated);
        SelectCompanyCommand = new AsyncRelayCommand<CompanyContextResponse>(SelectCompanyAsync, _ => !Operation.IsBusy && IsAuthenticated && Operation.IsOnline);
        SelectBranchCommand = new AsyncRelayCommand<BranchContextResponse>(SelectBranchAsync, _ => !Operation.IsBusy && IsAuthenticated && Operation.IsOnline);
        NavigateCommand = new AsyncRelayCommand<string>(NavigateAsync, id => !Operation.IsBusy && IsAuthenticated && Operation.IsOnline && Navigation.Items.Any(item => item.Id == id));
        QuickActionCommand = new AsyncRelayCommand<QuickAction>(action => action is null ? Task.CompletedTask : NavigateToAsync(action.Route, action.Parameter));
        OpenRecentCommand = new AsyncRelayCommand<SearchHitResponse>(OpenRecentAsync);
        OpenFavoriteCommand = new AsyncRelayCommand<string>(id => id is not null && CommandPaletteViewModel.RouteFor(id) is { } route
            ? NavigateToAsync(route, null) : Task.CompletedTask);
        ToggleFavoriteCommand = new AsyncRelayCommand<string>(id => id is null ? Task.CompletedTask : Workspace.ToggleFavoriteAsync(id));
        // Language and theme are saved per user: on the server workspace preferences (when a company is open) and locally.
        ToggleLanguageCommand = new RelayCommand(() => { Localization.SetLanguage(Localization.IsRightToLeft ? "en" : "ar"); SaveAppearance(); });
        ToggleThemeCommand = new RelayCommand(() => ApplyTheme(ThemeResolver.Next(Themes.Preference)));
        NewJournalCommand = new AsyncRelayCommand(() => NavigateToAsync("journals", NewJournalRequest.Instance), () => !Operation.IsBusy && CanCreateJournal);
        ToggleUserMenuCommand = new RelayCommand(() => UserMenuOpen = !UserMenuOpen);
        ActivateDocumentCommand = new RelayCommand<WorkspaceDocument>(Documents.Activate);
        CloseDocumentCommand = new AsyncRelayCommand<WorkspaceDocument>(document => CloseDocumentAsync(document));
        CloseActiveDocumentCommand = new AsyncRelayCommand(() => CloseDocumentAsync(Documents.Active));
        CloseAllDocumentsCommand = new AsyncRelayCommand(() => CloseDocumentsAsync(_ => true));
        CloseOtherDocumentsCommand = new AsyncRelayCommand(() => { var keep = Documents.Active; return CloseDocumentsAsync(document => !ReferenceEquals(document, keep)); });
        DetachDocumentCommand = new RelayCommand<WorkspaceDocument>(document => { if (Documents.DetachDocument(document!)) PersistWorkspace(); },
            document => document is { IsClosable: true, IsDetached: false });
        AttachDocumentCommand = new RelayCommand<WorkspaceDocument>(document => { if (Documents.AttachDocument(document!)) PersistWorkspace(); }, document => document is { IsDetached: true });
        DetachActiveDocumentCommand = new RelayCommand(() => { if (Documents.DetachDocument(Documents.Active)) PersistWorkspace(); }, () => Documents.Active.IsClosable);
        TileHorizontallyCommand = new RelayCommand(() => TileHorizontallyRequested?.Invoke());
        TileVerticallyCommand = new RelayCommand(() => TileVerticallyRequested?.Invoke());
        CascadeWindowsCommand = new RelayCommand(() => CascadeRequested?.Invoke());
        MinimizeActiveDocumentCommand = new RelayCommand(() => MinimizeActiveRequested?.Invoke());
        RestoreMinimizedDocumentsCommand = new RelayCommand(() => RestoreAllRequested?.Invoke());
        BringAllWindowsToFrontCommand = new RelayCommand(() => BringAllWindowsToFrontRequested?.Invoke());
        ReattachAllDocumentsCommand = new RelayCommand(() =>
        {
            foreach (var doc in Documents.Documents.Where(d => d.IsDetached).ToList())
            {
                Documents.AttachDocument(doc);
            }
            PersistWorkspace();
        });
        Documents.Documents.CollectionChanged += DocumentsChanged;
        NextDocumentCommand = new RelayCommand(() => Documents.Activate(Documents.Neighbor(1)));
        PreviousDocumentCommand = new RelayCommand(() => Documents.Activate(Documents.Neighbor(-1)));
        Operation.PropertyChanged += OperationChanged; Operation.Failed += Failed; Localization.LanguageChanged += LanguageChanged;
        Pages.Periods.CalendarChanged += CalendarChanged;
        Pages.Accounts.AccountDeleted += AccountDeleted;
        Themes.ThemeChanged += ThemeChanged;
        Dashboard = dashboard ?? new DashboardViewModel(null, null, localization);
        OpenTileCommand = new AsyncRelayCommand<DashboardTile>(tile => tile is null ? Task.CompletedTask : NavigateToAsync(tile.Route, tile.Parameter));
        InitializeExperience(preferencesService);
    }

    public LocalizationService Localization { get; }
    public IThemeService Themes { get; }
    public NavigationService Navigation { get; }
    public DesktopOperation Operation { get; }
    public ShellPages Pages { get; }
    public AdministrationViewModel Administration => Pages.Administration;
    public GeneralLedgerViewModel Ledger => Pages.Journals;
    public WorkspaceViewModel Workspace { get; }
    public WorkspaceSelectionViewModel WorkspaceSelection { get; }
    public bool ShowWorkspaceSelection { get => showWorkspaceSelection; set => SetProperty(ref showWorkspaceSelection, value); }
    public AsyncRelayCommand OpenWorkspaceDialogCommand { get; }
    /// <summary>The open workspace tabs; the active document's content is what the page host renders.</summary>
    public WorkspaceDocumentManager Documents { get; }
    public CommandPaletteViewModel Palette { get; }
    public NotificationCenter Notifications { get; }
    /// <summary>The in-shell dialog host when the confirmation service is the shell's own (null in tests with fakes).</summary>
    public ShellDialogService? Dialogs { get; }
    public ObservableCollection<CompanyContextResponse> Companies { get; } = [];
    public ObservableCollection<BranchContextResponse> Branches { get; } = [];
    public ObservableCollection<QuickAction> QuickActions { get; } = [];
    public CurrentUserResponse? Profile { get => profile; private set { SetProperty(ref profile, value); OnPropertyChanged(nameof(UserDisplay)); OnPropertyChanged(nameof(UserInitials)); OnPropertyChanged(nameof(UserLabel)); } }
    public string UserDisplay => Profile is null ? string.Empty : (Localization.IsRightToLeft ? Profile.DisplayNameArabic : Profile.DisplayNameEnglish ?? Profile.DisplayNameArabic) + " · " + Profile.UserName;
    public string UserInitials => Profile is null ? string.Empty : (Profile.DisplayNameEnglish ?? Profile.UserName).Trim()[..1].ToUpperInvariant();
    public bool IsAuthenticated { get => authenticated; private set { SetProperty(ref authenticated, value); OnPropertyChanged(nameof(ShowLogin)); Notify(); } }
    public bool PasswordChangeRequired { get => passwordChangeRequired; private set { SetProperty(ref passwordChangeRequired, value); OnPropertyChanged(nameof(ShowLogin)); Notify(); } }
    public bool ShowLogin => !IsAuthenticated && !PasswordChangeRequired;
    public string UserName { get => userName; set { SetProperty(ref userName, value); Notify(); } }
    public string Password { get => password; set { SetProperty(ref password, value); Notify(); } }
    public string CurrentPassword { get => currentPassword; set { SetProperty(ref currentPassword, value); Notify(); } }
    public string NewPassword { get => newPassword; set { SetProperty(ref newPassword, value); Notify(); } }
    public string ConfirmPassword { get => confirmPassword; set { SetProperty(ref confirmPassword, value); Notify(); } }
    public CompanyContextResponse? Company { get => company; private set { SetProperty(ref company, value); OnPropertyChanged(nameof(ContextReady)); OnPropertyChanged(nameof(CompanyLabel)); } }
    public BranchContextResponse? Branch { get => branch; private set { SetProperty(ref branch, value); OnPropertyChanged(nameof(ContextReady)); OnPropertyChanged(nameof(BranchLabel)); } }
    public string CompanyLabel => Company is null ? "شركة بترا للبرمجيات" : Localization.IsRightToLeft ? (string.IsNullOrWhiteSpace(Company.NameArabic) ? "شركة بترا للبرمجيات" : Company.NameArabic) : (string.IsNullOrWhiteSpace(Company.NameEnglish) ? Company.NameArabic : Company.NameEnglish);
    public string BranchLabel => Branch is null ? string.Empty : Localization.IsRightToLeft ? Branch.NameArabic : Branch.NameEnglish ?? Branch.NameArabic;
    public bool ContextReady => IsAuthenticated && Operation.IsOnline && Company is not null && Branch is not null;
    public string Version { get; } = typeof(DesktopViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    /// <summary>The active document's page object (selected by data template); null shows Home.</summary>
    public object? CurrentView => Documents.Active.Content;
    public bool IsHome => CurrentView is null;
    public AccountingPeriodResponse? CurrentPeriod { get => currentPeriod; private set { SetProperty(ref currentPeriod, value); OnPropertyChanged(nameof(PeriodLabel)); OnPropertyChanged(nameof(PeriodKind)); OnPropertyChanged(nameof(HasPeriod)); } }
    public bool HasPeriod => CurrentPeriod is not null;
    public string PeriodLabel => CurrentPeriod is null ? string.Empty : CurrentPeriod.Code + " · " + Localization["D_PeriodState_" + CurrentPeriod.State];
    public BadgeKind PeriodKind => CurrentPeriod is null ? BadgeKind.Neutral : FiscalPeriodsViewModel.KindOf(CurrentPeriod.State);
    public ServiceStatusResponse? ServerStatus { get => serverStatus; private set { SetProperty(ref serverStatus, value); OnPropertyChanged(nameof(ServerLabel)); } }
    public string ServerLabel => ServerStatus is null ? Localization["D_Disconnected"] : string.Format(Localization.Culture, Localization["D_ServerVersion"], ServerStatus.ProductVersion, ServerStatus.ApiVersion);
    public bool UserMenuOpen { get => userMenuOpen; set => SetProperty(ref userMenuOpen, value); }
    public string ThemeLabel => Localization["D_Theme_" + Themes.Preference];

    public AsyncRelayCommand LoginCommand { get; }
    public AsyncRelayCommand ChangePasswordCommand { get; }
    public AsyncRelayCommand LogoutCommand { get; }
    public AsyncRelayCommand LogoutAllCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand<CompanyContextResponse> SelectCompanyCommand { get; }
    public AsyncRelayCommand<BranchContextResponse> SelectBranchCommand { get; }
    public AsyncRelayCommand<string> NavigateCommand { get; }
    public AsyncRelayCommand<QuickAction> QuickActionCommand { get; }
    public AsyncRelayCommand<string> OpenFavoriteCommand { get; }
    public AsyncRelayCommand<SearchHitResponse> OpenRecentCommand { get; }
    public AsyncRelayCommand<string> ToggleFavoriteCommand { get; }
    public RelayCommand ToggleLanguageCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public AsyncRelayCommand NewJournalCommand { get; }
    public bool CanCreateJournal => IsAuthenticated && Navigation.Has("Ledger.Journals.Manage") && Navigation.Allows("journals");
    public RelayCommand ToggleUserMenuCommand { get; }
    public RelayCommand<WorkspaceDocument> ActivateDocumentCommand { get; }
    public AsyncRelayCommand<WorkspaceDocument> CloseDocumentCommand { get; }
    public AsyncRelayCommand CloseActiveDocumentCommand { get; }
    public AsyncRelayCommand CloseAllDocumentsCommand { get; }
    public AsyncRelayCommand CloseOtherDocumentsCommand { get; }
    /// <summary>Moves a document to its own top-level window (the WPF host creates the window).</summary>
    public RelayCommand<WorkspaceDocument> DetachDocumentCommand { get; }
    /// <summary>Returns a detached document to the tab strip.</summary>
    public RelayCommand<WorkspaceDocument> AttachDocumentCommand { get; }
    public RelayCommand DetachActiveDocumentCommand { get; }
    public Action? TileHorizontallyRequested { get; set; }
    public Action? TileVerticallyRequested { get; set; }
    public Action? CascadeRequested { get; set; }
    public Action? MinimizeActiveRequested { get; set; }
    public Action? RestoreAllRequested { get; set; }
    public Action? BringAllWindowsToFrontRequested { get; set; }

    public RelayCommand TileHorizontallyCommand { get; }
    public RelayCommand TileVerticallyCommand { get; }
    public RelayCommand CascadeWindowsCommand { get; }
    public RelayCommand MinimizeActiveDocumentCommand { get; }
    public RelayCommand RestoreMinimizedDocumentsCommand { get; }
    public RelayCommand BringAllWindowsToFrontCommand { get; }
    public RelayCommand ReattachAllDocumentsCommand { get; }
    /// <summary>The view model of the active journal document, if the active tab is one.</summary>
    public GeneralLedgerViewModel? ActiveJournal => Documents.Active.Content is GeneralLedgerViewModel { IsDocument: true } journal ? journal : null;
    public RelayCommand NextDocumentCommand { get; }
    public RelayCommand PreviousDocumentCommand { get; }
    public string UserLabel => Profile?.UserName ?? string.Empty;

    public async Task InitializeAsync()
    {
        if (await Operation.RunAsync(async () =>
            {
                ServerStatus = await client.StatusAsync(lifetime.Token).ConfigureAwait(true);
                if (await client.ResumeAsync(lifetime.Token).ConfigureAwait(true)) await LoadIdentityAsync().ConfigureAwait(true);
            }).ConfigureAwait(true))
        {
            await RestoreWorkspaceAsync().ConfigureAwait(true);
        }
    }

    private async Task LoginAsync()
    {
        if (!LoginCommand.CanExecute(null)) return;
        var secret = Password; Password = string.Empty;
        if (await Operation.RunAsync(async () =>
            {
                PasswordChangeRequired = await client.LoginAsync(UserName.Trim(), secret, lifetime.Token).ConfigureAwait(true);
                if (!PasswordChangeRequired) await LoadIdentityAsync().ConfigureAwait(true);
            }).ConfigureAwait(true))
        {
            await RestoreWorkspaceAsync().ConfigureAwait(true);
        }
    }

    private async Task OpenWorkspaceDialogAsync()
    {
        WorkspaceSelection.CanCancel = Company is not null;
        WorkspaceSelection.Preselect(Company?.Id, null);
        await WorkspaceSelection.LoadWorkspacesAsync().ConfigureAwait(true);
        ShowWorkspaceSelection = true;
    }

    private async Task ChangePasswordAsync()
    {
        if (!ChangePasswordCommand.CanExecute(null)) return;
        var current = CurrentPassword; var replacement = NewPassword;
        CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
        var succeeded = await Operation.RunAsync(async () =>
        {
            await client.ChangePasswordAsync(current, replacement, lifetime.Token).ConfigureAwait(true);
            PasswordChangeRequired = false; await LoadIdentityAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
        if (succeeded) { Notifications.Success("D_PasswordChanged"); await RestoreWorkspaceAsync().ConfigureAwait(true); }
        else { Reset(); await client.ClearAsync(CancellationToken.None).ConfigureAwait(true); }
    }

    private async Task LogoutAsync(bool all)
    {
        UserMenuOpen = false;
        var sessionId = Profile?.SessionId;
        if (all && !await confirmation.ConfirmAsync("D_ConfirmLogoutAll").ConfigureAwait(true)) return;
        if (!await ConfirmDiscardAsync().ConfigureAwait(true)) return;
        if (sessionId != Profile?.SessionId) return;
        PersistWorkspace();
        Reset();
        await Operation.RunAsync(() => client.LogoutAsync(all, lifetime.Token)).ConfigureAwait(true);
    }

    /// <summary>Before logout or a context switch: any open document with unsaved input needs one explicit confirmation.</summary>
    private Task<bool> ConfirmDiscardAsync()
    {
        if (Documents.Documents.FirstOrDefault(document => document.IsDirty) is not { } dirty) return Task.FromResult(true);
        Documents.Activate(dirty);
        return confirmation.ConfirmAsync("D_ConfirmDiscardChanges");
    }

    private void ActiveDocumentChanged(object? sender, EventArgs args)
    {
        Navigation.Navigate(Documents.Active.Route ?? "home");
        OnPropertyChanged(nameof(CurrentView)); OnPropertyChanged(nameof(IsHome)); OnPropertyChanged(nameof(ActiveJournal)); OnPropertyChanged(nameof(ActiveLoading)); OnPropertyChanged(nameof(ActiveSkeletonProfile));
        RefreshVisibleJournalList();
        PersistWorkspace();
        DetachActiveDocumentCommand.NotifyCanExecuteChanged();
    }

    private void Reset()
    {
        validatedPermissions.Clear();
        Profile = null; Company = null; Branch = null; Companies.Clear(); Branches.Clear(); QuickActions.Clear();
        Documents.Reset(); DisposeRetainedJournals(); Navigation.Reset(); Administration.Clear(); Pages.ClearLedger(); Workspace.Reset(); Notifications.Clear(); CurrentPeriod = null;
        IsAuthenticated = false; PasswordChangeRequired = false; ShowWorkspaceSelection = false;
        Preferences?.Load(null);
        _ = Dashboard?.RefreshAsync(null);
        Password = CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
    }

    private void Failed(object? sender, Exception exception)
    {
        if (DesktopErrors.EndsSession(exception)) { Reset(); return; }
        if (!Operation.IsOnline)
        {
            var ambiguous = PostingAttemptTracker.IsAmbiguous(exception);
            // Every journal document whose posting outcome is unknown is hidden but retained with its view model (and so its
            // caller-owned idempotency key) and its last authenticated scope. A refresh must reauthorize that same scope
            // before the document is shown again and the posting can be retried or resolved by a fresh server read.
            if (ambiguous) RetainPendingJournals();
            var uncertainPosting = ambiguous && (Ledger.HasPendingPostingAttempt || retainedJournals.Count > 0);
            Documents.Reset(); Navigation.Reset(); Administration.Clear(); Workspace.Reset();
            if (uncertainPosting)
            {
                foreach (var page in Pages.LedgerPages.Where(page => !ReferenceEquals(page, Ledger) || !Ledger.HasPendingPostingAttempt)) page.Clear();
            }
            else
            {
                DisposeRetainedJournals();
                Pages.ClearLedger(); Company = null; Branch = null; Companies.Clear(); Branches.Clear();
            }
            QuickActions.Clear(); CurrentPeriod = null;
        }
    }

    private void WorkspaceSessionLost(object? sender, EventArgs args) { Reset(); Operation.SetError("SessionEnded"); }

    private void LanguageChanged(object? sender, EventArgs args)
    {
        Operation.Localize(); Administration.Localize(); Pages.Localize();
        foreach (var name in new[] { nameof(UserDisplay), nameof(CompanyLabel), nameof(BranchLabel), nameof(PeriodLabel), nameof(ServerLabel), nameof(ThemeLabel) })
        {
            OnPropertyChanged(name);
        }

        if (IsAuthenticated && Profile is not null) { Navigation.Configure(Profile.Permissions); BuildQuickActions(); }
        Documents.Home.Title = Localization["D_Home"];
        foreach (var document in Documents.Documents)
        {
            if (document.Content is AccountCardViewModel card) { card.Localize(); document.Title = card.Title; }
            else if (document.Content is PatternCardViewModel patternCard) { patternCard.Localize(); document.Title = patternCard.Title; }
            else if (document.Content is GeneralLedgerViewModel { IsDocument: true } journal) { journal.Localize(); document.Title = journal.DocumentTitle; }
            else if (document.Content is PreferencesViewModel) document.Title = Localization["UI_Preferences"];
            else if (document.Route is { } route && Navigation.Items.FirstOrDefault(item => item.Id == route) is { } item) document.Title = item.Label;
        }
    }

    private void CalendarChanged(object? sender, EventArgs args)
    {
        if (Pages.Periods.Context is { } scope && scope.CompanyId == Company?.Id && scope.BranchId == Branch?.Id)
            CurrentPeriod = FiscalPeriodsViewModel.PeriodFor(Pages.Periods.Years, DateOnly.FromDateTime(time.GetLocalNow().DateTime));
    }
    private void ThemeChanged(object? sender, EventArgs args) => OnPropertyChanged(nameof(ThemeLabel));

    /// <summary>The account no longer exists: its card (in any branch of this company) closes without reuse.</summary>
    private void AccountDeleted(object? sender, Guid accountId) =>
        Documents.RemoveWhere(document => document.Content is AccountCardViewModel { AccountId: { } id } && id == accountId);

    private void OperationChanged(object? sender, PropertyChangedEventArgs args)
    {
        Notify(); OnPropertyChanged(nameof(ContextReady));
        // A list made stale by a document's write (for example from a detached window) reloads once the operation ends.
        if (args.PropertyName == nameof(DesktopOperation.IsBusy) && !Operation.IsBusy) RefreshVisibleJournalList();
    }

    private void Notify()
    {
        LoginCommand?.NotifyCanExecuteChanged(); ChangePasswordCommand?.NotifyCanExecuteChanged(); LogoutCommand?.NotifyCanExecuteChanged();
        LogoutAllCommand?.NotifyCanExecuteChanged(); RefreshCommand?.NotifyCanExecuteChanged(); SelectCompanyCommand?.NotifyCanExecuteChanged();
        SelectBranchCommand?.NotifyCanExecuteChanged(); NavigateCommand?.NotifyCanExecuteChanged();
        OpenWorkspaceDialogCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCreateJournal)); NewJournalCommand?.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        PersistWorkspace();
        // Disposal releases documents, not the remembered authorized references. Never overwrite them with an empty list.
        restoringWorkspace = true;
        Pages.Periods.CalendarChanged -= CalendarChanged; Pages.Accounts.AccountDeleted -= AccountDeleted;
        Palette.Dispose();
        Documents.ActiveChanged -= ActiveDocumentChanged; Documents.Reset(); DisposeRetainedJournals();
        Documents.Documents.CollectionChanged -= DocumentsChanged;
        lifetime.Cancel(); lifetime.Dispose(); Operation.PropertyChanged -= OperationChanged; Operation.Failed -= Failed; Localization.LanguageChanged -= LanguageChanged;
        Themes.ThemeChanged -= ThemeChanged; Workspace.SessionLost -= WorkspaceSessionLost; navigator.Requested -= OnNavigationRequested;
        Administration.Clear(); Pages.ClearLedger(); Workspace.Reset(); GC.SuppressFinalize(this);
    }
}
