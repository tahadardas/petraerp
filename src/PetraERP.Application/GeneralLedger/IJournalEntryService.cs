using PetraERP.Application.Common;

namespace PetraERP.Application.GeneralLedger;

public interface IJournalEntryService
{
    Task<JournalDetailsDto> CreateDraftAsync(
        JournalEntryScope scope,
        CreateJournalDraftCommand command,
        CancellationToken cancellationToken);

    Task<JournalDetailsDto> ReviseDraftAsync(
        JournalEntryScope scope,
        Guid journalId,
        ReviseJournalDraftCommand command,
        CancellationToken cancellationToken);

    Task<JournalDetailsDto> ValidateDraftAsync(
        JournalEntryScope scope,
        Guid journalId,
        JournalDraftActionCommand command,
        CancellationToken cancellationToken);

    Task<JournalDetailsDto> ApproveDraftAsync(
        JournalEntryScope scope,
        Guid journalId,
        JournalDraftActionCommand command,
        CancellationToken cancellationToken);

    Task<JournalDetailsDto> ReturnToDraftAsync(
        JournalEntryScope scope,
        Guid journalId,
        JournalDraftActionCommand command,
        CancellationToken cancellationToken);

    Task<JournalDetailsDto> CreateReversalDraftAsync(
        JournalEntryScope scope,
        CreateReversalDraftCommand command,
        CancellationToken cancellationToken);

    Task DeleteDraftAsync(
        JournalEntryScope scope,
        Guid journalId,
        ConcurrencyToken expectedVersion,
        CancellationToken cancellationToken);

    Task<JournalDetailsDto> GetJournalAsync(
        JournalEntryScope scope,
        Guid journalId,
        CancellationToken cancellationToken);

    Task<PagedResult<JournalSummaryDto>> SearchJournalsAsync(
        JournalEntryScope scope,
        DateOnly? fromDate,
        DateOnly? toDate,
        byte? state,
        string? search,
        PageRequest page,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountLookupDto>> GetAccountsAsync(
        Guid companyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CostCenterLookupDto>> GetCostCentersAsync(
        Guid companyId,
        CancellationToken cancellationToken);

    Task<JournalDraftValidationResult> ValidateJournalLinesAsync(
        JournalEntryScope scope,
        DateOnly accountingDate,
        IReadOnlyList<JournalDraftLineInput> lines,
        CancellationToken cancellationToken);
}
