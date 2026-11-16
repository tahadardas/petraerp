using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Security;
using PetraERP.Contracts.Financial;

namespace PetraERP.Api.Endpoints;

/// <summary>Analytical report suite: one route, many catalogued reports (see <see cref="IAnalyticalReportService.SupportedReportIds"/>).</summary>
internal static class AnalyticalReportEndpoints
{
    internal static void MapAnalyticalReportEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/companies/{companyId:guid}/branches/{branchId:guid}/reports/analytical/{reportId}", RunAsync).WithName("RunAnalyticalReport")
            .WithTags("Financial and commercial reports")
            .WithSummary("Runs a catalogued analytical report (FX-04, FX-05, GL-08, FIN-03, FIN-04, FIN-08) over posted journal lines in the caller's branch scope.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.ReportsView))
            .Produces<AnalyticalReportResponse>().ProducesStandardProblems();
    }

    private static async Task<IResult> RunAsync(Guid companyId, Guid branchId, string reportId, DateOnly? from, DateOnly? to, string? currencyCode, Guid? accountId,
        bool? consolidated, IAnalyticalReportService service, HttpContext http, CancellationToken ct)
    {
        var id = reportId.Trim().ToUpperInvariant();
        if (!service.SupportedReportIds.Contains(id, StringComparer.Ordinal))
            throw new ResourceNotFoundException("report");
        var scope = LedgerReportScope.Create(companyId, branchId, http.RequireSession().UserId, consolidated ?? false);
        var r = await service.RunAsync(scope, id, new AnalyticalQuery(from, to, currencyCode, accountId), ct).ConfigureAwait(false);
        return TypedResults.Ok(new AnalyticalReportResponse
        {
            ReportId = r.ReportId, TitleArabic = r.TitleArabic, TitleEnglish = r.TitleEnglish, BaseCurrencyCode = r.BaseCurrencyCode,
            Columns = r.Columns.Select(c => new AnalyticalColumnDto { Key = c.Key, TitleArabic = c.TitleArabic, TitleEnglish = c.TitleEnglish, Kind = c.Kind.ToString() }).ToList(),
            Rows = r.Rows.Select(row => new AnalyticalRowDto { Cells = row.ToList() }).ToList(),
            Totals = r.Totals?.ToList(), Notes = r.Notes.ToList(),
        });
    }
}
