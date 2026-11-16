namespace PetraERP.Application.DocumentNumbers;

/// <summary>The document families whose printed number can be maintained.</summary>
public enum DocumentNumberFamily : byte
{
    SalesInvoice = 1,
    PurchaseInvoice = 2,
    Voucher = 3,
}

public enum DocumentNumberChangeState : byte
{
    Pending = 1,
    Applied = 2,
    Rejected = 3,
    Cancelled = 4,
}

/// <summary>A document that already uses a proposed number (or a pending request that reserves it).</summary>
public sealed record DocumentNumberCollision(DocumentNumberFamily Family, Guid? DocumentId, string Kind);

/// <summary>Dry run of a renumbering: nothing is written. <see cref="CanRequest"/> is false when any collision or rule blocks it.</summary>
public sealed record DocumentNumberPreviewResult(
    DocumentNumberFamily Family,
    Guid DocumentId,
    string? CurrentNumber,
    string ProposedNumber,
    bool CanRequest,
    string? BlockingCode,
    IReadOnlyList<DocumentNumberCollision> Collisions);

public sealed record RequestDocumentNumberChangeCommand(DocumentNumberFamily Family, Guid DocumentId, string NewNumber, string Reason);

public sealed record DocumentNumberDecisionCommand(string Reason);

public sealed record DocumentNumberChangeResult(
    Guid Id,
    DocumentNumberFamily Family,
    Guid DocumentId,
    string? OldNumber,
    string NewNumber,
    string Reason,
    DocumentNumberChangeState State,
    Guid RequestedByUserId,
    DateTimeOffset RequestedAtUtc,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason);

public sealed record DocumentNumberAuditEntry(
    long Sequence,
    Guid RequestId,
    DocumentNumberFamily Family,
    Guid DocumentId,
    string? OldNumber,
    string NewNumber,
    Guid RequestedByUserId,
    Guid ApprovedByUserId,
    DateTimeOffset OccurredAtUtc,
    string PreviousHash,
    string Hash);

public sealed record DocumentNumberChainVerification(bool Intact, long EntriesChecked, long? FirstBrokenSequence);

/// <summary>
/// Maintenance of printed document numbers. A change is a request approved by a different person; approval rewrites only
/// the number text (never the internal id, type, status or journal link) and appends a hash-chained audit entry. Stock
/// movements and journals keep the number they were created with as a historical snapshot and stay linked by id.
/// </summary>
public interface IDocumentNumberMaintenanceService
{
    Task<DocumentNumberPreviewResult> PreviewAsync(Guid companyId, Guid branchId, DocumentNumberFamily family, Guid documentId, string proposedNumber,
        CancellationToken cancellationToken = default);

    Task<DocumentNumberChangeResult> RequestAsync(Guid companyId, Guid branchId, Guid userId, RequestDocumentNumberChangeCommand command,
        CancellationToken cancellationToken = default);

    Task<DocumentNumberChangeResult> ApproveAsync(Guid companyId, Guid branchId, Guid userId, Guid requestId, DocumentNumberDecisionCommand command,
        CancellationToken cancellationToken = default);

    Task<DocumentNumberChangeResult> RejectAsync(Guid companyId, Guid branchId, Guid userId, Guid requestId, DocumentNumberDecisionCommand command,
        CancellationToken cancellationToken = default);

    Task<DocumentNumberChangeResult> CancelAsync(Guid companyId, Guid branchId, Guid userId, Guid requestId, DocumentNumberDecisionCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentNumberChangeResult>> ListAsync(Guid companyId, Guid branchId, DocumentNumberChangeState? state, int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentNumberAuditEntry>> AuditAsync(Guid companyId, Guid branchId, int take, CancellationToken cancellationToken = default);

    Task<DocumentNumberChainVerification> VerifyAuditChainAsync(Guid companyId, CancellationToken cancellationToken = default);
}
