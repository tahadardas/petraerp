using PetraERP.ApiClient;
using PetraERP.Desktop.Purchases;

namespace PetraERP.Desktop.Tests;

/// <summary>In-memory purchase client that records requests; stock and ledger rules are proven by the SQL tests.</summary>
internal sealed class PurchaseFixture : IPurchaseInvoicesClient
{
    public Dictionary<Guid, PurchaseDetailDto> Details { get; } = [];
    public List<SavePurchaseInvoiceRequest> Saves { get; } = [];
    public List<SaveAndProcessPurchaseInvoiceRequest> ProcessRequests { get; } = [];
    public List<PostPurchaseInvoiceRequest> Posts { get; } = [];
    public List<ReversePurchaseInvoiceRequest> Reversals { get; } = [];
    public List<CreatePurchaseReturnRequest> Returns { get; } = [];
    public bool FailPostAfterSend { get; set; }

    public Task<PurchasePageResponse> ListAsync(Guid companyId, Guid branchId, Guid? supplierId, PurchaseInvoiceStatus? status, DateTimeOffset? fromDate,
        DateTimeOffset? toDate, Guid? returnOfInvoiceId, int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(new PurchasePageResponse { Page = page, PageSize = pageSize, TotalCount = Details.Count, Items = [] });

    public Task<PurchaseDetailDto> GetAsync(Guid companyId, Guid branchId, Guid invoiceId, CancellationToken cancellationToken) => Task.FromResult(Details[invoiceId]);

    public Task<PurchaseDetailDto> CreateAsync(Guid companyId, Guid branchId, SavePurchaseInvoiceRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Store(Guid.NewGuid(), request));

    public Task<PurchaseDetailDto> UpdateAsync(Guid companyId, Guid branchId, Guid invoiceId, SavePurchaseInvoiceRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Store(invoiceId, request));

    public Task<DocumentProcessingResultDto> SaveAndProcessAsync(Guid companyId, Guid branchId, SaveAndProcessPurchaseInvoiceRequest request, CancellationToken cancellationToken)
    {
        ProcessRequests.Add(request);
        var detail = Store(Guid.NewGuid(), request.Document!);
        return Task.FromResult(Outcome(request, detail));
    }

    public Task<DocumentProcessingResultDto> SaveAndProcessUpdateAsync(Guid companyId, Guid branchId, Guid invoiceId, SaveAndProcessPurchaseInvoiceRequest request, CancellationToken cancellationToken)
    {
        ProcessRequests.Add(request);
        var detail = Store(invoiceId, request.Document!);
        return Task.FromResult(Outcome(request, detail));
    }

    private static DocumentProcessingResultDto Outcome(SaveAndProcessPurchaseInvoiceRequest request, PurchaseDetailDto detail) => new()
    {
        DocumentType = "PurchaseInvoice",
        DocumentId = detail.Id,
        DocumentNumber = detail.InvoiceNumber,
        Status = detail.Status.ToString(),
        Outcome = request.SaveAsDraft == true ? "SavedDraft" : "Approved",
        ApprovalApplied = request.SaveAsDraft != true,
        EffectivePostingMode = "ApproveOnly",
        Version = detail.Version,
        Message = request.SaveAsDraft == true ? "draft" : "approved",
    };

    public Task<PurchaseDetailDto> ApproveAsync(Guid companyId, Guid branchId, Guid invoiceId, string expectedVersion, CancellationToken cancellationToken)
    {
        var approved = Details[invoiceId];
        approved.Status = PurchaseInvoiceStatus.Approved;
        approved.Version = "AAAAAAAAB9H=";
        return Task.FromResult(approved);
    }

    public Task DeleteAsync(Guid companyId, Guid branchId, Guid invoiceId, string expectedVersion, CancellationToken cancellationToken)
    {
        Details.Remove(invoiceId);
        return Task.CompletedTask;
    }

    public Task<PurchaseDetailDto> PostAsync(Guid companyId, Guid branchId, Guid invoiceId, PostPurchaseInvoiceRequest request, CancellationToken cancellationToken)
    {
        Posts.Add(request);
        if (FailPostAfterSend) throw new HttpRequestException("lost response");
        var posted = Details[invoiceId];
        posted.Status = PurchaseInvoiceStatus.Posted; posted.InvoiceNumber = "PI-0001"; posted.JournalEntryId = Guid.NewGuid(); posted.Version = "AAAAAAAAB9I=";
        foreach (var line in posted.Lines ?? []) line.Id ??= Guid.NewGuid();
        return Task.FromResult(posted);
    }

    public Task<PurchaseDetailDto> ReverseAsync(Guid companyId, Guid branchId, Guid invoiceId, ReversePurchaseInvoiceRequest request, CancellationToken cancellationToken)
    {
        Reversals.Add(request);
        var original = Details[invoiceId];
        var reversal = new PurchaseDetailDto
        {
            Id = Guid.NewGuid(), InvoiceNumber = "PI-0002", Status = PurchaseInvoiceStatus.Posted, ReversesInvoiceId = invoiceId,
            SupplierId = original.SupplierId, WarehouseId = original.WarehouseId, CurrencyCode = original.CurrencyCode,
            ExchangeRate = original.ExchangeRate, PaymentMethod = original.PaymentMethod, GrandTotal = original.GrandTotal, Version = "AAAAAAAAB9K=",
            Lines = [],
        };
        Details[reversal.Id!.Value] = reversal;
        original.Status = PurchaseInvoiceStatus.Reversed;
        original.ReversedByInvoiceId = reversal.Id;
        return Task.FromResult(original);
    }

    public Task<PurchaseDetailDto> ReturnAsync(Guid companyId, Guid branchId, Guid invoiceId, CreatePurchaseReturnRequest request, CancellationToken cancellationToken)
    {
        Returns.Add(request);
        var document = new PurchaseDetailDto { Id = Guid.NewGuid(), InvoiceNumber = "PI-0002", Status = PurchaseInvoiceStatus.Posted, ReturnOfInvoiceId = invoiceId,
            Version = "AAAAAAAAB9J=", Lines = [] };
        Details[document.Id!.Value] = document;
        return Task.FromResult(document);
    }

    private PurchaseDetailDto Store(Guid id, SavePurchaseInvoiceRequest request)
    {
        Saves.Add(request);
        var detail = new PurchaseDetailDto
        {
            Id = id, PatternRevisionId = request.PatternRevisionId, SupplierId = request.SupplierId, WarehouseId = request.WarehouseId,
            InvoiceDateUtc = request.InvoiceDateUtc, PaymentMethod = request.PaymentMethod, CurrencyCode = request.CurrencyCode ?? "SYP", ExchangeRate = request.ExchangeRate ?? "1.000000",
            Status = PurchaseInvoiceStatus.Draft, Version = "AAAAAAAAB9E=",
            Lines = (request.Lines ?? []).Select(l => new PurchaseLineDto { Id = Guid.NewGuid(), ItemId = l.ItemId, UnitId = l.UnitId, Quantity = l.Quantity,
                UnitCost = l.UnitCost, DiscountAmount = l.DiscountAmount ?? "0", TaxAmount = l.TaxAmount ?? "0", ReturnedQuantity = "0.0000", ItemName = "إسمنت" }).ToList(),
        };
        Details[id] = detail;
        return detail;
    }
}
