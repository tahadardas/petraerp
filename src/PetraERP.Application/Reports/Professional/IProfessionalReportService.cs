using PetraERP.Application.GeneralLedger.Reports;

namespace PetraERP.Application.Reports.Professional;

public interface IProfessionalReportService
{
    Task<ProfessionalReportCatalogResponse> GetCatalogAsync(LedgerReportScope scope, CancellationToken cancellationToken);
    Task<ProfessionalReportExecutionResponse> ExecuteAsync(LedgerReportScope scope, ProfessionalReportRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetPartyActivityCurrenciesAsync(LedgerReportScope scope, Guid fiscalYearId, Guid partyId,
        ProfessionalReportKind kind, DateOnly asOfDate, CancellationToken cancellationToken);
}
