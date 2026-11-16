using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Security;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Endpoints;

/// <summary>Al-Ameen account-card profile: read with the view permission, replaced with the manage permission.</summary>
internal static class AccountProfileEndpoints
{
    internal static void MapAccountProfile(this RouteGroupBuilder accounts)
    {
        accounts.MapGet("/{accountId:guid}/profile", GetAsync).WithName("GetAccountProfile")
            .WithSummary("Reads the Al-Ameen account-card profile.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.AccountsView))
            .Produces<AccountProfileDto>().ProducesStandardProblems();
        accounts.MapPut("/{accountId:guid}/profile", SetAsync).WithName("SetAccountProfile")
            .WithSummary("Replaces the Al-Ameen account-card profile.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.AccountsManage))
            .Produces<AccountProfileDto>().ProducesStandardProblems();
    }

    private static async Task<IResult> GetAsync(Guid companyId, Guid branchId, Guid accountId, IAccountProfileService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok(ToDto(await service.GetAsync(AccountHistoryEndpoints.Scope(companyId, branchId, http), accountId, token).ConfigureAwait(false)));

    private static async Task<IResult> SetAsync(Guid companyId, Guid branchId, Guid accountId, AccountProfileDto request, IAccountProfileService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok(ToDto(await service.SetAsync(AccountHistoryEndpoints.Scope(companyId, branchId, http), accountId, ToData(request), token).ConfigureAwait(false)));

    private static AccountProfileData ToData(AccountProfileDto dto) => new(
        Notes: Text(dto.Notes), Nature: dto.Nature, ClosingAccountId: dto.ClosingAccountId, DefaultCostCenterId: dto.DefaultCostCenterId,
        BudgetKind: dto.BudgetKind, BudgetAmount: Parse(dto.BudgetAmount, FinancialDecimalText.MoneyScale, 15),
        BudgetCurrencyCode: Text(dto.BudgetCurrencyCode)?.ToUpperInvariant(),
        BudgetRate: Parse(dto.BudgetRate, FinancialDecimalText.RateScale, FinancialDecimalText.RateIntegerDigits),
        IncludeCommercialPapers: dto.IncludeCommercialPapers, IncomeStatementCategory: Text(dto.IncomeStatementCategory),
        EquityCategory: Text(dto.EquityCategory), FinancialPositionCategory: Text(dto.FinancialPositionCategory), CashFlowCategory: Text(dto.CashFlowCategory));

    private static AccountProfileDto ToDto(AccountProfileData data) => new()
    {
        Notes = data.Notes, Nature = data.Nature, ClosingAccountId = data.ClosingAccountId, DefaultCostCenterId = data.DefaultCostCenterId,
        BudgetKind = data.BudgetKind,
        BudgetAmount = data.BudgetAmount is { } amount ? FinancialDecimalText.Format(amount, FinancialDecimalText.MoneyScale) : null,
        BudgetCurrencyCode = data.BudgetCurrencyCode,
        BudgetRate = data.BudgetRate is { } rate ? FinancialDecimalText.Format(rate, FinancialDecimalText.RateScale) : null,
        IncludeCommercialPapers = data.IncludeCommercialPapers, IncomeStatementCategory = data.IncomeStatementCategory,
        EquityCategory = data.EquityCategory, FinancialPositionCategory = data.FinancialPositionCategory, CashFlowCategory = data.CashFlowCategory,
    };

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal? Parse(string? text, int scale, int integerDigits) =>
        FinancialDecimalText.TryParse(text, scale, integerDigits, out var value) ? value : null;
}
