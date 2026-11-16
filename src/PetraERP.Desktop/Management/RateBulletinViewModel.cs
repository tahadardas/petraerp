using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Management;

/// <summary>One editable bulletin line. A blank rate means the currency is not part of this bulletin.</summary>
public sealed partial class BulletinLine : ObservableObject
{
    public BulletinLine(string code, string name) { Code = code; Name = name; }
    public string Code { get; }
    public string Name { get; }
    [ObservableProperty] private string rate = string.Empty;
    public bool HasRate => !string.IsNullOrWhiteSpace(Rate);
    partial void OnRateChanged(string value) => OnPropertyChanged(nameof(HasRate));
}

/// <summary>Daily rate bulletin: one effective date, 1..50 currencies, saved atomically (all lines or none).</summary>
public sealed partial class RateBulletinViewModel : PageViewModel
{
    public const string ViewPermission = "Ledger.Accounts.View";
    public const string ManagePermission = "Ledger.Accounts.Manage";

    private readonly IManagementClient client;
    private readonly NotificationCenter notifications;

    [ObservableProperty] private DateTime effectiveDate = DateTime.Today;
    [ObservableProperty] private string source = string.Empty;
    [ObservableProperty] private string reason = string.Empty;
    [ObservableProperty] private string baseCurrencyCode = string.Empty;
    [ObservableProperty] private string historyCurrency = string.Empty;
    [ObservableProperty] private DateTime historyFrom = DateTime.Today.AddMonths(-3);
    [ObservableProperty] private DateTime historyTo = DateTime.Today;
    [ObservableProperty] private DateTime copyFromDate = DateTime.Today.AddDays(-1);
    [ObservableProperty] private ExchangeRateDto? selectedHistory;
    [ObservableProperty] private string historyStatus = string.Empty;

    public RateBulletinViewModel(IManagementClient client, NotificationCenter notifications, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.notifications = notifications;
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(LoadAllAsync), () => HasContext && Idle);
        SearchHistoryCommand = new AsyncRelayCommand(() => RunAsync(LoadHistoryAsync), () => HasContext && Idle);
        CopyBulletinCommand = new AsyncRelayCommand(() => RunAsync(CopyBulletinAsync), () => HasContext && Idle);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => HasContext && Idle && CanManage && !string.IsNullOrWhiteSpace(Source) && !string.IsNullOrWhiteSpace(Reason) && Lines.Any(l => l.HasRate));
    }

    public ObservableCollection<BulletinLine> Lines { get; } = [];
    public ObservableCollection<ExchangeRateDto> LastSaved { get; } = [];
    /// <summary>Full registered-rate history (newest first), filtered by currency and effective-date range; one page of up to 200 rows.</summary>
    public ObservableCollection<ExchangeRateDto> History { get; } = [];
    public AsyncRelayCommand SearchHistoryCommand { get; }
    public AsyncRelayCommand CopyBulletinCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public bool CanManage => Can(ManagePermission);

    protected override string RequiredPermission => ViewPermission;
    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken) => RunAsync(LoadAllAsync);

    protected override void ClearPage() { Lines.Clear(); LastSaved.Clear(); History.Clear(); HistoryStatus = string.Empty; Source = string.Empty; Reason = string.Empty; }

    protected override void NotifyCommands() { SearchHistoryCommand?.NotifyCanExecuteChanged(); CopyBulletinCommand?.NotifyCanExecuteChanged(); RefreshCommand?.NotifyCanExecuteChanged(); SaveCommand?.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(CanManage)); }

    partial void OnSourceChanged(string value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnReasonChanged(string value) => SaveCommand.NotifyCanExecuteChanged();

    private async Task LoadAllAsync(CancellationToken token)
    {
        var settings = await client.GetCurrencySettingsAsync(CompanyId, BranchId, token).ConfigureAwait(true);
        BaseCurrencyCode = settings.BaseCurrencyCode ?? string.Empty;
        await LoadHistoryAsync(token).ConfigureAwait(true);
        foreach (var l in Lines) l.PropertyChanged -= LineChanged;
        Lines.Clear();
        foreach (var c in (settings.Currencies ?? []).Where(c => c.IsBase != true && c.IsActive == true).OrderBy(c => c.Code, StringComparer.Ordinal))
        {
            var line = new BulletinLine(c.Code ?? string.Empty, c.NameArabic ?? c.NameEnglish ?? string.Empty);
            line.PropertyChanged += LineChanged;
            Lines.Add(line);
        }
    }

    private async Task LoadHistoryAsync(CancellationToken token)
    {
        if (HistoryFrom > HistoryTo) { HistoryStatus = "تاريخ البداية بعد تاريخ النهاية"; return; }
        var code = HistoryCurrency.Trim().ToUpperInvariant();
        var page = await client.ListRatesAsync(CompanyId, BranchId, code.Length == 0 ? null : code, DateOnly.FromDateTime(HistoryFrom), DateOnly.FromDateTime(HistoryTo), 1, 200, token).ConfigureAwait(true);
        History.Clear();
        foreach (var r in (page.Items ?? []).OrderByDescending(r => r.EffectiveDate).ThenBy(r => r.CurrencyCode, StringComparer.Ordinal)) History.Add(r);
        HistoryStatus = string.Create(CultureInfo.InvariantCulture, $"{History.Count} من {page.TotalCount ?? History.Count}");
    }

    /// <summary>Loads the bulletin registered for <see cref="CopyFromDate"/> into the editor so it can be saved under a new effective date.</summary>
    private async Task CopyBulletinAsync(CancellationToken token)
    {
        var day = DateOnly.FromDateTime(CopyFromDate);
        var page = await client.ListRatesAsync(CompanyId, BranchId, null, day, day, 1, 200, token).ConfigureAwait(true);
        var byCurrency = (page.Items ?? []).Where(r => r.EffectiveDate == day).GroupBy(r => r.CurrencyCode ?? string.Empty, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last().Rate, StringComparer.Ordinal);
        foreach (var line in Lines) line.Rate = byCurrency.TryGetValue(line.Code, out var rate) ? rate : string.Empty;
        HistoryStatus = byCurrency.Count == 0 ? "لا توجد نشرة بهذا التاريخ" : string.Create(CultureInfo.InvariantCulture, $"نُسخت {byCurrency.Count} عملة؛ عدّل تاريخ السريان ثم احفظ");
    }

    private void LineChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => SaveCommand.NotifyCanExecuteChanged();

    private async Task SaveAsync()
    {
        var rates = Lines.Where(l => l.HasRate).Select(l => new BulletinRateRequest { CurrencyCode = l.Code, Rate = l.Rate.Trim().Replace(',', '.') }).ToList();
        var request = new RegisterRateBulletinRequest { EffectiveDate = DateOnly.FromDateTime(EffectiveDate), Source = Source.Trim(), Reason = Reason.Trim(), Rates = rates };
        var ok = await RunAsync(async token =>
        {
            var saved = await client.RegisterRateBulletinAsync(CompanyId, BranchId, request, token).ConfigureAwait(true);
            LastSaved.Clear();
            foreach (var r in saved) LastSaved.Add(r);
            await LoadHistoryAsync(token).ConfigureAwait(true);
        }, OperationKind.Save).ConfigureAwait(true);
        if (ok)
        {
            foreach (var l in Lines) l.Rate = string.Empty;
            notifications.Success("D_Saved");
        }
    }
}
