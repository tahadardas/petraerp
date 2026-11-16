using PetraERP.ApiClient;

namespace PetraERP.Desktop.GeneralLedger;

public interface IGeneralLedgerClient
{
    Task<JournalPagedResponse> SearchJournalsAsync(Guid companyId, Guid branchId, JournalState? state, DateOnly? fromDate, DateOnly? toDate, string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<JournalDetailsResponse> GetJournalAsync(Guid companyId, Guid branchId, Guid journalId, CancellationToken cancellationToken);
    Task<JournalDetailsResponse> CreateDraftAsync(Guid companyId, Guid branchId, CreateJournalDraftRequest request, CancellationToken cancellationToken);
    Task<JournalDetailsResponse> ReviseDraftAsync(Guid companyId, Guid branchId, Guid journalId, ReviseJournalDraftRequest request, CancellationToken cancellationToken);
    Task<JournalDetailsResponse> ValidateDraftAsync(Guid companyId, Guid branchId, Guid journalId, JournalActionRequest request, CancellationToken cancellationToken);
    Task<JournalDetailsResponse> ApproveDraftAsync(Guid companyId, Guid branchId, Guid journalId, JournalActionRequest request, CancellationToken cancellationToken);
    Task<JournalDetailsResponse> ReturnToDraftAsync(Guid companyId, Guid branchId, Guid journalId, JournalActionRequest request, CancellationToken cancellationToken);
    Task<JournalDetailsResponse> CreateReversalDraftAsync(Guid companyId, Guid branchId, Guid journalId, CreateReversalDraftRequest request, CancellationToken cancellationToken);
    Task DeleteDraftAsync(Guid companyId, Guid branchId, Guid journalId, string expectedVersion, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountLookupResponse>> GetAccountsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CostCenterLookupResponse>> GetCostCentersAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    /// <summary>Posts an approved journal. The caller owns the idempotency key of the logical attempt.</summary>
    Task<PostingReceiptResponse> PostJournalAsync(Guid companyId, Guid branchId, Guid journalId, PostJournalRequest request, Guid idempotencyKey, CancellationToken cancellationToken);
    /// <summary>Posts an approved reversal draft and marks its original Reversed in one server transaction.</summary>
    Task<PostingReceiptResponse> PostJournalReversalAsync(Guid companyId, Guid branchId, Guid journalId, PostJournalReversalRequest request, Guid idempotencyKey, CancellationToken cancellationToken);
    Task<CompanyCurrencyContextResponse> GetCurrencyContextAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<JournalValidationResponse> ValidateLinesAsync(Guid companyId, Guid branchId, ValidateJournalLinesRequest request, CancellationToken cancellationToken);

    /// <summary>The cash-book voucher marker of a journal; an empty <c>Kind</c> means an ordinary journal.</summary>
    Task<JournalVoucherHeaderDto> GetVoucherHeaderAsync(Guid companyId, Guid branchId, Guid journalId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<JournalVoucherHeaderDto> SetVoucherHeaderAsync(Guid companyId, Guid branchId, Guid journalId, JournalVoucherHeaderDto header, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
