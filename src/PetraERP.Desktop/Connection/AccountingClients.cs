using System.IO;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;

namespace PetraERP.Desktop.Connection;

public sealed class ChartOfAccountsClient(DesktopSession session) : IChartOfAccountsClient
{
    public Task<ChartFilePreview> PreviewTemplateFileAsync(Guid companyId,Guid branchId,string fileName,Stream content,string metadata,CancellationToken token) => session.Api.PreviewChartFileAsync(companyId,branchId,fileName,content,metadata,token);
    public Task<ChartFileImportResult> ImportTemplateAsync(Guid companyId,Guid branchId,System.Text.Json.JsonElement chartTemplate,CancellationToken token) => session.Api.CommitChartFileAsync(companyId,branchId,chartTemplate,token);
    public Task<CostCenterHistoryPageResponse> GetCostCenterHistoryAsync(Guid companyId, Guid branchId, Guid costCenterId, int page, int pageSize, CancellationToken cancellationToken) =>
        session.Api.GetCostCenterHistoryAsync(companyId, branchId, costCenterId, page, pageSize, cancellationToken);
    public Task<AccountProfileDto> GetAccountProfileAsync(Guid companyId, Guid branchId, Guid accountId, CancellationToken cancellationToken) =>
        session.Api.GetAccountProfileAsync(companyId, branchId, accountId, cancellationToken);
    public Task SetAccountProfileAsync(Guid companyId, Guid branchId, Guid accountId, AccountProfileDto profile, CancellationToken cancellationToken) =>
        session.Api.SetAccountProfileAsync(companyId, branchId, accountId, profile, cancellationToken);
    public Task<AccountCardContextResponse> GetAccountCardContextAsync(Guid companyId, Guid branchId, Guid accountId, int page, int pageSize, CancellationToken cancellationToken) =>
        session.Api.GetAccountCardContextAsync(companyId, branchId, accountId, page, pageSize, cancellationToken);
    public Task<AccountHistoryPageResponse> GetAccountHistoryAsync(Guid companyId, Guid branchId, Guid accountId, int page, int pageSize, CancellationToken cancellationToken) =>
        session.Api.GetAccountHistoryAsync(companyId, branchId, accountId, page, pageSize, cancellationToken);
    public Task<IReadOnlyList<AccountResponse>> GetAccountTreeAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.CachedAsync(branchId, "AccountsTree", $"{companyId:N}",
            ct => session.Api.GetAccountTreeAsync(companyId, branchId, ct),
            TimeSpan.FromMinutes(15), cancellationToken);

    public Task<AccountResponse> GetAccountAsync(Guid companyId, Guid branchId, Guid accountId, CancellationToken cancellationToken) =>
        session.Api.GetAccountAsync(companyId, branchId, accountId, cancellationToken);

    public async Task<AccountResponse> CreateAccountAsync(Guid companyId, Guid branchId, AccountRequest request, CancellationToken cancellationToken)
    {
        var res = await session.Api.CreateAccountAsync(companyId, branchId, request, cancellationToken).ConfigureAwait(false);
        session.InvalidateCached("AccountsTree");
        return res;
    }

    public async Task<AccountResponse> UpdateAccountAsync(Guid companyId, Guid branchId, Guid accountId, string expectedVersion, AccountRequest request, CancellationToken cancellationToken)
    {
        var res = await session.Api.UpdateAccountAsync(companyId, branchId, accountId, expectedVersion, request, cancellationToken).ConfigureAwait(false);
        session.InvalidateCached("AccountsTree");
        return res;
    }

    public async Task<AccountResponse> SetAccountActiveAsync(Guid companyId, Guid branchId, Guid accountId, SetActiveRequest request, CancellationToken cancellationToken)
    {
        var res = await session.Api.SetAccountActiveAsync(companyId, branchId, accountId, request, cancellationToken).ConfigureAwait(false);
        session.InvalidateCached("AccountsTree");
        return res;
    }

    public Task<AccountNavigationResponse> NavigateAccountAsync(Guid companyId, Guid branchId, AccountNavigationDirection direction, Guid? fromAccountId,
        CancellationToken cancellationToken) =>
        session.Api.NavigateAccountAsync(companyId, branchId, direction, fromAccountId, cancellationToken);

    public async Task DeleteAccountAsync(Guid companyId, Guid branchId, Guid accountId, DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        await session.Api.DeleteAccountAsync(companyId, branchId, accountId, request, cancellationToken).ConfigureAwait(false);
        session.InvalidateCached("AccountsTree");
    }

    public Task<CostCenterPageResponse> SearchCostCentersAsync(Guid companyId, Guid branchId, string? search, bool? isActive, int page, int pageSize, CancellationToken cancellationToken) =>
        session.Api.SearchCostCentersAsync(companyId, branchId, search, isActive, page, pageSize, cancellationToken);

    public Task<CostCenterResponse> CreateCostCenterAsync(Guid companyId, Guid branchId, CostCenterRequest request, CancellationToken cancellationToken) =>
        session.Api.CreateCostCenterAsync(companyId, branchId, request, cancellationToken);

    public Task<CostCenterResponse> UpdateCostCenterAsync(Guid companyId, Guid branchId, Guid costCenterId, string expectedVersion, CostCenterRequest request, CancellationToken cancellationToken) =>
        session.Api.UpdateCostCenterAsync(companyId, branchId, costCenterId, expectedVersion, request, cancellationToken);

    public Task<CostCenterResponse> SetCostCenterActiveAsync(Guid companyId, Guid branchId, Guid costCenterId, SetActiveRequest request, CancellationToken cancellationToken) =>
        session.Api.SetCostCenterActiveAsync(companyId, branchId, costCenterId, request, cancellationToken);

    public Task<CostCenterResponse> GetCostCenterAsync(Guid companyId, Guid branchId, Guid costCenterId, CancellationToken cancellationToken) =>
        session.Api.GetCostCenterAsync(companyId, branchId, costCenterId, cancellationToken);

    public Task<IReadOnlyList<CostCenterResponse>> GetCostCenterTreeAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.Api.GetCostCenterTreeAsync(companyId, branchId, cancellationToken);

    public Task<IReadOnlyList<CostCenterAllocationResponse>> GetCostCenterAllocationsAsync(Guid companyId, Guid branchId, Guid costCenterId, CancellationToken cancellationToken) =>
        session.Api.GetCostCenterAllocationsAsync(companyId, branchId, costCenterId, cancellationToken);

    public Task<IReadOnlyList<CostCenterAllocationResponse>> SetCostCenterAllocationsAsync(Guid companyId, Guid branchId, Guid costCenterId, SetCostCenterAllocationsRequest request, CancellationToken cancellationToken) =>
        session.Api.SetCostCenterAllocationsAsync(companyId, branchId, costCenterId, request, cancellationToken);
}

public sealed class FiscalCalendarClient(DesktopSession session) : IFiscalCalendarClient
{
    public Task<IReadOnlyList<FiscalYearResponse>> ListFiscalYearsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.Api.ListFiscalYearsAsync(companyId, branchId, cancellationToken);

    public Task<FiscalYearResponse> CreateFiscalYearAsync(Guid companyId, Guid branchId, CreateFiscalYearRequest request, CancellationToken cancellationToken) =>
        session.Api.CreateFiscalYearAsync(companyId, branchId, request, cancellationToken);

    public Task<AccountingPeriodResponse> TransitionAsync(Guid companyId, Guid branchId, Guid periodId, PeriodAction action, PeriodTransitionRequest request, CancellationToken cancellationToken) => action switch
    {
        PeriodAction.SoftClose => session.Api.SoftClosePeriodAsync(companyId, branchId, periodId, request, cancellationToken),
        PeriodAction.Close => session.Api.ClosePeriodAsync(companyId, branchId, periodId, request, cancellationToken),
        PeriodAction.Reopen => session.Api.ReopenPeriodAsync(companyId, branchId, periodId, request, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public Task<IReadOnlyList<PeriodAuditResponse>> GetPeriodAuditAsync(Guid companyId, Guid branchId, Guid periodId, CancellationToken cancellationToken) =>
        session.Api.GetPeriodAuditAsync(companyId, branchId, periodId, cancellationToken);
}

public sealed class LedgerReportsClient(DesktopSession session) : ILedgerReportsClient
{
    public Task<GeneralJournalResponse> GetGeneralJournalAsync(Guid companyId, Guid branchId, GeneralJournalFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return session.Api.GetGeneralJournalAsync(companyId, branchId, filter.From, filter.To, filter.AccountId, filter.CostCenterId, filter.State,
            filter.Search, filter.Consolidated, page, pageSize, cancellationToken);
    }

    public Task<AccountLedgerResponse> GetAccountLedgerAsync(Guid companyId, Guid branchId, Guid accountId, DateOnly? from, DateOnly? until, bool consolidated, int page, int pageSize, string? cursor, CancellationToken cancellationToken) =>
        session.Api.GetAccountLedgerAsync(companyId, branchId, accountId, from, until, consolidated, page, pageSize, cursor, cancellationToken);

    public Task<TrialBalanceResponse> GetTrialBalanceAsync(Guid companyId, Guid branchId, TrialBalanceFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return session.Api.GetTrialBalanceAsync(companyId, branchId, filter.From, filter.To, filter.PeriodId, filter.AccountType, filter.IncludeZeroRows,
            filter.MaximumLevel, filter.Consolidated, filter.Parameters?.AccountId, filter.Parameters?.CostCenterId, filter.Parameters?.AccountScope,
            filter.Parameters?.FromJournalNumber, filter.Parameters?.ToJournalNumber, filter.Parameters?.Sources, filter.Parameters?.DisplayCurrency,
            filter.Parameters?.DisplayRate, filter.Parameters?.ShowLastMovement, cancellationToken);
    }
}
