using PetraERP.ApiClient;

namespace PetraERP.Desktop.Currencies;

public interface IFxRevaluationClient
{
    Task<FxRevaluationPreviewResponse> PreviewFxRevaluationAsync(
        Guid companyId,
        Guid branchId,
        FxRevaluationPreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<FxRateAuditResponse> AuditFxRatesAsync(
        Guid companyId,
        Guid branchId,
        FxRateAuditRequest request,
        CancellationToken cancellationToken = default);

    Task<FxRevaluationExecutionResponse> ExecuteFxRevaluationAsync(
        Guid companyId,
        Guid branchId,
        ExecuteFxRevaluationRequest request,
        CancellationToken cancellationToken = default);
}
