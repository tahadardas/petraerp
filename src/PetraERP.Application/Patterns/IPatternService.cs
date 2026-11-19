using PetraERP.Application.Common;
using PetraERP.Domain.Patterns;

namespace PetraERP.Application.Patterns;

public interface IPatternService
{
    Task<DocumentPatternDetailsDto> CreatePatternAsync(
        PatternScope scope,
        CreateDocumentPatternCommand command,
        CancellationToken cancellationToken);

    Task<DocumentPatternDetailsDto> UpdatePatternAsync(
        PatternScope scope,
        Guid patternId,
        UpdateDocumentPatternCommand command,
        CancellationToken cancellationToken);

    Task<DocumentPatternDetailsDto> GetPatternAsync(
        PatternScope scope,
        Guid patternId,
        CancellationToken cancellationToken);

    Task<PagedResult<DocumentPatternSummaryDto>> ListPatternsAsync(
        PatternScope scope,
        BusinessOperationKind? kind,
        bool? activeOnly,
        PageRequest page,
        CancellationToken cancellationToken);

    Task<PatternRevisionDto> CreateRevisionAsync(
        PatternScope scope,
        Guid patternId,
        CreatePatternRevisionCommand command,
        CancellationToken cancellationToken);

    Task<DocumentPatternDetailsDto> PublishRevisionAsync(
        PatternScope scope,
        Guid patternId,
        PublishPatternRevisionCommand command,
        CancellationToken cancellationToken);

    Task<DocumentPatternDetailsDto> RetirePatternAsync(
        PatternScope scope,
        Guid patternId,
        RetirePatternCommand command,
        CancellationToken cancellationToken);

    Task<PatternNumberResultDto> GenerateNextNumberAsync(
        PatternScope scope,
        GetNextPatternNumberCommand command,
        CancellationToken cancellationToken);

    Task<PatternNumberPreviewDto> PreviewNextNumberAsync(
        PatternScope scope,
        GetNextPatternNumberCommand command,
        CancellationToken cancellationToken);

    /// <summary>The active published revision of a pattern this branch may run, voucher block resolved; not found otherwise.</summary>
    Task<EffectivePatternDto> GetEffectivePatternAsync(PatternScope scope, Guid patternId, CancellationToken cancellationToken);

    /// <summary>Posting accounts the pattern's lines may use (its lookup restriction applied), searched by code prefix or name.</summary>
    Task<IReadOnlyList<PatternAccountLookupItemDto>> SearchEligibleAccountsAsync(PatternScope scope, Guid patternId, string? search, int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentPatternSummaryDto>> GetRunnablePatternsForBranchAsync(
        PatternScope scope,
        CancellationToken cancellationToken);
}
