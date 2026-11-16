using PetraERP.ApiClient;

namespace PetraERP.Desktop.Purchases;

/// <summary>Typed-client seam for the purchases workstation; stock, cost and payable rules stay on the server.</summary>
public interface IPurchaseInvoicesClient
{
    Task<PurchasePageResponse> ListAsync(Guid companyId, Guid branchId, Guid? supplierId, PurchaseInvoiceStatus? status, DateTimeOffset? fromDate,
        DateTimeOffset? toDate, Guid? returnOfInvoiceId, int page, int pageSize, CancellationToken cancellationToken);

    Task<PurchaseDetailDto> GetAsync(Guid companyId, Guid branchId, Guid invoiceId, CancellationToken cancellationToken);

    Task<PurchaseDetailDto> CreateAsync(Guid companyId, Guid branchId, SavePurchaseInvoiceRequest request, CancellationToken cancellationToken);

    Task<PurchaseDetailDto> UpdateAsync(Guid companyId, Guid branchId, Guid invoiceId, SavePurchaseInvoiceRequest request, CancellationToken cancellationToken);

    Task<DocumentProcessingResultDto> SaveAndProcessAsync(Guid companyId, Guid branchId, SaveAndProcessPurchaseInvoiceRequest request, CancellationToken cancellationToken);

    Task<DocumentProcessingResultDto> SaveAndProcessUpdateAsync(Guid companyId, Guid branchId, Guid invoiceId, SaveAndProcessPurchaseInvoiceRequest request, CancellationToken cancellationToken);

    Task<PurchaseDetailDto> ApproveAsync(Guid companyId, Guid branchId, Guid invoiceId, string expectedVersion, CancellationToken cancellationToken);

    Task DeleteAsync(Guid companyId, Guid branchId, Guid invoiceId, string expectedVersion, CancellationToken cancellationToken);

    Task<PurchaseDetailDto> PostAsync(Guid companyId, Guid branchId, Guid invoiceId, PostPurchaseInvoiceRequest request, CancellationToken cancellationToken);

    /// <summary>Reverses a posted invoice. The same request key replays the server's reversal instead of creating a second one.</summary>
    Task<PurchaseDetailDto> ReverseAsync(Guid companyId, Guid branchId, Guid invoiceId, ReversePurchaseInvoiceRequest request, CancellationToken cancellationToken);

    Task<PurchaseDetailDto> ReturnAsync(Guid companyId, Guid branchId, Guid invoiceId, CreatePurchaseReturnRequest request, CancellationToken cancellationToken);
}
