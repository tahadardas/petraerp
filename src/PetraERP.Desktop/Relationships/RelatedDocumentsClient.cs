using PetraERP.ApiClient;

namespace PetraERP.Desktop.Relationships;

public enum RelatedDocumentSource { SupplierPurchases, CustomerReceipts, SupplierPayments, InvoiceReceipts, JournalVouchers }

public sealed record RelatedDocumentRow(Guid Id, string Number, DateTimeOffset? DateUtc, string Amount, string CurrencyCode, string State);
public sealed record RelatedDocumentPage(int Page, int TotalCount, IReadOnlyList<RelatedDocumentRow> Items);

public interface IRelatedDocumentsClient
{
    Task<RelatedDocumentPage> ListAsync(Guid companyId, Guid branchId, RelatedDocumentSource source, Guid sourceId,
        int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>Projects existing server-paged purchase/voucher contracts. No balances or accounting effects are computed here.</summary>
public sealed class RelatedDocumentsClient(Connection.DesktopSession session) : IRelatedDocumentsClient
{
    public async Task<RelatedDocumentPage> ListAsync(Guid companyId, Guid branchId, RelatedDocumentSource source, Guid sourceId,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        if (source == RelatedDocumentSource.SupplierPurchases)
        {
            var result = await session.Api.ListPurchaseInvoicesAsync(companyId, branchId, supplierId: sourceId,
                page: page, pageSize: pageSize, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new(result.Page ?? page, result.TotalCount ?? 0, (result.Items ?? []).Where(i => i.Id.HasValue)
                .Select(i => new RelatedDocumentRow(i.Id!.Value, i.InvoiceNumber ?? i.Id.Value.ToString(), i.InvoiceDateUtc,
                    i.GrandTotal ?? string.Empty, i.CurrencyCode ?? string.Empty, i.Status?.ToString() ?? string.Empty)).ToArray());
        }

        var vouchers = await session.Api.ListVouchersAsync(companyId, branchId,
            kind: source switch
            {
                RelatedDocumentSource.CustomerReceipts or RelatedDocumentSource.InvoiceReceipts => VoucherKind.Receipt,
                RelatedDocumentSource.SupplierPayments => VoucherKind.Payment,
                _ => null,
            },
            partyId: source is RelatedDocumentSource.CustomerReceipts or RelatedDocumentSource.SupplierPayments ? sourceId : null,
            salesInvoiceId: source == RelatedDocumentSource.InvoiceReceipts ? sourceId : null,
            journalEntryId: source == RelatedDocumentSource.JournalVouchers ? sourceId : null,
            page: page, pageSize: pageSize, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new(vouchers.Page ?? page, vouchers.TotalCount ?? 0, (vouchers.Items ?? []).Where(v => v.Id.HasValue)
            .Select(v => new RelatedDocumentRow(v.Id!.Value, v.VoucherNumber ?? v.Id.Value.ToString(), v.VoucherDateUtc,
                v.Amount ?? string.Empty, v.CurrencyCode ?? string.Empty, v.Status?.ToString() ?? string.Empty)).ToArray());
    }
}
