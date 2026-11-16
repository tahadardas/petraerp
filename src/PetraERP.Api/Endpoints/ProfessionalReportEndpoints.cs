using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports.Professional;
using PetraERP.Application.Security;
using Wire = PetraERP.Contracts.Reports;

namespace PetraERP.Api.Endpoints;

internal static class ProfessionalReportEndpoints
{
    internal static void MapProfessionalReportEndpoints(this RouteGroupBuilder api)
    {
        var reports = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/reporting")
            .WithTags("Professional reporting").RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.ReportsView));
        reports.MapGet("/catalog", CatalogAsync).WithName("GetProfessionalReportCatalog")
            .WithSummary("Permission-aware report sources, fields, implemented options and company fiscal years.")
            .Produces<Wire.ProfessionalReportCatalogResponse>().ProducesStandardProblems();
        reports.MapPost("/execute", ExecuteAsync).WithName("ExecuteProfessionalReport")
            .WithSummary("Complete immutable financial snapshot; actual balances remain distinct from filtered display totals. Rejects more than 100,000 rows.")
            .Produces<Wire.ProfessionalReportExecutionResponse>().ProducesStandardProblems();
        reports.MapGet("/parties/{partyId:guid}/activity-currencies", PartyCurrenciesAsync).WithName("GetProfessionalPartyActivityCurrencies")
            .WithSummary("Transaction currencies with posted party activity through the selected date.")
            .Produces<IReadOnlyList<string>>().ProducesStandardProblems();
    }

    private static async Task<IResult> PartyCurrenciesAsync(Guid companyId, Guid branchId, Guid partyId, Guid fiscalYearId,
        Wire.ProfessionalReportKind kind, DateOnly to, IProfessionalReportService service, HttpContext http, CancellationToken cancellationToken)
    {
        var scope = LedgerReportScope.Create(companyId, branchId, http.RequireSession().UserId, false);
        var result = await service.GetPartyActivityCurrenciesAsync(scope, fiscalYearId, partyId,
            (PetraERP.Application.Reports.Professional.ProfessionalReportKind)kind, to, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> CatalogAsync(Guid companyId, Guid branchId, IProfessionalReportService service, HttpContext http,
        CancellationToken cancellationToken)
    {
        var scope = LedgerReportScope.Create(companyId, branchId, http.RequireSession().UserId, false);
        var result = await service.GetCatalogAsync(scope, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ProfessionalReportMapping.Map(result));
    }

    private static async Task<IResult> ExecuteAsync(Guid companyId, Guid branchId, Wire.ProfessionalReportRequest request,
        IProfessionalReportService service, HttpContext http, CancellationToken cancellationToken)
    {
        var scope = LedgerReportScope.Create(companyId, branchId, http.RequireSession().UserId, false);
        if (request.Sources is null || request.Fields is null || request.PatternRevisionIds is null)
            throw new RequestValidationException("reporting.parameters_invalid", "Report collections cannot be null.", "parameters");
        var result = await service.ExecuteAsync(scope, ProfessionalReportMapping.ToApplication(request), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ProfessionalReportMapping.Map(result));
    }
}
