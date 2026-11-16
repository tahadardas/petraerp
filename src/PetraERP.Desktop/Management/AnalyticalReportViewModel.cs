using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Management;

public sealed record AnalyticalReportOption(string Id, string Title);

/// <summary>Opens the analytical report page with a report and its criteria (used by saved filters).</summary>
public sealed record OpenAnalyticalReportRequest(string ReportId, DateOnly? From = null, DateOnly? To = null, string? CurrencyCode = null, Guid? AccountId = null);

/// <summary>
/// One page for the server-computed analytical reports (currency ledger / TB, dormant accounts, cash flow, monthly P&amp;L).
/// Columns come from the server response, so the page never guesses a layout; amounts are shown exactly as the server returned them.
/// </summary>
public sealed partial class AnalyticalReportViewModel : PageViewModel, ISavedFilterSource
{
    public const string ViewPermission = "Ledger.Reports.View";
    private const string RoutePrefix = "/reports/analytical/";

    private readonly IManagementClient client;

    [ObservableProperty] private AnalyticalReportOption? selectedReport;
    [ObservableProperty] private DateTime fromDate = new(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private string currencyCode = string.Empty;
    [ObservableProperty] private string accountIdText = string.Empty;
    [ObservableProperty] private DataTable? table;
    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string totalsText = string.Empty;
    [ObservableProperty] private string notesText = string.Empty;
    [ObservableProperty] private string validation = string.Empty;

    public AnalyticalReportViewModel(IManagementClient client, DesktopOperation operation, ILocalizationService localization) : base(operation, localization)
    {
        this.client = client;
        SaveFilterCommand = new RelayCommand(() => { if (CaptureFilter(CompanyId) is { } filter) FilterSink?.Save(filter); }, () => FilterSink is not null && SelectedReport is not null);
        RunCommand = new AsyncRelayCommand(() => RunAsync(RunReportAsync), () => HasContext && Idle && SelectedReport is not null);
    }

    public ObservableCollection<AnalyticalReportOption> Reports { get; } =
    [
        new("FX-04", "دفتر الأستاذ بالعملة"),
        new("FX-05", "ميزان المراجعة بالعملة"),
        new("GL-08", "الحسابات الراكدة"),
        new("FIN-03", "التدفقات النقدية (مباشر)"),
        new("FIN-04", "التدفقات النقدية (غير مباشر)"),
        new("FIN-08", "الإيرادات والمصروفات الشهرية"),
        new("FIN-09", "نتائج الفروع"),
        new("SALES-02", "مبيعات شهرية"),
        new("SALES-03", "مبيعات بالفرع"),
        new("BUY-02", "مشتريات شهرية"),
        new("GL-12", "قيود غير مرحّلة"),
        new("FX-09", "حركة أسعار الصرف"),
        new("INV-11", "مخزون سالب"),
        new("INV-13", "مواد راكدة"),
        new("INV-07", "طبقات FIFO المفتوحة"),
        new("FX-06", "أرصدة بعملتين"),
        new("FX-07", "فروقات صرف محققة"),
        new("INV-09", "تحويلات المخزون"),
        new("INV-12", "تكلفة البضاعة المباعة"),
        new("GL-10", "التسويات والعكس"),
        new("SALES-12", "مبيعات حسب العملة"),
        new("GL-13", "لقطة ربط الحسابات بالفروع"),
        new("FIN-05", "حساب المتاجرة"),
        new("FIN-06", "حساب التشغيل"),
        new("FIN-07", "حركة حقوق الملكية"),
        new("CC-03", "ميزان مراكز الكلفة"),
        new("CC-04", "ربحية مركز كلفة"),
        new("CC-06", "مراكز الكلفة حسب الفرع"),
        new("CC-07", "مصروفات غير مخصصة"),
        new("CC-08", "مراكز الأكثر حركة"),
        new("CASH-06", "مصاريف نقدية"),
        new("SALES-01", "المبيعات اليومية"),
        new("BUY-01", "المشتريات اليومية"),
        new("SALES-07", "مردودات المبيعات"),
        new("BUY-05", "مردودات المشتريات"),
        new("SALES-10", "هامش ربح فرع"),
        new("INV-10", "مواد تحت الحد"),
        new("INV-16", "تغير أسعار المواد"),
        new("SALES-11", "فواتير مبيعات معلقة"),
        new("BUY-07", "فواتير مشتريات معلقة"),
        new("BUY-08", "تحليل أسعار موردين"),
        new("ARAP-06", "دفعات غير مخصصة"),
        new("ARAP-11", "حركات ربط/فك تسوية"),
        new("GL-11", "قيود معدّلة/معكوسة"),
        new("ARAP-05", "فواتير مستحقة"),
        new("ARAP-07", "سلف ومدفوعات مقدمة"),
        new("ARAP-08", "ملخص تحصيل شهري"),
        new("CASH-05", "حركات شيكات"),
        new("CASH-07", "كشف سندات قبض/دفع"),
        new("INV-14", "دوران المخزون"),
    ];

    public AsyncRelayCommand RunCommand { get; }
    public RelayCommand SaveFilterCommand { get; }

    /// <summary>Set by the shell; receives this page's criteria when the user saves a filter.</summary>
    public ISavedFilterSink? FilterSink { get => filterSink; set { filterSink = value; SaveFilterCommand.NotifyCanExecuteChanged(); } }
    private ISavedFilterSink? filterSink;

    public SavedFilter? CaptureFilter(Guid companyId)
    {
        if (companyId == Guid.Empty || SelectedReport is null) return null;
        Guid? account = Guid.TryParse(AccountIdText.Trim(), out var parsed) ? parsed : null;
        var from = DateOnly.FromDateTime(FromDate); var to = DateOnly.FromDateTime(ToDate);
        return new SavedFilter(Guid.NewGuid(), UserPreferencesService.DescribeFilter(SelectedReport.Title, from, to), "analytical-report", companyId, from, to, account,
            ReportId: SelectedReport.Id, CurrencyCode: CurrencyCode.Trim().Length == 0 ? null : CurrencyCode.Trim().ToUpperInvariant());
    }
    protected override string RequiredPermission => ViewPermission;

    /// <summary>Maps a catalogue route (<c>/reports/analytical/FX-04</c>) to its report id.</summary>
    public static bool TryReportId(string? route, out string reportId)
    {
        reportId = string.Empty;
        if (route is null || !route.StartsWith(RoutePrefix, StringComparison.OrdinalIgnoreCase)) return false;
        reportId = route[RoutePrefix.Length..];
        return reportId.Length > 0;
    }

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is OpenAnalyticalReportRequest request)
        {
            if (Reports.FirstOrDefault(r => string.Equals(r.Id, request.ReportId, StringComparison.OrdinalIgnoreCase)) is { } found) SelectedReport = found;
            if (request.From is { } from) FromDate = from.ToDateTime(TimeOnly.MinValue);
            if (request.To is { } to) ToDate = to.ToDateTime(TimeOnly.MinValue);
            CurrencyCode = request.CurrencyCode ?? string.Empty;
            AccountIdText = request.AccountId?.ToString() ?? string.Empty;
        }
        else if (parameter is string id && Reports.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) is { } option) SelectedReport = option;
        SelectedReport ??= Reports[0];
        return Task.CompletedTask;
    }

    protected override void ClearPage() { Table = null; Title = TotalsText = NotesText = Validation = string.Empty; }
    protected override void NotifyCommands() => RunCommand?.NotifyCanExecuteChanged();
    partial void OnSelectedReportChanged(AnalyticalReportOption? value) { RunCommand.NotifyCanExecuteChanged(); SaveFilterCommand.NotifyCanExecuteChanged(); }

    private async Task RunReportAsync(CancellationToken token)
    {
        Validation = string.Empty;
        if (SelectedReport is null) return;
        if (FromDate > ToDate) { Validation = "تاريخ البداية بعد تاريخ النهاية"; return; }
        Guid? account = null;
        if (AccountIdText.Trim().Length > 0)
        {
            if (!Guid.TryParse(AccountIdText.Trim(), out var parsed)) { Validation = "معرّف الحساب غير صالح"; return; }
            account = parsed;
        }

        var currency = CurrencyCode.Trim().Length == 0 ? null : CurrencyCode.Trim().ToUpperInvariant();
        var r = await client.RunAnalyticalReportAsync(CompanyId, BranchId, SelectedReport.Id, DateOnly.FromDateTime(FromDate), DateOnly.FromDateTime(ToDate), currency, account, token).ConfigureAwait(true);
        var columns = r.Columns ?? [];
        var dt = new DataTable();
        for (var i = 0; i < columns.Count; i++) dt.Columns.Add("c" + i.ToString(CultureInfo.InvariantCulture), typeof(string)).Caption = columns[i].TitleArabic ?? columns[i].Key ?? string.Empty;
        foreach (var row in r.Rows ?? [])
        {
            var cells = row.Cells ?? [];
            dt.Rows.Add(Enumerable.Range(0, columns.Count).Select(i => i < cells.Count ? cells[i] : string.Empty).Cast<object>().ToArray());
        }

        Table = dt;
        Title = r.TitleArabic ?? SelectedReport.Title;
        TotalsText = r.Totals is { Count: > 0 } t ? "الإجماليات: " + string.Join(" | ", t) : string.Empty;
        NotesText = r.Notes is { Count: > 0 } n ? string.Join(Environment.NewLine, n) : string.Empty;
    }

    /// <summary>
    /// Export snapshot of exactly what is on screen. Cells stay the server's text (no re-parsing of amounts), so Excel/PDF show the
    /// same lossless values; filters used for the run are disclosed on the Filters sheet.
    /// </summary>
    public PetraERP.Reporting.OpenSource.ReportDocument? BuildDocument()
    {
        if (Table is null || Table.Columns.Count == 0 || Table.Columns.Count > PetraERP.Reporting.OpenSource.ReportDocument.MaximumColumns) return null;
        var columns = Table.Columns.Cast<DataColumn>()
            .Select(c => new PetraERP.Reporting.OpenSource.ReportColumn(c.ColumnName, string.IsNullOrWhiteSpace(c.Caption) ? c.ColumnName : c.Caption)).ToList();
        var rows = Table.Rows.Cast<DataRow>().Select(r => (IReadOnlyDictionary<string, object?>)Table.Columns.Cast<DataColumn>()
            .ToDictionary(c => c.ColumnName, c => (object?)(r[c]?.ToString() ?? string.Empty))).ToList();
        var filters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["From"] = FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["To"] = ToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrWhiteSpace(CurrencyCode)) filters["Currency"] = CurrencyCode.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(TotalsText)) filters["Totals"] = TotalsText;
        var id = SelectedReport?.Id ?? "REPORT";
        return new PetraERP.Reporting.OpenSource.ReportDocument(id.Replace('-', '_'), string.IsNullOrWhiteSpace(Title) ? id : Title, string.Empty,
            filters["From"] + " – " + filters["To"], filters.GetValueOrDefault("Currency", string.Empty), columns, rows, filters);
    }

    /// <summary>CSV text (UTF-8 BOM added by the view) of exactly what is on screen.</summary>
    public string BuildCsv()
    {
        if (Table is null) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendJoin(',', Table.Columns.Cast<DataColumn>().Select(c => Quote(c.Caption))).Append("\r\n");
        foreach (DataRow row in Table.Rows) sb.AppendJoin(',', row.ItemArray.Select(v => Quote(v?.ToString() ?? string.Empty))).Append("\r\n");
        return sb.ToString();
    }

    private static string Quote(string v)
    {
        // Neutralise spreadsheet formula injection while keeping plain negatives readable.
        if (v.Length > 0 && v[0] is '=' or '+' or '@' && !decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) v = "'" + v;
        return '"' + v.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }
}
