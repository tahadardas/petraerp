using System.Globalization;
using System.IO;
using System.Printing;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Microsoft.Win32;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Platform;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Reporting;

/// <summary>
/// Modeless native report workspace. Interactive rows, print pages and every export are projections
/// of the same immutable, server-authoritative execution snapshot.
/// </summary>
public partial class PetraReportViewerWindow : Window, IDisposable
{
    private readonly ILocalizationService localization;
    private readonly Action<ProfessionalReportRowDto> openSource;
    private readonly Action? editParameters;
    private readonly ProfessionalPreviewRequest request;
    private readonly Window? contextWindow;
    internal PetraReportWorkspaceStore WorkspaceStore { get; set; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PetraERP", "Preferences", "ProfessionalReportWorkspaces"));
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private IReadOnlyList<ReportDocument> documents = [];
    private readonly ObservableCollection<FilterConditionDisplay> filterConditions = [];
    private IReadOnlyList<PetraReportSearchMatch> searchMatches = [];
    private int searchMatchIndex = -1;
    private IReadOnlyList<PetraReportWorkspace> workspaces = [];
    private readonly PetraReportFilterBuilderViewModel treeFilterBuilderModel = new();
    private readonly PetraReportSummaryManager summaryManager = new();
    private readonly ObservableCollection<PetraConditionalFormatRule> formatRules = [];
    private readonly PetraReportBrandingStore brandingStore = new();
    private PetraReportBranding? currentBranding;
    private bool ready;
    private bool busy;
    private bool disposed;

    /// <summary>Side panels shrink on narrow windows / high DPI so the result grid keeps most of the width.</summary>
    private void AdaptSidebars()
    {
        var share = ActualWidth * 0.32;
        FilterSidebar.Width = Math.Clamp(share, 260, 380);
        FieldsSidebar.Width = Math.Clamp(share, 240, 300);
        SummariesSidebar.Width = Math.Clamp(share, 250, 320);
        ConditionalFormattingSidebar.Width = Math.Clamp(share, 260, 330);
    }

    public PetraReportViewerWindow(
        ProfessionalPreviewRequest request,
        ILocalizationService localization,
        Action<ProfessionalReportRowDto> openSource,
        Window? contextWindow = null,
        Action? editParameters = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(openSource);
        this.request = request;
        this.localization = localization;
        this.openSource = openSource;
        this.contextWindow = contextWindow;
        this.editParameters = editParameters;
        InitializeComponent();
        ResultsGrid.HeaderFilterRequested += HeaderFilterRequested;
        ResultsGrid.AutoFilterRequested += AutoFilterRequested;
        ResultsGrid.HeaderFilterConditionApplied += HeaderFilterConditionApplied;
        ResultsGrid.HeaderFilterCleared += HeaderFilterCleared;
        ResultsGrid.HeaderSelectionProvider = HeaderSelectionFor;
        ResultsGrid.ViewOperationFailed += (_, exception) => ReportFailure(exception);
        FlowDirection = localization.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        InitializeLabels();
        SizeChanged += (_, _) => AdaptSidebars();
        Loaded += (_, _) => AdaptSidebars();
        Loaded += LoadedAsync;
        Closed += (_, _) => Dispose();
        if (contextWindow is not null)
            SourceInitialized += (_, _) => WindowsDocumentPlacement.Apply(this, contextWindow, null, 1);
        SetBusy(true);
    }

    internal ReportDocument? SelectedDocument => Sections.SelectedItem as ReportDocument;
    internal bool IsPrintPreviewBuilt => PrintViewer.Document is not null;
    internal ReportDocument? OutputDocument => ScopedDocument();

    private string T(string ar, string en) => localization.IsRightToLeft ? ar : en;

    /// <summary>Document labels carry both languages so an exported file reads on its own; the window shows only the active one.</summary>
    private string Chip(string text)
    {
        var parts = text.Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return text;
        return (localization.IsRightToLeft ? parts.FirstOrDefault(Arabic) : parts.FirstOrDefault(part => !Arabic(part))) ?? text;
    }

    private static bool Arabic(string text) => text.Any(c => c is >= '؀' and <= 'ۿ');

    /// <summary>Server parameters arrive as enum names; the operator reads them with the wording the criteria dialog used.</summary>
    private string ChipValue(string value)
    {
        if (!localization.IsRightToLeft) return value;
        if (ValueLabels.TryGetValue(value, out var label)) return label;
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 1 && parts.All(SourceLabels.ContainsKey))
            return string.Join("، ", parts.Select(part => SourceLabels[part]));
        return value;
    }

    /// <summary>The report source names the server echoes back, in the same words the criteria list uses.</summary>
    private static readonly Dictionary<string, string> SourceLabels = new(StringComparer.Ordinal)
    {
        ["ManualJournal"] = "قيود يومية", ["SalesInvoice"] = "فواتير مبيعات", ["SalesReturn"] = "مرتجعات وعكس المبيعات",
        ["PurchaseInvoice"] = "فواتير مشتريات", ["PurchaseReturn"] = "مرتجعات المشتريات",
        ["ReceiptVoucher"] = "سندات قبض وعكسها", ["PaymentVoucher"] = "سندات دفع وعكسها",
        ["InventoryDocument"] = "مستندات مخزنية", ["Settlement"] = "تخصيص وتسوية عملات",
        ["UnclassifiedJournal"] = "قيود مرحّلة غير مصنفة المنشأ",
    };

    private static readonly Dictionary<string, string> ValueLabels = new(StringComparer.Ordinal)
    {
        ["Base"] = "الأساسية", ["Transaction"] = "عملة الحركة",
        ["Actual"] = "الرصيد الحقيقي", ["ZeroAtPeriodStart"] = "تصفير بداية المدة",
        ["None"] = "بدون", ["Account"] = "حسب الحساب", ["Source"] = "حسب المصدر", ["Month"] = "حسب الشهر",
        ["Date"] = "التاريخ", ["PostingSequence"] = "تسلسل الترحيل", ["AccountCode"] = "رمز الحساب",
    };

    private void InitializeLabels()
    {
        Title = request.Title + " · " + T("مركز تقارير بترا", "Petra Report Center");
        ReportTitle.Text = request.Title;
        ReportContext.Text = request.Snapshot.CompanyName + " · " + request.Snapshot.FiscalYearCode + " · " + request.Snapshot.BranchName;
        SnapshotCaption.Text = T("نسخة خادم ثابتة", "Fixed server snapshot");
        SnapshotLabel.Text = request.Snapshot.ExecutionId.ToString("D", CultureInfo.InvariantCulture) + " · " +
            request.Snapshot.GeneratedAtUtc.ToString("u", CultureInfo.InvariantCulture);
        SectionLabel.Text = T("القسم", "Section");
        OpenSourceButton.Content = T("فتح المصدر", "Open source");
        OpenSourceButton.ToolTip = T("Enter أو نقرتان على السطر", "Enter or double-click the row");
        ParametersButton.Content = T("المعايير", "Parameters");
        PreviewButton.Content = T("معاينة الطباعة", "Print preview");
        PrintButton.Content = T("طباعة", "Print");
        PdfButton.Content = "PDF";
        ExcelButton.Content = T("Excel تحليلي", "Analytical Excel");
        ExcelPrintButton.Content = T("Excel للطباعة", "Print Excel");
        CsvButton.Content = "CSV";
        CancelButton.Content = T("إلغاء العملية", "Cancel operation");
        CloseButton.Content = T("إغلاق", "Close");
        ResultsTab.Header = T("نتائج تفاعلية", "Interactive results");
        PrintPreviewTab.Header = T("معاينة الطباعة", "Print preview");
        VisibleRowsLabel.Text = T("السجلات الظاهرة:", "Visible rows:");
        PrintScope.ItemsSource = new[]
        {
            new Choice<PetraReportOutputScope>(PetraReportOutputScope.FullExecution, T("التقرير المنفذ كاملاً", "Full executed report")),
            new Choice<PetraReportOutputScope>(PetraReportOutputScope.VisibleRows, T("الصفوف المصفاة الحالية", "Current filtered rows")),
            new Choice<PetraReportOutputScope>(PetraReportOutputScope.SelectedRows, T("الصفوف المحددة", "Selected rows")),
        };
        PrintScope.SelectedIndex = 0;
        SearchBox.ToolTip = T("بحث داخل النتائج (Ctrl+F)", "Search results (Ctrl+F)");
        PreviousMatchButton.Content = T("السابق", "Previous");
        NextMatchButton.Content = T("التالي", "Next");
        FilterButton.Content = T("تصفية", "Filter");
        FieldsButton.Content = T("الحقول", "Fields");
        SummariesButton.Content = T("ملخصات", "Summaries");
        AlternatingRowsButton.Content = T("أسطر متباينة", "Alternating rows");
        AddGroupingButton.Content = T("تجميع", "Group");
        ClearGroupingButton.Content = T("إلغاء التجميع", "Clear groups");
        FilterTitle.Text = T("شروط التصفية", "Filter conditions");
        FieldsTitle.Text = T("قائمة الحقول", "Field chooser");
        SummariesTitle.Text = T("مصمم الملخصات المخصصة", "Summary Designer");
        ConditionalFormattingButton.Content = T("تنسيق شرطي", "Formatting");
        ConditionalFormattingButton.ToolTip = T("تنسيق شرطي للأسطر وفق قواعد مخصصة (الأمين 10)", "Conditional formatting (Al-Ameen 10)");
        ConditionalFormattingTitle.Text = T("التنسيق الشرطي", "Conditional Formatting");
        FormatColumnLabel.Text = T("الحقل", "Field");
        FormatOperatorLabel.Text = T("الشرط", "Operator");
        FormatValueLabel.Text = T("القيمة", "Value");
        FormatColorLabel.Text = T("نمط التمييز", "Highlight Style");
        AddFormatRuleButton.Content = T("إضافة قاعدة", "Add Rule");
        RemoveFormatRuleButton.Content = T("حذف", "Remove");
        ClearFormatRulesButton.Content = T("مسح الكل", "Clear All");

        FormatOperator.ItemsSource = new[]
        {
            new Choice<PetraFormatOperator>(PetraFormatOperator.GreaterThan, T("أكبر من (>)", "Greater than (>)")),
            new Choice<PetraFormatOperator>(PetraFormatOperator.LessThan, T("أصغر من (<)", "Less than (<)")),
            new Choice<PetraFormatOperator>(PetraFormatOperator.Equals, T("يساوي (=)", "Equals (=)")),
            new Choice<PetraFormatOperator>(PetraFormatOperator.NotEquals, T("لا يساوي (!=)", "Not equals (!=)")),
            new Choice<PetraFormatOperator>(PetraFormatOperator.Contains, T("يحتوي على", "Contains")),
            new Choice<PetraFormatOperator>(PetraFormatOperator.Between, T("بين قيمتين", "Between")),
        };
        FormatOperator.SelectedIndex = 0;

        FormatColor.ItemsSource = new[]
        {
            new Choice<PetraFormatColor>(PetraFormatColor.SoftRed, T("أحمر ناعم (تنبيه / مدين)", "Soft Red (Alert / Debit)")),
            new Choice<PetraFormatColor>(PetraFormatColor.SoftGreen, T("أخضر ناعم (ربح / معتمد)", "Soft Green (Profit / Credit)")),
            new Choice<PetraFormatColor>(PetraFormatColor.SoftYellow, T("أصفر ناعم (تحذير / معلق)", "Soft Yellow (Warning / Pending)")),
            new Choice<PetraFormatColor>(PetraFormatColor.SoftBlue, T("أزرق ناعم (معلومات / مسوى)", "Soft Blue (Info / Settled)")),
        };
        FormatColor.SelectedIndex = 0;
        SearchGroupLabel.Text = T("بحث", "Search");
        ViewGroupLabel.Text = T("عرض", "View");
        LayoutGroupLabel.Text = T("تجميع وأقسام", "Grouping and sections");
        SourceGroupLabel.Text = T("المصدر", "Source");
        PrintGroupLabel.Text = T("إخراج", "Output");
        OutputGroupLabel.Text = T("تصدير", "Export");
        CriteriaLabel.Text = T("معايير التنفيذ", "Run criteria");
        TotalsLabel.Text = T("مجاميع الخادم", "Server totals");
        FilterTreeTab.Header = T("شجرة الشروط", "Condition tree");
        FilterQuickTab.Header = T("تصفية سريعة", "Quick filter");
        ApplyTreeFilterButton.Content = T("تطبيق الشجرة", "Apply tree");
        ClearTreeFilterButton.Content = T("مسح الشروط", "Clear conditions");
        FilterColumnLabel.Text = T("الحقل", "Field");
        FilterOperatorLabel.Text = T("العامل", "Operator");
        ColumnFieldsLabel.Text = T("الحقول المتاحة", "Available fields");
        WorkspaceNameLabel.Text = T("اسم مساحة العرض", "Workspace name");
        WorkspaceListLabel.Text = T("مساحات محفوظة", "Saved workspaces");
        SummaryColumnLabel.Text = T("حقل الملخص", "Summary field");
        SummaryKindLabel.Text = T("نوع الملخص", "Summary kind");
        SummaryCustomLabelLabel.Text = T("تسمية مخصصة (اختياري)", "Custom label (optional)");
        AddSummaryButton.Content = T("إضافة الملخص", "Add summary");
        RemoveSummaryButton.Content = T("حذف", "Remove");
        ClearSummariesButton.Content = T("مسح الكل", "Clear all");
        SummaryKindCombo.ItemsSource = new[]
        {
            new Choice<PetraSummaryKind>(PetraSummaryKind.Sum, T("مجموع", "Sum")),
            new Choice<PetraSummaryKind>(PetraSummaryKind.Average, T("متوسط", "Average")),
            new Choice<PetraSummaryKind>(PetraSummaryKind.Min, T("أدنى قيمة", "Minimum")),
            new Choice<PetraSummaryKind>(PetraSummaryKind.Max, T("أعلى قيمة", "Maximum")),
            new Choice<PetraSummaryKind>(PetraSummaryKind.Count, T("عدد السجلات", "Count")),
            new Choice<PetraSummaryKind>(PetraSummaryKind.DistinctCount, T("قيم فريدة", "Distinct Count")),
        };
        SummaryKindCombo.SelectedIndex = 0;
        ShowColumnButton.Content = T("إظهار", "Show");
        HideColumnButton.Content = T("إخفاء", "Hide");
        MoveColumnUpButton.Content = T("للأعلى", "Move up");
        MoveColumnDownButton.Content = T("للأسفل", "Move down");
        FreezeColumnButton.Content = T("تجميد حتى الحقل", "Freeze through");
        ResetColumnsButton.Content = T("إعادة الضبط", "Reset");
        WorkspaceName.ToolTip = T("اسم مساحة العرض", "Viewer workspace name");
        SaveWorkspaceButton.Content = T("حفظ", "Save");
        LoadWorkspaceButton.Content = T("استعادة", "Load");
        DeleteWorkspaceButton.Content = T("حذف", "Delete");
        AddFilterButton.Content = T("إضافة الشرط", "Add condition");
        RemoveFilterButton.Content = T("حذف", "Remove");
        ApplyFilterButton.Content = T("تطبيق", "Apply");
        ClearFiltersButton.Content = T("مسح الكل", "Clear all");
        ActiveFilters.ItemsSource = filterConditions;
        ActiveFormatRules.ItemsSource = formatRules;
        FilterLogic.ItemsSource = new[]
        {
            new Choice<PetraReportFilterLogic>(PetraReportFilterLogic.And, "AND"),
            new Choice<PetraReportFilterLogic>(PetraReportFilterLogic.Or, "OR"),
        };
        FilterLogic.SelectedIndex = 0;
        TreeFilterBuilder.DataContext = treeFilterBuilderModel;
        Status.Text = T("جارٍ تجهيز نسخة التقرير…", "Preparing the report snapshot…");
    }

    private async void LoadedAsync(object sender, RoutedEventArgs args)
    {
        Loaded -= LoadedAsync;
        try
        {
            currentBranding = await brandingStore.LoadAsync(request.Snapshot.CompanyId, request.Snapshot.CompanyName, lifetime.Token).ConfigureAwait(true);
            documents = await Task.Run(() => ProfessionalReportDocumentFactory.CreateSections(request), lifetime.Token).ConfigureAwait(true);
            lifetime.Token.ThrowIfCancellationRequested();
            Sections.ItemsSource = documents;
            ready = true;
            Sections.SelectedIndex = 0;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OutOfMemoryException) { ReportFailure(exception); }
        finally { SetBusy(false); }
    }

    private void SectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!ready || SelectedDocument is not { } report) return;
        CancelCurrentOperation();
        ResultsGrid.Document = report;
        PrintViewer.Document = null;
        ViewerTabs.SelectedItem = ResultsTab;
        CriteriaChips.ItemsSource = report.Filters
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => Chip(pair.Key) + ": " + ChipValue(pair.Value))
            .ToArray();
        AuthoritativeTotals.ItemsSource = report.Summaries.Select(value => new SummaryDisplay(
            Chip(value.Label), value.Value.ToString("N4", CultureInfo.InvariantCulture), value.CurrencyCode)).ToArray();
        SearchColumn.ItemsSource = new[] { new SearchColumnChoice(null, T("كل الأعمدة", "All columns")) }
            .Concat(report.Columns.Select(column => new SearchColumnChoice(column.Key, column.Heading))).ToArray();
        SearchColumn.SelectedIndex = 0;
        FilterColumn.ItemsSource = report.Columns;
        FilterColumn.SelectedIndex = report.Columns.Count == 0 ? -1 : 0;
        ColumnFields.ItemsSource = report.Columns;
        ColumnFields.SelectedIndex = report.Columns.Count == 0 ? -1 : 0;
        GroupColumn.ItemsSource = report.Columns.Where(column => column.Kind is ReportValueKind.Text or ReportValueKind.Date).ToArray();
        GroupColumn.SelectedIndex = GroupColumn.Items.Count == 0 ? -1 : 0;
        SummaryColumn.ItemsSource = report.Columns;
        SummaryColumn.SelectedIndex = report.Columns.Count == 0 ? -1 : 0;
        FormatColumn.ItemsSource = report.Columns;
        FormatColumn.SelectedIndex = report.Columns.Count == 0 ? -1 : 0;
        treeFilterBuilderModel.SetAvailableColumns(report.Columns);
        filterConditions.Clear();
        ResetSearch();
        VisibleRows.Text = report.Rows.Count.ToString("N0", CultureInfo.InvariantCulture);
        UpdatePresentationSummary();
        Status.Text = report.Rows.Count.ToString("N0", CultureInfo.InvariantCulture) +
            T(" سجل من نسخة الخادم؛ المعاينة الطباعية تُنشأ عند الطلب.", " rows from the server snapshot; print preview is generated on demand.");
        UpdateCommandState();
    }

    private TaskCompletionSource? idleSignal;

    /// <summary>
    /// Completes when no cancellable operation is running. A preview/print request made while a filter or sort is
    /// still finishing is honoured after it instead of being silently dropped.
    /// </summary>
    private Task WhenIdleAsync()
    {
        if (!busy || disposed) return Task.CompletedTask;
        idleSignal ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return idleSignal.Task;
    }

    private async Task<bool> EnsurePrintPreviewAsync()
    {
        if (PrintViewer.Document is not null) return true;
        await WhenIdleAsync().ConfigureAwait(true);
        if (disposed || PrintViewer.Document is not null) return !disposed;
        if (busy || ScopedDocument() is not { } report) return false;
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var current = operation;
        SetBusy(true);
        Status.Text = T("جارٍ تجهيز صفحات الطباعة…", "Preparing print pages…");
        try
        {
            var document = await ProfessionalReportPageBuilder.BuildAsync(report, localization.IsRightToLeft, current.Token, currentBranding).ConfigureAwait(true);
            current.Token.ThrowIfCancellationRequested();
            PrintViewer.Document = document;
            Status.Text = report.Rows.Count.ToString("N0", CultureInfo.InvariantCulture) + T(" سجل · ", " rows · ") +
                document.Pages.Count.ToString("N0", CultureInfo.InvariantCulture) + T(" صفحة.", " pages.");
            return true;
        }
        catch (OperationCanceledException)
        {
            Status.Text = T("أُلغيت عملية تجهيز المعاينة؛ بقيت نتائج التقرير متاحة.", "Preview generation was cancelled; report results remain available.");
            return false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportFailure(exception);
            return false;
        }
        finally { FinishOperation(current); }
    }

    private async void ViewerTabChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!ReferenceEquals(args.Source, ViewerTabs) || ViewerTabs.SelectedItem != PrintPreviewTab) return;
        await EnsurePrintPreviewAsync().ConfigureAwait(true);
    }

    private async void PreviewClicked(object sender, RoutedEventArgs args)
    {
        ViewerTabs.SelectedItem = PrintPreviewTab;
        await EnsurePrintPreviewAsync().ConfigureAwait(true);
    }

    private async void PrintClicked(object sender, RoutedEventArgs args)
    {
        if (!await EnsurePrintPreviewAsync().ConfigureAwait(true) ||
            PrintViewer.Document is not FixedDocument document || ScopedDocument() is not { } report) return;
        try
        {
            var dialog = new PrintDialog();
            dialog.PrintTicket.PageOrientation = report.Columns.Count > 7 ? PageOrientation.Landscape : PageOrientation.Portrait;
            dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
            if (dialog.ShowDialog() == true) dialog.PrintDocument(document.DocumentPaginator, report.Title);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { ReportFailure(exception); }
    }

    private async void PdfClicked(object sender, RoutedEventArgs args) =>
        await ExportAsync("pdf", report => new FastReportPdfExporter().Export(report)).ConfigureAwait(true);

    private async void ExcelClicked(object sender, RoutedEventArgs args) =>
        await ExportAsync("xlsx", report => new ExcelReportExporter().Export(report, ExcelExportLayout.Analytical)).ConfigureAwait(true);

    private async void ExcelPrintClicked(object sender, RoutedEventArgs args) =>
        await ExportAsync("xlsx", report => new ExcelReportExporter().Export(report, ExcelExportLayout.PrintLayout)).ConfigureAwait(true);

    private async void CsvClicked(object sender, RoutedEventArgs args) =>
        await ExportAsync("csv", report => new CsvReportExporter().Export(report, true)).ConfigureAwait(true);

    private async Task ExportAsync(string extension, Func<ReportDocument, byte[]> export)
    {
        if (busy || ScopedDocument() is not { } report) return;
        var dialog = new SaveFileDialog
        {
            Title = T("تصدير القسم المحدد · ", "Export selected section · ") + report.Title,
            FileName = report.ReportCode + "_" + report.ExecutionId.ToString("N", CultureInfo.InvariantCulture),
            DefaultExt = "." + extension,
            Filter = extension.ToUpperInvariant() + "|*." + extension,
            AddExtension = true,
            OverwritePrompt = true,
            CheckPathExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var current = operation;
        var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        SetBusy(true);
        Status.Text = T("جارٍ تصدير القسم المحدد كاملاً…", "Exporting the complete selected section…");
        try
        {
            var content = await Task.Run(() =>
            {
                current.Token.ThrowIfCancellationRequested();
                return export(report);
            }, current.Token).ConfigureAwait(true);
            await File.WriteAllBytesAsync(temporary, content, current.Token).ConfigureAwait(true);
            current.Token.ThrowIfCancellationRequested();
            File.Move(temporary, dialog.FileName, true);
            Status.Text = T("تم حفظ القسم المحدد: ", "Selected section saved: ") + dialog.FileName;
        }
        catch (OperationCanceledException) { Status.Text = T("أُلغي التصدير.", "Export cancelled."); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { ReportFailure(exception); }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { ReportFailure(exception); }
            FinishOperation(current);
        }
    }

    private void ResultActivated(object sender, EventArgs args) => TryOpenSelectedSource();
    private void ResultSelectionChanged(object sender, EventArgs args)
    {
        if (PrintScope.SelectedValue is PetraReportOutputScope.SelectedRows) PrintViewer.Document = null;
        UpdateCommandState();
    }
    private void VisibleRowsChanged(object sender, EventArgs args)
    {
        VisibleRows.Text = ResultsGrid.VisibleRowCount.ToString("N0", CultureInfo.InvariantCulture);
        if (PrintScope.SelectedValue is PetraReportOutputScope.VisibleRows) PrintViewer.Document = null;
        UpdatePresentationSummary();
    }
    private void OpenSourceClicked(object sender, RoutedEventArgs args) => TryOpenSelectedSource();

    private async void SearchBoxKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            SearchBox.Clear();
            ResetSearch();
            ResultsGrid.DataGrid.Focus();
            args.Handled = true;
            return;
        }
        if (args.Key != Key.Enter) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            await MoveSearchAsync(-1).ConfigureAwait(true);
        else
            await MoveSearchAsync(1).ConfigureAwait(true);
        args.Handled = true;
    }

    private async void PreviousMatchClicked(object sender, RoutedEventArgs args) => await MoveSearchAsync(-1).ConfigureAwait(true);
    private async void NextMatchClicked(object sender, RoutedEventArgs args) => await MoveSearchAsync(1).ConfigureAwait(true);

    private async Task MoveSearchAsync(int direction)
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text)) { SearchBox.Focus(); return; }
        if (searchMatches.Count == 0 && !await RunSearchAsync().ConfigureAwait(true)) return;
        if (searchMatches.Count == 0) return;
        searchMatchIndex = searchMatchIndex < 0
            ? (direction > 0 ? 0 : searchMatches.Count - 1)
            : (searchMatchIndex + direction + searchMatches.Count) % searchMatches.Count;
        ResultsGrid.SelectMatch(searchMatches[searchMatchIndex]);
        SearchResultLabel.Text = (searchMatchIndex + 1).ToString(CultureInfo.InvariantCulture) + " / " +
            searchMatches.Count.ToString(CultureInfo.InvariantCulture);
    }

    private async Task<bool> RunSearchAsync()
    {
        CancelCurrentOperation();
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var current = operation;
        SetBusy(true);
        Status.Text = T("جارٍ البحث داخل النتائج الظاهرة…", "Searching visible results…");
        try
        {
            var column = SearchColumn.SelectedValue as string;
            searchMatches = await ResultsGrid.SearchAsync(new PetraReportSearchOptions(SearchBox.Text, column), current.Token).ConfigureAwait(true);
            searchMatchIndex = -1;
            SearchResultLabel.Text = searchMatches.Count == 0 ? "0 / 0" : "0 / " + searchMatches.Count.ToString(CultureInfo.InvariantCulture);
            Status.Text = searchMatches.Count.ToString("N0", CultureInfo.InvariantCulture) +
                T(" تطابق؛ البحث لا يزيل أي صف.", " matches; Find does not remove rows.");
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { ReportFailure(exception); return false; }
        finally { FinishOperation(current); }
    }

    private void ResetSearch()
    {
        searchMatches = [];
        searchMatchIndex = -1;
        SearchResultLabel.Text = string.Empty;
    }

    private void FilterClicked(object sender, RoutedEventArgs args)
    {
        ConditionalFormattingSidebar.Visibility = Visibility.Collapsed;
        SummariesSidebar.Visibility = Visibility.Collapsed;
        FieldsSidebar.Visibility = Visibility.Collapsed;
        FilterSidebar.Visibility = FilterSidebar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

        private void ConditionalFormattingClicked(object sender, RoutedEventArgs args)
    {
        FieldsSidebar.Visibility = Visibility.Collapsed;
        FilterSidebar.Visibility = Visibility.Collapsed;
        SummariesSidebar.Visibility = Visibility.Collapsed;
        ConditionalFormattingSidebar.Visibility = ConditionalFormattingSidebar.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void AddFormatRuleClicked(object sender, RoutedEventArgs args)
    {
        if (FormatColumn.SelectedItem is not ReportColumn column ||
            FormatOperator.SelectedValue is not PetraFormatOperator op ||
            FormatColor.SelectedValue is not PetraFormatColor color ||
            string.IsNullOrWhiteSpace(FormatValue1.Text)) return;

        var rule = new PetraConditionalFormatRule(
            column.Key,
            column.Heading,
            op,
            FormatValue1.Text.Trim(),
            FormatValue2.Text?.Trim() ?? string.Empty,
            color);

        formatRules.Add(rule);
        ResultsGrid.ApplyConditionalFormatting(formatRules);
        FormatValue1.Clear();
        FormatValue2.Clear();
    }

    private void RemoveFormatRuleClicked(object sender, RoutedEventArgs args)
    {
        if (ActiveFormatRules.SelectedItem is PetraConditionalFormatRule rule)
        {
            formatRules.Remove(rule);
            ResultsGrid.ApplyConditionalFormatting(formatRules);
        }
    }

    private void ClearFormatRulesClicked(object sender, RoutedEventArgs args)
    {
        formatRules.Clear();
        ResultsGrid.ClearConditionalFormatting();
    }

    private void AlternatingRowsClicked(object sender, RoutedEventArgs args)
    {
        var current = ReportDisplayOptionsHub.Current;
        ReportDisplayOptionsHub.Set(current with { AlternatingRows = !current.AlternatingRows });
    }

    private async void FieldsClicked(object sender, RoutedEventArgs args)
    {
        FilterSidebar.Visibility = Visibility.Collapsed;
        FieldsSidebar.Visibility = FieldsSidebar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (FieldsSidebar.Visibility == Visibility.Visible) await LoadWorkspaceListAsync().ConfigureAwait(true);
    }

    private ReportColumn? SelectedField => ColumnFields.SelectedItem as ReportColumn;

    private void ShowColumnClicked(object sender, RoutedEventArgs args)
    {
        if (SelectedField is { } column) ResultsGrid.SetColumnVisibility(column.Key, true);
    }

    private void HideColumnClicked(object sender, RoutedEventArgs args)
    {
        if (SelectedField is not { } column) return;
        if (!ResultsGrid.SetColumnVisibility(column.Key, false))
            Status.Text = T("يجب إبقاء حقل واحد على الأقل ظاهراً.", "At least one field must remain visible.");
    }

    private void MoveColumnUpClicked(object sender, RoutedEventArgs args)
    {
        if (SelectedField is { } column) ResultsGrid.MoveColumn(column.Key, -1);
    }

    private void MoveColumnDownClicked(object sender, RoutedEventArgs args)
    {
        if (SelectedField is { } column) ResultsGrid.MoveColumn(column.Key, 1);
    }

    private void FreezeColumnClicked(object sender, RoutedEventArgs args)
    {
        if (SelectedField is not { } column || !ResultsGrid.FreezeThrough(column.Key)) return;
        Status.Text = T("تم تجميد الحقول حتى: ", "Columns frozen through: ") + column.Heading;
    }

    private void ResetColumnsClicked(object sender, RoutedEventArgs args)
    {
        ResultsGrid.ResetColumnLayout();
        Status.Text = T("تمت استعادة ترتيب الحقول وعرضها الافتراضي.", "Default column visibility and order restored.");
    }

    private void AddGroupingClicked(object sender, RoutedEventArgs args)
    {
        if (GroupColumn.SelectedItem is not ReportColumn column) return;
        if (!ResultsGrid.AddGrouping(column.Key))
        {
            Status.Text = T("الحقل مجمّع بالفعل أو بلغ التجميع الحد الأقصى.", "The field is already grouped or the grouping limit was reached.");
            return;
        }
        Status.Text = T("مستويات التجميع: ", "Grouping levels: ") + string.Join(" → ", ResultsGrid.GroupingKeys);
    }

    private void ClearGroupingClicked(object sender, RoutedEventArgs args)
    {
        ResultsGrid.ClearGrouping();
        Status.Text = T("تم إلغاء تجميع العرض.", "Display grouping cleared.");
    }

    private void PrintScopeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!ready) return;
        PrintViewer.Document = null;
        Status.Text = T("نطاق الطباعة والتصدير: ", "Print/export scope: ") +
            (PrintScope.SelectedItem as Choice<PetraReportOutputScope>)?.Label;
    }

    private ReportDocument? ScopedDocument()
    {
        if (SelectedDocument is not { } report) return null;
        var scope = PrintScope.SelectedValue is PetraReportOutputScope value ? value : PetraReportOutputScope.FullExecution;
        if (scope == PetraReportOutputScope.FullExecution) return report;
        var rows = scope == PetraReportOutputScope.VisibleRows
            ? ResultsGrid.VisibleRowsSnapshot
            : ResultsGrid.SelectedRowsSnapshot;
        if (scope == PetraReportOutputScope.SelectedRows && rows.Count == 0)
        {
            Status.Text = T("حدد صفاً واحداً على الأقل قبل المعاينة أو التصدير.", "Select at least one row before preview or export.");
            return null;
        }
        var filters = report.Filters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        filters["Output scope / نطاق الإخراج"] = scope == PetraReportOutputScope.VisibleRows
            ? T("الصفوف المصفاة الحالية؛ المجاميع أدناه تخص التنفيذ الكامل المعتمد.",
                "Current filtered rows; summaries below remain authoritative for the full execution.")
            : T("الصفوف المحددة؛ المجاميع أدناه تخص التنفيذ الكامل المعتمد.",
                "Selected rows; summaries below remain authoritative for the full execution.");
        filters["Output rows / عدد صفوف الإخراج"] = rows.Count.ToString(CultureInfo.InvariantCulture);
        return new ReportDocument(report.ReportCode, report.Title, report.CompanyLabel, report.PeriodLabel, report.CurrencyCode,
            report.Columns, rows, filters, report.GeneratedAtUtc, report.ExecutionId, report.Context, report.Summaries);
    }

    private async Task LoadWorkspaceListAsync()
    {
        if (SelectedDocument is not { Context: { } context } report) return;
        try
        {
            workspaces = await WorkspaceStore.LoadAsync(request.ActorId, context.CompanyId, context.FiscalYearId,
                report.ReportCode, lifetime.Token).ConfigureAwait(true);
            WorkspaceList.ItemsSource = workspaces;
            if (WorkspaceList.SelectedIndex < 0 && workspaces.Count != 0) WorkspaceList.SelectedIndex = 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException) { ReportFailure(exception); }
    }

    private async void SaveWorkspaceClicked(object sender, RoutedEventArgs args)
    {
        if (SelectedDocument is not { Context: { } context } report || string.IsNullOrWhiteSpace(WorkspaceName.Text))
        {
            Status.Text = T("اكتب اسماً لمساحة العرض.", "Enter a workspace name.");
            return;
        }
        var sorted = ResultsGrid.DataGrid.Columns.FirstOrDefault(column => column.SortDirection is not null);
        var workspace = new PetraReportWorkspace(
            PetraReportWorkspaceStore.CurrentVersion,
            WorkspaceName.Text.Trim(),
            request.ActorId,
            context.CompanyId,
            context.FiscalYearId,
            report.ReportCode,
            ResultsGrid.CaptureColumnLayout(),
            ResultsGrid.GroupingKeys.ToArray(),
            filterConditions.Select(item => new PetraReportWorkspaceFilter(item.Condition.ColumnKey, item.Condition.Operator,
                item.Condition.Value, item.Condition.SecondValue, item.Logic, item.IsAuto)).ToArray(),
            sorted?.SortMemberPath,
            sorted?.SortDirection,
            "Standard",
            null,
            DateTimeOffset.UtcNow,
            summaryManager.ConfiguredSummaries.ToList());
        try
        {
            await WorkspaceStore.SaveAsync(workspace, lifetime.Token).ConfigureAwait(true);
            await LoadWorkspaceListAsync().ConfigureAwait(true);
            WorkspaceList.SelectedItem = workspaces.FirstOrDefault(value => string.Equals(value.Name, workspace.Name, StringComparison.OrdinalIgnoreCase));
            Status.Text = T("تم حفظ مساحة عرض التقرير.", "Report workspace saved.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException) { ReportFailure(exception); }
    }

    private async void LoadWorkspaceClicked(object sender, RoutedEventArgs args)
    {
        if (WorkspaceList.SelectedItem is not PetraReportWorkspace workspace || SelectedDocument is not { } report) return;
        var warnings = ResultsGrid.ApplyColumnLayout(workspace.Columns).ToList();
        ResultsGrid.ClearGrouping();
        foreach (var groupingKey in workspace.Grouping)
            if (!ResultsGrid.AddGrouping(groupingKey)) warnings.Add(T("تعذر استعادة التجميع: ", "Grouping could not be restored: ") + groupingKey);
        filterConditions.Clear();
        foreach (var saved in workspace.Filters)
        {
            var condition = new PetraReportFilterCondition(saved.ColumnKey, saved.Operator, saved.Value, saved.SecondValue);
            var validation = PetraReportFilterEngine.Validate(report, condition);
            if (!validation.IsValid) { warnings.AddRange(validation.Errors); continue; }
            var column = report.Columns.First(value => string.Equals(value.Key, saved.ColumnKey, StringComparison.Ordinal));
            filterConditions.Add(new FilterConditionDisplay(condition, saved.Logic,
                saved.Logic.ToString().ToUpperInvariant() + " · " + column.Heading + " · " + saved.Operator + " · " + saved.Value, saved.IsAuto));
        }
        await ApplyFiltersAsync().ConfigureAwait(true);
        if (workspace.SortKey is { } key && workspace.SortDirection is { } direction && ReferenceEquals(SelectedDocument, report))
        {
            var restored = false;
            try { restored = await ResultsGrid.ApplySortAsync(key, direction, lifetime.Token).ConfigureAwait(true); }
            catch (OperationCanceledException) { }
            if (!restored) warnings.Add(T("تعذر استعادة الترتيب: ", "Sort could not be restored: ") + key);
        }
        summaryManager.Clear();
        if (workspace.CustomSummaries is not null)
        {
            foreach (var s in workspace.CustomSummaries) summaryManager.ConfiguredSummaries.Add(s);
            ActiveSummariesList.ItemsSource = summaryManager.ConfiguredSummaries.Select(s => new { Label = $"{s.Kind}: {s.ColumnHeading}" }).ToList();
        }
        UpdatePresentationSummary();
        WorkspaceName.Text = workspace.Name;
        Status.Text = warnings.Count == 0
            ? T("تمت استعادة مساحة العرض.", "Report workspace restored.")
            : T("تمت الاستعادة مع تنبيهات: ", "Workspace restored with warnings: ") + string.Join(" ", warnings);
    }

    private async void DeleteWorkspaceClicked(object sender, RoutedEventArgs args)
    {
        if (WorkspaceList.SelectedItem is not PetraReportWorkspace workspace) return;
        try
        {
            await WorkspaceStore.DeleteAsync(workspace, lifetime.Token).ConfigureAwait(true);
            WorkspaceName.Clear();
            await LoadWorkspaceListAsync().ConfigureAwait(true);
            Status.Text = T("تم حذف مساحة العرض.", "Report workspace deleted.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException) { ReportFailure(exception); }
    }

    private void SummariesClicked(object sender, RoutedEventArgs args)
    {
        ConditionalFormattingSidebar.Visibility = Visibility.Collapsed;
        FieldsSidebar.Visibility = Visibility.Collapsed;
        FilterSidebar.Visibility = Visibility.Collapsed;
        SummariesSidebar.Visibility = SummariesSidebar.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void AddSummaryClicked(object sender, RoutedEventArgs args)
    {
        if (SummaryColumn.SelectedItem is not ReportColumn column ||
            SummaryKindCombo.SelectedValue is not PetraSummaryKind kind) return;
        var customLabel = string.IsNullOrWhiteSpace(SummaryCustomLabel.Text) ? null : SummaryCustomLabel.Text.Trim();
        summaryManager.AddSummary(column.Key, column.Heading, kind, customLabel);
        ActiveSummariesList.ItemsSource = summaryManager.ConfiguredSummaries.Select(s => new { Label = $"{s.Kind}: {s.ColumnHeading}" }).ToList();
        SummaryCustomLabel.Clear();
        UpdatePresentationSummary();
    }

    private void RemoveSummaryClicked(object sender, RoutedEventArgs args)
    {
        if (ActiveSummariesList.SelectedIndex >= 0 && ActiveSummariesList.SelectedIndex < summaryManager.ConfiguredSummaries.Count)
        {
            summaryManager.ConfiguredSummaries.RemoveAt(ActiveSummariesList.SelectedIndex);
            ActiveSummariesList.ItemsSource = summaryManager.ConfiguredSummaries.Select(s => new { Label = $"{s.Kind}: {s.ColumnHeading}" }).ToList();
            UpdatePresentationSummary();
        }
    }

    private void ClearSummariesClicked(object sender, RoutedEventArgs args)
    {
        summaryManager.Clear();
        ActiveSummariesList.ItemsSource = null;
        UpdatePresentationSummary();
    }

    private void UpdatePresentationSummary()
    {
        var values = ResultsGrid.VisibleSummaries();
        var custom = summaryManager.Calculate(SelectedDocument, ResultsGrid.VisibleRowsSnapshot);
        var parts = new List<string>();
        if (values.Count > 0)
        {
            parts.Add(T("مجموع الصفوف: ", "Visible sums: ") + string.Join(" · ", values.Take(2).Select(value =>
                value.Label + " = " + value.Value.ToString("N4", CultureInfo.InvariantCulture))));
        }
        foreach (var c in custom)
        {
            parts.Add(c.Label + " = " + c.Value + (c.Currency is not null ? " " + c.Currency : string.Empty));
        }
        PresentationSummary.Text = string.Join(" | ", parts);
    }

    private void FilterColumnChanged(object sender, SelectionChangedEventArgs args)
    {
        if (FilterColumn.SelectedItem is not ReportColumn column) return;
        FilterOperator.ItemsSource = Operators(column).ToArray();
        FilterOperator.SelectedIndex = 0;
        FilterValue.FlowDirection = FlowDirection.LeftToRight;
        FilterSecondValue.FlowDirection = FlowDirection.LeftToRight;
    }

    private IEnumerable<Choice<PetraReportFilterOperator>> Operators(ReportColumn column)
    {
        yield return new(PetraReportFilterOperator.Equals, T("يساوي", "Equals"));
        yield return new(PetraReportFilterOperator.NotEquals, T("لا يساوي", "Not equals"));
        if (column.Kind == ReportValueKind.Text)
        {
            yield return new(PetraReportFilterOperator.Contains, T("يحوي", "Contains"));
            yield return new(PetraReportFilterOperator.DoesNotContain, T("لا يحوي", "Does not contain"));
            yield return new(PetraReportFilterOperator.StartsWith, T("يبدأ بـ", "Starts with"));
            yield return new(PetraReportFilterOperator.EndsWith, T("ينتهي بـ", "Ends with"));
        }
        else
        {
            yield return new(PetraReportFilterOperator.GreaterThan, T("أكبر من", "Greater than"));
            yield return new(PetraReportFilterOperator.GreaterThanOrEqual, T("أكبر أو يساوي", "Greater/equal"));
            yield return new(PetraReportFilterOperator.LessThan, T("أصغر من", "Less than"));
            yield return new(PetraReportFilterOperator.LessThanOrEqual, T("أصغر أو يساوي", "Less/equal"));
            yield return new(PetraReportFilterOperator.Between, T("بين", "Between"));
        }
        yield return new(PetraReportFilterOperator.IsEmpty, T("فارغ", "Is empty"));
        yield return new(PetraReportFilterOperator.IsNotEmpty, T("غير فارغ", "Is not empty"));
    }

    private void AddFilterClicked(object sender, RoutedEventArgs args)
    {
        if (SelectedDocument is not { } report || FilterColumn.SelectedItem is not ReportColumn column ||
            FilterOperator.SelectedValue is not PetraReportFilterOperator filterOperator) return;
        var condition = new PetraReportFilterCondition(column.Key, filterOperator, FilterValue.Text, FilterSecondValue.Text);
        var validation = PetraReportFilterEngine.Validate(report, condition);
        if (!validation.IsValid) { Status.Text = string.Join(" ", validation.Errors); return; }
        var logic = FilterLogic.SelectedValue is PetraReportFilterLogic selectedLogic ? selectedLogic : PetraReportFilterLogic.And;
        filterConditions.Add(new FilterConditionDisplay(condition, logic,
            (filterConditions.Count == 0 ? string.Empty : logic.ToString().ToUpperInvariant() + " · ") +
            column.Heading + " · " + ((Choice<PetraReportFilterOperator>)FilterOperator.SelectedItem).Label +
            (filterOperator is PetraReportFilterOperator.IsEmpty or PetraReportFilterOperator.IsNotEmpty ? string.Empty : " · " + FilterValue.Text) +
            (filterOperator == PetraReportFilterOperator.Between ? " … " + FilterSecondValue.Text : string.Empty), false));
        FilterValue.Clear();
        FilterSecondValue.Clear();
        ActiveFilters.SelectedIndex = filterConditions.Count - 1;
    }

    private void RemoveFilterClicked(object sender, RoutedEventArgs args)
    {
        if (ActiveFilters.SelectedItem is FilterConditionDisplay selected) filterConditions.Remove(selected);
    }

    private async void ApplyFilterClicked(object sender, RoutedEventArgs args) => await ApplyFiltersAsync().ConfigureAwait(true);

    private async Task ApplyFiltersAsync()
    {
        CancelCurrentOperation();
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var current = operation;
        var reportAtStart = SelectedDocument;
        SetBusy(true);
        Status.Text = T("جارٍ تطبيق التصفية على نسخة التقرير…", "Applying filters to the report snapshot…");
        try
        {
            var validation = await ResultsGrid.ApplyFilterAsync(BuildFilterTree(), current.Token).ConfigureAwait(true);
            if (!validation.IsValid) { Status.Text = string.Join(" ", validation.Errors); return; }
            ResetSearch();
            Status.Text = ResultsGrid.VisibleRowCount.ToString("N0", CultureInfo.InvariantCulture) +
                T(" سجل مطابق. المجاميع المحاسبية أدناه بقيت معتمدة من الخادم ولم تتغير.",
                    " matching rows. Authoritative server totals below remain unchanged.");
        }
        catch (OperationCanceledException)
        {
            // A result rejected because the section changed or the window closed must not overwrite the new section's status.
            if (!disposed && ReferenceEquals(SelectedDocument, reportAtStart)) Status.Text = T("أُلغيت التصفية.", "Filtering cancelled.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { ReportFailure(exception); }
        finally { FinishOperation(current); }
    }

    private void HeaderFilterRequested(object? sender, ReportColumnFilterRequest args)
    {
        FieldsSidebar.Visibility = Visibility.Collapsed;
        FilterSidebar.Visibility = Visibility.Visible;
        FilterColumn.SelectedValue = args.Column.Key;
        FilterValue.Focus();
    }

    // One authoritative model: filterConditions (header / auto-filter row / sidebar items, each AND- or OR-joined) + the advanced
    // group tree. A header or auto filter REPLACES only its own column's previous entry of the same source and never touches the rest.
    private IReadOnlyCollection<string>? HeaderSelectionFor(string columnKey) =>
        filterConditions.FirstOrDefault(item => item.IsHeader && string.Equals(item.Condition.ColumnKey, columnKey, StringComparison.Ordinal))?.Condition.Values;

    private async void HeaderFilterConditionApplied(object? sender, PetraReportFilterCondition? condition)
    {
        if (condition is null) return;
        RemoveHeaderFilter(condition.ColumnKey);
        var heading = SelectedDocument?.Columns.FirstOrDefault(c => c.Key == condition.ColumnKey)?.Heading ?? condition.ColumnKey;
        filterConditions.Add(new FilterConditionDisplay(condition, PetraReportFilterLogic.And,
            $"AND · {heading} · ضمن ({condition.Values?.Count ?? 0} قيم)", false, IsHeader: true));
        await ApplyFiltersAsync().ConfigureAwait(true);
    }

    private async void HeaderFilterCleared(object? sender, string columnKey)
    {
        if (!RemoveHeaderFilter(columnKey)) return;
        await ApplyFiltersAsync().ConfigureAwait(true);
    }

    private bool RemoveHeaderFilter(string columnKey)
    {
        var existing = filterConditions.Where(item => item.IsHeader && string.Equals(item.Condition.ColumnKey, columnKey, StringComparison.Ordinal)).ToArray();
        foreach (var item in existing) filterConditions.Remove(item);
        return existing.Length > 0;
    }

    private async void AutoFilterRequested(object? sender, ReportColumnAutoFilterRequest args)
    {
        var existing = filterConditions.FirstOrDefault(item => item.IsAuto &&
            string.Equals(item.Condition.ColumnKey, args.Column.Key, StringComparison.Ordinal));
        if (existing is not null) filterConditions.Remove(existing);
        if (!string.IsNullOrWhiteSpace(args.Value))
        {
            var filterOperator = args.Column.Kind == ReportValueKind.Text
                ? PetraReportFilterOperator.Contains
                : PetraReportFilterOperator.Equals;
            var condition = new PetraReportFilterCondition(args.Column.Key, filterOperator, args.Value.Trim());
            if (SelectedDocument is { } report)
            {
                var validation = PetraReportFilterEngine.Validate(report, condition);
                if (!validation.IsValid) { Status.Text = string.Join(" ", validation.Errors); return; }
            }
            filterConditions.Add(new FilterConditionDisplay(condition, PetraReportFilterLogic.And,
                "AND · " + args.Column.Heading + " · " + args.Value.Trim(), true));
        }
        await ApplyFiltersAsync().ConfigureAwait(true);
    }

    private async void ClearFiltersClicked(object sender, RoutedEventArgs args)
    {
        filterConditions.Clear();
        treeFilterBuilderModel.Clear();
        CancelCurrentOperation();
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var current = operation;
        SetBusy(true);
        try
        {
            await ResultsGrid.ApplyFilterAsync(null, current.Token).ConfigureAwait(true);
            ResetSearch();
            Status.Text = T("تم مسح تصفية العرض؛ عادت كل صفوف النسخة.", "Display filters cleared; all snapshot rows are visible.");
        }
        catch (OperationCanceledException) { }
        finally { FinishOperation(current); }
    }

    private PetraReportFilterNode? BuildFilterTree()
    {
        var treeNode = treeFilterBuilderModel.Build();
        if (filterConditions.Count == 0) return treeNode;
        PetraReportFilterNode flatResult = filterConditions[0].Condition;
        foreach (var item in filterConditions.Skip(1))
            flatResult = new PetraReportFilterGroup(item.Logic, [flatResult, item.Condition]);
        if (treeNode is null) return flatResult;
        return new PetraReportFilterGroup(PetraReportFilterLogic.And, [flatResult, treeNode]);
    }

    private void TryOpenSelectedSource()
    {
        if (!TryGetSelectedSource(out var row)) return;
        openSource(row);
        if (contextWindow is { IsVisible: true }) contextWindow.Activate();
    }

    private bool TryGetSelectedSource(out ProfessionalReportRowDto row)
    {
        row = null!;
        if (busy || Sections.SelectedIndex != 0) return false;
        var index = ResultsGrid.SelectedDocumentRowIndex;
        if (index < 0 || index >= request.Snapshot.Rows.Count) return false;
        var candidate = request.Snapshot.Rows[index];
        if (!candidate.CanDrillThrough || candidate.IsAggregate || candidate.JournalId is null && candidate.DocumentId is null) return false;
        row = candidate;
        return true;
    }

    private void ParametersClicked(object sender, RoutedEventArgs args)
    {
        editParameters?.Invoke();
        if (contextWindow is { IsVisible: true }) contextWindow.Activate();
    }

    private void CancelClicked(object sender, RoutedEventArgs args) => CancelCurrentOperation();
    private void CloseClicked(object sender, RoutedEventArgs args) => Close();

    private void WindowPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && args.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            args.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && args.Key == Key.W) { Close(); args.Handled = true; return; }
        if (Keyboard.Modifiers == ModifierKeys.Control && args.Key == Key.P) { PrintClicked(this, args); args.Handled = true; return; }
        if (args.Key == Key.Escape && busy) { CancelCurrentOperation(); args.Handled = true; }
    }

    private void CancelCurrentOperation() => operation?.Cancel();

    /// <summary>
    /// Ends one cancellable operation. The busy state is released only when no NEWER operation took over, so a
    /// superseded (stale) operation can never re-enable the UI while the current one is still running.
    /// </summary>
    private void FinishOperation(CancellationTokenSource current)
    {
        if (ReferenceEquals(operation, current)) operation = null;
        current.Dispose();
        if (operation is null && !disposed) SetBusy(false);
    }

    private void SetBusy(bool value)
    {
        busy = value;
        if (!value) { var waiting = idleSignal; idleSignal = null; waiting?.TrySetResult(); }
        Progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = value;
        Sections.IsEnabled = !value;
        ParametersButton.IsEnabled = !value;
        PreviewButton.IsEnabled = !value && SelectedDocument is not null;
        PrintButton.IsEnabled = !value && SelectedDocument is not null;
        PdfButton.IsEnabled = !value && SelectedDocument is not null;
        ExcelButton.IsEnabled = PdfButton.IsEnabled;
        ExcelPrintButton.IsEnabled = PdfButton.IsEnabled;
        CsvButton.IsEnabled = PdfButton.IsEnabled;
        UpdateCommandState();
    }

    private void UpdateCommandState() => OpenSourceButton.IsEnabled = TryGetSelectedSource(out _);

    private void ReportFailure(Exception exception) =>
        Status.Text = T("تعذر تنفيذ العملية: ", "The operation could not complete: ") + Connection.DesktopErrors.Key(exception);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        operation?.Cancel();
        operation?.Dispose();
        ResultsGrid.CancelPendingViewWork(); // a filter/sort finishing after close must not touch the grid
        idleSignal?.TrySetResult();
        lifetime.Cancel();
        lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record SummaryDisplay(string Label, string Value, string Currency);
    private sealed record SearchColumnChoice(string? Key, string Heading);
    private sealed record Choice<T>(T Value, string Label);
    private sealed record FilterConditionDisplay(PetraReportFilterCondition Condition, PetraReportFilterLogic Logic, string Label, bool IsAuto, bool IsHeader = false);
    private enum PetraReportOutputScope { FullExecution, VisibleRows, SelectedRows }
}
