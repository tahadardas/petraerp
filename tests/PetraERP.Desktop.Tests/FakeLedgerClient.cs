using System.Text.Json;
using PetraERP.ApiClient;
using PetraERP.Desktop.GeneralLedger;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// Test-only in-memory ledger that follows the server lifecycle (Draft → Validated → Approved → Posted → Reversed)
/// with versions and per-company currency context. It records every posting call and idempotency key.
/// </summary>
internal sealed class FakeLedgerClient : IGeneralLedgerClient
{
    private readonly Dictionary<Guid, JournalDetailsResponse> journals = [];
    private readonly Dictionary<Guid, PostingReceiptResponse> receiptsByKey = [];
    private long version = 1;
    private long sequence;

    public Dictionary<Guid, (string BaseCurrency, string[] Additional)> Companies { get; } = [];
    public List<(Guid JournalId, Guid Key, string Reason)> PostCalls { get; } = [];
    public List<(Guid JournalId, Guid Key, Guid OriginalId, string OriginalVersion)> ReversalCalls { get; } = [];
    public CreateJournalDraftRequest? LastCreateRequest { get; private set; }
    public Dictionary<Guid, JournalVoucherHeaderDto> VoucherHeaders { get; } = [];

    public Task<JournalVoucherHeaderDto> GetVoucherHeaderAsync(Guid companyId, Guid branchId, Guid journalId, CancellationToken cancellationToken) =>
        Task.FromResult(VoucherHeaders.TryGetValue(journalId, out var header) ? header : new JournalVoucherHeaderDto { Kind = string.Empty, CashAccountId = Guid.Empty });

    public Task<JournalVoucherHeaderDto> SetVoucherHeaderAsync(Guid companyId, Guid branchId, Guid journalId, JournalVoucherHeaderDto header, CancellationToken cancellationToken)
    {
        VoucherHeaders[journalId] = header;
        return Task.FromResult(header);
    }
    /// <summary>Failures thrown by the next posting call(s), in order. When <c>CommitBeforeFailure</c>, the post commits first.</summary>
    public Queue<(Exception Failure, bool CommitBeforeFailure)> PostingFailures { get; } = new();

    public JournalDetailsResponse Add(Guid companyId, Guid branchId, JournalState state, string baseCurrency = "JOD", Guid? reverses = null, Guid? reversedBy = null)
    {
        var id = Guid.NewGuid();
        var journal = Journal(id, companyId, branchId, state, baseCurrency, reverses, reversedBy);
        journals[id] = journal;
        return journal;
    }

    public JournalDetailsResponse Get(Guid id) => journals[id];

    /// <summary>Server responses are fresh objects; never hand the view model a shared mutable instance.</summary>
    private static JournalDetailsResponse Copy(JournalDetailsResponse source, Action<JournalDetailsResponse> change)
    {
        var copy = JsonSerializer.Deserialize<JournalDetailsResponse>(JsonSerializer.Serialize(source))!;
        change(copy);
        return copy;
    }

    private JournalDetailsResponse Journal(Guid id, Guid companyId, Guid branchId, JournalState state, string baseCurrency, Guid? reverses, Guid? reversedBy) => new()
    {
        Id = id,
        CompanyId = companyId,
        BranchId = branchId,
        AccountingDate = new DateOnly(2026, 1, 15),
        Description = "قيد اختبار",
        State = state,
        StateName = state.ToString(),
        Revision = 1,
        Version = NextVersion(),
        ReversesEntryId = reverses,
        ReversedByEntryId = reversedBy,
        TotalDebit = "100.0000",
        TotalCredit = "100.0000",
        BaseCurrencyCode = baseCurrency,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
        Lines = [],
        AuditTrail = [],
    };

    private string NextVersion() => Convert.ToBase64String(BitConverter.GetBytes(version++));

    private JournalDetailsResponse Transition(Guid id, string expectedVersion, JournalState from, JournalState to)
    {
        var current = journals[id];
        if (current.Version != expectedVersion) throw new PetraApiException("conflict", 409, "{\"code\":\"concurrency.conflict\"}", new Dictionary<string, IEnumerable<string>>(), null);
        if (current.State != from) throw new PetraApiException("state", 422, "{\"code\":\"ledger.journal_state_invalid\"}", new Dictionary<string, IEnumerable<string>>(), null);
        journals[id] = Copy(current, j => { j.State = to; j.StateName = to.ToString(); j.Version = NextVersion(); });
        return journals[id];
    }

    public Task<JournalPagedResponse> SearchJournalsAsync(Guid companyId, Guid branchId, JournalState? state, DateOnly? fromDate, DateOnly? toDate, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var items = journals.Values.Where(j => j.CompanyId == companyId && j.BranchId == branchId && (state is null || j.State == state))
            .Select(j => new JournalSummaryItemResponse
            {
                Id = j.Id, AccountingDate = j.AccountingDate, Description = j.Description, State = j.State, StateName = j.StateName,
                PostingSequence = j.PostingSequence, TotalDebit = j.TotalDebit, TotalCredit = j.TotalCredit, BaseCurrencyCode = j.BaseCurrencyCode,
                LineCount = 2, Version = j.Version,
            }).ToArray();
        return Task.FromResult(new JournalPagedResponse { Items = items, Page = page, PageSize = pageSize, TotalCount = items.Length });
    }

    public Task<JournalDetailsResponse> GetJournalAsync(Guid companyId, Guid branchId, Guid journalId, CancellationToken cancellationToken) =>
        journals.TryGetValue(journalId, out var journal) && journal.CompanyId == companyId && journal.BranchId == branchId
            ? Task.FromResult(Copy(journal, static _ => { }))
            : Task.FromException<JournalDetailsResponse>(new PetraApiException("missing", 404, "{\"code\":\"resource.not_found\"}", new Dictionary<string, IEnumerable<string>>(), null));

    public Task<JournalDetailsResponse> CreateDraftAsync(Guid companyId, Guid branchId, CreateJournalDraftRequest request, CancellationToken cancellationToken)
    {
        LastCreateRequest = request;
        var created = Add(companyId, branchId, JournalState.Draft, Companies.TryGetValue(companyId, out var c) ? c.BaseCurrency : "JOD");
        journals[created.Id] = Copy(created, j => { j.Description = request.Description; j.AccountingDate = request.AccountingDate; });
        return Task.FromResult(journals[created.Id]);
    }

    public Task<JournalDetailsResponse> ReviseDraftAsync(Guid companyId, Guid branchId, Guid journalId, ReviseJournalDraftRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Transition(journalId, request.ExpectedVersion, JournalState.Draft, JournalState.Draft));

    public Task<JournalDetailsResponse> ValidateDraftAsync(Guid companyId, Guid branchId, Guid journalId, JournalActionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Transition(journalId, request.ExpectedVersion, JournalState.Draft, JournalState.Validated));

    public Task<JournalDetailsResponse> ApproveDraftAsync(Guid companyId, Guid branchId, Guid journalId, JournalActionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Transition(journalId, request.ExpectedVersion, JournalState.Validated, JournalState.Approved));

    public Task<JournalDetailsResponse> ReturnToDraftAsync(Guid companyId, Guid branchId, Guid journalId, JournalActionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Transition(journalId, request.ExpectedVersion, journals[journalId].State, JournalState.Draft));

    public Task<JournalDetailsResponse> CreateReversalDraftAsync(Guid companyId, Guid branchId, Guid journalId, CreateReversalDraftRequest request, CancellationToken cancellationToken)
    {
        var original = journals[journalId];
        return Task.FromResult(Add(companyId, branchId, JournalState.Draft, original.BaseCurrencyCode, reverses: journalId));
    }

    public Task DeleteDraftAsync(Guid companyId, Guid branchId, Guid journalId, string expectedVersion, CancellationToken cancellationToken)
    {
        journals.Remove(journalId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AccountLookupResponse>> GetAccountsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AccountLookupResponse>>([]);

    public Task<IReadOnlyList<CostCenterLookupResponse>> GetCostCentersAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CostCenterLookupResponse>>([]);

    public Task<PostingReceiptResponse> PostJournalAsync(Guid companyId, Guid branchId, Guid journalId, PostJournalRequest request, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        PostCalls.Add((journalId, idempotencyKey, request.Reason));
        return Post(idempotencyKey, () =>
        {
            Transition(journalId, request.ExpectedVersion, JournalState.Approved, JournalState.Posted);
            journals[journalId] = Copy(journals[journalId], j => j.PostingSequence = ++sequence);
            return Receipt(idempotencyKey, companyId, branchId, journalId, null, PostingOperation.PostJournal);
        });
    }

    public Task<PostingReceiptResponse> PostJournalReversalAsync(Guid companyId, Guid branchId, Guid journalId, PostJournalReversalRequest request, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        ReversalCalls.Add((journalId, idempotencyKey, request.OriginalJournalId, request.OriginalExpectedVersion));
        return Post(idempotencyKey, () =>
        {
            Transition(journalId, request.ExpectedVersion, JournalState.Approved, JournalState.Posted);
            Transition(request.OriginalJournalId, request.OriginalExpectedVersion, JournalState.Posted, JournalState.Reversed);
            journals[request.OriginalJournalId] = Copy(journals[request.OriginalJournalId], j => j.ReversedByEntryId = journalId);
            return Receipt(idempotencyKey, companyId, branchId, journalId, request.OriginalJournalId, PostingOperation.PostJournalReversal);
        });
    }

    private Task<PostingReceiptResponse> Post(Guid key, Func<PostingReceiptResponse> commit)
    {
        // Same key replays the stored receipt, exactly like the server's scoped idempotency record.
        if (receiptsByKey.TryGetValue(key, out var replay)) return Task.FromResult(replay);
        if (PostingFailures.TryDequeue(out var failure))
        {
            if (failure.CommitBeforeFailure) receiptsByKey[key] = commit();
            return Task.FromException<PostingReceiptResponse>(failure.Failure);
        }

        try
        {
            receiptsByKey[key] = commit();
            return Task.FromResult(receiptsByKey[key]);
        }
        catch (PetraApiException exception)
        {
            return Task.FromException<PostingReceiptResponse>(exception);
        }
    }

    private PostingReceiptResponse Receipt(Guid key, Guid companyId, Guid branchId, Guid journalId, Guid? originalId, PostingOperation operation) => new()
    {
        IdempotencyKey = key, Operation = operation, CompanyId = companyId, BranchId = branchId, JournalId = journalId,
        OriginalJournalId = originalId, FiscalYearId = Guid.Empty, PostingSequence = sequence,
    };

    public Task<CompanyCurrencyContextResponse> GetCurrencyContextAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken)
    {
        var (baseCurrency, additional) = Companies.TryGetValue(companyId, out var c) ? c : ("JOD", []);
        return Task.FromResult(new CompanyCurrencyContextResponse
        {
            CompanyId = companyId,
            BaseCurrency = new CurrencyResponse { Code = baseCurrency, NameArabic = baseCurrency },
            AdditionalCurrencies = additional.Select(code => new CurrencyResponse { Code = code, NameArabic = code }).ToArray(),
        });
    }

    public Task<JournalValidationResponse> ValidateLinesAsync(Guid companyId, Guid branchId, ValidateJournalLinesRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new JournalValidationResponse { IsValid = true, Errors = [], TotalDebit = "0.0000", TotalCredit = "0.0000", Difference = "0.0000", IsBalanced = true });
}
