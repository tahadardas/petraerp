using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Security;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Endpoints;

internal static class CostCenterHistoryEndpoints
{
    internal static void MapCostCenterHistory(this RouteGroupBuilder centers) =>
        centers.MapGet("/{costCenterId:guid}/audit", ReadAsync).WithName("GetCostCenterHistory")
            .WithSummary("Paged company-wide cost-center history recorded since audit deployment; prior events are not reconstructed.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.CostCentersView))
            .Produces<CostCenterHistoryPageResponse>().ProducesStandardProblems();

    private static async Task<IResult> ReadAsync(Guid companyId, Guid branchId, Guid costCenterId, int? page, int? pageSize,
        IChartOfAccountsService service, HttpContext http, CancellationToken token)
    {
        var result = await service.GetCostCenterHistoryAsync(AccountHistoryEndpoints.Scope(companyId, branchId, http), costCenterId,
            PageRequest.Create(page, pageSize), token).ConfigureAwait(false);
        return TypedResults.Ok(new CostCenterHistoryPageResponse { Page = result.Page, PageSize = result.PageSize, TotalCount = result.TotalCount,
            Items = result.Items.Select(item => new CostCenterHistoryResponse
            {
                EventId = item.EventId, CostCenterId = item.CostCenterId, Action = (CostCenterAuditAction)item.Action,
                ActorUserId = item.ActorUserId, ActorNameArabic = item.ActorNameArabic, ActorNameEnglish = item.ActorNameEnglish,
                WorkingBranchId = item.WorkingBranchId, CorrelationId = item.CorrelationId, OccurredAtUtc = item.OccurredAtUtc,
                Before = item.Before is null ? null : Map(item.Before), After = Map(item.After),
            }).ToArray() });
    }

    private static CostCenterStateResponse Map(CostCenterStateDto state) => new()
    { Code = state.Code, NameArabic = state.NameArabic, NameEnglish = state.NameEnglish, IsActive = state.IsActive };
}
