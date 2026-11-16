namespace PetraERP.Application.DocumentProcessing;

/// <summary>Document families the bulk engine knows how to discover and hand to their own posting services.</summary>
public enum BulkPostingDocumentType
{
    SalesInvoice = 0,
    PurchaseInvoice = 1,
    Voucher = 2,
    Journal = 3,
    InventoryDocument = 4
}

/// <summary>Why a discovered document will be posted, skipped or left for reconciliation.</summary>
public enum BulkPostingClassification
{
    ReadyForPosting = 0,
    CanBeApprovedAutomatically = 1,
    Incomplete = 2,
    MissingPermission = 3,
    ClosedAccountingPeriod = 4,
    InvalidCurrencyOrExchangeRate = 5,
    MissingAccountMapping = 6,
    InsufficientStockOrValuation = 7,
    ConcurrencyConflict = 8,
    AlreadyPosted = 9,
    UnsupportedSpecialDocumentType = 10,
    IneligibleDocumentState = 11,
    Unknown = 12
}

public enum BulkPostingBatchStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    CompletedWithFailures = 3,
    Failed = 4,
    Cancelled = 5
}

public enum BulkPostingItemStatus
{
    Pending = 0,
    Running = 1,
    Posted = 2,
    Skipped = 3,
    Failed = 4,
    Unknown = 5
}

public sealed record BulkPostingDocumentReference(BulkPostingDocumentType DocumentType, Guid DocumentId);

/// <summary>Server-side scope for candidate discovery. Searches and date bounds are applied before classification.</summary>
public sealed record BulkPostingFilter
{
    public IReadOnlyList<BulkPostingDocumentType> DocumentTypes { get; init; } =
    [
        BulkPostingDocumentType.SalesInvoice,
        BulkPostingDocumentType.PurchaseInvoice,
        BulkPostingDocumentType.Voucher,
        BulkPostingDocumentType.Journal,
        BulkPostingDocumentType.InventoryDocument
    ];

    public DateTimeOffset? FromDateUtc { get; init; }
    public DateTimeOffset? ToDateUtc { get; init; }
    public string? Search { get; init; }
}

public sealed record BulkPostingPreviewItem(
    BulkPostingDocumentType DocumentType,
    Guid DocumentId,
    string? DocumentNumber,
    DateTimeOffset DocumentDateUtc,
    string? PartyName,
    decimal? Amount,
    string? CurrencyCode,
    string CurrentStatus,
    BulkPostingClassification Classification,
    bool Postable,
    string Reason,
    string Version);

public sealed record BulkPostingClassificationCount(BulkPostingClassification Classification, int Count);

public sealed record BulkPostingPreviewResult(
    int TotalExamined,
    int ReadyToPost,
    int NeedsApproval,
    int Ineligible,
    IReadOnlyList<BulkPostingClassificationCount> Classifications,
    IReadOnlyList<BulkPostingPreviewItem> Items);

/// <summary>A confirmed request to persist and execute a batch. The idempotency key identifies the whole batch.</summary>
public sealed record StartBulkPostingCommand
{
    public required string IdempotencyKey { get; init; }
    public required BulkPostingFilter Filter { get; init; }
    public IReadOnlyList<BulkPostingDocumentReference>? Documents { get; init; }
    public bool Confirmed { get; init; }
    public string Reason { get; init; } = "ترحيل جماعي للمستندات";
}

public sealed record ResumeBulkPostingCommand(bool RetryFailed = false);

public sealed record BulkPostingBatchResult(
    Guid BatchId,
    BulkPostingBatchStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int TotalExamined,
    int AlreadyPosted,
    int Eligible,
    int Posted,
    int Skipped,
    int Failed,
    int Unknown,
    string Message);

public sealed record BulkPostingItemResult(
    Guid ItemId,
    BulkPostingDocumentType DocumentType,
    Guid DocumentId,
    string? DocumentNumber,
    DateTimeOffset DocumentDateUtc,
    string? PartyName,
    decimal? Amount,
    string? CurrencyCode,
    string PreviousStatus,
    string? NewStatus,
    Guid? JournalEntryId,
    string? PostingNumber,
    BulkPostingItemStatus Status,
    BulkPostingClassification Classification,
    string Reason,
    string? FailureCode,
    int AttemptCount,
    DateTimeOffset? LastAttemptAtUtc);

public sealed record BulkPostingResultsPage(
    int Page,
    int PageSize,
    int TotalCount,
    BulkPostingBatchResult Batch,
    IReadOnlyList<BulkPostingItemResult> Items);

/// <summary>
/// Discovers unposted documents from the authoritative services, classifies every one of them and posts the
/// eligible items one at a time through the existing document-specific posting engine. Batch progress is
/// persisted after every item so an interrupted request can resume with the same per-document keys.
/// </summary>
public interface IBulkDocumentPostingService
{
    Task<BulkPostingPreviewResult> PreviewAsync(Guid companyId, Guid branchId, Guid userId, BulkPostingFilter filter,
        CancellationToken cancellationToken = default);

    Task<BulkPostingBatchResult> StartAsync(Guid companyId, Guid branchId, Guid userId, StartBulkPostingCommand command,
        CancellationToken cancellationToken = default);

    Task<BulkPostingBatchResult> GetAsync(Guid companyId, Guid branchId, Guid batchId,
        CancellationToken cancellationToken = default);

    Task<BulkPostingResultsPage> GetResultsAsync(Guid companyId, Guid branchId, Guid batchId,
        BulkPostingItemStatus? status, BulkPostingDocumentType? documentType, int page, int pageSize,
        CancellationToken cancellationToken = default);

    Task<BulkPostingBatchResult> ResumeAsync(Guid companyId, Guid branchId, Guid userId, Guid batchId,
        bool retryFailed, CancellationToken cancellationToken = default);
}
