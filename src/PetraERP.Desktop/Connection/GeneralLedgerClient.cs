using PetraERP.ApiClient;
using PetraERP.Desktop.GeneralLedger;

namespace PetraERP.Desktop.Connection;

public sealed class GeneralLedgerClient(DesktopSession session) : IGeneralLedgerClient
{
    public Task<JournalPagedResponse> SearchJournalsAsync(
        Guid companyId,
        Guid branchId,
        JournalState? state,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        session.Api.SearchJournalsAsync(companyId, branchId, state, fromDate, toDate, search, page, pageSize, cancellationToken);

    public Task<JournalDetailsResponse> GetJournalAsync(
        Guid companyId,
        Guid branchId,
        Guid journalId,
        CancellationToken cancellationToken) =>
        session.Api.GetJournalAsync(companyId, branchId, journalId, cancellationToken);

    public Task<JournalDetailsResponse> CreateDraftAsync(
        Guid companyId,
        Guid branchId,
        CreateJournalDraftRequest request,
        CancellationToken cancellationToken) =>
        session.Api.CreateJournalDraftAsync(companyId, branchId, request, cancellationToken);

    public Task<JournalDetailsResponse> ReviseDraftAsync(
        Guid companyId,
        Guid branchId,
        Guid journalId,
        ReviseJournalDraftRequest request,
        CancellationToken cancellationToken) =>
        session.Api.ReviseJournalDraftAsync(companyId, branchId, journalId, request, cancellationToken);

    public Task<JournalDetailsResponse> ValidateDraftAsync(
        Guid companyId,
        Guid branchId,
        Guid journalId,
        JournalActionRequest request,
        CancellationToken cancellationToken) =>
        session.Api.ValidateJournalDraftAsync(companyId, branchId, journalId, request, cancellationToken);

    public Task<JournalDetailsResponse> ApproveDraftAsync(
        Guid companyId,
        Guid branchId,
        Guid journalId,
        JournalActionRequest request,
        CancellationToken cancellationToken) =>
        session.Api.ApproveJournalDraftAsync(companyId, branchId, journalId, request, cancellationToken);

    public Task<JournalDetailsResponse> ReturnToDraftAsync(
        Guid companyId,
        Guid branchId,
        Guid journalId,
        JournalActionRequest request,
        CancellationToken cancellationToken) =>
        session.Api.ReturnJournalToDraftAsync(companyId, branchId, journalId, request, cancellationToken);

    public Task<JournalDetailsResponse> CreateReversalDraftAsync(
        Guid companyId,
        Guid branchId,
        Guid journalId,
        CreateReversalDraftRequest request,
        CancellationToken cancellationToken) =>
        session.Api.CreateReversalDraftAsync(companyId, branchId, journalId, request, cancellationToken);

    public Task DeleteDraftAsync(
        Guid companyId,
        Guid branchId,
        Guid journalId,
        string expectedVersion,
        CancellationToken cancellationToken) =>
        session.Api.DeleteJournalDraftAsync(companyId, branchId, journalId, expectedVersion, cancellationToken);

    public Task<IReadOnlyList<AccountLookupResponse>> GetAccountsAsync(
        Guid companyId,
        Guid branchId,
        CancellationToken cancellationToken) =>
        session.Api.GetAccountsAsync(companyId, branchId, cancellationToken);

    public Task<IReadOnlyList<CostCenterLookupResponse>> GetCostCentersAsync(
        Guid companyId,
        Guid branchId,
        CancellationToken cancellationToken) =>
        session.Api.GetCostCentersAsync(companyId, branchId, cancellationToken);

    public Task<PostingReceiptResponse> PostJournalAsync(Guid companyId, Guid branchId, Guid journalId, PostJournalRequest request,
        Guid idempotencyKey, CancellationToken cancellationToken) =>
        session.Api.PostJournalAsync(companyId, branchId, journalId, request, IdempotencyHeader(idempotencyKey), cancellationToken);

    public Task<JournalVoucherHeaderDto> GetVoucherHeaderAsync(Guid companyId, Guid branchId, Guid journalId, CancellationToken cancellationToken) =>
        session.Api.GetJournalVoucherHeaderAsync(companyId, branchId, journalId, cancellationToken);

    public Task<JournalVoucherHeaderDto> SetVoucherHeaderAsync(Guid companyId, Guid branchId, Guid journalId, JournalVoucherHeaderDto header, CancellationToken cancellationToken) =>
        session.Api.SetJournalVoucherHeaderAsync(companyId, branchId, journalId, header, cancellationToken);

    public Task<PostingReceiptResponse> PostJournalReversalAsync(Guid companyId, Guid branchId, Guid journalId, PostJournalReversalRequest request,
        Guid idempotencyKey, CancellationToken cancellationToken) =>
        session.Api.PostJournalReversalAsync(companyId, branchId, journalId, request, IdempotencyHeader(idempotencyKey), cancellationToken);

    public Task<CompanyCurrencyContextResponse> GetCurrencyContextAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.Api.GetCompanyCurrencyContextAsync(companyId, branchId, cancellationToken);

    /// <summary>The server requires a nonempty UUID in 8-4-4-4-12 form.</summary>
    internal static string IdempotencyHeader(Guid key) =>
        key == Guid.Empty ? throw new ArgumentException("An idempotency key is required.", nameof(key)) : key.ToString("D");

    public Task<JournalValidationResponse> ValidateLinesAsync(
        Guid companyId,
        Guid branchId,
        ValidateJournalLinesRequest request,
        CancellationToken cancellationToken) =>
        session.Api.ValidateJournalLinesAsync(companyId, branchId, request, cancellationToken);
}
