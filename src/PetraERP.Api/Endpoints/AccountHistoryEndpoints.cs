using PetraERP.Api.Errors;
using PetraERP.Api.Hosting;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Security;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Endpoints;

internal static class AccountHistoryEndpoints
{
    internal static void MapAccountHistory(this RouteGroupBuilder accounts) =>
        accounts.MapGet("/{accountId:guid}/audit", ReadAsync).WithName("GetAccountHistory")
            .WithSummary("Paged company-wide account lifecycle history, retaining historical values after deletion.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.AccountsView))
            .Produces<AccountHistoryPageResponse>().ProducesStandardProblems();

    internal static LedgerMasterScope Scope(Guid companyId, Guid branchId, HttpContext http) =>
        LedgerMasterScope.Create(companyId, http.RequireSession().UserId, branchId,
            Guid.TryParse(CorrelationIdMiddleware.Get(http), out var correlation) && correlation != Guid.Empty ? correlation : null);

    private static async Task<IResult> ReadAsync(Guid companyId, Guid branchId, Guid accountId, int? page, int? pageSize,
        IChartOfAccountsService service, HttpContext http, CancellationToken token)
    {
        var result = await service.GetAccountHistoryAsync(Scope(companyId, branchId, http), accountId, PageRequest.Create(page, pageSize), token).ConfigureAwait(false);
        return TypedResults.Ok(new AccountHistoryPageResponse
        {
            Page = result.Page, PageSize = result.PageSize, TotalCount = result.TotalCount,
            Items = result.Items.Select(ToResponse).ToArray(),
        });
    }

    private static AccountHistoryResponse ToResponse(AccountHistoryDto item) => new()
    {
        EventId = item.EventId, AccountId = item.AccountId, Action = (AccountAuditAction)item.Action,
        Code = item.Code, NameArabic = item.NameArabic, NameEnglish = item.NameEnglish,
        ActorUserId = item.ActorUserId, ActorNameArabic = item.ActorNameArabic, ActorNameEnglish = item.ActorNameEnglish,
        WorkingBranchId = item.WorkingBranchId, CorrelationId = item.CorrelationId, Reason = item.Reason,
        OccurredAtUtc = item.OccurredAtUtc, Before = ToResponse(item.Before), After = ToResponse(item.After),
    };

    private static AccountStateResponse? ToResponse(AccountStateDto? state) => state is null ? null : new()
    {
        Code = state.Code, NameArabic = state.NameArabic, NameEnglish = state.NameEnglish, AccountType = (AccountType)state.AccountType,
        PostingAllowed = state.PostingAllowed, CostCenterPolicy = (CostCenterPolicy)state.CostCenterPolicy,
        CurrencyMode = (AccountCurrencyMode)state.CurrencyMode, FixedCurrencyCode = state.FixedCurrencyCode,
        ParentId = state.ParentId, IsActive = state.IsActive,
    };
}
