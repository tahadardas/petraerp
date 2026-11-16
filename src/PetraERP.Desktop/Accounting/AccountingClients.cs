using System.IO;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Accounting;

/// <summary>Chart of accounts and cost centers (company master data) through the generated client only.</summary>
public interface IChartOfAccountsClient
{
    Task<ChartFilePreview> PreviewTemplateFileAsync(Guid companyId,Guid branchId,string fileName,Stream content,string metadata,CancellationToken token) => throw new NotSupportedException();
    Task<ChartFileImportResult> ImportTemplateAsync(Guid companyId,Guid branchId,System.Text.Json.JsonElement chartTemplate,CancellationToken token) => throw new NotSupportedException();
    Task<CostCenterHistoryPageResponse> GetCostCenterHistoryAsync(Guid companyId, Guid branchId, Guid costCenterId, int page, int pageSize, CancellationToken cancellationToken);
    Task<AccountCardContextResponse> GetAccountCardContextAsync(Guid companyId, Guid branchId, Guid accountId, int page, int pageSize, CancellationToken cancellationToken);
    Task<AccountHistoryPageResponse> GetAccountHistoryAsync(Guid companyId, Guid branchId, Guid accountId, int page, int pageSize, CancellationToken cancellationToken);
    Task<AccountProfileDto> GetAccountProfileAsync(Guid companyId, Guid branchId, Guid accountId, CancellationToken cancellationToken) => throw new NotSupportedException();
    Task SetAccountProfileAsync(Guid companyId, Guid branchId, Guid accountId, AccountProfileDto profile, CancellationToken cancellationToken) => throw new NotSupportedException();
    Task<IReadOnlyList<AccountResponse>> GetAccountTreeAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<AccountResponse> GetAccountAsync(Guid companyId, Guid branchId, Guid accountId, CancellationToken cancellationToken);
    Task<AccountResponse> CreateAccountAsync(Guid companyId, Guid branchId, AccountRequest request, CancellationToken cancellationToken);
    Task<AccountResponse> UpdateAccountAsync(Guid companyId, Guid branchId, Guid accountId, string expectedVersion, AccountRequest request, CancellationToken cancellationToken);
    Task<AccountResponse> SetAccountActiveAsync(Guid companyId, Guid branchId, Guid accountId, SetActiveRequest request, CancellationToken cancellationToken);
    /// <summary>First/previous/next/last account in code order (one row per call); the response's account is null past either end.</summary>
    Task<AccountNavigationResponse> NavigateAccountAsync(Guid companyId, Guid branchId, AccountNavigationDirection direction, Guid? fromAccountId, CancellationToken cancellationToken);
    /// <summary>Deletes an unused leaf account; the server rejects accounts with children or journal lines.</summary>
    Task DeleteAccountAsync(Guid companyId, Guid branchId, Guid accountId, DeleteAccountRequest request, CancellationToken cancellationToken);
    Task<CostCenterPageResponse> SearchCostCentersAsync(Guid companyId, Guid branchId, string? search, bool? isActive, int page, int pageSize, CancellationToken cancellationToken);
    Task<CostCenterResponse> GetCostCenterAsync(Guid companyId, Guid branchId, Guid costCenterId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CostCenterResponse>> GetCostCenterTreeAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<CostCenterResponse> CreateCostCenterAsync(Guid companyId, Guid branchId, CostCenterRequest request, CancellationToken cancellationToken);
    Task<CostCenterResponse> UpdateCostCenterAsync(Guid companyId, Guid branchId, Guid costCenterId, string expectedVersion, CostCenterRequest request, CancellationToken cancellationToken);
    Task<CostCenterResponse> SetCostCenterActiveAsync(Guid companyId, Guid branchId, Guid costCenterId, SetActiveRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<CostCenterAllocationResponse>> GetCostCenterAllocationsAsync(Guid companyId, Guid branchId, Guid costCenterId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CostCenterAllocationResponse>> SetCostCenterAllocationsAsync(Guid companyId, Guid branchId, Guid costCenterId, SetCostCenterAllocationsRequest request, CancellationToken cancellationToken);
}

/// <summary>Fiscal years, accounting periods and their audited state transitions.</summary>
public interface IFiscalCalendarClient
{
    Task<IReadOnlyList<FiscalYearResponse>> ListFiscalYearsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<FiscalYearResponse> CreateFiscalYearAsync(Guid companyId, Guid branchId, CreateFiscalYearRequest request, CancellationToken cancellationToken);
    Task<AccountingPeriodResponse> TransitionAsync(Guid companyId, Guid branchId, Guid periodId, PeriodAction action, PeriodTransitionRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PeriodAuditResponse>> GetPeriodAuditAsync(Guid companyId, Guid branchId, Guid periodId, CancellationToken cancellationToken);
}

public enum PeriodAction
{
    SoftClose,
    Close,
    Reopen,
}

/// <summary>Server-authoritative ledger read models. The Desktop renders them and never recomputes report truth.</summary>
public interface ILedgerReportsClient
{
    Task<GeneralJournalResponse> GetGeneralJournalAsync(Guid companyId, Guid branchId, GeneralJournalFilter filter, int page, int pageSize, CancellationToken cancellationToken);
    Task<AccountLedgerResponse> GetAccountLedgerAsync(Guid companyId, Guid branchId, Guid accountId, DateOnly? from, DateOnly? until, bool consolidated, int page, int pageSize, string? cursor, CancellationToken cancellationToken);
    Task<TrialBalanceResponse> GetTrialBalanceAsync(Guid companyId, Guid branchId, TrialBalanceFilter filter, CancellationToken cancellationToken);
}

public sealed record GeneralJournalFilter(DateOnly? From, DateOnly? To, Guid? AccountId, Guid? CostCenterId, JournalState? State, string? Search, bool Consolidated);

public sealed record TrialBalanceFilter(DateOnly? From, DateOnly? To, Guid? PeriodId, AccountType? AccountType, bool IncludeZeroRows, int? MaximumLevel, bool Consolidated,
    TrialBalanceParameters? Parameters = null);

/// <summary>Al-Ameen report-request parameters of the trial balance, sent to the server as query parameters.</summary>
public sealed record TrialBalanceParameters(Guid? AccountId, Guid? CostCenterId, int AccountScope, long? FromJournalNumber, long? ToJournalNumber,
    string? Sources, string? DisplayCurrency, string? DisplayRate, bool ShowLastMovement);
