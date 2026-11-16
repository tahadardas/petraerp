using PetraERP.ApiClient;
using PetraERP.Desktop.Treasury;

namespace PetraERP.Desktop.Connection;

public sealed class TreasuryVouchersClient(DesktopSession session) : ITreasuryVouchersClient
{
    public Task<VoucherPageResponse> ListAsync(Guid companyId, Guid branchId, VoucherKind? kind, Guid? partyId, Guid? salesInvoiceId, int page, int pageSize,
        CancellationToken cancellationToken) =>
        session.Api.ListVouchersAsync(companyId, branchId, kind, partyId, salesInvoiceId: salesInvoiceId, page: page, pageSize: pageSize,
            cancellationToken: cancellationToken);

    public Task<VoucherDetailDto> GetAsync(Guid companyId, Guid branchId, Guid voucherId, CancellationToken cancellationToken) =>
        session.Api.GetVoucherAsync(companyId, branchId, voucherId, cancellationToken);

    public Task<VoucherDetailDto> CreateAsync(Guid companyId, Guid branchId, SaveVoucherRequest request, CancellationToken cancellationToken) =>
        session.Api.CreateVoucherAsync(companyId, branchId, request, cancellationToken);

    public Task<VoucherDetailDto> UpdateAsync(Guid companyId, Guid branchId, Guid voucherId, SaveVoucherRequest request, CancellationToken cancellationToken) =>
        session.Api.UpdateVoucherAsync(companyId, branchId, voucherId, request, cancellationToken);

    public Task<DocumentProcessingResultDto> SaveAndProcessAsync(Guid companyId, Guid branchId, SaveAndProcessVoucherRequest request,
        CancellationToken cancellationToken) =>
        session.PostWithOutcomeAsync(companyId, branchId, request.IdempotencyKey, () => session.Api.SaveAndProcessVoucherAsync(companyId, branchId, request, cancellationToken), cancellationToken);

    public Task<DocumentProcessingResultDto> SaveAndProcessUpdateAsync(Guid companyId, Guid branchId, Guid voucherId, SaveAndProcessVoucherRequest request,
        CancellationToken cancellationToken) =>
        session.PostWithOutcomeAsync(companyId, branchId, request.IdempotencyKey, () => session.Api.SaveAndProcessUpdateVoucherAsync(companyId, branchId, voucherId, request, cancellationToken), cancellationToken);

    public Task DeleteAsync(Guid companyId, Guid branchId, Guid voucherId, string expectedVersion, CancellationToken cancellationToken) =>
        session.Api.DeleteVoucherAsync(companyId, branchId, voucherId, expectedVersion, cancellationToken);

    public Task<VoucherDetailDto> PostAsync(Guid companyId, Guid branchId, Guid voucherId, PostVoucherRequest request, CancellationToken cancellationToken) =>
        session.Api.PostVoucherAsync(companyId, branchId, voucherId, request, cancellationToken);

    public Task<VoucherDetailDto> ReverseAsync(Guid companyId, Guid branchId, Guid voucherId, ReverseVoucherRequest request, CancellationToken cancellationToken) =>
        session.Api.ReverseVoucherAsync(companyId, branchId, voucherId, request, cancellationToken);

    public Task<IReadOnlyList<OutstandingInvoiceDto>> OutstandingAsync(Guid companyId, Guid branchId, Guid customerId, string currencyCode,
        CancellationToken cancellationToken) =>
        session.Api.ListOutstandingInvoicesAsync(companyId, branchId, customerId, currencyCode, cancellationToken);
    public Task<SettlementInvoicePageResponse> SettlementInvoicesAsync(Guid companyId, Guid branchId, Guid partyId, VoucherKind kind,
        string? currencyCode, SettlementStateFilter state, int page, int pageSize, CancellationToken cancellationToken) =>
        session.Api.ListPartySettlementInvoicesAsync(companyId, branchId, partyId, kind, currencyCode: currencyCode, state: state,
            page: page, pageSize: pageSize, cancellationToken: cancellationToken);

    public Task<VoucherDetailDto> ApproveAsync(Guid companyId, Guid branchId, Guid voucherId, VoucherActionRequest request, CancellationToken cancellationToken) =>
        session.Api.ApproveVoucherAsync(companyId, branchId, voucherId, request, cancellationToken);

    public Task<VoucherDetailDto> ApplyAdvanceAsync(Guid companyId, Guid branchId, Guid voucherId, ApplyAdvanceRequest request, CancellationToken cancellationToken) =>
        session.Api.ApplyVoucherAdvanceAsync(companyId, branchId, voucherId, request, cancellationToken);

    public Task<VoucherDetailDto> ReverseSettlementAsync(Guid companyId, Guid branchId, Guid voucherId, Guid batchId, VoucherActionRequest request,
        CancellationToken cancellationToken) => session.Api.ReverseVoucherSettlementAsync(companyId, branchId, voucherId, batchId, request, cancellationToken);
}

