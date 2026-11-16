using PetraERP.ApiClient;

namespace PetraERP.Desktop.Treasury;

public interface ICommercialPapersClient
{
    Task<CommercialPaperListResponse> ListAsync(
        Guid companyId,
        Guid branchId,
        CommercialPaperStatus? status = null,
        CommercialPaperKind? kind = null,
        Guid? partyId = null,
        DateOnly? fromDueDate = null,
        DateOnly? toDueDate = null,
        string? query = null,
        CancellationToken cancellationToken = default);

    Task<CommercialPaperDto> RegisterAsync(
        Guid companyId,
        Guid branchId,
        RegisterCommercialPaperRequest request,
        CancellationToken cancellationToken = default);

    Task<CommercialPaperDto> TransitionStatusAsync(
        Guid companyId,
        Guid branchId,
        Guid paperId,
        TransitionCommercialPaperStatusRequest request,
        CancellationToken cancellationToken = default);

    Task<CommercialPaperAlertsResponse> GetAlertsAsync(
        Guid companyId,
        Guid branchId,
        int? daysAhead = null,
        CancellationToken cancellationToken = default);
}
