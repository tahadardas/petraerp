using PetraERP.ApiClient;

namespace PetraERP.Desktop.Connection;

public sealed class DocumentPostingClient(DesktopSession session) : DocumentProcessing.IDocumentPostingClient
{
    public Task<BulkPostingPreviewResponse> PreviewAsync(Guid companyId, Guid branchId, BulkPostingFilterDto filter,
        CancellationToken cancellationToken) =>
        session.Api.PreviewPostingBatchAsync(companyId, branchId, new BulkPostingPreviewRequest { Filter = filter },
            cancellationToken);

    public Task<BulkPostingBatchDto> StartAsync(Guid companyId, Guid branchId, StartBulkPostingRequest request,
        CancellationToken cancellationToken) =>
        session.Api.StartPostingBatchAsync(companyId, branchId, request, cancellationToken);

    public Task<BulkPostingBatchDto> GetAsync(Guid companyId, Guid branchId, Guid batchId,
        CancellationToken cancellationToken) =>
        session.Api.GetPostingBatchAsync(companyId, branchId, batchId, cancellationToken);

    public Task<BulkPostingBatchDto> ResumeAsync(Guid companyId, Guid branchId, Guid batchId, bool retryFailed,
        CancellationToken cancellationToken) =>
        session.Api.ResumePostingBatchAsync(companyId, branchId, batchId, new ResumeBulkPostingRequest { RetryFailed = retryFailed },
            cancellationToken);

    public Task<BulkPostingResultsResponse> GetResultsAsync(Guid companyId, Guid branchId, Guid batchId,
        BulkPostingItemStatus? status, BulkPostingDocumentType? documentType, int page, int pageSize,
        CancellationToken cancellationToken) =>
        session.Api.GetPostingBatchResultsAsync(companyId, branchId, batchId, status, documentType, page, pageSize,
            cancellationToken);
}
