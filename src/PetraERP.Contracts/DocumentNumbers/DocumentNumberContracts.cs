using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.DocumentNumbers;

public sealed record DocumentNumberCollisionDto
{
    /// <summary>SalesInvoice | PurchaseInvoice | Voucher.</summary>
    public string Family { get; init; } = string.Empty;
    public Guid? DocumentId { get; init; }
    /// <summary>Document | PendingRequest.</summary>
    public string Kind { get; init; } = string.Empty;
}

public sealed record DocumentNumberPreviewResponse
{
    public string Family { get; init; } = string.Empty;
    public Guid DocumentId { get; init; }
    public string? CurrentNumber { get; init; }
    public string ProposedNumber { get; init; } = string.Empty;
    public bool CanRequest { get; init; }
    public string? BlockingCode { get; init; }
    public List<DocumentNumberCollisionDto> Collisions { get; init; } = [];
}

public sealed record RequestDocumentNumberChangeRequest
{
    /// <summary>SalesInvoice | PurchaseInvoice | Voucher.</summary>
    [Required, MaxLength(32)]
    public string Family { get; init; } = string.Empty;

    [Required]
    public Guid DocumentId { get; init; }

    [Required, MaxLength(64)]
    public string NewNumber { get; init; } = string.Empty;

    [Required, MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record DocumentNumberDecisionRequest
{
    [Required, MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record DocumentNumberChangeDto
{
    public Guid Id { get; init; }
    public string Family { get; init; } = string.Empty;
    public Guid DocumentId { get; init; }
    public string? OldNumber { get; init; }
    public string NewNumber { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    /// <summary>Pending | Applied | Rejected | Cancelled.</summary>
    public string State { get; init; } = string.Empty;
    public Guid RequestedByUserId { get; init; }
    public DateTimeOffset RequestedAtUtc { get; init; }
    public Guid? DecidedByUserId { get; init; }
    public DateTimeOffset? DecidedAtUtc { get; init; }
    public string? DecisionReason { get; init; }
}

public sealed record DocumentNumberChangeListResponse
{
    public List<DocumentNumberChangeDto> Items { get; init; } = [];
}

public sealed record DocumentNumberAuditDto
{
    public long Sequence { get; init; }
    public Guid RequestId { get; init; }
    public string Family { get; init; } = string.Empty;
    public Guid DocumentId { get; init; }
    public string? OldNumber { get; init; }
    public string NewNumber { get; init; } = string.Empty;
    public Guid RequestedByUserId { get; init; }
    public Guid ApprovedByUserId { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string PreviousHash { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
}

public sealed record DocumentNumberAuditResponse
{
    public List<DocumentNumberAuditDto> Items { get; init; } = [];
}

public sealed record DocumentNumberChainVerificationResponse
{
    public bool Intact { get; init; }
    public long EntriesChecked { get; init; }
    public long? FirstBrokenSequence { get; init; }
}
