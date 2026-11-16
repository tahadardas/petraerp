using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Purchases;
using PetraERP.Contracts.Sales;

namespace PetraERP.Contracts.DocumentProcessing;

/// <summary>
/// One save call under the effective company posting policy. The server reads the policy and the
/// caller's real grants; the client cannot select the mode or claim permission.
/// </summary>
public sealed record SaveAndProcessSalesInvoiceRequest
{
    public CreateSalesInvoiceRequest Document { get; init; } = new();

    /// <summary>Omit to create; supply to update an existing draft together with ExpectedVersion.</summary>
    public Guid? InvoiceId { get; init; }

    public string? ExpectedVersion { get; init; }

    /// <summary>Explicit Save as Draft. Never approves or posts in either mode.</summary>
    public bool SaveAsDraft { get; init; }

    [Required, MaxLength(128)]
    public string IdempotencyKey { get; init; } = string.Empty;

    [MaxLength(128)]
    public string? ManualNumber { get; init; }

    [MaxLength(500)]
    public string? PostingNotes { get; init; }

    /// <summary>The caller's explicit credit-warning acknowledgement; never assumed by the server.</summary>
    public bool ConfirmCreditWarning { get; init; }
    public bool OverrideCreditPolicy { get; init; }
    [MaxLength(500)]
    public string? CreditOverrideReason { get; init; }

    /// <summary>Required only when the company policy allows a negative stock issue and the line exceeds availability.</summary>
    [MaxLength(500)]
    public string? NegativeStockReason { get; init; }
}

public sealed record SaveAndProcessPurchaseInvoiceRequest
{
    public SavePurchaseInvoiceRequest Document { get; init; } = new();

    public Guid? InvoiceId { get; init; }
    public string? ExpectedVersion { get; init; }
    public bool SaveAsDraft { get; init; }

    [Required, MaxLength(128)]
    public string IdempotencyKey { get; init; } = string.Empty;

    [MaxLength(128)]
    public string? ManualNumber { get; init; }
}

public sealed record DocumentProcessingResultDto
{
    public string DocumentType { get; init; } = string.Empty;
    public Guid DocumentId { get; init; }
    public string? DocumentNumber { get; init; }
    public string Status { get; init; } = string.Empty;

    /// <summary>SavedDraft, ApprovalPermissionMissing, Approved, PostingPermissionMissing, Posted, PostingFailed, AlreadyPosted or ApprovalFailed.</summary>
    public string Outcome { get; init; } = string.Empty;

    public bool ApprovalApplied { get; init; }
    public bool PostingApplied { get; init; }
    public Guid? JournalEntryId { get; init; }
    public string EffectivePostingMode { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? Message { get; init; }
    public string? FailureCode { get; init; }
}

public sealed record DocumentCapabilityDto
{
    public string DocumentType { get; init; } = string.Empty;
    public bool SupportsDraft { get; init; }
    public bool SupportsApproval { get; init; }
    public bool SupportsPosting { get; init; }
    public bool SupportsAutomaticFinalization { get; init; }
    public bool RequiresSpecialConfirmation { get; init; }
    public string Note { get; init; } = string.Empty;
}

public sealed record SaveAndProcessVoucherRequest
{
    public PetraERP.Contracts.Treasury.SaveVoucherRequest Document { get; init; } = new();

    public Guid? VoucherId { get; init; }
    public string? ExpectedVersion { get; init; }
    public bool SaveAsDraft { get; init; }

    [Required, MaxLength(128)]
    public string IdempotencyKey { get; init; } = string.Empty;

    [MaxLength(128)]
    public string? ManualNumber { get; init; }
}
