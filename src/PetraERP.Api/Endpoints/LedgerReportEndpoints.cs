using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Security;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Endpoints;

/// <summary>
/// Server-authoritative General Journal, Account Ledger and Trial Balance. Values derive from posted journal lines
/// only; clients render them and never recompute report truth. <c>consolidated=true</c> includes every branch and is
/// refused unless the caller holds a scope grant for each branch of the company.
/// </summary>
internal static class LedgerReportEndpoints
{
    internal static void MapLedgerReportEndpoints(this RouteGroupBuilder api)
    {
        var reports = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/ledger/reports")
            .WithTags("General ledger: reports")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.ReportsView));

        reports.MapGet("/general-journal", GeneralJournalAsync).WithName("GetGeneralJournal")
            .WithSummary("Paged journal lines; posted journals by default, optional state filter.")
            .Produces<GeneralJournalResponse>().ProducesStandardProblems();
        reports.MapGet("/account-ledger/{accountId:guid}", AccountLedgerAsync).WithName("GetAccountLedger")
            .WithSummary("Opening balance, paged posted lines with running balance, and closing balance for an account (control accounts include descendants).")
            .Produces<AccountLedgerResponse>().ProducesStandardProblems();
        reports.MapGet("/trial-balance", TrialBalanceAsync).WithName("GetTrialBalance")
            .WithSummary("Opening, period and ending balances per account with hierarchy roll-up and reconciled totals.")
            .Produces<TrialBalanceResponse>().ProducesStandardProblems();
    }

    private static LedgerReportScope Scope(Guid companyId, Guid branchId, bool? consolidated, HttpContext http) =>
        LedgerReportScope.Create(companyId, branchId, http.RequireSession().UserId, consolidated ?? false);

    private static async Task<IResult> GeneralJournalAsync(Guid companyId, Guid branchId, DateOnly? from, DateOnly? to, Guid? accountId, Guid? costCenterId,
        JournalState? state, string? search, bool? consolidated, int? page, int? pageSize, ILedgerQueryService service, HttpContext http,
        CancellationToken cancellationToken)
    {
        var states = state is { } filter ? new[] { LedgerJournalMapping.ToPersistedState(filter) } : null;
        var result = await service.GetGeneralJournalAsync(Scope(companyId, branchId, consolidated, http),
            new GeneralJournalQuery(from, to, accountId, costCenterId, states, search, PageRequest.Create(page, pageSize)), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }

    private static async Task<IResult> AccountLedgerAsync(Guid companyId, Guid branchId, Guid accountId, DateOnly? from, DateOnly? to, bool? consolidated,
        int? page, int? pageSize, string? cursor, string? sources, ILedgerQueryService service, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await service.GetAccountLedgerAsync(Scope(companyId, branchId, consolidated, http),
            new AccountLedgerQuery(accountId, from, to, PageRequest.Create(page, pageSize), cursor) { Sources = ParseSources(sources) }, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }

    /// <summary>Comma-separated document sources (case-insensitive); unknown values are a client error, never silently ignored.</summary>
    private static List<string>? ParseSources(string? sources)
    {
        var list = string.IsNullOrWhiteSpace(sources) ? null
            : sources.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => s.ToUpperInvariant()).Distinct().ToList();
        if (list is not null && list.Exists(s => !TrialBalanceSources.All.Contains(s)))
        {
            throw new RequestValidationException(LedgerMasterErrorCodes.ReportRangeInvalid, "Unknown ledger source.", "sources");
        }

        return list;
    }

    private static async Task<IResult> TrialBalanceAsync(Guid companyId, Guid branchId, DateOnly? from, DateOnly? to, Guid? periodId, AccountType? accountType,
        bool? includeZeroRows, int? maximumLevel, bool? consolidated, Guid? accountId, Guid? costCenterId, int? accountScope, long? fromJournalNumber,
        long? toJournalNumber, string? sources, string? displayCurrency, string? displayRate, bool? showLastMovement,
        ILedgerQueryService service, HttpContext http, CancellationToken cancellationToken)
    {
        if (accountScope is < 0 or > 2)
        {
            throw new RequestValidationException(LedgerMasterErrorCodes.ReportRangeInvalid, "accountScope must be 0, 1 or 2.", "accountScope");
        }

        if (fromJournalNumber is not null && toJournalNumber is not null && fromJournalNumber > toJournalNumber)
        {
            throw new RequestValidationException(LedgerMasterErrorCodes.ReportRangeInvalid, "fromJournalNumber must not exceed toJournalNumber.", "fromJournalNumber");
        }

        var sourceList = ParseSources(sources);

        if (to is null && periodId is null)
        {
            throw new RequestValidationException(LedgerMasterErrorCodes.ReportRangeInvalid, "Either 'to' or 'periodId' is required.", "to");
        }

        if (maximumLevel is < 0 or > 32)
        {
            throw new RequestValidationException(LedgerMasterErrorCodes.ReportRangeInvalid, "maximumLevel must be between 0 and 32.", "maximumLevel");
        }

        var result = await service.GetTrialBalanceAsync(Scope(companyId, branchId, consolidated, http),
            new TrialBalanceQuery(from, to ?? DateOnly.MaxValue, periodId, (AccountTypeCode?)accountType, includeZeroRows ?? false, maximumLevel)
            {
                Options = new TrialBalanceOptions
                {
                    AccountId = accountId,
                    CostCenterId = costCenterId,
                    AccountScope = (TrialBalanceAccountScope)(accountScope ?? 0),
                    FromJournalNumber = fromJournalNumber,
                    ToJournalNumber = toJournalNumber,
                    Sources = sourceList,
                    DisplayCurrencyCode = string.IsNullOrWhiteSpace(displayCurrency) ? null : displayCurrency.Trim().ToUpperInvariant(),
                    DisplayRate = ParseDisplayRate(displayRate),
                    ShowLastMovement = showLastMovement ?? false,
                },
            },
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }

    internal static decimal? ParseDisplayRate(string? text)
    {
        if (text is null) return null;
        if (!FinancialDecimalText.TryParse(text, FinancialDecimalText.RateScale, FinancialDecimalText.RateIntegerDigits, out var rate) || rate.Value <= 0m)
            throw new RequestValidationException("ledger.report_rate_invalid", "Display rate must be a positive decimal string with at most eight fractional digits.", "displayRate");
        return rate.Value;
    }
}
