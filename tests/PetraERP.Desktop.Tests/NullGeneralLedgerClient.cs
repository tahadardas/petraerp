using PetraERP.ApiClient;
using PetraERP.Desktop.GeneralLedger;

namespace PetraERP.Desktop.Tests;

internal sealed class NullGeneralLedgerClient : IGeneralLedgerClient
{
    public Task<JournalPagedResponse> SearchJournalsAsync(Guid companyId, Guid branchId, JournalState? state, DateOnly? fromDate, DateOnly? toDate, string? search, int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(new JournalPagedResponse { Items = [], Page = 1, PageSize = pageSize, TotalCount = 0 });

    public Task<JournalDetailsResponse> GetJournalAsync(Guid companyId, Guid branchId, Guid journalId, CancellationToken cancellationToken) =>
        throw new KeyNotFoundException();

    public Task<JournalDetailsResponse> CreateDraftAsync(Guid companyId, Guid branchId, CreateJournalDraftRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<JournalDetailsResponse> ReviseDraftAsync(Guid companyId, Guid branchId, Guid journalId, ReviseJournalDraftRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<JournalDetailsResponse> ValidateDraftAsync(Guid companyId, Guid branchId, Guid journalId, JournalActionRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<JournalDetailsResponse> ApproveDraftAsync(Guid companyId, Guid branchId, Guid journalId, JournalActionRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<JournalDetailsResponse> ReturnToDraftAsync(Guid companyId, Guid branchId, Guid journalId, JournalActionRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<JournalDetailsResponse> CreateReversalDraftAsync(Guid companyId, Guid branchId, Guid journalId, CreateReversalDraftRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task DeleteDraftAsync(Guid companyId, Guid branchId, Guid journalId, string expectedVersion, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<AccountLookupResponse>> GetAccountsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AccountLookupResponse>>([]);

    public Task<IReadOnlyList<CostCenterLookupResponse>> GetCostCentersAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CostCenterLookupResponse>>([]);

    public Task<PostingReceiptResponse> PostJournalAsync(Guid companyId, Guid branchId, Guid journalId, PostJournalRequest request, Guid idempotencyKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<PostingReceiptResponse> PostJournalReversalAsync(Guid companyId, Guid branchId, Guid journalId, PostJournalReversalRequest request, Guid idempotencyKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<CompanyCurrencyContextResponse> GetCurrencyContextAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        Task.FromResult(new CompanyCurrencyContextResponse { CompanyId = companyId, BaseCurrency = new CurrencyResponse { Code = "JOD", NameArabic = "دينار" }, AdditionalCurrencies = [] });

    public Task<JournalValidationResponse> ValidateLinesAsync(Guid companyId, Guid branchId, ValidateJournalLinesRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new JournalValidationResponse { IsValid = true, Errors = [], TotalDebit = "0.0000", TotalCredit = "0.0000", Difference = "0.0000", IsBalanced = true });
}
