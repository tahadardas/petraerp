using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.Reports;
using PetraERP.Application.Security;
using PetraERP.Contracts.Financial;

namespace PetraERP.Api.Endpoints;

/// <summary>The master report catalogue with an honest delivery state per report (implemented, partial or not built).</summary>
internal static class ReportMasterCatalogEndpoints
{
    internal static void MapReportMasterCatalogEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/companies/{companyId:guid}/branches/{branchId:guid}/reports/master-catalog", Get).WithName("GetReportMasterCatalog")
            .WithTags("Financial and commercial reports")
            .WithSummary("All catalogued reports (GL, FIN, FX, ARAP, CASH, INV, SALES, BUY, CC, SYSTEM) with delivery state; optional group and delivery filters.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.ReportsView))
            .Produces<ReportMasterCatalogResponse>().ProducesStandardProblems();
    }

    private static Microsoft.AspNetCore.Http.HttpResults.Ok<ReportMasterCatalogResponse> Get(Guid companyId, Guid branchId, string? group, string? delivery)
    {
        ReportDelivery? wanted = null;
        if (!string.IsNullOrWhiteSpace(delivery))
        {
            if (!Enum.TryParse<ReportDelivery>(delivery, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
                throw new RequestValidationException("reporting.delivery_invalid", "delivery must be NotImplemented, Partial or Implemented.", "delivery");
            wanted = parsed;
        }
        var all = ReportMasterCatalog.All;
        var items = all.Where(e => (string.IsNullOrWhiteSpace(group) || string.Equals(e.Group, group.Trim(), StringComparison.OrdinalIgnoreCase))
            && (wanted is null || e.Delivery == wanted)).ToList();
        return TypedResults.Ok(new ReportMasterCatalogResponse
        {
            Items = items.Select(e => new ReportCatalogEntryDto
            {
                Id = e.Id, Group = e.Group, NameArabic = e.NameArabic, NameEnglish = e.NameEnglish, Delivery = e.Delivery.ToString(), Route = e.Route, Permission = e.Permission, Note = e.Note,
            }).ToList(),
            Total = all.Count,
            Implemented = all.Count(e => e.Delivery == ReportDelivery.Implemented),
            Partial = all.Count(e => e.Delivery == ReportDelivery.Partial),
            NotImplemented = all.Count(e => e.Delivery == ReportDelivery.NotImplemented),
        });
    }
}
