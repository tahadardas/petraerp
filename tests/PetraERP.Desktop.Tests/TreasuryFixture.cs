using PetraERP.ApiClient;
using PetraERP.Desktop.Treasury;

namespace PetraERP.Desktop.Tests;

/// <summary>In-memory voucher client that records requests; money rules are proven by the SQL tests.</summary>
internal sealed class TreasuryFixture : ITreasuryVouchersClient
{
    public List<VoucherSummaryDto> Vouchers { get; } = [];
    public Dictionary<Guid, VoucherDetailDto> Details { get; } = [];
    public List<OutstandingInvoiceDto> Outstanding { get; } = [];
    public List<SaveVoucherRequest> Saves { get; } = [];
    public List<PostVoucherRequest> Posts { get; } = [];
    public List<ReverseVoucherRequest> Reversals { get; } = [];
    public bool FailPostAfterSend { get; set; }

    public Task<VoucherPageResponse> ListAsync(Guid companyId, Guid branchId, VoucherKind? kind, Guid? partyId, Guid? salesInvoiceId, int page, int pageSize,
        CancellationToken cancellationToken) =>
        Task.FromResult(new VoucherPageResponse { Page = 1, PageSize = pageSize, TotalCount = Vouchers.Count, Items = Vouchers.Where(v => kind is null || v.Kind == kind).ToList() });

    /// <summary>Number of upcoming GetAsync calls that fail as a lost response (the save itself already committed).</summary>
    public int FailGets { get; set; }

    public Task<VoucherDetailDto> GetAsync(Guid companyId, Guid branchId, Guid voucherId, CancellationToken cancellationToken)
    {
        if (FailGets > 0) { FailGets--; throw new HttpRequestException("lost get"); }
        return Task.FromResult(Details[voucherId]);
    }

    public Task<VoucherDetailDto> CreateAsync(Guid companyId, Guid branchId, SaveVoucherRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Store(Guid.NewGuid(), request, VoucherStatus.Draft));

    public Task<VoucherDetailDto> UpdateAsync(Guid companyId, Guid branchId, Guid voucherId, SaveVoucherRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Store(voucherId, request, VoucherStatus.Draft));

    public List<SaveAndProcessVoucherRequest> SaveAndProcessRequests { get; } = [];
    public string ProcessingOutcome { get; set; } = "SavedAsDraft";

    public Task<DocumentProcessingResultDto> SaveAndProcessAsync(Guid companyId, Guid branchId, SaveAndProcessVoucherRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Process(Guid.NewGuid(), request));

    public Task<DocumentProcessingResultDto> SaveAndProcessUpdateAsync(Guid companyId, Guid branchId, Guid voucherId, SaveAndProcessVoucherRequest request,
        CancellationToken cancellationToken) => Task.FromResult(Process(voucherId, request));

    private DocumentProcessingResultDto Process(Guid id, SaveAndProcessVoucherRequest request)
    {
        SaveAndProcessRequests.Add(request);
        var posted = ProcessingOutcome == "Posted" && request.SaveAsDraft != true;
        var detail = Store(id, request.Document!, posted ? VoucherStatus.Posted : VoucherStatus.Draft);
        return new DocumentProcessingResultDto
        {
            DocumentType = "Voucher", DocumentId = id, Status = detail.Status.ToString(), Outcome = ProcessingOutcome, PostingApplied = posted,
            Version = detail.Version, Message = string.Empty,
        };
    }

    public Task DeleteAsync(Guid companyId, Guid branchId, Guid voucherId, string expectedVersion, CancellationToken cancellationToken)
    {
        Details.Remove(voucherId);
        return Task.CompletedTask;
    }

    public Task<VoucherDetailDto> PostAsync(Guid companyId, Guid branchId, Guid voucherId, PostVoucherRequest request, CancellationToken cancellationToken)
    {
        Posts.Add(request);
        if (FailPostAfterSend) throw new HttpRequestException("lost response");
        var posted = Details[voucherId];
        posted.Status = VoucherStatus.Posted; posted.VoucherNumber = "RV-0001"; posted.JournalEntryId = Guid.NewGuid(); posted.Version = "AAAAAAAAB9I=";
        return Task.FromResult(posted);
    }

    public Task<VoucherDetailDto> ReverseAsync(Guid companyId, Guid branchId, Guid voucherId, ReverseVoucherRequest request, CancellationToken cancellationToken)
    {
        Reversals.Add(request);
        var reversed = Details[voucherId];
        reversed.Status = VoucherStatus.Reversed; reversed.ReversalReason = request.Reason;
        return Task.FromResult(reversed);
    }

    public Task<IReadOnlyList<OutstandingInvoiceDto>> OutstandingAsync(Guid companyId, Guid branchId, Guid customerId, string currencyCode,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OutstandingInvoiceDto>>(Outstanding);

    public Task<SettlementInvoicePageResponse> SettlementInvoicesAsync(Guid companyId, Guid branchId, Guid partyId, VoucherKind kind,
        string? currencyCode, SettlementStateFilter state, int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(new SettlementInvoicePageResponse
        {
            Page = page, PageSize = pageSize, TotalCount = Outstanding.Count,
            Items = Outstanding.Select(i => new SettlementInvoiceDto
            {
                InvoiceId = i.InvoiceId, PartyId = partyId, Number = i.InvoiceNumber, DateUtc = i.InvoiceDateUtc,
                CurrencyCode = i.CurrencyCode, Total = i.GrandTotal, Returned = i.ReturnedAmount, Paid = i.PaidAmount,
                Outstanding = i.Outstanding, State = SettlementStateFilter.Open,
            }).ToList(),
        });

    // Interface adaptation only: the new mutation scenarios are deferred to prompt12 with their acceptance cases.
    public Task<VoucherDetailDto> ApproveAsync(Guid companyId, Guid branchId, Guid voucherId, VoucherActionRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Approval acceptance fixture is deferred to the final verification phase.");

    public Task<VoucherDetailDto> ApplyAdvanceAsync(Guid companyId, Guid branchId, Guid voucherId, ApplyAdvanceRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Advance application acceptance fixture is deferred to the final verification phase.");

    public Task<VoucherDetailDto> ReverseSettlementAsync(Guid companyId, Guid branchId, Guid voucherId, Guid batchId, VoucherActionRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException("Settlement reversal acceptance fixture is deferred to the final verification phase.");

    private VoucherDetailDto Store(Guid id, SaveVoucherRequest request, VoucherStatus status)
    {
        Saves.Add(request);
        var detail = new VoucherDetailDto
        {
            Id = id, Kind = request.Kind, PatternRevisionId = request.PatternRevisionId, PartyId = request.PartyId, SettlementMethod = request.SettlementMethod,
            VoucherDateUtc = request.VoucherDateUtc, CurrencyCode = request.CurrencyCode ?? "SYP", ExchangeRate = request.ExchangeRate ?? "1.000000", Amount = request.Amount,
            Notes = request.Notes, Status = status, Version = "AAAAAAAAB9E=",
            Allocations = (request.Allocations ?? []).Select(a => new VoucherAllocationDto { Id = Guid.NewGuid(), SalesInvoiceId = a.SalesInvoiceId, Amount = a.Amount }).ToList(),
            Lines = (request.Lines ?? []).Select(l => new VoucherLineDto { Id = Guid.NewGuid(), PartyId = l.PartyId, AccountId = l.AccountId, Amount = l.Amount }).ToList(),
        };
        Details[id] = detail;
        return detail;
    }
}
