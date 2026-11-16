using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Security;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Endpoints;

internal static class AccountCardContextEndpoints
{
    internal static void MapAccountCardContext(this RouteGroupBuilder accounts) =>
        accounts.MapGet("/{accountId:guid}/context", ReadAsync).WithName("GetAccountCardContext")
            .WithSummary("Root-to-account path and a bounded page of immediate children in ordinal code order.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.AccountsView))
            .Produces<AccountCardContextResponse>().ProducesStandardProblems();

    private static async Task<IResult> ReadAsync(Guid companyId, Guid branchId, Guid accountId, int? page, int? pageSize,
        IChartOfAccountsService service, HttpContext http, CancellationToken token)
    {
        var result = await service.GetAccountCardContextAsync(AccountHistoryEndpoints.Scope(companyId, branchId, http), accountId,
            PageRequest.Create(page, pageSize), token).ConfigureAwait(false);
        return TypedResults.Ok(new AccountCardContextResponse
        {
            Path = result.Path.Select(item => item.ToResponse()).ToArray(),
            Children = new AccountPageResponse { Items = result.Children.Items.Select(item => item.ToResponse()).ToArray(),
                Page = result.Children.Page, PageSize = result.Children.PageSize, TotalCount = result.Children.TotalCount },
        });
    }
}
