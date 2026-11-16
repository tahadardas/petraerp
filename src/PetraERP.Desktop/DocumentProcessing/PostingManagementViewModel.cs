using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.DocumentProcessing;

/// <summary>
/// Accounting → Posting Management. The server discovers and classifies the documents; this page only presents the
/// preview, collects the mandatory confirmation and reason, and displays the persisted per-document outcome. A post is
/// never simulated locally: totals and statuses come back from the batch response and the results endpoint.
/// </summary>
public sealed partial class PostingManagementViewModel : PageViewModel
{
    public const string ManagePermission = "Posting.Batches.Manage";
    private const int ResultsPageSize = 200;
    /// <summary>How many times an interrupted bulk request is re-issued with its own key before the page asks the operator to resume it.</summary>
    private const int SafeResumeAttempts = 10;

    private readonly IDocumentPostingClient client;
    private readonly IUserConfirmation confirmation;
    private string? pendingKey;

    public PostingManagementViewModel(IDocumentPostingClient client, IUserConfirmation confirmation,
        DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.confirmation = confirmation;

        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => HasContext && Idle && HasAnyType);
        PostAllCommand = new AsyncRelayCommand(PostAllAsync,
            () => HasContext && Idle && HasPreview && IsConfirmed && HasPostable);
        PostSelectedCommand = new AsyncRelayCommand(PostSelectedAsync,
            () => HasContext && Idle && HasPreview && IsConfirmed && SelectedCount > 0);
        ResumeCommand = new AsyncRelayCommand(ResumeAsync, () => HasContext && Idle && LastBatchId is not null);
        RefreshResultsCommand = new AsyncRelayCommand(RefreshResultsAsync,
            () => HasContext && Idle && LastBatchId is not null);
        DiscardPendingCommand = new RelayCommand(DiscardPending, () => IsUncertain && !Operation.IsBusy);
    }

    protected override string RequiredPermission => ManagePermission;

    public ObservableCollection<PostingPreviewRow> PreviewItems { get; } = [];
    public ObservableCollection<PostingResultRow> Results { get; } = [];

    [ObservableProperty] private bool includeSales = true;
    [ObservableProperty] private bool includePurchases = true;
    [ObservableProperty] private bool includeVouchers = true;
    [ObservableProperty] private bool includeJournals = true;
    [ObservableProperty] private bool includeInventory = true;
    [ObservableProperty] private DateTime? fromDate;
    [ObservableProperty] private DateTime? toDate;
    [ObservableProperty] private string search = string.Empty;
    [ObservableProperty] private string reason = "ترحيل جماعي معتمد من إدارة ترحيل المستندات.";
    [ObservableProperty] private bool isConfirmed;
    [ObservableProperty] private bool retryFailed;
    [ObservableProperty] private bool hasPreview;
    [ObservableProperty] private bool hasBatch;
    [ObservableProperty] private bool isUncertain;
    [ObservableProperty] private int totalExamined;
    [ObservableProperty] private int readyToPost;
    [ObservableProperty] private int needsApproval;
    [ObservableProperty] private int ineligible;
    [ObservableProperty] private int alreadyPosted;
    [ObservableProperty] private int eligible;
    [ObservableProperty] private int posted;
    [ObservableProperty] private int skipped;
    [ObservableProperty] private int failed;
    [ObservableProperty] private int unknown;
    [ObservableProperty] private int selectedCount;
    [ObservableProperty] private int resultsTotal;
    [ObservableProperty] private Guid? lastBatchId;
    [ObservableProperty] private string lastBatchStatus = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string batchMessage = string.Empty;

    public AsyncRelayCommand PreviewCommand { get; }
    public AsyncRelayCommand PostAllCommand { get; }
    public AsyncRelayCommand PostSelectedCommand { get; }
    public AsyncRelayCommand ResumeCommand { get; }
    public AsyncRelayCommand RefreshResultsCommand { get; }
    public RelayCommand DiscardPendingCommand { get; }

    public bool HasAnyType => IncludeSales || IncludePurchases || IncludeVouchers || IncludeJournals || IncludeInventory;
    public bool HasPostable => PreviewItems.Any(i => i.Postable);
    public bool HasResults => Results.Count > 0;
    public bool CanManage => Can(ManagePermission);
    public string TotalsText =>
        $"المفحوص: {TotalExamined} · المؤهل: {Eligible} · المُرحّل: {Posted} · مُرحّل مسبقاً: {AlreadyPosted} · "
        + $"متخطى: {Skipped} · فشل: {Failed} · غير مؤكد: {Unknown}";
    public string PreviewSummaryText =>
        $"المفحوص: {TotalExamined} · جاهز: {ReadyToPost} · يحتاج اعتماداً: {NeedsApproval} · غير مؤهل: {Ineligible}";
    public string SelectionText => $"المحدد: {SelectedCount}";

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        NotifyCommands();
        if (parameter is BulkPostingNavigation navigation)
        {
            ApplyNavigation(navigation);
            await PreviewCoreAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        if (LastBatchId is { } batchId)
            await LoadResultsCoreAsync(batchId, cancellationToken).ConfigureAwait(true);
    }

    protected override void ClearPage()
    {
        foreach (var row in PreviewItems) row.PropertyChanged -= PreviewRowChanged;
        PreviewItems.Clear();
        Results.Clear();
        IncludeSales = true; IncludePurchases = true; IncludeVouchers = true; IncludeJournals = true; IncludeInventory = true;
        FromDate = null; ToDate = null; Search = string.Empty;
        IsConfirmed = false; RetryFailed = false;
        HasPreview = false; HasBatch = false; IsUncertain = false;
        TotalExamined = ReadyToPost = NeedsApproval = Ineligible = 0;
        AlreadyPosted = Eligible = Posted = Skipped = Failed = Unknown = 0;
        SelectedCount = 0; ResultsTotal = 0;
        LastBatchId = null; LastBatchStatus = string.Empty; StatusMessage = string.Empty; BatchMessage = string.Empty;
        pendingKey = null;
        NotifyViewState();
    }

    public void Localize() => NotifyViewState();

    protected override void NotifyCommands()
    {
        PreviewCommand?.NotifyCanExecuteChanged();
        PostAllCommand?.NotifyCanExecuteChanged();
        PostSelectedCommand?.NotifyCanExecuteChanged();
        ResumeCommand?.NotifyCanExecuteChanged();
        RefreshResultsCommand?.NotifyCanExecuteChanged();
        DiscardPendingCommand?.NotifyCanExecuteChanged();
    }

    partial void OnIsConfirmedChanged(bool value) => NotifyCommands();
    partial void OnIncludeSalesChanged(bool value) => TypeChanged();
    partial void OnIncludePurchasesChanged(bool value) => TypeChanged();
    partial void OnIncludeVouchersChanged(bool value) => TypeChanged();
    partial void OnIncludeJournalsChanged(bool value) => TypeChanged();
    partial void OnIncludeInventoryChanged(bool value) => TypeChanged();

    private void TypeChanged()
    {
        OnPropertyChanged(nameof(HasAnyType));
        NotifyCommands();
    }

    private void ApplyNavigation(BulkPostingNavigation navigation)
    {
        IncludeSales = navigation.DocumentType == BulkPostingDocumentType.SalesInvoice;
        IncludePurchases = navigation.DocumentType == BulkPostingDocumentType.PurchaseInvoice;
        IncludeVouchers = navigation.DocumentType == BulkPostingDocumentType.Voucher;
        IncludeJournals = navigation.DocumentType == BulkPostingDocumentType.Journal;
        IncludeInventory = navigation.DocumentType == BulkPostingDocumentType.InventoryDocument;
        FromDate = navigation.FromDate?.LocalDateTime.Date;
        ToDate = navigation.ToDate?.LocalDateTime.Date;
        Search = navigation.Search ?? string.Empty;
    }

    private BulkPostingFilterDto BuildFilter() => new()
    {
        DocumentTypes = SelectedTypes(),
        FromDateUtc = FromDate is { } from ? new DateTimeOffset(DateTime.SpecifyKind(from.Date, DateTimeKind.Unspecified), TimeSpan.Zero) : null,
        ToDateUtc = ToDate is { } to ? new DateTimeOffset(DateTime.SpecifyKind(to.Date.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified), TimeSpan.Zero) : null,
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
    };

    private List<BulkPostingDocumentType> SelectedTypes()
    {
        var types = new List<BulkPostingDocumentType>(5);
        if (IncludeSales) types.Add(BulkPostingDocumentType.SalesInvoice);
        if (IncludePurchases) types.Add(BulkPostingDocumentType.PurchaseInvoice);
        if (IncludeVouchers) types.Add(BulkPostingDocumentType.Voucher);
        if (IncludeJournals) types.Add(BulkPostingDocumentType.Journal);
        if (IncludeInventory) types.Add(BulkPostingDocumentType.InventoryDocument);
        return types;
    }

    private Task<bool> PreviewAsync() => RunAsync(PreviewCoreAsync, OperationKind.Read);

    private async Task PreviewCoreAsync(CancellationToken cancellationToken)
    {
        if (!HasContext) return;
        var result = await client.PreviewAsync(CompanyId, BranchId, BuildFilter(), cancellationToken).ConfigureAwait(true);
        foreach (var row in PreviewItems) row.PropertyChanged -= PreviewRowChanged;
        PreviewItems.Clear();
        foreach (var item in result.Items ?? []) PreviewItems.Add(new PostingPreviewRow(item));
        foreach (var row in PreviewItems) row.PropertyChanged += PreviewRowChanged;
        TotalExamined = result.TotalExamined ?? PreviewItems.Count;
        ReadyToPost = result.ReadyToPost ?? 0;
        NeedsApproval = result.NeedsApproval ?? 0;
        Ineligible = result.Ineligible ?? 0;
        HasPreview = true;
        IsConfirmed = false;
        SelectedCount = 0;
        StatusMessage = PreviewItems.Count == 0
            ? "لا توجد مستندات مطابقة للتصفية المحددة."
            : PreviewSummaryText;
        NotifyViewState();
    }

    private void PreviewRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(PostingPreviewRow.IsSelected)) return;
        SelectedCount = PreviewItems.Count(i => i.IsSelected && i.Postable);
        OnPropertyChanged(nameof(SelectionText));
        NotifyCommands();
    }

    private async Task PostAllAsync()
    {
        if (!IsConfirmed || !HasPostable) return;
        await PostCoreAsync(null).ConfigureAwait(true);
    }

    private async Task PostSelectedAsync()
    {
        if (!IsConfirmed) return;
        var documents = PreviewItems
            .Where(i => i.IsSelected && i.Postable)
            .Select(i => new BulkPostingDocumentReferenceDto { DocumentType = i.DocumentType, DocumentId = i.DocumentId })
            .ToList();
        if (documents.Count == 0) return;
        await PostCoreAsync(documents).ConfigureAwait(true);
    }

    private async Task PostCoreAsync(IReadOnlyList<BulkPostingDocumentReferenceDto>? documents)
    {
        if (!await confirmation.ConfirmAsync("D_ConfirmBulkPosting").ConfigureAwait(true)) return;
        // The key identifies this whole request. After an ambiguous failure it is reused, never replaced, so a retry
        // reports the existing batch instead of posting the same documents under a second key.
        pendingKey ??= "wpf-" + Guid.NewGuid().ToString("N");
        var request = new StartBulkPostingRequest
        {
            IdempotencyKey = pendingKey,
            Filter = BuildFilter(),
            Documents = documents,
            Confirmed = true,
            Reason = string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim(),
        };

        var completed = await RunAsync(async token =>
        {
            BulkPostingBatchDto batch;
            // A timeout is not a rejection: the server keeps the batch with its per-document keys, so starting again with the SAME key
            // resumes where it stopped instead of posting anything twice. Any other failure (503, validation) stays operator-driven,
            // because re-issuing a request the server refused would just repeat the refusal.
            var timeouts = 0;
            while (true)
            {
                try
                {
                    batch = await client.StartAsync(CompanyId, BranchId, request, token).ConfigureAwait(true);
                    break;
                }
                catch (TaskCanceledException) when (!token.IsCancellationRequested && ++timeouts < SafeResumeAttempts)
                {
                    StatusMessage = $"انتهت مهلة الاستجابة أثناء تنفيذ الدفعة؛ يُستأنف بنفس المفتاح (محاولة {timeouts} من {SafeResumeAttempts}) — لا تُرحَّل أي مستندية مرتين.";
                    NotifyViewState();
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(true);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    IsUncertain = true;
                    StatusMessage = "لم نتأكد من نتيجة الطلب. استخدم إعادة المحاولة الآمنة بنفس المفتاح قبل أي طلب جديد.";
                    NotifyViewState();
                    throw;
                }
            }

            pendingKey = null;
            IsUncertain = false;
            ApplyBatch(batch);
            IsConfirmed = false;
            if (batch.BatchId is { } batchId) await LoadResultsCoreAsync(batchId, token).ConfigureAwait(true);
            await PreviewCoreAsync(token).ConfigureAwait(true);
            StatusMessage = batch.Status == BulkPostingBatchStatus.Running || batch.Status == BulkPostingBatchStatus.Pending
                ? "الدفعة ما زالت تعمل على المستندات المتبقية؛ تابع النتائج ثم اضغط استئناف لإكمالها."
                : "اكتملت الدفعة. راجع النتائج لكل مستند أدناه.";
            NotifyViewState();
        }, OperationKind.Post).ConfigureAwait(true);

        if (!completed) NotifyCommands();
    }

    private Task<bool> ResumeAsync() => RunAsync(async token =>
    {
        if (LastBatchId is not { } batchId) return;
        var batch = await client.ResumeAsync(CompanyId, BranchId, batchId, RetryFailed, token).ConfigureAwait(true);
        ApplyBatch(batch);
        await LoadResultsCoreAsync(batchId, token).ConfigureAwait(true);
        StatusMessage = "أُعيد تشغيل الدفعة بنفس المفاتيح لكل مستند.";
        NotifyViewState();
    }, OperationKind.Post);

    private Task<bool> RefreshResultsAsync() => RunAsync(async token =>
    {
        if (LastBatchId is { } batchId) await LoadResultsCoreAsync(batchId, token).ConfigureAwait(true);
    }, OperationKind.Read);

    private async Task LoadResultsCoreAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var page = await client.GetResultsAsync(CompanyId, BranchId, batchId, null, null, 1, ResultsPageSize,
            cancellationToken).ConfigureAwait(true);
        Results.Clear();
        foreach (var item in page.Items ?? []) Results.Add(new PostingResultRow(item));
        ResultsTotal = page.TotalCount ?? Results.Count;
        if (page.Batch is { } batch) ApplyBatch(batch);
        OnPropertyChanged(nameof(HasResults));
        NotifyViewState();
    }

    private void ApplyBatch(BulkPostingBatchDto batch)
    {
        LastBatchId = batch.BatchId;
        LastBatchStatus = PostingDisplay.BatchStatus(batch.Status);
        BatchMessage = batch.Message ?? string.Empty;
        TotalExamined = batch.TotalExamined ?? 0;
        AlreadyPosted = batch.AlreadyPosted ?? 0;
        Eligible = batch.Eligible ?? 0;
        Posted = batch.Posted ?? 0;
        Skipped = batch.Skipped ?? 0;
        Failed = batch.Failed ?? 0;
        Unknown = batch.Unknown ?? 0;
        HasBatch = true;
        NotifyViewState();
    }

    private void DiscardPending()
    {
        pendingKey = null;
        IsUncertain = false;
        StatusMessage = "تم تجاهل المحاولة غير المؤكدة. اعرض المستندات من جديد قبل الترحيل.";
        NotifyViewState();
    }

    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(HasPostable));
        OnPropertyChanged(nameof(HasAnyType));
        OnPropertyChanged(nameof(TotalsText));
        OnPropertyChanged(nameof(PreviewSummaryText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(CanManage));
        NotifyCommands();
    }
}

/// <summary>One classified document in the preview. Only rows the server marked postable can be selected.</summary>
public sealed partial class PostingPreviewRow : ObservableObject
{
    public PostingPreviewRow(BulkPostingPreviewItemDto item)
    {
        DocumentType = item.DocumentType ?? BulkPostingDocumentType.SalesInvoice;
        DocumentId = item.DocumentId ?? Guid.Empty;
        DocumentNumber = item.DocumentNumber;
        DocumentDateUtc = item.DocumentDateUtc;
        PartyName = item.PartyName;
        Amount = item.Amount;
        CurrencyCode = item.CurrencyCode;
        CurrentStatus = item.CurrentStatus;
        Classification = item.Classification ?? BulkPostingClassification.Unknown;
        Postable = item.Postable == true;
        Reason = item.Reason ?? string.Empty;
    }

    public BulkPostingDocumentType DocumentType { get; }
    public Guid DocumentId { get; }
    public string? DocumentNumber { get; }
    public DateTimeOffset? DocumentDateUtc { get; }
    public string? PartyName { get; }
    public string? Amount { get; }
    public string? CurrencyCode { get; }
    public string? CurrentStatus { get; }
    public BulkPostingClassification Classification { get; }
    public bool Postable { get; }
    public string Reason { get; }

    [ObservableProperty] private bool isSelected;

    public string TypeDisplay => PostingDisplay.DocumentType(DocumentType);
    public string NumberDisplay => string.IsNullOrWhiteSpace(DocumentNumber) ? "(بلا رقم)" : DocumentNumber;
    public string DateDisplay => DocumentDateUtc?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    public string PartyDisplay => string.IsNullOrWhiteSpace(PartyName) ? "—" : PartyName;
    public string AmountDisplay => string.IsNullOrWhiteSpace(Amount) ? "—" : Amount + (string.IsNullOrWhiteSpace(CurrencyCode) ? string.Empty : " " + CurrencyCode);
    public string ClassificationDisplay => PostingDisplay.Classification(Classification);
}

/// <summary>One persisted per-document outcome after a batch ran.</summary>
public sealed class PostingResultRow
{
    /// <summary>Document lifecycle statuses arrive as English codes from the server; the grid shows them in Arabic.</summary>
    private static string? StatusText(string? status) => status switch
    {
        "Draft" => "مسودة",
        "Approved" => "معتمد",
        "Posted" => "مرحّل",
        "Reversed" => "معكوس",
        "Cancelled" or "Canceled" => "ملغى",
        "Pending" => "قيد الانتظار",
        _ => status,
    };

    public PostingResultRow(BulkPostingItemDto item)
    {
        DocumentType = item.DocumentType ?? BulkPostingDocumentType.SalesInvoice;
        DocumentNumber = item.DocumentNumber;
        DocumentDateUtc = item.DocumentDateUtc;
        PartyName = item.PartyName;
        Amount = item.Amount;
        CurrencyCode = item.CurrencyCode;
        PreviousStatus = StatusText(item.PreviousStatus);
        NewStatus = StatusText(item.NewStatus);
        JournalEntryId = item.JournalEntryId;
        PostingNumber = item.PostingNumber;
        Status = item.Status ?? BulkPostingItemStatus.Pending;
        Classification = item.Classification ?? BulkPostingClassification.Unknown;
        Reason = item.Reason ?? string.Empty;
        FailureCode = item.FailureCode;
        AttemptCount = item.AttemptCount ?? 0;
        LastAttemptAtUtc = item.LastAttemptAtUtc;
    }

    public BulkPostingDocumentType DocumentType { get; }
    public string? DocumentNumber { get; }
    public DateTimeOffset? DocumentDateUtc { get; }
    public string? PartyName { get; }
    public string? Amount { get; }
    public string? CurrencyCode { get; }
    public string? PreviousStatus { get; }
    public string? NewStatus { get; }
    public Guid? JournalEntryId { get; }
    public string? PostingNumber { get; }
    public BulkPostingItemStatus Status { get; }
    public BulkPostingClassification Classification { get; }
    public string Reason { get; }
    public string? FailureCode { get; }
    public int AttemptCount { get; }
    public DateTimeOffset? LastAttemptAtUtc { get; }

    public string TypeDisplay => PostingDisplay.DocumentType(DocumentType);
    public string NumberDisplay => string.IsNullOrWhiteSpace(DocumentNumber) ? "(بلا رقم)" : DocumentNumber;
    public string DateDisplay => DocumentDateUtc?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    public string PartyDisplay => string.IsNullOrWhiteSpace(PartyName) ? "—" : PartyName;
    public string AmountDisplay => string.IsNullOrWhiteSpace(Amount) ? "—" : Amount + (string.IsNullOrWhiteSpace(CurrencyCode) ? string.Empty : " " + CurrencyCode);
    public string JournalDisplay => JournalEntryId?.ToString("N")[..8] ?? "—";
    public string PostingNumberDisplay => string.IsNullOrWhiteSpace(PostingNumber) ? "—" : PostingNumber;
    public string StatusDisplay => PostingDisplay.ItemStatus(Status);
    public string ClassificationDisplay => PostingDisplay.Classification(Classification);
}

internal static class PostingDisplay
{
    internal static string DocumentType(BulkPostingDocumentType type) => type switch
    {
        BulkPostingDocumentType.SalesInvoice => "فاتورة مبيعات",
        BulkPostingDocumentType.PurchaseInvoice => "فاتورة شراء",
        BulkPostingDocumentType.Voucher => "سند",
        BulkPostingDocumentType.Journal => "قيد",
        BulkPostingDocumentType.InventoryDocument => "مستند مخزون",
        _ => type.ToString(),
    };

    internal static string Classification(BulkPostingClassification classification) => classification switch
    {
        BulkPostingClassification.ReadyForPosting => "جاهز للترحيل",
        BulkPostingClassification.CanBeApprovedAutomatically => "سيُعتمد ويُرحّل تلقائياً",
        BulkPostingClassification.Incomplete => "غير مكتمل",
        BulkPostingClassification.MissingPermission => "صلاحية ناقصة",
        BulkPostingClassification.ClosedAccountingPeriod => "فترة محاسبية مغلقة",
        BulkPostingClassification.InvalidCurrencyOrExchangeRate => "عملة أو سعر صرف غير صالح",
        BulkPostingClassification.MissingAccountMapping => "ربط حسابي ناقص",
        BulkPostingClassification.InsufficientStockOrValuation => "مخزون أو تكلفة غير كافية",
        BulkPostingClassification.ConcurrencyConflict => "تعارض تعديل",
        BulkPostingClassification.AlreadyPosted => "مُرحّل مسبقاً",
        BulkPostingClassification.UnsupportedSpecialDocumentType => "مستند خاص يحتاج معالجة فردية",
        BulkPostingClassification.IneligibleDocumentState => "حالة لا تسمح بالترحيل",
        _ => "غير معروف",
    };

    internal static string ItemStatus(BulkPostingItemStatus status) => status switch
    {
        BulkPostingItemStatus.Pending => "بانتظار",
        BulkPostingItemStatus.Running => "قيد التنفيذ",
        BulkPostingItemStatus.Posted => "مُرحّل",
        BulkPostingItemStatus.Skipped => "تم تخطيه",
        BulkPostingItemStatus.Failed => "فشل",
        BulkPostingItemStatus.Unknown => "غير مؤكد",
        _ => status.ToString(),
    };

    internal static string BatchStatus(BulkPostingBatchStatus? status) => status switch
    {
        BulkPostingBatchStatus.Pending => "بانتظار التنفيذ",
        BulkPostingBatchStatus.Running => "قيد التنفيذ",
        BulkPostingBatchStatus.Completed => "اكتملت",
        BulkPostingBatchStatus.CompletedWithFailures => "اكتملت مع حالات لم تُرحّل",
        BulkPostingBatchStatus.Failed => "فشلت",
        BulkPostingBatchStatus.Cancelled => "ملغاة",
        _ => "غير معروفة",
    };
}
