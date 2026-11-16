using PetraERP.ApiClient;
using PetraERP.Desktop.Purchases;

namespace PetraERP.Desktop.Connection;

public sealed class PurchaseInvoicesClient(DesktopSession session) : IPurchaseInvoicesClient
{
    public Task<PurchasePageResponse> ListAsync(Guid companyId, Guid branchId, Guid? supplierId, PurchaseInvoiceStatus? status, DateTimeOffset? fromDate,
        DateTimeOffset? toDate, Guid? returnOfInvoiceId, int page, int pageSize, CancellationToken cancellationToken) =>
        session.Api.ListPurchaseInvoicesAsync(companyId, branchId, supplierId, status, fromDate, toDate, returnOfInvoiceId: returnOfInvoiceId, page: page,
            pageSize: pageSize, cancellationToken: cancellationToken);

    public Task<PurchaseDetailDto> GetAsync(Guid companyId, Guid branchId, Guid invoiceId, CancellationToken cancellationToken) =>
        session.Api.GetPurchaseInvoiceAsync(companyId, branchId, invoiceId, cancellationToken);

    public Task<PurchaseDetailDto> CreateAsync(Guid companyId, Guid branchId, SavePurchaseInvoiceRequest request, CancellationToken cancellationToken) =>
        session.Api.CreatePurchaseInvoiceAsync(companyId, branchId, request, cancellationToken);

    public Task<PurchaseDetailDto> UpdateAsync(Guid companyId, Guid branchId, Guid invoiceId, SavePurchaseInvoiceRequest request, CancellationToken cancellationToken) =>
        session.Api.UpdatePurchaseInvoiceAsync(companyId, branchId, invoiceId, request, cancellationToken);

    public Task<DocumentProcessingResultDto> SaveAndProcessAsync(Guid companyId, Guid branchId, SaveAndProcessPurchaseInvoiceRequest request, CancellationToken cancellationToken) =>
        session.PostWithOutcomeAsync(companyId, branchId, request.IdempotencyKey, () => session.Api.SaveAndProcessPurchaseInvoiceAsync(companyId, branchId, request, cancellationToken), cancellationToken);

    public Task<DocumentProcessingResultDto> SaveAndProcessUpdateAsync(Guid companyId, Guid branchId, Guid invoiceId, SaveAndProcessPurchaseInvoiceRequest request, CancellationToken cancellationToken) =>
        session.PostWithOutcomeAsync(companyId, branchId, request.IdempotencyKey, () => session.Api.SaveAndProcessUpdatePurchaseInvoiceAsync(companyId, branchId, invoiceId, request, cancellationToken), cancellationToken);

    public Task<PurchaseDetailDto> ApproveAsync(Guid companyId, Guid branchId, Guid invoiceId, string expectedVersion, CancellationToken cancellationToken) =>
        session.Api.ApprovePurchaseInvoiceAsync(companyId, branchId, invoiceId, new ApprovePurchaseInvoiceRequest { ExpectedVersion = expectedVersion }, cancellationToken);

    public Task DeleteAsync(Guid companyId, Guid branchId, Guid invoiceId, string expectedVersion, CancellationToken cancellationToken) =>
        session.Api.DeletePurchaseInvoiceAsync(companyId, branchId, invoiceId, expectedVersion, cancellationToken);

    public Task<PurchaseDetailDto> PostAsync(Guid companyId, Guid branchId, Guid invoiceId, PostPurchaseInvoiceRequest request, CancellationToken cancellationToken) =>
        session.Api.PostPurchaseInvoiceAsync(companyId, branchId, invoiceId, request, cancellationToken);

    public Task<PurchaseDetailDto> ReverseAsync(Guid companyId, Guid branchId, Guid invoiceId, ReversePurchaseInvoiceRequest request, CancellationToken cancellationToken) =>
        session.Api.ReversePurchaseInvoiceAsync(companyId, branchId, invoiceId, request, cancellationToken);

    public Task<PurchaseDetailDto> ReturnAsync(Guid companyId, Guid branchId, Guid invoiceId, CreatePurchaseReturnRequest request, CancellationToken cancellationToken) =>
        session.Api.ReturnPurchaseInvoiceAsync(companyId, branchId, invoiceId, request, cancellationToken);
}
