using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.DocumentProcessing;

public enum BulkPostingDocumentType
{
    SalesInvoice = 0,
    PurchaseInvoice = 1,
    Voucher = 2,
    Journal = 3,
    InventoryDocument = 4
}

/// <summary>Why the server will post, skip or leave a discovered document for reconciliation.</summary>
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

/// <summary>Server-side discovery scope. An empty type list means every supported document family.</summary>
public sealed record BulkPostingFilterDto
{
    public IReadOnlyList<BulkPostingDocumentType> DocumentTypes { get; init; } = [];
    public DateTimeOffset? FromDateUtc { get; init; }
    public DateTimeOffset? ToDateUtc { get; init; }
    [MaxLength(200)] public string? Search { get; init; }
}

public sealed record BulkPostingDocumentReferenceDto
{
    public BulkPostingDocumentType DocumentType { get; init; }
    public Guid DocumentId { get; init; }
}

public sealed record BulkPostingPreviewRequest
{
    public BulkPostingFilterDto Filter { get; init; } = new();

    /// <summary>Optional explicit selection. When supplied, the server still classifies and re-checks every one.</summary>
    public IReadOnlyList<BulkPostingDocumentReferenceDto>? Documents { get; init; }
}

public sealed record BulkPostingPreviewItemDto
{
    public BulkPostingDocumentType DocumentType { get; init; }
    public Guid DocumentId { get; init; }
    public string? DocumentNumber { get; init; }
    public DateTimeOffset DocumentDateUtc { get; init; }
    public string? PartyName { get; init; }

    /// <summary>Invariant decimal string, never a binary floating-point number.</summary>
    public string? Amount { get; init; }

    public string? CurrencyCode { get; init; }
    public string CurrentStatus { get; init; } = string.Empty;
    public BulkPostingClassification Classification { get; init; }
    public bool Postable { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
}

public sealed record BulkPostingClassificationCountDto
{
    public BulkPostingClassification Classification { get; init; }
    public int Count { get; init; }
}

public sealed record BulkPostingPreviewResponse
{
    public int TotalExamined { get; init; }
    public int ReadyToPost { get; init; }
    public int NeedsApproval { get; init; }
    public int Ineligible { get; init; }
    public IReadOnlyList<BulkPostingClassificationCountDto> Classifications { get; init; } = [];
    public IReadOnlyList<BulkPostingPreviewItemDto> Items { get; init; } = [];
}

/// <summary>A confirmed, idempotent bulk posting request. The key identifies the whole batch.</summary>
public sealed record StartBulkPostingRequest
{
    [Required, MaxLength(128)] public string IdempotencyKey { get; init; } = string.Empty;
    public BulkPostingFilterDto Filter { get; init; } = new();
    public IReadOnlyList<BulkPostingDocumentReferenceDto>? Documents { get; init; }

    /// <summary>The caller's explicit confirmation after reviewing the preview. Never assumed by the server.</summary>
    public bool Confirmed { get; init; }

    [MaxLength(500)] public string? Reason { get; init; }
}

public sealed record ResumeBulkPostingRequest
{
    /// <summary>Also retry documents whose last attempt failed deterministically. Unknown items always resume.</summary>
    public bool RetryFailed { get; init; }
}

public sealed record BulkPostingBatchDto
{
    public Guid BatchId { get; init; }
    public BulkPostingBatchStatus Status { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public int TotalExamined { get; init; }
    public int AlreadyPosted { get; init; }
    public int Eligible { get; init; }
    public int Posted { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public int Unknown { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed record BulkPostingItemDto
{
    public Guid ItemId { get; init; }
    public BulkPostingDocumentType DocumentType { get; init; }
    public Guid DocumentId { get; init; }
    public string? DocumentNumber { get; init; }
    public DateTimeOffset DocumentDateUtc { get; init; }
    public string? PartyName { get; init; }

    /// <summary>Invariant decimal string, never a binary floating-point number.</summary>
    public string? Amount { get; init; }

    public string? CurrencyCode { get; init; }
    public string PreviousStatus { get; init; } = string.Empty;
    public string? NewStatus { get; init; }
    public Guid? JournalEntryId { get; init; }
    public string? PostingNumber { get; init; }
    public BulkPostingItemStatus Status { get; init; }
    public BulkPostingClassification Classification { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? FailureCode { get; init; }
    public int AttemptCount { get; init; }
    public DateTimeOffset? LastAttemptAtUtc { get; init; }
}

public sealed record BulkPostingResultsResponse
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public BulkPostingBatchDto Batch { get; init; } = new();
    public IReadOnlyList<BulkPostingItemDto> Items { get; init; } = [];
}
