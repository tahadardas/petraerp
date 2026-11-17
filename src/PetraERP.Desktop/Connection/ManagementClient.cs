using PetraERP.ApiClient;
using PetraERP.Desktop.Management;

namespace PetraERP.Desktop.Connection;

public sealed class ManagementClient(DesktopSession session) : IManagementClient
{
    public async Task<IReadOnlyList<BranchCardDto>> ListBranchesAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        (await session.Api.ListCompanyBranchesAsync(companyId, branchId, true, cancellationToken).ConfigureAwait(false)).Items ?? [];

    public Task<BranchCardDto> CreateBranchAsync(Guid companyId, Guid branchId, SaveBranchRequest request, CancellationToken cancellationToken) =>
        session.Api.CreateBranchAsync(companyId, branchId, request, cancellationToken);

    public Task<BranchCardDto> UpdateBranchAsync(Guid companyId, Guid branchId, Guid target, UpdateBranchRequest request, CancellationToken cancellationToken) =>
        session.Api.UpdateBranchAsync(companyId, branchId, target, request, cancellationToken);

    public Task<BranchCardDto> SetBranchActiveAsync(Guid companyId, Guid branchId, Guid target, SetBranchActiveRequest request, CancellationToken cancellationToken) =>
        session.Api.SetBranchActiveAsync(companyId, branchId, target, request, cancellationToken);

    public async Task<IReadOnlyList<BranchAccountLinkDto>> ListBranchAccountLinksAsync(Guid companyId, Guid branchId, Guid target, CancellationToken cancellationToken) =>
        (await session.Api.ListBranchAccountLinksAsync(companyId, branchId, target, null, cancellationToken).ConfigureAwait(false)).Items ?? [];

    public Task<BranchAccountLinkDto> SetBranchAccountLinkAsync(Guid companyId, Guid branchId, Guid target, SetBranchAccountLinkRequest request, CancellationToken cancellationToken) =>
        session.Api.SetBranchAccountLinkAsync(companyId, branchId, target, request, cancellationToken);

    public async Task<IReadOnlyList<BranchSubAccountPlanDto>> PreviewBranchSubAccountsAsync(Guid companyId, Guid branchId, Guid target, Guid modelBranchId, CancellationToken cancellationToken) =>
        (await session.Api.PreviewBranchSubAccountsAsync(companyId, branchId, target, modelBranchId, cancellationToken).ConfigureAwait(false)).Items ?? [];

    public Task<BranchSubAccountApplyResponse> ApplyBranchSubAccountsAsync(Guid companyId, Guid branchId, Guid target, ApplyBranchSubAccountsRequest request, CancellationToken cancellationToken) =>
        session.Api.ApplyBranchSubAccountsAsync(companyId, branchId, target, request, cancellationToken);

    public Task<BranchTrialBalanceResponse> GetBranchTrialBalanceAsync(Guid companyId, Guid branchId, DateOnly? fromDate, DateOnly? toDate, string? branchIds, bool eliminate, CancellationToken cancellationToken) =>
        session.Api.GetBranchTrialBalanceAsync(companyId, branchId, fromDate, toDate, branchIds, eliminate, cancellationToken);

    public Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.Api.ListWarehousesAsync(companyId, branchId, null, cancellationToken);

    public async Task<WarehouseDto> CreateWarehouseAsync(Guid companyId, Guid branchId, CreateWarehouseRequest request, CancellationToken cancellationToken)
    {
        var created = await session.Api.CreateWarehouseAsync(companyId, branchId, request, cancellationToken).ConfigureAwait(false);
        session.InvalidateCached(InventoryClient.WarehousesCategory);
        return created;
    }

    public async Task<WarehouseDto> UpdateWarehouseAsync(Guid companyId, Guid branchId, Guid warehouseId, UpdateWarehouseRequest request, CancellationToken cancellationToken)
    {
        var updated = await session.Api.UpdateWarehouseAsync(companyId, branchId, warehouseId, request, cancellationToken).ConfigureAwait(false);
        session.InvalidateCached(InventoryClient.WarehousesCategory);
        return updated;
    }

    public Task<CompanyCurrencySettingsResponse> GetCurrencySettingsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.Api.GetCompanyCurrencySettingsAsync(companyId, branchId, cancellationToken);

    public async Task<IReadOnlyList<DocumentNumberCandidate>> FindDocumentsAsync(Guid companyId, Guid branchId, string family, string? text, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken)
    {
        DateTimeOffset? start = fromDate is { } f ? new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
        DateTimeOffset? end = toDate is { } t ? new DateTimeOffset(t.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero) : null;
        var needle = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        bool Match(string? number) => needle is null || (number ?? string.Empty).Contains(needle, StringComparison.OrdinalIgnoreCase);
        static DateOnly? Day(DateTimeOffset? value) => value is { } v ? DateOnly.FromDateTime(v.UtcDateTime) : null;
        switch (family)
        {
            case "SalesInvoice":
                var sales = await session.Api.ListSalesInvoicesAsync(companyId, branchId, 1, 200, null, null, null, null, start, end, null, needle, null, null, cancellationToken).ConfigureAwait(false);
                return (sales.Items ?? []).Where(i => i.Id is not null).Select(i => new DocumentNumberCandidate(i.Id!.Value, i.InvoiceNumber ?? string.Empty, Day(i.InvoiceDateUtc), i.CustomerName ?? string.Empty, i.Status?.ToString() ?? string.Empty)).ToList();
            case "PurchaseInvoice":
                var purchases = await session.Api.ListPurchaseInvoicesAsync(companyId, branchId, null, null, start, end, null, 1, 200, cancellationToken).ConfigureAwait(false);
                return (purchases.Items ?? []).Where(i => i.Id is not null && Match(i.InvoiceNumber)).Select(i => new DocumentNumberCandidate(i.Id!.Value, i.InvoiceNumber ?? string.Empty, Day(i.InvoiceDateUtc), i.SupplierName ?? string.Empty, string.Empty)).ToList();
            default:
                var vouchers = await session.Api.ListVouchersAsync(companyId, branchId, null, null, null, start, end, null, null, 1, 200, cancellationToken).ConfigureAwait(false);
                return (vouchers.Items ?? []).Where(i => i.Id is not null && Match(i.VoucherNumber)).Select(i => new DocumentNumberCandidate(i.Id!.Value, i.VoucherNumber ?? string.Empty, Day(i.VoucherDateUtc), i.PartyName ?? string.Empty, i.Status?.ToString() ?? string.Empty)).ToList();
        }
    }

    public Task<ExchangeRatePageResponse> ListRatesAsync(Guid companyId, Guid branchId, string? currencyCode, DateOnly? validFrom, DateOnly? validTo, int page, int pageSize, CancellationToken cancellationToken) =>
        session.Api.ListExchangeRatesAsync(companyId, branchId, currencyCode, validFrom, validTo, page: page, pageSize: pageSize, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<ExchangeRateDto>> RegisterRateBulletinAsync(Guid companyId, Guid branchId, RegisterRateBulletinRequest request, CancellationToken cancellationToken) =>
        session.Api.RegisterExchangeRateBulletinAsync(companyId, branchId, request, cancellationToken);

    public Task<DocumentNumberPreviewResponse> PreviewDocumentNumberAsync(Guid companyId, Guid branchId, string family, Guid documentId, string number, CancellationToken cancellationToken) =>
        session.Api.PreviewDocumentNumberChangeAsync(companyId, branchId, family, documentId, number, cancellationToken);

    public Task<DocumentNumberChangeDto> RequestDocumentNumberChangeAsync(Guid companyId, Guid branchId, RequestDocumentNumberChangeRequest request, CancellationToken cancellationToken) =>
        session.Api.RequestDocumentNumberChangeAsync(companyId, branchId, request, cancellationToken);

    public Task<DocumentNumberChangeDto> DecideDocumentNumberChangeAsync(Guid companyId, Guid branchId, Guid requestId, string decision, string reason, CancellationToken cancellationToken)
    {
        var body = new DocumentNumberDecisionRequest { Reason = reason };
        return decision switch
        {
            "approve" => session.Api.ApproveDocumentNumberChangeAsync(companyId, branchId, requestId, body, cancellationToken),
            "reject" => session.Api.RejectDocumentNumberChangeAsync(companyId, branchId, requestId, body, cancellationToken),
            _ => session.Api.CancelDocumentNumberChangeAsync(companyId, branchId, requestId, body, cancellationToken),
        };
    }

    public async Task<IReadOnlyList<DocumentNumberChangeDto>> ListDocumentNumberChangesAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        (await session.Api.ListDocumentNumberChangesAsync(companyId, branchId, null, 200, cancellationToken).ConfigureAwait(false)).Items ?? [];

    public async Task<IReadOnlyList<DocumentNumberAuditDto>> ListDocumentNumberAuditAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        (await session.Api.ListDocumentNumberAuditAsync(companyId, branchId, 200, cancellationToken).ConfigureAwait(false)).Items ?? [];

    public Task<DocumentNumberChainVerificationResponse> VerifyDocumentNumberAuditAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.Api.VerifyDocumentNumberAuditChainAsync(companyId, branchId, cancellationToken);

    public async Task<IReadOnlyList<InterCompanyLinkDto>> ListInterCompanyLinksAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        (await session.Api.ListInterCompanyLinksAsync(companyId, branchId, true, cancellationToken).ConfigureAwait(false)).Items ?? [];

    public Task<InterCompanyLinkDto> SaveInterCompanyLinkAsync(Guid companyId, Guid branchId, SaveInterCompanyLinkRequest request, CancellationToken cancellationToken) =>
        session.Api.SaveInterCompanyLinkAsync(companyId, branchId, request, cancellationToken);

    public Task<InterCompanyLinkDto> SetInterCompanyLinkActiveAsync(Guid companyId, Guid branchId, Guid linkId, bool active, CancellationToken cancellationToken) =>
        session.Api.SetInterCompanyLinkActiveAsync(companyId, branchId, linkId, new SetInterCompanyLinkActiveRequest { IsActive = active }, cancellationToken);

    public Task<InterCompanyReconciliationResponse> ReconcileInterCompanyAsync(Guid companyId, Guid branchId, Guid linkId, DateOnly? asOf, CancellationToken cancellationToken) =>
        session.Api.ReconcileInterCompanyLinkAsync(companyId, branchId, linkId, asOf, cancellationToken);

    public Task<AnalyticalReportResponse> RunAnalyticalReportAsync(Guid companyId, Guid branchId, string reportId, DateOnly? fromDate, DateOnly? toDate, string? currencyCode, Guid? accountId, CancellationToken cancellationToken) =>
        session.Api.RunAnalyticalReportAsync(companyId, branchId, reportId, fromDate, toDate, currencyCode, accountId, null, cancellationToken);

    public Task<ReportMasterCatalogResponse> GetReportCatalogAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.Api.GetReportMasterCatalogAsync(companyId, branchId, null, null, cancellationToken);

    public Task<WarehouseAccountingDto> GetWarehouseAccountingAsync(Guid companyId, Guid branchId, Guid warehouseId, CancellationToken cancellationToken) =>
        session.Api.GetWarehouseAccountingAsync(companyId, branchId, warehouseId, cancellationToken);

    public Task<WarehouseAccountingDto> SetWarehouseAccountingAsync(Guid companyId, Guid branchId, Guid warehouseId, SetWarehouseAccountingRequest request, CancellationToken cancellationToken) =>
        session.Api.SetWarehouseAccountingAsync(companyId, branchId, warehouseId, request, cancellationToken);
}
