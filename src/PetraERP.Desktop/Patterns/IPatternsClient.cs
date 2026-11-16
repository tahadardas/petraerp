using PetraERP.ApiClient;

namespace PetraERP.Desktop.Patterns;

/// <summary>
/// Client contract for Document Pattern operations through the generated API client.
/// All money values use exact decimal strings; operations are server-authoritative.
/// </summary>
public interface IPatternsClient
{
    Task<DocumentPatternPageResponse> ListPatternsAsync(Guid companyId, Guid branchId, int page, int pageSize, BusinessOperationKind? kind, bool? activeOnly, CancellationToken cancellationToken);
    Task<DocumentPatternResponse> GetPatternAsync(Guid companyId, Guid branchId, Guid patternId, CancellationToken cancellationToken);
    Task<DocumentPatternResponse> CreatePatternAsync(Guid companyId, Guid branchId, CreateDocumentPatternRequest request, CancellationToken cancellationToken);
    Task<DocumentPatternResponse> UpdatePatternAsync(Guid companyId, Guid branchId, Guid patternId, string expectedVersion, UpdateDocumentPatternRequest request, CancellationToken cancellationToken);
    Task<PatternRevisionResponse> CreateRevisionAsync(Guid companyId, Guid branchId, Guid patternId, CreatePatternRevisionRequest request, CancellationToken cancellationToken);
    Task<DocumentPatternResponse> PublishRevisionAsync(Guid companyId, Guid branchId, Guid patternId, PublishPatternRevisionRequest request, CancellationToken cancellationToken);
    Task<DocumentPatternResponse> RetirePatternAsync(Guid companyId, Guid branchId, Guid patternId, RetirePatternRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentPatternSummaryResponse>> GetRunnablePatternsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<NextNumberResponse> GenerateNextNumberAsync(Guid companyId, Guid branchId, Guid patternId, Guid? fiscalYearId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the document number the pattern would most likely issue, without reserving it, so an abandoned draft cannot
    /// burn a number. The saved document always carries the number the server allocated at save time.
    /// </summary>
    Task<NextNumberPreviewResponse> PreviewNextNumberAsync(Guid companyId, Guid branchId, Guid patternId, Guid? fiscalYearId,
        DateOnly? accountingDate, Guid? revisionId, CancellationToken cancellationToken);
}
