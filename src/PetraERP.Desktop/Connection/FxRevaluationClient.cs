using PetraERP.ApiClient;
using PetraERP.Desktop.Currencies;

namespace PetraERP.Desktop.Connection;

public sealed class FxRevaluationClient(DesktopSession session) : IFxRevaluationClient
{
    public Task<FxRevaluationPreviewResponse> PreviewFxRevaluationAsync(
        Guid companyId,
        Guid branchId,
        FxRevaluationPreviewRequest request,
        CancellationToken cancellationToken = default) =>
        session.Api.PreviewFxRevaluationAsync(companyId, branchId, request, cancellationToken);

    public Task<FxRateAuditResponse> AuditFxRatesAsync(
        Guid companyId,
        Guid branchId,
        FxRateAuditRequest request,
        CancellationToken cancellationToken = default) =>
        session.Api.AuditFxRatesAsync(companyId, branchId, request, cancellationToken);

    public Task<FxRevaluationExecutionResponse> ExecuteFxRevaluationAsync(
        Guid companyId,
        Guid branchId,
        ExecuteFxRevaluationRequest request,
        CancellationToken cancellationToken = default) =>
        session.Api.ExecuteFxRevaluationAsync(companyId, branchId, request, cancellationToken);
}
