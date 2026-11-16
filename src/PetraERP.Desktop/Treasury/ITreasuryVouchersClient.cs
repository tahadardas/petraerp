using PetraERP.ApiClient;

namespace PetraERP.Desktop.Treasury;

/// <summary>Typed-client seam for the treasury voucher workstation; the server stays authoritative for all money rules.</summary>
public interface ITreasuryVouchersClient
{
    Task<VoucherPageResponse> ListAsync(Guid companyId, Guid branchId, VoucherKind? kind, Guid? partyId, Guid? salesInvoiceId, int page, int pageSize,
        CancellationToken cancellationToken);

    Task<VoucherDetailDto> GetAsync(Guid companyId, Guid branchId, Guid voucherId, CancellationToken cancellationToken);

    Task<VoucherDetailDto> CreateAsync(Guid companyId, Guid branchId, SaveVoucherRequest request, CancellationToken cancellationToken);

    Task<VoucherDetailDto> UpdateAsync(Guid companyId, Guid branchId, Guid voucherId, SaveVoucherRequest request, CancellationToken cancellationToken);

    Task<DocumentProcessingResultDto> SaveAndProcessAsync(Guid companyId, Guid branchId, SaveAndProcessVoucherRequest request,
        CancellationToken cancellationToken);

    Task<DocumentProcessingResultDto> SaveAndProcessUpdateAsync(Guid companyId, Guid branchId, Guid voucherId, SaveAndProcessVoucherRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(Guid companyId, Guid branchId, Guid voucherId, string expectedVersion, CancellationToken cancellationToken);

    Task<VoucherDetailDto> PostAsync(Guid companyId, Guid branchId, Guid voucherId, PostVoucherRequest request, CancellationToken cancellationToken);

    Task<VoucherDetailDto> ReverseAsync(Guid companyId, Guid branchId, Guid voucherId, ReverseVoucherRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<OutstandingInvoiceDto>> OutstandingAsync(Guid companyId, Guid branchId, Guid customerId, string currencyCode,
        CancellationToken cancellationToken);
    Task<SettlementInvoicePageResponse> SettlementInvoicesAsync(Guid companyId, Guid branchId, Guid partyId, VoucherKind kind,
        string? currencyCode, SettlementStateFilter state, int page, int pageSize, CancellationToken cancellationToken);

    Task<VoucherDetailDto> ApproveAsync(Guid companyId, Guid branchId, Guid voucherId, VoucherActionRequest request, CancellationToken cancellationToken);

    Task<VoucherDetailDto> ApplyAdvanceAsync(Guid companyId, Guid branchId, Guid voucherId, ApplyAdvanceRequest request, CancellationToken cancellationToken);

    Task<VoucherDetailDto> ReverseSettlementAsync(Guid companyId, Guid branchId, Guid voucherId, Guid batchId, VoucherActionRequest request,
        CancellationToken cancellationToken);
}

