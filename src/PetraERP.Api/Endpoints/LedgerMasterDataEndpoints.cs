using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Calendars;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Security;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Endpoints;

/// <summary>
/// Chart of accounts, cost centers, fiscal calendar and company currency context. Master data is company-wide
/// (company-scoped permissions); the route branch is the caller's working context, as for every ledger route.
/// </summary>
internal static class LedgerMasterDataEndpoints
{
    internal static void MapLedgerMasterDataEndpoints(this RouteGroupBuilder api)
    {
        var ledger = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/ledger");
        var accounts = ledger.MapGroup("/chart/accounts").WithTags("General ledger: chart of accounts");
        var viewAccounts = PetraPolicies.Permission(PermissionCatalog.AccountsView);
        var manageAccounts = PetraPolicies.Permission(PermissionCatalog.AccountsManage);
        accounts.MapAccountHistory();
        accounts.MapAccountCardContext();
        accounts.MapAccountProfile();

        accounts.MapGet("/tree", AccountTreeAsync).WithName("GetAccountTree").WithSummary("Returns the company account hierarchy depth-first by code.")
            .RequireAuthorization(viewAccounts).Produces<IReadOnlyList<AccountResponse>>().ProducesStandardProblems();
        accounts.MapGet("", SearchAccountsAsync).WithName("SearchAccounts").WithSummary("Searches accounts with paging.")
            .RequireAuthorization(viewAccounts).Produces<AccountPageResponse>().ProducesStandardProblems();
        accounts.MapGet("/{accountId:guid}", GetAccountAsync).WithName("GetAccount")
            .RequireAuthorization(viewAccounts).Produces<AccountResponse>().ProducesStandardProblems();
        accounts.MapPost("", CreateAccountAsync).WithName("CreateAccount").RequireAuthorization(manageAccounts)
            .Validate<AccountRequest>().Produces<AccountResponse>(StatusCodes.Status201Created).ProducesStandardProblems();
        accounts.MapPut("/{accountId:guid}", UpdateAccountAsync).WithName("UpdateAccount").RequireAuthorization(manageAccounts)
            .Validate<AccountRequest>().Produces<AccountResponse>().ProducesStandardProblems();
        accounts.MapPost("/{accountId:guid}/activation", SetAccountActiveAsync).WithName("SetAccountActive").RequireAuthorization(manageAccounts)
            .Validate<SetActiveRequest>().Produces<AccountResponse>().ProducesStandardProblems();
        accounts.MapPost("/{accountId:guid}/deletion", DeleteAccountAsync).WithName("DeleteAccount")
            .WithSummary("Deletes an unused leaf account (no children, no journal lines in any state) with a reason; used accounts must be deactivated.")
            .RequireAuthorization(manageAccounts).Validate<DeleteAccountRequest>().Produces(StatusCodes.Status204NoContent).ProducesStandardProblems();
        accounts.MapGet("/navigation", NavigateAccountAsync).WithName("NavigateAccount")
            .WithSummary("Returns the first, last, previous or next account in code order (one row; null past either end).")
            .RequireAuthorization(viewAccounts).Produces<AccountNavigationResponse>().ProducesStandardProblems();
        accounts.MapGet("/deletions", DeletedAccountsAuditAsync).WithName("GetDeletedAccountsAudit")
            .WithSummary("Returns the append-only audit of deleted accounts, newest first.")
            .RequireAuthorization(viewAccounts).Produces<IReadOnlyList<AccountAuditResponse>>().ProducesStandardProblems();

        var centers = ledger.MapGroup("/chart/cost-centers").WithTags("General ledger: cost centers");
        centers.MapCostCenterHistory();
        var viewCenters = PetraPolicies.Permission(PermissionCatalog.CostCentersView);
        var manageCenters = PetraPolicies.Permission(PermissionCatalog.CostCentersManage);
        centers.MapGet("", SearchCostCentersAsync).WithName("SearchCostCenters")
            .RequireAuthorization(viewCenters).Produces<CostCenterPageResponse>().ProducesStandardProblems();
        centers.MapGet("/tree", GetCostCenterTreeAsync).WithName("GetCostCenterTree")
            .RequireAuthorization(viewCenters).Produces<IReadOnlyList<CostCenterResponse>>().ProducesStandardProblems();
        centers.MapGet("/{costCenterId:guid}", GetCostCenterAsync).WithName("GetCostCenter")
            .RequireAuthorization(viewCenters).Produces<CostCenterResponse>().ProducesStandardProblems();
        centers.MapGet("/{costCenterId:guid}/allocations", GetCostCenterAllocationsAsync).WithName("GetCostCenterAllocations")
            .RequireAuthorization(viewCenters).Produces<IReadOnlyList<CostCenterAllocationResponse>>().ProducesStandardProblems();
        centers.MapPut("/{costCenterId:guid}/allocations", SetCostCenterAllocationsAsync).WithName("SetCostCenterAllocations")
            .RequireAuthorization(manageCenters).Validate<SetCostCenterAllocationsRequest>().Produces<IReadOnlyList<CostCenterAllocationResponse>>().ProducesStandardProblems();
        centers.MapPost("", CreateCostCenterAsync).WithName("CreateCostCenter").RequireAuthorization(manageCenters)
            .Validate<CostCenterRequest>().Produces<CostCenterResponse>(StatusCodes.Status201Created).ProducesStandardProblems();
        centers.MapPut("/{costCenterId:guid}", UpdateCostCenterAsync).WithName("UpdateCostCenter").RequireAuthorization(manageCenters)
            .Validate<CostCenterRequest>().Produces<CostCenterResponse>().ProducesStandardProblems();
        centers.MapPost("/{costCenterId:guid}/activation", SetCostCenterActiveAsync).WithName("SetCostCenterActive").RequireAuthorization(manageCenters)
            .Validate<SetActiveRequest>().Produces<CostCenterResponse>().ProducesStandardProblems();

        var calendar = ledger.MapGroup("/calendar").WithTags("General ledger: fiscal calendar");
        var viewPeriods = PetraPolicies.Permission(PermissionCatalog.PeriodsView);
        var managePeriods = PetraPolicies.Permission(PermissionCatalog.PeriodsManage);
        calendar.MapGet("/fiscal-years", ListFiscalYearsAsync).WithName("ListFiscalYears")
            .RequireAuthorization(viewPeriods).Produces<IReadOnlyList<FiscalYearResponse>>().ProducesStandardProblems();
        calendar.MapGet("/fiscal-years/{fiscalYearId:guid}", GetFiscalYearAsync).WithName("GetFiscalYear")
            .RequireAuthorization(viewPeriods).Produces<FiscalYearResponse>().ProducesStandardProblems();
        calendar.MapPost("/fiscal-years", CreateFiscalYearAsync).WithName("CreateFiscalYear").RequireAuthorization(managePeriods)
            .Validate<CreateFiscalYearRequest>().Produces<FiscalYearResponse>(StatusCodes.Status201Created).ProducesStandardProblems();
        calendar.MapPost("/periods/{periodId:guid}/soft-close", (Guid companyId, Guid branchId, Guid periodId, PeriodTransitionRequest request, IAccountingCalendarService service, HttpContext http, CancellationToken token) =>
                TransitionAsync(companyId, branchId, periodId, PeriodTransition.SoftClose, request, service, http, token))
            .WithName("SoftClosePeriod").RequireAuthorization(managePeriods)
            .Validate<PeriodTransitionRequest>().Produces<AccountingPeriodResponse>().ProducesStandardProblems();
        calendar.MapPost("/periods/{periodId:guid}/close", (Guid companyId, Guid branchId, Guid periodId, PeriodTransitionRequest request, IAccountingCalendarService service, HttpContext http, CancellationToken token) =>
                TransitionAsync(companyId, branchId, periodId, PeriodTransition.Close, request, service, http, token))
            .WithName("ClosePeriod").RequireAuthorization(managePeriods)
            .Validate<PeriodTransitionRequest>().Produces<AccountingPeriodResponse>().ProducesStandardProblems();
        calendar.MapPost("/periods/{periodId:guid}/reopen", (Guid companyId, Guid branchId, Guid periodId, PeriodTransitionRequest request, IAccountingCalendarService service, HttpContext http, CancellationToken token) =>
                TransitionAsync(companyId, branchId, periodId, PeriodTransition.Reopen, request, service, http, token))
            .WithName("ReopenPeriod").WithSummary("Reopens a soft-closed or closed period. Requires the elevated Ledger.Periods.Reopen permission and a reason.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.PeriodsReopen))
            .Validate<PeriodTransitionRequest>().Produces<AccountingPeriodResponse>().ProducesStandardProblems();
        calendar.MapGet("/periods/{periodId:guid}/audit", PeriodAuditAsync).WithName("GetPeriodAudit")
            .RequireAuthorization(viewPeriods).Produces<IReadOnlyList<PeriodAuditResponse>>().ProducesStandardProblems();

        ledger.MapGet("/currency-context", CurrencyContextAsync).WithTags("General ledger: fiscal calendar").WithName("GetCompanyCurrencyContext")
            .WithSummary("Base and additional currencies enabled for the company. Exchange-rate history is not yet implemented.")
            .RequireAuthorization(PetraPolicies.CompanyScope).Produces<CompanyCurrencyContextResponse>().ProducesStandardProblems();
    }

    private static LedgerMasterScope Scope(Guid companyId, HttpContext http) => LedgerMasterScope.Create(companyId, http.RequireSession().UserId);

    private static ConcurrencyToken Version(string value, string field) =>
        ConcurrencyToken.TryParse(value, out var token)
            ? token
            : throw new RequestValidationException(ApiErrorCodes.VersionInvalid, "The version must be the canonical 12-character Base64 value read from the server.", field);

    private static string Location(Guid companyId, Guid branchId, string path) => $"/api/v1/companies/{companyId:D}/branches/{branchId:D}/ledger/{path}";

    private static async Task<IResult> AccountTreeAsync(Guid companyId, Guid branchId, IChartOfAccountsService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok((IReadOnlyList<AccountResponse>)(await service.GetAccountTreeAsync(Scope(companyId, http), token).ConfigureAwait(false))
            .Select(static a => a.ToResponse()).ToArray());

    private static async Task<IResult> SearchAccountsAsync(Guid companyId, Guid branchId, string? search, AccountType? accountType, bool? isActive, bool? postingAllowed,
        Guid? parentId, int? page, int? pageSize, IChartOfAccountsService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok((await service.SearchAccountsAsync(Scope(companyId, http), new AccountQuery(search, (AccountTypeCode?)accountType, isActive,
            postingAllowed, parentId, PageRequest.Create(page, pageSize)), token).ConfigureAwait(false)).ToResponse());

    private static async Task<IResult> GetAccountAsync(Guid companyId, Guid branchId, Guid accountId, IChartOfAccountsService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok((await service.GetAccountAsync(Scope(companyId, http), accountId, token).ConfigureAwait(false)).ToResponse());

    private static async Task<IResult> CreateAccountAsync(Guid companyId, Guid branchId, AccountRequest request, IChartOfAccountsService service,
        HttpContext http, CancellationToken token)
    {
        var created = await service.CreateAccountAsync(AccountHistoryEndpoints.Scope(companyId, branchId, http), request.ToInput(), token).ConfigureAwait(false);
        return TypedResults.Created(Location(companyId, branchId, $"chart/accounts/{created.Id:D}"), created.ToResponse());
    }

    private static async Task<IResult> UpdateAccountAsync(Guid companyId, Guid branchId, Guid accountId, [FromQuery] string expectedVersion, AccountRequest request,
        IChartOfAccountsService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok((await service.UpdateAccountAsync(AccountHistoryEndpoints.Scope(companyId, branchId, http), accountId, request.ToInput(), Version(expectedVersion, "expectedVersion"), token)
            .ConfigureAwait(false)).ToResponse());

    private static async Task<IResult> DeleteAccountAsync(Guid companyId, Guid branchId, Guid accountId, DeleteAccountRequest request,
        IChartOfAccountsService service, HttpContext http, CancellationToken token)
    {
        await service.DeleteAccountAsync(AccountHistoryEndpoints.Scope(companyId, branchId, http), accountId, Version(request.ExpectedVersion, "expectedVersion"), request.Reason, token)
            .ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> NavigateAccountAsync(Guid companyId, Guid branchId, AccountNavigationDirection direction, Guid? fromAccountId,
        IChartOfAccountsService service, HttpContext http, CancellationToken token)
    {
        if (!Enum.IsDefined(direction)) throw new RequestValidationException("request.invalid", "Unknown navigation direction.", "direction");
        var account = await service.NavigateAccountAsync(Scope(companyId, http), (AccountNavigationCode)direction, fromAccountId, token).ConfigureAwait(false);
        return TypedResults.Ok(new AccountNavigationResponse { Account = account?.ToResponse() });
    }

    private static async Task<IResult> DeletedAccountsAuditAsync(Guid companyId, Guid branchId, IChartOfAccountsService service, HttpContext http,
        CancellationToken token) =>
        TypedResults.Ok((IReadOnlyList<AccountAuditResponse>)(await service.GetDeletedAccountsAuditAsync(Scope(companyId, http), token).ConfigureAwait(false))
            .Select(static a => a.ToResponse()).ToArray());

    private static async Task<IResult> SetAccountActiveAsync(Guid companyId, Guid branchId, Guid accountId, SetActiveRequest request, IChartOfAccountsService service,
        HttpContext http, CancellationToken token) =>
        TypedResults.Ok((await service.SetAccountActiveAsync(AccountHistoryEndpoints.Scope(companyId, branchId, http), accountId, request.IsActive, Version(request.ExpectedVersion, "expectedVersion"), token)
            .ConfigureAwait(false)).ToResponse());

    private static async Task<IResult> SearchCostCentersAsync(Guid companyId, Guid branchId, string? search, bool? isActive, int? page, int? pageSize,
        IChartOfAccountsService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok((await service.SearchCostCentersAsync(Scope(companyId, http), new CostCenterQuery(search, isActive, PageRequest.Create(page, pageSize)), token)
            .ConfigureAwait(false)).ToResponse());

    private static async Task<IResult> GetCostCenterAsync(Guid companyId, Guid branchId, Guid costCenterId, IChartOfAccountsService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok((await service.GetCostCenterAsync(Scope(companyId, http), costCenterId, token).ConfigureAwait(false)).ToResponse());

    private static async Task<IResult> CreateCostCenterAsync(Guid companyId, Guid branchId, CostCenterRequest request, IChartOfAccountsService service,
        HttpContext http, CancellationToken token)
    {
        var created = await service.CreateCostCenterAsync(AccountHistoryEndpoints.Scope(companyId, branchId, http), request.ToInput(), token).ConfigureAwait(false);
        return TypedResults.Created(Location(companyId, branchId, $"chart/cost-centers/{created.Id:D}"), created.ToResponse());
    }

    private static async Task<IResult> UpdateCostCenterAsync(Guid companyId, Guid branchId, Guid costCenterId, [FromQuery] string expectedVersion, CostCenterRequest request,
        IChartOfAccountsService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok((await service.UpdateCostCenterAsync(AccountHistoryEndpoints.Scope(companyId, branchId, http), costCenterId, request.ToInput(), Version(expectedVersion, "expectedVersion"), token)
            .ConfigureAwait(false)).ToResponse());

    private static async Task<IResult> SetCostCenterActiveAsync(Guid companyId, Guid branchId, Guid costCenterId, SetActiveRequest request, IChartOfAccountsService service,
        HttpContext http, CancellationToken token) =>
        TypedResults.Ok((await service.SetCostCenterActiveAsync(AccountHistoryEndpoints.Scope(companyId, branchId, http), costCenterId, request.IsActive, Version(request.ExpectedVersion, "expectedVersion"), token)
            .ConfigureAwait(false)).ToResponse());

    private static async Task<IResult> GetCostCenterTreeAsync(Guid companyId, Guid branchId, IChartOfAccountsService service,
        HttpContext http, CancellationToken token) =>
        TypedResults.Ok((IReadOnlyList<CostCenterResponse>)(await service.GetCostCenterTreeAsync(Scope(companyId, http), token).ConfigureAwait(false))
            .Select(c => c.ToResponse()).ToArray());

    private static async Task<IResult> GetCostCenterAllocationsAsync(Guid companyId, Guid branchId, Guid costCenterId, IChartOfAccountsService service,
        HttpContext http, CancellationToken token) =>
        TypedResults.Ok((IReadOnlyList<CostCenterAllocationResponse>)(await service.GetCostCenterAllocationsAsync(Scope(companyId, http), costCenterId, token).ConfigureAwait(false))
            .Select(a => a.ToResponse()).ToArray());

    private static async Task<IResult> SetCostCenterAllocationsAsync(Guid companyId, Guid branchId, Guid costCenterId, SetCostCenterAllocationsRequest request,
        IChartOfAccountsService service, HttpContext http, CancellationToken token)
    {
        var inputs = request.Allocations.Select(a => new SetCostCenterAllocationInput
        {
            TargetCostCenterId = a.TargetCostCenterId,
            Percentage = decimal.TryParse(a.Percentage, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0m
        }).ToList();
        await service.SetCostCenterAllocationsAsync(Scope(companyId, http), costCenterId, inputs, token).ConfigureAwait(false);
        var result = await service.GetCostCenterAllocationsAsync(Scope(companyId, http), costCenterId, token).ConfigureAwait(false);
        return TypedResults.Ok((IReadOnlyList<CostCenterAllocationResponse>)result.Select(a => a.ToResponse()).ToArray());
    }

    private static async Task<IResult> ListFiscalYearsAsync(Guid companyId, Guid branchId, IAccountingCalendarService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok((IReadOnlyList<FiscalYearResponse>)(await service.ListFiscalYearsAsync(Scope(companyId, http), token).ConfigureAwait(false))
            .Select(static y => y.ToResponse()).ToArray());

    private static async Task<IResult> GetFiscalYearAsync(Guid companyId, Guid branchId, Guid fiscalYearId, IAccountingCalendarService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok((await service.GetFiscalYearAsync(Scope(companyId, http), fiscalYearId, token).ConfigureAwait(false)).ToResponse());

    private static async Task<IResult> CreateFiscalYearAsync(Guid companyId, Guid branchId, CreateFiscalYearRequest request, IAccountingCalendarService service,
        HttpContext http, CancellationToken token)
    {
        var input = new CreateFiscalYearInput(request.Code, request.Start, request.End,
            request.Periods?.Select(static p => new PeriodDefinitionInput(p.Code, p.Start, p.End)).ToArray());
        var created = await service.CreateFiscalYearAsync(Scope(companyId, http), input, token).ConfigureAwait(false);
        return TypedResults.Created(Location(companyId, branchId, $"calendar/fiscal-years/{created.Id:D}"), created.ToResponse());
    }

    private static async Task<IResult> TransitionAsync(Guid companyId, Guid branchId, Guid periodId, PeriodTransition transition, PeriodTransitionRequest request,
        IAccountingCalendarService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok((await service.TransitionPeriodAsync(Scope(companyId, http), periodId, transition, request.Reason,
            Version(request.ExpectedVersion, "expectedVersion"), token).ConfigureAwait(false)).ToResponse());

    private static async Task<IResult> PeriodAuditAsync(Guid companyId, Guid branchId, Guid periodId, IAccountingCalendarService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok((IReadOnlyList<PeriodAuditResponse>)(await service.GetPeriodAuditAsync(Scope(companyId, http), periodId, token).ConfigureAwait(false))
            .Select(static a => a.ToResponse()).ToArray());

    private static async Task<IResult> CurrencyContextAsync(Guid companyId, Guid branchId, ILedgerQueryService service, CancellationToken token) =>
        TypedResults.Ok((await service.GetCurrencyContextAsync(companyId, token).ConfigureAwait(false)).ToResponse());
}
