using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Reporting;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// A saved report filter: criteria only (dates, ids, flags) for one route in one company. It holds no amounts, no
/// results and no expressions; opening it re-runs the report through the normal authorized path.
/// </summary>
public sealed record SavedFilter(Guid Id, string Name, string Route, Guid CompanyId, DateOnly? From = null, DateOnly? To = null,
    Guid? AccountId = null, Guid? PeriodId = null, Guid? CostCenterId = null, bool? Consolidated = null, bool? IncludeZeroRows = null, int? MaximumLevel = null, Guid? FiscalYearId = null,
    string? ReportId = null, string? CurrencyCode = null)
{
    public const int MaximumPerUser = 30;

    /// <summary>The typed drill-down request the route's page already understands.</summary>
    public object? ToRequest() => Route switch
    {
        "trial-balance" => new OpenTrialBalanceRequest(PeriodId, From, To, IncludeZeroRows, MaximumLevel, Consolidated),
        "account-ledger" when AccountId is { } account => new OpenAccountLedgerRequest(account, From, To, Consolidated ?? false),
        "general-journal" => new OpenGeneralJournalRequest(CostCenterId, AccountId, From, To, Consolidated),
        "analytical-report" when ReportId is { Length: > 0 } report => new Management.OpenAnalyticalReportRequest(report, From, To, CurrencyCode, AccountId),
        _ => null,
    };
}

/// <summary>A report page that can describe its current criteria as a saved filter.</summary>
public interface ISavedFilterSource
{
    /// <summary>The current criteria, or null when there is nothing meaningful to save yet.</summary>
    SavedFilter? CaptureFilter(Guid companyId);
}

/// <summary>Receives "save this filter" requests from report pages (the preference service).</summary>
public interface ISavedFilterSink
{
    void Save(SavedFilter filter);
}

public enum WindowPresentationMode
{
    Tabbed = 0,
    SeparateWindows = 1,
    /// <summary>Al-Ameen style: documents are internal windows (title bar, move, resize, minimize, maximize, tile, cascade) inside the main window.</summary>
    ClassicMdi = 2,
}

/// <summary>
/// How invoices are entered on this computer: the warnings shown before saving and the starting values of a new document.
/// Server rules (credit policy, stock policy, posting) stay with the company options and are never overridden here.
/// </summary>
public sealed record EntryOptions
{
    public const int MinRows = 10, MaxRows = 200, DefaultRows = 50;

    // --- Addition Policies (سياسات الإضافة) ---
    /// <summary>توليد الرمز تلقائياً عند إضافة بطاقة مادة أو حساب جديد.</summary>
    public bool AutoGenerateCodeOnAdd { get; init; } = true;
    /// <summary>A new sales invoice starts as a credit (receivable) invoice instead of cash.</summary>
    public bool SalesStartsOnCredit { get; init; }
    /// <summary>A new purchase invoice starts as a credit (payable) invoice instead of cash.</summary>
    public bool PurchasesStartOnCredit { get; init; } = true;
    /// <summary>Empty lines prepared in a new invoice (Al-Ameen shows a fixed grid).</summary>
    public int InvoiceRows { get; init; } = DefaultRows;
    /// <summary>Ask before saving an invoice that lists the same item on more than one line.</summary>
    public bool DuplicateItemWarning { get; init; } = true;
    /// <summary>Ask for confirmation when a purchase would push a material over its maximum stock.</summary>
    public bool MaxStockWarning { get; init; } = true;
    /// <summary>Ask before saving a document whose date is earlier than today (the fiscal-year limits are always enforced).</summary>
    public bool PastDateWarning { get; init; } = true;
    /// <summary>طلب تأكيد الحفظ عند إضافة مستند أو بطاقة جديدة.</summary>
    public bool ConfirmOnAdd { get; init; }

    // --- Editing Policies (سياسات التعديل) ---
    /// <summary>منع تعديل رمز الحساب أو المادة عند وجود حركات سابقة.</summary>
    public bool PreventModifyingCodeWithMovements { get; init; } = true;
    /// <summary>منع تعديل المستندات والفواتير المرحلة.</summary>
    public bool PreventEditingPostedDocuments { get; init; } = true;
    /// <summary>تحذير عند تعديل سعر المادة بالبيع ليكون أقل من سعر التكلفة.</summary>
    public bool WarnOnBelowCost { get; init; } = true;
    /// <summary>تحذير عند تعديل سعر المادة عن السعر المحدد في بطاقتها.</summary>
    public bool WarnOnPriceChange { get; init; }
    /// <summary>طلب تأكيد الحفظ عند تعديل مستند أو بطاقة موجودة مسبقاً.</summary>
    public bool ConfirmOnEdit { get; init; }
    /// <summary>طلب تأكيد تجاهل التعديلات غير المحفوظة عند الإغلاق.</summary>
    public bool ConfirmOnDiscard { get; init; } = true;

    // --- General Options ---
    /// <summary>Ask for confirmation before every invoice save.</summary>
    public bool ConfirmOnSave { get; init; }
    /// <summary>Ask for confirmation before the program closes.</summary>
    public bool ConfirmOnExit { get; init; }

    public EntryOptions Sanitized() => this with { InvoiceRows = Math.Clamp(InvoiceRows, MinRows, MaxRows) };
}

/// <summary>The current user's <see cref="EntryOptions"/>, published by <see cref="UserPreferencesService"/> for the document screens.</summary>
public static class EntryOptionsHub
{
    public static EntryOptions Current { get; private set; } = new();
    public static void Set(EntryOptions options) => Current = (options ?? new()).Sanitized();
}

/// <summary>Presentation-only preferences kept on this computer, per signed-in user. Never financial data.</summary>
public sealed record LocalUserPreferences
{
    public ThemePreference Theme { get; init; } = ThemePreference.System;
    public DensityMode Density { get; init; } = DensityMode.Standard;
    public ThemeSkin Skin { get; init; } = ThemeSkin.Petra;
    public NumberDisplayMode Numbers { get; init; } = NumberDisplayMode.Invariant;
    public DecimalDisplayMode Decimals { get; init; } = DecimalDisplayMode.Automatic;
    /// <summary>Al-Ameen style internal windows are the reference presentation; Tabbed and SeparateWindows stay available.</summary>
    public WindowPresentationMode WindowMode { get; init; } = WindowPresentationMode.ClassicMdi;
    /// <summary>Presentation of report results and of the report request window (never affects a query or a total).</summary>
    public ReportDisplayOptions Report { get; init; } = new();
    /// <summary>Document-entry behaviour (Al-Ameen "خيارات البرنامج": warnings and default values). Never changes a total or a posting.</summary>
    public EntryOptions Entry { get; init; } = new();
    public IReadOnlyList<SavedFilter> Filters { get; init; } = [];
}

public interface IUserPreferenceStore
{
    /// <summary>Preferences of a user; <paramref name="userId"/> null is the device default used before sign-in.</summary>
    LocalUserPreferences Read(Guid? userId);
    bool Has(Guid? userId);
    void Save(Guid? userId, LocalUserPreferences preferences);
}

/// <summary>JSON files under %LOCALAPPDATA%\PetraERP\Preferences, one per user (bounded size, atomic replace).</summary>
public sealed class UserPreferenceStore(string directory) : IUserPreferenceStore
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private string PathFor(Guid? userId) => Path.Combine(directory, (userId?.ToString("N") ?? "device") + ".json");

    public bool Has(Guid? userId) => File.Exists(PathFor(userId));

    public LocalUserPreferences Read(Guid? userId)
    {
        try
        {
            var path = PathFor(userId);
            if (File.Exists(path) && new FileInfo(path).Length <= 64 * 1024)
            {
                var preferences = JsonSerializer.Deserialize<LocalUserPreferences>(File.ReadAllText(path), Json) ?? new();
                // Al-Ameen standard: classic internal MDI is the reference and enforced default.
                if (preferences.WindowMode != WindowPresentationMode.ClassicMdi)
                    preferences = preferences with { WindowMode = WindowPresentationMode.ClassicMdi };
                return preferences;
            }
            return new();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return new();
        }
    }

    public void Save(Guid? userId, LocalUserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        try
        {
            Directory.CreateDirectory(directory);
            var path = PathFor(userId); var temporary = path + ".tmp";
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, Json)); File.Move(temporary, path, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience; a locked or read-only profile must not break the workflow.
        }
    }
}

/// <summary>
/// Applies and remembers appearance preferences: theme (also saved to the server workspace preferences when a company
/// context exists), density and number display (this computer only), and saved report filters. Theme changes are applied
/// instantly through <see cref="IThemeService"/>.
/// </summary>
public sealed partial class UserPreferencesService : ObservableObject, ISavedFilterSink
{
    private readonly IUserPreferenceStore store;
    private readonly IThemeService themes;
    private readonly ILocalizationService localization;
    private Guid? userId;
    private LocalUserPreferences current;

    public UserPreferencesService(IUserPreferenceStore store, IThemeService themes, ILocalizationService localization)
    {
        this.store = store; this.themes = themes; this.localization = localization;
        current = store.Read(null);
        localization.LanguageChanged += (_, _) => ApplyNumbers();
        SetThemeCommand = new RelayCommand<ThemePreference>(SetTheme);
        SetDensityCommand = new RelayCommand<DensityMode>(SetDensity);
        SetNumbersCommand = new RelayCommand<NumberDisplayMode>(SetNumbers);
        DeleteFilterCommand = new RelayCommand<SavedFilter>(DeleteFilter);
    }

    /// <summary>Raised after the theme changes so the shell can also save it to the server workspace preferences.</summary>
    public event EventHandler? ThemeSaved;
    /// <summary>Raised when saved filters change (the palette lists them).</summary>
    public event EventHandler? FiltersChanged;

    public ObservableCollection<SavedFilter> Filters { get; } = [];
    public ThemePreference Theme => current.Theme;
    public DensityMode Density => current.Density;
    public ThemeSkin Skin => current.Skin;
    public NumberDisplayMode Numbers => current.Numbers;
    public DecimalDisplayMode Decimals => current.Decimals;
    public void SetDecimals(DecimalDisplayMode value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
        current = current with { Decimals = value }; ApplyNumbers(); Persist(); OnPropertyChanged(nameof(Decimals));
    }
    public WindowPresentationMode WindowMode => current.WindowMode;
    public void SetWindowMode(WindowPresentationMode value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
        current = current with { WindowMode = value }; Persist(); OnPropertyChanged(nameof(WindowMode));
    }
    public ReportDisplayOptions ReportOptions => current.Report;
    public EntryOptions Entry => current.Entry;

    /// <summary>Saves the document-entry options for this user and publishes them to the invoice screens.</summary>
    public void SetEntryOptions(EntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        current = current with { Entry = options.Sanitized() };
        Persist(); EntryOptionsHub.Set(current.Entry); OnPropertyChanged(nameof(Entry));
    }

    /// <summary>Saves the report presentation options for this user and publishes them to open report grids and request windows.</summary>
    public void SetReportOptions(ReportDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        current = current with { Report = options.Sanitized() };
        Persist(); ReportDisplayOptionsHub.Set(current.Report); OnPropertyChanged(nameof(ReportOptions));
    }

    public RelayCommand<ThemePreference> SetThemeCommand { get; }
    public RelayCommand<DensityMode> SetDensityCommand { get; }
    public RelayCommand<NumberDisplayMode> SetNumbersCommand { get; }
    public RelayCommand<SavedFilter> DeleteFilterCommand { get; }

    /// <summary>Applies the device default (before sign-in) or a user's saved preferences.</summary>
    public void Load(Guid? user)
    {
        userId = user;
        // A user without saved preferences starts from this computer's last appearance (no saved filters carried over).
        current = user is not null && !store.Has(user) ? store.Read(null) with { Filters = [] } : store.Read(user);
        current = current with { Numbers = Enum.IsDefined(current.Numbers) ? current.Numbers : NumberDisplayMode.Invariant,
            Decimals = Enum.IsDefined(current.Decimals) ? current.Decimals : DecimalDisplayMode.Automatic,
            WindowMode = Enum.IsDefined(current.WindowMode) ? current.WindowMode : WindowPresentationMode.ClassicMdi };
        if (!Enum.IsDefined(current.Skin)) current = current with { Skin = ThemeSkin.Petra };
        current = current with { Report = (current.Report ?? new ReportDisplayOptions()).Sanitized() };
        ReportDisplayOptionsHub.Set(current.Report);
        current = current with { Entry = (current.Entry ?? new EntryOptions()).Sanitized() };
        EntryOptionsHub.Set(current.Entry);
        themes.ApplySkin(current.Skin);
        themes.Apply(current.Theme);
        themes.ApplyDensity(current.Density);
        ApplyNumbers();
        Filters.Clear();
        foreach (var filter in current.Filters) Filters.Add(filter);
        RaiseAll();
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps the local copy in step when the theme is changed elsewhere (top bar toggle, server preferences).</summary>
    public void RememberTheme(ThemePreference theme)
    {
        if (current.Theme == theme) return;
        current = current with { Theme = theme };
        Persist(); OnPropertyChanged(nameof(Theme));
    }

    public void SetTheme(ThemePreference theme)
    {
        themes.Apply(theme);
        RememberTheme(theme);
        ThemeSaved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Chooses the program appearance (مظهر البرنامج); presentation only, remembered per user and on this computer.</summary>
    public void SetSkin(ThemeSkin skin)
    {
        if (!Enum.IsDefined(skin)) throw new ArgumentOutOfRangeException(nameof(skin));
        current = current with { Skin = skin };
        themes.ApplySkin(skin); Persist(); OnPropertyChanged(nameof(Skin));
        ThemeSaved?.Invoke(this, EventArgs.Empty);
    }

    public void SetDensity(DensityMode density)
    {
        current = current with { Density = density };
        themes.ApplyDensity(density); Persist(); OnPropertyChanged(nameof(Density));
    }

    public void SetNumbers(NumberDisplayMode mode)
    {
        current = current with { Numbers = mode };
        ApplyNumbers(); Persist(); OnPropertyChanged(nameof(Numbers)); OnPropertyChanged(nameof(Decimals));
    }

    /// <summary>Saves (or replaces by name) a filter for the signed-in user; the oldest are dropped beyond the limit.</summary>
    public void Save(SavedFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (userId is null) return;
        var list = current.Filters.Where(item => !(item.Route == filter.Route && item.CompanyId == filter.CompanyId && item.Name == filter.Name))
            .Prepend(filter).Take(SavedFilter.MaximumPerUser).ToArray();
        current = current with { Filters = list };
        Persist(); Filters.Clear(); foreach (var item in list) Filters.Add(item);
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteFilter(SavedFilter? filter)
    {
        if (filter is null) return;
        current = current with { Filters = current.Filters.Where(item => item.Id != filter.Id).ToArray() };
        Persist(); Filters.Remove(filter);
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Resets density, number display and theme to defaults and removes saved filters (layouts are reset separately).</summary>
    public void ResetAppearance()
    {
        current = new LocalUserPreferences();
        ReportDisplayOptionsHub.Set(current.Report); EntryOptionsHub.Set(current.Entry);
        themes.ApplySkin(current.Skin); themes.Apply(current.Theme); themes.ApplyDensity(current.Density); ApplyNumbers();
        Persist(); Filters.Clear(); RaiseAll();
        FiltersChanged?.Invoke(this, EventArgs.Empty);
        ThemeSaved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Default filter name: report title and date range (dates as ISO text, digits left to right).</summary>
    public static string DescribeFilter(string routeLabel, DateOnly? from, DateOnly? to) =>
        routeLabel + " · " + (from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "…") + " → " + (to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "…");

    private void ApplyNumbers() => PetraNumberFormat.Configure(current.Numbers, localization.Culture, current.Decimals);

    private void Persist()
    {
        store.Save(userId, current);
        if (userId is not null) store.Save(null, new LocalUserPreferences { Theme = current.Theme, Skin = current.Skin, Density = current.Density, Numbers = current.Numbers, Decimals = current.Decimals, WindowMode = current.WindowMode, Report = current.Report });
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Theme)); OnPropertyChanged(nameof(Skin)); OnPropertyChanged(nameof(Density)); OnPropertyChanged(nameof(Numbers)); OnPropertyChanged(nameof(Decimals)); OnPropertyChanged(nameof(WindowMode));
    }
}
