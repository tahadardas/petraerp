using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// Premium-UX shell wiring: per-user appearance preferences, the Preferences page, saved report filters, the palette's
/// shell rows (open tabs with unsaved markers, quick actions, local commands, saved filters), tab context commands and the
/// active screen's loading state. Nothing here mutates accounting data.
/// </summary>
public sealed partial class DesktopViewModel
{
    private PreferencesViewModel? preferencesPage;

    /// <summary>Per-user appearance preferences (null only in tests that build the shell without them).</summary>
    public UserPreferencesService? Preferences { get; private set; }

    /// <summary>Entries of the Window ▸ Program appearance menu (one per skin, the active one checked).</summary>
    public ObservableCollection<SkinMenuItem> SkinItems { get; } = [];

    /// <summary>One selectable program appearance in the menu.</summary>
    public sealed partial class SkinMenuItem : ObservableObject
    {
        public SkinMenuItem(ThemeSkin skin, string label, ICommand command) { Skin = skin; this.label = label; Command = command; }
        public ThemeSkin Skin { get; }
        public ICommand Command { get; }
        [ObservableProperty] private string label;
        [ObservableProperty] private bool isChecked;
    }

    /// <summary>The Preferences route page (null when preferences are not available).</summary>
    public PreferencesViewModel? PreferencesPage => preferencesPage;

    /// <summary>The active screen's operation state for the shell's loading host (null on Home and non-page documents).</summary>
    public PetraLoadingState? ActiveLoading => (Documents.Active.Content as ILoadingAware)?.Loading;

    /// <summary>Layout-shaped first-load skeleton chosen from the active route; no data or progress is invented.</summary>
    public Controls.SkeletonProfile ActiveSkeletonProfile
    {
        get
        {
            var route = Documents.Active.Route ?? string.Empty;
            if (route is "sales-invoice-card" or "purchase-invoices" or "treasury-vouchers" or "journals") return Controls.SkeletonProfile.Document;
            if (route is "accounts" or "cost-centers") return Controls.SkeletonProfile.Tree;
            if (route.Contains("report", StringComparison.Ordinal) || route is "trial-balance" or "account-ledger" or "general-journal") return Controls.SkeletonProfile.Report;
            return Controls.SkeletonProfile.List;
        }
    }

    /// <summary>Home dashboard tiles (real server counts only).</summary>
    public DashboardViewModel Dashboard { get; private set; } = null!;

    public AsyncRelayCommand<DashboardTile> OpenTileCommand { get; private set; } = null!;

    public AsyncRelayCommand<WorkspaceDocument> CloseOtherTabsCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenPreferencesCommand { get; private set; } = null!;

    /// <summary>Opens the program options window; set by the WPF shell window.</summary>
    public Action? ProgramOptionsRequested { get; set; }

    // Gregorian Calendar & Sidebar Widget Properties
    private static readonly System.Globalization.CultureInfo ArabicGregorianCulture = new("ar-EG")
    {
        DateTimeFormat = { Calendar = new System.Globalization.GregorianCalendar() }
    };

    public string CalendarDayName { get { _ = Localization; return DateTime.Today.ToString("dddd", ArabicGregorianCulture); } }
    public string CalendarDayNumber { get { _ = Localization; return DateTime.Today.Day.ToString(System.Globalization.CultureInfo.InvariantCulture); } }
    public string CalendarMonthYear { get { _ = Localization; return DateTime.Today.ToString("MMMM yyyy", ArabicGregorianCulture); } }
    public string CalendarGregorianFullDate { get { _ = Localization; return $"التاريخ الميلادي: {DateTime.Today.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture)} م"; } }
    public string CalendarHijriDate => CalendarGregorianFullDate;
    public string CalendarTime { get { _ = Localization; return DateTime.Now.ToString("hh:mm tt", ArabicGregorianCulture); } }

    private void InitializeExperience(UserPreferencesService? service)
    {
        CloseOtherTabsCommand = new AsyncRelayCommand<WorkspaceDocument>(keep => keep is null ? Task.CompletedTask : CloseDocumentsAsync(document => !ReferenceEquals(document, keep)));
        // The program options window when the host provides it; the preferences page otherwise (headless hosts and tests).
        OpenPreferencesCommand = new AsyncRelayCommand(() =>
        {
            if (ProgramOptionsRequested is { } open && PreferencesPage is not null) { open(); return Task.CompletedTask; }
            return NavigateToAsync("preferences", null);
        }, () => IsAuthenticated);
        Palette.ShellEntries = BuildShellPaletteEntries;
        Preferences = service;
        if (service is null) return;
        preferencesPage = new PreferencesViewModel(service, Workspace, Localization, confirmation, Notifications, SaveAppearance);
        preferencesPage.ConfigureCompanyOptions(client as PetraERP.Desktop.Connection.ICompanyOptionsClient, Operation,
            () => (Company?.Id ?? Guid.Empty, Branch?.Id ?? Guid.Empty, selectedFinancialWorkspaceId ?? Guid.Empty),
            permission => validatedPermissions.Contains(permission));
        service.ThemeSaved += (_, _) => SaveAppearance();
        var setSkin = new RelayCommand<ThemeSkin>(service.SetSkin);
        foreach (var skin in Enum.GetValues<ThemeSkin>()) SkinItems.Add(new SkinMenuItem(skin, Localization["UI_Skin_" + skin], new RelayCommand(() => setSkin.Execute(skin))));
        void SyncSkins() { foreach (var item in SkinItems) { item.IsChecked = item.Skin == service.Skin; item.Label = Localization["UI_Skin_" + item.Skin]; } }
        service.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(UserPreferencesService.Skin)) SyncSkins(); };
        Localization.LanguageChanged += (_, _) => SyncSkins();
        SyncSkins();
        var sink = new NotifyingFilterSink(service, Notifications);
        Pages.TrialBalance.FilterSink = sink; Pages.AccountLedger.FilterSink = sink; Pages.GeneralJournal.FilterSink = sink;
        if (Pages.Management is { } management) management.Analytical.FilterSink = sink;
        Themes.ThemeChanged += (_, _) => service.RememberTheme(Themes.Preference);
    }

    /// <summary>Saves language and theme to the server workspace preferences when a company context exists.</summary>
    private void SaveAppearance()
    {
        if (Workspace.ContextReady) _ = Workspace.SavePreferencesAsync();
    }

    private IEnumerable<PaletteEntry> BuildShellPaletteEntries()
    {
        foreach (var document in Documents.Documents.Where(document => document.IsClosable))
        {
            var target = document;
            yield return new PaletteEntry(PaletteEntryKind.Tab, "tab:" + document.Key, document.Title,
                Localization[document.IsDirty ? "UI_OpenTabUnsaved" : "UI_OpenTab"], document.Glyph, document.Route ?? "home", null,
                Run: () => { Documents.Activate(target); return Task.CompletedTask; }, IsDirty: document.IsDirty);
        }

        foreach (var action in QuickActions.Where(action => action.Parameter is not null))
            yield return new PaletteEntry(PaletteEntryKind.Action, action.Id, action.Label, Localization["D_QuickActions"], action.Glyph, action.Route, action.Parameter);

        if (Preferences is { } preferences && Company is { } company)
        {
            foreach (var filter in preferences.Filters.Where(filter => filter.CompanyId == company.Id && (filter.FiscalYearId == null || filter.FiscalYearId == reportingFiscalYearId)))
            {
                if (filter.ToRequest() is not { } request) continue;
                yield return new PaletteEntry(PaletteEntryKind.Filter, "filter:" + filter.Id.ToString("N"), filter.Name, Localization["UI_SavedFilter"], "", filter.Route, request);
            }
        }

        yield return Command("cmd.theme.light", "UI_Cmd_ThemeLight", "", () => ApplyTheme(ThemePreference.Light));
        yield return Command("cmd.theme.dark", "UI_Cmd_ThemeDark", "", () => ApplyTheme(ThemePreference.Dark));
        yield return Command("cmd.theme.system", "UI_Cmd_ThemeSystem", "", () => ApplyTheme(ThemePreference.System));
        foreach (var skin in Enum.GetValues<ThemeSkin>())
            yield return Command("cmd.skin." + skin, "UI_Skin_" + skin, "", () => Preferences?.SetSkin(skin));
        yield return Command("cmd.language", "UI_Cmd_Language", "", () => ToggleLanguageCommand.Execute(null));
        yield return Command("cmd.notifications", "UI_Notifications", "", () => Notifications.TogglePanelCommand.Execute(null));
        if (PreferencesPage is not null) yield return Command("cmd.preferences", "UI_Preferences", "", () => _ = NavigateToAsync("preferences", null));
        yield return Command("cmd.template-designer", "PRN_TemplateDesignerTitle", "\uE790", () => OpenTemplateDesignerCommand.Execute(null));
    }

    private PaletteEntry Command(string id, string labelKey, string glyph, Action action) =>
        new(PaletteEntryKind.Command, id, Localization[labelKey], Localization["UI_Command"], glyph, "home", null, Run: () => { action(); return Task.CompletedTask; });

    private void ApplyTheme(ThemePreference theme)
    {
        if (Preferences is { } preferences) preferences.SetTheme(theme);
        else { Themes.Apply(theme); SaveAppearance(); }
    }

    /// <summary>Saves a report filter and confirms it with a toast (the filter then appears in Ctrl+K).</summary>
    private sealed class NotifyingFilterSink(UserPreferencesService preferences, NotificationCenter notifications) : ISavedFilterSink
    {
        public void Save(SavedFilter filter)
        {
            preferences.Save(filter);
            notifications.Success("UI_FilterSaved");
        }
    }
}
