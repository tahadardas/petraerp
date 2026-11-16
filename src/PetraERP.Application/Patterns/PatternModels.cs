using PetraERP.Application.Common;
using PetraERP.Domain.Patterns;

namespace PetraERP.Application.Patterns;

public sealed record PatternScope
{
    public Guid CompanyId { get; }
    public Guid BranchId { get; }
    public Guid ActorUserId { get; }

    private PatternScope(Guid companyId, Guid branchId, Guid actorUserId)
    {
        CompanyId = companyId;
        BranchId = branchId;
        ActorUserId = actorUserId;
    }

    public static PatternScope Create(Guid companyId, Guid branchId, Guid actorUserId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID is required.", nameof(companyId));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID is required.", nameof(branchId));
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor user ID is required.", nameof(actorUserId));
        return new PatternScope(companyId, branchId, actorUserId);
    }
}

public sealed record CreateDocumentPatternCommand
{
    public required BusinessOperationKind OperationKind { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required string ShortCode { get; init; }
    public string? Icon { get; init; }
    public string? Group { get; init; }
    public bool IsDefault { get; init; }
    public IReadOnlyList<Guid>? BranchIds { get; init; }
    public PatternConfiguration? InitialConfiguration { get; init; }
}

public sealed record UpdateDocumentPatternCommand
{
    public required ConcurrencyToken ExpectedVersion { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required string ShortCode { get; init; }
    public string? Icon { get; init; }
    public string? Group { get; init; }
    public bool IsActive { get; init; }
    public bool IsDefault { get; init; }
    public IReadOnlyList<Guid>? BranchIds { get; init; }
}

public sealed record CreatePatternRevisionCommand
{
    public required ConcurrencyToken ExpectedVersion { get; init; }
    public required PatternConfiguration Configuration { get; init; }
    public required string Reason { get; init; }
}

public sealed record PublishPatternRevisionCommand
{
    public required ConcurrencyToken ExpectedVersion { get; init; }
    public required Guid RevisionId { get; init; }
    public required string Reason { get; init; }
}

public sealed record RetirePatternCommand
{
    public required ConcurrencyToken ExpectedVersion { get; init; }
    public required string Reason { get; init; }
}

public sealed record GetNextPatternNumberCommand
{
    public Guid? AllocationKey { get; init; }
    public DateOnly? AccountingDate { get; init; }
    public string? ManualNumber { get; init; }
    public required Guid PatternId { get; init; }
    public Guid? FiscalYearId { get; init; }
    /// <summary>Frozen revision for an existing document; HTTP number previews keep using the active revision.</summary>
    public Guid? RevisionId { get; init; }
}

public sealed record DocumentPatternSummaryDto
{
    public required Guid Id { get; init; }
    public required BusinessOperationKind OperationKind { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required string ShortCode { get; init; }
    public string? Icon { get; init; }
    public string? Group { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsDefault { get; init; }
    public required bool IsRunnable { get; init; }
    public int? ActiveRevisionNumber { get; init; }
    public required ConcurrencyToken Version { get; init; }
}

public sealed record DocumentPatternDetailsDto
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public required BusinessOperationKind OperationKind { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required string ShortCode { get; init; }
    public string? Icon { get; init; }
    public string? Group { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsDefault { get; init; }
    public required bool IsRunnable { get; init; }
    public required ConcurrencyToken Version { get; init; }
    public required IReadOnlyList<Guid> BranchIds { get; init; }
    public PatternRevisionDto? ActiveRevision { get; init; }
    public IReadOnlyList<PatternRevisionDto> Revisions { get; init; } = [];
}

public sealed record PatternRevisionDto
{
    public required Guid Id { get; init; }
    public required Guid PatternId { get; init; }
    public required int RevisionNumber { get; init; }
    public required PatternRevisionState State { get; init; }
    public DateTime? EffectiveFromUtc { get; init; }
    public DateTime? EffectiveToUtc { get; init; }
    public required PatternConfiguration Configuration { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required string Reason { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}

public sealed record PatternNumberResultDto
{
    public required long SequenceNumber { get; init; }
    public required string FormattedNumber { get; init; }
}

/// <summary>
/// The number a document would most likely receive, read without touching the counter so an abandoned draft cannot
/// burn a number. Another session can take it first, so it is shown as an estimate and the real value is allocated
/// by the server when the document is saved.
/// </summary>
public sealed record PatternNumberPreviewDto
{
    public required bool IsAvailable { get; init; }
    public required long SequenceNumber { get; init; }
    public required string FormattedNumber { get; init; }

    /// <summary>Stable reason the estimate is unavailable, for the tooltip; null when a number was produced.</summary>
    public string? UnavailableReason { get; init; }

    public static PatternNumberPreviewDto Unavailable(string reason) =>
        new() { IsAvailable = false, SequenceNumber = 0, FormattedNumber = string.Empty, UnavailableReason = reason };
}
