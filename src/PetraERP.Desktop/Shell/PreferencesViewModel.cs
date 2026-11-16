using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Productivity;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Shell;

/// <summary>A labelled choice for the preference pickers.</summary>
public sealed record PreferenceOption<T>(T Value, string Label);

/// <summary>
/// User preferences screen (no permission needed: presentation only). Theme, language, density and number display apply
/// instantly. "Reset layouts" removes this user's saved grid layouts for the current company/branch on the server and
/// restores every open grid to its designed columns; "Reset appearance" restores the defaults and removes saved filters.
/// </summary>
public sealed partial class PreferencesViewModel : ObservableObject
{
    private readonly UserPreferencesService preferences;
    private readonly WorkspaceViewModel workspace;
    private readonly IUserConfirmation confirmation;
    private readonly NotificationCenter notifications;
    private readonly Action saveAppearance;

    public PreferencesViewModel(UserPreferencesService preferences, WorkspaceViewModel workspace, LocalizationService localization, IUserConfirmation confirmation,
        NotificationCenter notifications, Action saveAppearance)
    {
        this.preferences = preferences; this.workspace = workspace; this.confirmation = confirmation; this.notifications = notifications; this.saveAppearance = saveAppearance;
        Localization = localization;
        ResetLayoutsCommand = new AsyncRelayCommand(ResetLayoutsAsync, () => workspace.ContextReady);
        ResetAppearanceCommand = new AsyncRelayCommand(ResetAppearanceAsync);
        DeleteFilterCommand = preferences.DeleteFilterCommand;
        localization.LanguageChanged += (_, _) => Localize();
        preferences.PropertyChanged += (_, _) => Sync();
        preferences.Filters.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasFilters));
        workspace.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(WorkspaceViewModel.ContextReady)) ResetLayoutsCommand.NotifyCanExecuteChanged(); };
        Localize();
    }

    public LocalizationService Localization { get; }
    public IReadOnlyList<PreferenceOption<ThemePreference>> Themes { get; private set; } = [];
    public IReadOnlyList<PreferenceOption<DensityMode>> Densities { get; private set; } = [];
    public IReadOnlyList<PreferenceOption<ThemeSkin>> Skins { get; private set; } = [];
    public IReadOnlyList<PreferenceOption<NumberDisplayMode>> NumberModes { get; private set; } = [];
    public IReadOnlyList<PreferenceOption<DecimalDisplayMode>> DecimalModes { get; private set; } = [];
    public PreferenceOption<DecimalDisplayMode>? DecimalMode
    {
        get => DecimalModes.FirstOrDefault(o => o.Value == preferences.Decimals);
        set { if (value is not null) { preferences.SetDecimals(value.Value); OnPropertyChanged(); Sync(); } }
    }
    public IReadOnlyList<PreferenceOption<WindowPresentationMode>> WindowModes { get; private set; } = [];

    // Document entry (warnings and starting values); saved per user on this computer, applied to the next document.
    public bool EntryMaxStockWarning
    {
        get => preferences.Entry.MaxStockWarning;
        set { if (value != preferences.Entry.MaxStockWarning) { preferences.SetEntryOptions(preferences.Entry with { MaxStockWarning = value }); OnPropertyChanged(); } }
    }
    public bool EntryPastDateWarning
    {
        get => preferences.Entry.PastDateWarning;
        set { if (value != preferences.Entry.PastDateWarning) { preferences.SetEntryOptions(preferences.Entry with { PastDateWarning = value }); OnPropertyChanged(); } }
    }
    public bool EntryDuplicateItemWarning
    {
        get => preferences.Entry.DuplicateItemWarning;
        set { if (value != preferences.Entry.DuplicateItemWarning) { preferences.SetEntryOptions(preferences.Entry with { DuplicateItemWarning = value }); OnPropertyChanged(); } }
    }
    public bool EntryConfirmOnSave
    {
        get => preferences.Entry.ConfirmOnSave;
        set { if (value != preferences.Entry.ConfirmOnSave) { preferences.SetEntryOptions(preferences.Entry with { ConfirmOnSave = value }); OnPropertyChanged(); } }
    }
    public bool EntryConfirmOnExit
    {
        get => preferences.Entry.ConfirmOnExit;
        set { if (value != preferences.Entry.ConfirmOnExit) { preferences.SetEntryOptions(preferences.Entry with { ConfirmOnExit = value }); OnPropertyChanged(); } }
    }
    // Addition policies
    public bool EntryAutoGenerateCodeOnAdd
    {
        get => preferences.Entry.AutoGenerateCodeOnAdd;
        set { if (value != preferences.Entry.AutoGenerateCodeOnAdd) { preferences.SetEntryOptions(preferences.Entry with { AutoGenerateCodeOnAdd = value }); OnPropertyChanged(); } }
    }
    public bool EntryConfirmOnAdd
    {
        get => preferences.Entry.ConfirmOnAdd;
        set { if (value != preferences.Entry.ConfirmOnAdd) { preferences.SetEntryOptions(preferences.Entry with { ConfirmOnAdd = value }); OnPropertyChanged(); } }
    }

    // Editing policies
    public bool EntryPreventModifyingCodeWithMovements
    {
        get => preferences.Entry.PreventModifyingCodeWithMovements;
        set { if (value != preferences.Entry.PreventModifyingCodeWithMovements) { preferences.SetEntryOptions(preferences.Entry with { PreventModifyingCodeWithMovements = value }); OnPropertyChanged(); } }
    }
    public bool EntryPreventEditingPostedDocuments
    {
        get => preferences.Entry.PreventEditingPostedDocuments;
        set { if (value != preferences.Entry.PreventEditingPostedDocuments) { preferences.SetEntryOptions(preferences.Entry with { PreventEditingPostedDocuments = value }); OnPropertyChanged(); } }
    }
    public bool EntryWarnOnBelowCost
    {
        get => preferences.Entry.WarnOnBelowCost;
        set { if (value != preferences.Entry.WarnOnBelowCost) { preferences.SetEntryOptions(preferences.Entry with { WarnOnBelowCost = value }); OnPropertyChanged(); } }
    }
    public bool EntryWarnOnPriceChange
    {
        get => preferences.Entry.WarnOnPriceChange;
        set { if (value != preferences.Entry.WarnOnPriceChange) { preferences.SetEntryOptions(preferences.Entry with { WarnOnPriceChange = value }); OnPropertyChanged(); } }
    }
    public bool EntryConfirmOnEdit
    {
        get => preferences.Entry.ConfirmOnEdit;
        set { if (value != preferences.Entry.ConfirmOnEdit) { preferences.SetEntryOptions(preferences.Entry with { ConfirmOnEdit = value }); OnPropertyChanged(); } }
    }
    public bool EntryConfirmOnDiscard
    {
        get => preferences.Entry.ConfirmOnDiscard;
        set { if (value != preferences.Entry.ConfirmOnDiscard) { preferences.SetEntryOptions(preferences.Entry with { ConfirmOnDiscard = value }); OnPropertyChanged(); } }
    }

    public bool EntrySalesStartsOnCredit
    {
        get => preferences.Entry.SalesStartsOnCredit;
        set { if (value != preferences.Entry.SalesStartsOnCredit) { preferences.SetEntryOptions(preferences.Entry with { SalesStartsOnCredit = value }); OnPropertyChanged(); } }
    }
    public bool EntryPurchasesStartOnCredit
    {
        get => preferences.Entry.PurchasesStartOnCredit;
        set { if (value != preferences.Entry.PurchasesStartOnCredit) { preferences.SetEntryOptions(preferences.Entry with { PurchasesStartOnCredit = value }); OnPropertyChanged(); } }
    }
    public IReadOnlyList<int> InvoiceRowChoices { get; } = [10, 20, 30, 50, 100, 200];
    public int EntryInvoiceRows
    {
        get => preferences.Entry.InvoiceRows;
        set { if (value != preferences.Entry.InvoiceRows) { preferences.SetEntryOptions(preferences.Entry with { InvoiceRows = value }); OnPropertyChanged(); } }
    }
    public PreferenceOption<WindowPresentationMode>? WindowMode
    {
        get => WindowModes.FirstOrDefault(o => o.Value == preferences.WindowMode);
        set { if (value is not null && value.Value != preferences.WindowMode) { preferences.SetWindowMode(value.Value); OnPropertyChanged(); } }
    }
    public IReadOnlyList<PreferenceOption<string>> Languages { get; private set; } = [];
    public System.Collections.ObjectModel.ObservableCollection<SavedFilter> Filters => preferences.Filters;
    public bool HasFilters => preferences.Filters.Count > 0;
    public AsyncRelayCommand ResetLayoutsCommand { get; }
    public AsyncRelayCommand ResetAppearanceCommand { get; }
    public RelayCommand<SavedFilter> DeleteFilterCommand { get; }
    /// <summary>A formatted sample so the user sees the effect of the number display choice.</summary>
    public string NumberSample { get; private set; } = string.Empty;

    public PreferenceOption<ThemePreference>? Theme
    {
        get => Themes.FirstOrDefault(option => option.Value == preferences.Theme);
        set { if (value is not null && value.Value != preferences.Theme) { preferences.SetTheme(value.Value); OnPropertyChanged(); } }
    }

    public PreferenceOption<ThemeSkin>? Skin
    {
        get => Skins.FirstOrDefault(option => option.Value == preferences.Skin);
        set { if (value is not null && value.Value != preferences.Skin) { preferences.SetSkin(value.Value); OnPropertyChanged(); } }
    }

    public PreferenceOption<DensityMode>? Density
    {
        get => Densities.FirstOrDefault(option => option.Value == preferences.Density);
        set { if (value is not null && value.Value != preferences.Density) { preferences.SetDensity(value.Value); OnPropertyChanged(); } }
    }

    public PreferenceOption<NumberDisplayMode>? NumberMode
    {
        get => NumberModes.FirstOrDefault(option => option.Value == preferences.Numbers);
        set { if (value is not null && value.Value != preferences.Numbers) { preferences.SetNumbers(value.Value); OnPropertyChanged(); Sync(); } }
    }

    public PreferenceOption<string>? Language
    {
        get => Languages.FirstOrDefault(option => option.Value == Localization.Language);
        set { if (value is not null && value.Value != Localization.Language) { Localization.SetLanguage(value.Value); saveAppearance(); } }
    }

    /// <summary>The local presentation choices, kept so that Cancel in the program options window can restore them.</summary>
    public sealed record AppearanceSnapshot(ThemePreference Theme, ThemeSkin Skin, DensityMode Density, NumberDisplayMode Numbers,
        DecimalDisplayMode Decimals, WindowPresentationMode WindowMode, string Language, Reporting.ReportDisplayOptions Report, EntryOptions Entry);

    public AppearanceSnapshot CaptureAppearance() =>
        new(preferences.Theme, preferences.Skin, preferences.Density, preferences.Numbers, preferences.Decimals, preferences.WindowMode, Localization.Language, preferences.ReportOptions, preferences.Entry);

    /// <summary>Puts every local presentation choice back to the captured value (only the ones that changed).</summary>
    public void RestoreAppearance(AppearanceSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (preferences.Theme != value.Theme) preferences.SetTheme(value.Theme);
        if (preferences.Skin != value.Skin) preferences.SetSkin(value.Skin);
        if (preferences.Density != value.Density) preferences.SetDensity(value.Density);
        if (preferences.Numbers != value.Numbers) preferences.SetNumbers(value.Numbers);
        if (preferences.Decimals != value.Decimals) preferences.SetDecimals(value.Decimals);
        if (preferences.WindowMode != value.WindowMode) preferences.SetWindowMode(value.WindowMode);
        if (preferences.ReportOptions != value.Report) preferences.SetReportOptions(value.Report);
        if (preferences.Entry != value.Entry) preferences.SetEntryOptions(value.Entry);
        if (Localization.Language != value.Language) { Localization.SetLanguage(value.Language); saveAppearance(); }
    }

    private async Task ResetLayoutsAsync()
    {
        if (!await confirmation.ConfirmAsync("UI_ConfirmResetLayouts").ConfigureAwait(true)) return;
        GridLayouts.ResetAllOpen();
        await workspace.ResetGridsAsync().ConfigureAwait(true);
        if (string.IsNullOrEmpty(workspace.Error)) notifications.Success("UI_LayoutsReset");
    }

    private async Task ResetAppearanceAsync()
    {
        if (!await confirmation.ConfirmAsync("UI_ConfirmResetAppearance").ConfigureAwait(true)) return;
        preferences.ResetAppearance();
        notifications.Success("UI_AppearanceReset");
    }

    private void Localize()
    {
        Themes = Enum.GetValues<ThemePreference>().Select(value => new PreferenceOption<ThemePreference>(value, Localization["D_Theme_" + value])).ToArray();
        Skins = Enum.GetValues<ThemeSkin>().Select(value => new PreferenceOption<ThemeSkin>(value, Localization["UI_Skin_" + value])).ToArray();
        Densities = Enum.GetValues<DensityMode>().Select(value => new PreferenceOption<DensityMode>(value, Localization["UI_Density_" + value])).ToArray();
        NumberModes = Enum.GetValues<NumberDisplayMode>().Select(value => new PreferenceOption<NumberDisplayMode>(value, Localization["UI_Numbers_" + value])).ToArray();
        DecimalModes = Enum.GetValues<DecimalDisplayMode>().Select(value => new PreferenceOption<DecimalDisplayMode>(value, Localization["UI_Decimals_" + value])).ToArray();
        OnPropertyChanged(nameof(DecimalModes));
        Languages = [new(LocalizationService.Arabic, "العربية"), new(LocalizationService.English, "English")];
        WindowModes =
        [
            new(WindowPresentationMode.Tabbed, Localization["UI_WindowMode_Tabbed"]),
            new(WindowPresentationMode.SeparateWindows, Localization["UI_WindowMode_SeparateWindows"]),
            new(WindowPresentationMode.ClassicMdi, Localization["UI_WindowMode_ClassicMdi"]),
        ];
        OnPropertyChanged(nameof(WindowModes));
        OnPropertyChanged(nameof(WindowMode));
        OnPropertyChanged(nameof(Themes)); OnPropertyChanged(nameof(Skins)); OnPropertyChanged(nameof(Densities)); OnPropertyChanged(nameof(NumberModes)); OnPropertyChanged(nameof(Languages));
        Sync();
    }

    private void Sync()
    {
        OnPropertyChanged(nameof(DecimalMode)); OnPropertyChanged(nameof(Theme)); OnPropertyChanged(nameof(Skin)); OnPropertyChanged(nameof(Density)); OnPropertyChanged(nameof(NumberMode)); OnPropertyChanged(nameof(Language));
        NumberSample = PetraNumberFormat.Format(1234567890m, NumberKind.Quantity) + "   " + PetraNumberFormat.Format(-1234567.5m, NumberKind.Money, true) + "   " + PetraNumberFormat.Format(98765.4321m, NumberKind.Quantity);
        OnPropertyChanged(nameof(NumberSample)); OnPropertyChanged(nameof(HasFilters));
    }
}
