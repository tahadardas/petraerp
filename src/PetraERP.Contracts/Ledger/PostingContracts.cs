using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.Ledger;

/// <summary>
/// Posts an approved journal. The idempotency key travels in the <c>Idempotency-Key</c> header and the
/// company/branch in the route; both are bound into the server's posting fingerprint.
/// </summary>
public sealed record PostJournalRequest
{
    /// <summary>Canonical Base64 of the journal's 8-byte database version read by the client.</summary>
    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string ExpectedVersion { get; init; }

    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Reason { get; init; }
}

public sealed record PostJournalReversalRequest
{
    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string ExpectedVersion { get; init; }

    [Required]
    public required Guid OriginalJournalId { get; init; }

    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string OriginalExpectedVersion { get; init; }

    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Reason { get; init; }
}

public enum PostingOperation
{
    PostJournal = 1,
    PostJournalReversal = 2,
}

/// <summary>Immutable acknowledgement of a committed posting. A replay with the same key returns the same receipt.</summary>
public sealed record PostingReceiptResponse
{
    public required Guid IdempotencyKey { get; init; }
    public required PostingOperation Operation { get; init; }
    public required Guid CompanyId { get; init; }
    public required Guid BranchId { get; init; }
    public required Guid JournalId { get; init; }
    public Guid? OriginalJournalId { get; init; }
    public required Guid FiscalYearId { get; init; }
    public required long PostingSequence { get; init; }
}
