using PetraERP.ApiClient;
using PetraERP.Desktop.Treasury;

namespace PetraERP.Desktop.Connection;

public sealed class CommercialPapersClient(DesktopSession session) : ICommercialPapersClient
{
    public Task<CommercialPaperListResponse> ListAsync(
        Guid companyId,
        Guid branchId,
        CommercialPaperStatus? status = null,
        CommercialPaperKind? kind = null,
        Guid? partyId = null,
        DateOnly? fromDueDate = null,
        DateOnly? toDueDate = null,
        string? query = null,
        CancellationToken cancellationToken = default) =>
        session.Api.ListCommercialPapersAsync(companyId, branchId, status, kind, partyId, fromDueDate, toDueDate, query, cancellationToken);

    public Task<CommercialPaperDto> RegisterAsync(
        Guid companyId,
        Guid branchId,
        RegisterCommercialPaperRequest request,
        CancellationToken cancellationToken = default) =>
        session.Api.RegisterCommercialPaperAsync(companyId, branchId, request, cancellationToken);

    public Task<CommercialPaperDto> TransitionStatusAsync(
        Guid companyId,
        Guid branchId,
        Guid paperId,
        TransitionCommercialPaperStatusRequest request,
        CancellationToken cancellationToken = default) =>
        session.Api.TransitionCommercialPaperStatusAsync(companyId, branchId, paperId, request, cancellationToken);

    public Task<CommercialPaperAlertsResponse> GetAlertsAsync(
        Guid companyId,
        Guid branchId,
        int? daysAhead = null,
        CancellationToken cancellationToken = default) =>
        session.Api.GetCommercialPaperAlertsAsync(companyId, branchId, daysAhead, cancellationToken);
}
