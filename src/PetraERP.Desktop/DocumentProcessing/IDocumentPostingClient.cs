using PetraERP.ApiClient;

namespace PetraERP.Desktop.DocumentProcessing;

/// <summary>Server-authoritative bulk posting: preview, execute, resume and paginated per-document results.</summary>
public interface IDocumentPostingClient
{
    Task<BulkPostingPreviewResponse> PreviewAsync(Guid companyId, Guid branchId, BulkPostingFilterDto filter,
        CancellationToken cancellationToken);

    Task<BulkPostingBatchDto> StartAsync(Guid companyId, Guid branchId, StartBulkPostingRequest request,
        CancellationToken cancellationToken);

    Task<BulkPostingBatchDto> GetAsync(Guid companyId, Guid branchId, Guid batchId, CancellationToken cancellationToken);

    Task<BulkPostingBatchDto> ResumeAsync(Guid companyId, Guid branchId, Guid batchId, bool retryFailed,
        CancellationToken cancellationToken);

    Task<BulkPostingResultsResponse> GetResultsAsync(Guid companyId, Guid branchId, Guid batchId,
        BulkPostingItemStatus? status, BulkPostingDocumentType? documentType, int page, int pageSize,
        CancellationToken cancellationToken);
}

/// <summary>Typed drill-down from a document list's "Post All" button into Posting Management.</summary>
public sealed record BulkPostingNavigation(BulkPostingDocumentType DocumentType, DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null, string? Search = null);
