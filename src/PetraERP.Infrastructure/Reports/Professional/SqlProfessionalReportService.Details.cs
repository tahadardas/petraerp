using Microsoft.EntityFrameworkCore;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports.Professional;

namespace PetraERP.Infrastructure.Reports.Professional;

public sealed partial class SqlProfessionalReportService
{
    private async Task<IReadOnlyList<ProfessionalInvoiceDetailDto>> DetailsAsync(LedgerReportScope scope, ProfessionalReportRequest request,
        IQueryable<PartyEffect> filtered, CancellationToken cancellationToken)
    {
        var ids = filtered.Where(e => e.Source == ProfessionalReportSource.SalesInvoice || e.Source == ProfessionalReportSource.SalesReturn
            || e.Source == ProfessionalReportSource.PurchaseInvoice || e.Source == ProfessionalReportSource.PurchaseReturn).Select(e => e.DocumentId);
        if (request.Kind == ProfessionalReportKind.CombinedPartyStatement)
        {
            var sales = context.SalesInvoiceLines.AsNoTracking().Where(l => ids.Contains(l.SalesInvoiceId) && l.Invoice!.CompanyId == scope.CompanyId && l.Invoice.BranchId == scope.BranchId);
            var purchases = context.PurchaseInvoiceLines.AsNoTracking().Where(l => ids.Contains(l.PurchaseInvoiceId) && l.Invoice!.CompanyId == scope.CompanyId && l.Invoice.BranchId == scope.BranchId);
            if (await sales.LongCountAsync(cancellationToken).ConfigureAwait(false) + await purchases.LongCountAsync(cancellationToken).ConfigureAwait(false) > ProfessionalReportCatalog.MaximumRows) throw TooLarge();
            var salesRows = await sales.OrderBy(l => l.SalesInvoiceId).ThenBy(l => l.DisplayOrder).Select(l => new ProfessionalInvoiceDetailDto(
                l.SalesInvoiceId, l.Id, l.Invoice!.ReturnOfInvoiceId != null || l.Invoice.ReversesInvoiceId != null ? ProfessionalReportSource.SalesReturn : ProfessionalReportSource.SalesInvoice,
                l.Invoice.InvoiceNumber, l.Item!.Code, l.Item.NameArabic, l.Unit!.Code, l.Quantity, l.UnitPrice, l.DiscountAmount, l.TaxAmount, l.LineTotal, l.Invoice.CurrencyCode))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var purchaseRows = await purchases.OrderBy(l => l.PurchaseInvoiceId).ThenBy(l => l.DisplayOrder).Select(l => new ProfessionalInvoiceDetailDto(
                l.PurchaseInvoiceId, l.Id, l.Invoice!.ReturnOfInvoiceId != null || l.Invoice.ReversesInvoiceId != null ? ProfessionalReportSource.PurchaseReturn : ProfessionalReportSource.PurchaseInvoice,
                l.Invoice.InvoiceNumber, l.Item!.Code, l.Item.NameArabic,
                context.UnitsOfMeasure.Where(u => u.Id == l.UnitId && u.CompanyId == scope.CompanyId).Select(u => u.Code).First(),
                l.Quantity, l.UnitCost, l.DiscountAmount, l.TaxAmount, l.LineTotal, l.Invoice.CurrencyCode))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return [.. salesRows, .. purchaseRows];
        }
        else if (request.Kind == ProfessionalReportKind.CustomerStatement)
        {
            var query = context.SalesInvoiceLines.AsNoTracking().Where(l => ids.Contains(l.SalesInvoiceId) && l.Invoice!.CompanyId == scope.CompanyId && l.Invoice.BranchId == scope.BranchId);
            if (await query.LongCountAsync(cancellationToken).ConfigureAwait(false) > ProfessionalReportCatalog.MaximumRows) throw TooLarge();
            return await query.OrderBy(l => l.SalesInvoiceId).ThenBy(l => l.DisplayOrder).Select(l => new ProfessionalInvoiceDetailDto(
                l.SalesInvoiceId, l.Id, l.Invoice!.ReturnOfInvoiceId != null || l.Invoice.ReversesInvoiceId != null ? ProfessionalReportSource.SalesReturn : ProfessionalReportSource.SalesInvoice,
                l.Invoice.InvoiceNumber, l.Item!.Code, l.Item.NameArabic, l.Unit!.Code, l.Quantity, l.UnitPrice, l.DiscountAmount, l.TaxAmount, l.LineTotal, l.Invoice.CurrencyCode))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var query = context.PurchaseInvoiceLines.AsNoTracking().Where(l => ids.Contains(l.PurchaseInvoiceId) && l.Invoice!.CompanyId == scope.CompanyId && l.Invoice.BranchId == scope.BranchId);
            if (await query.LongCountAsync(cancellationToken).ConfigureAwait(false) > ProfessionalReportCatalog.MaximumRows) throw TooLarge();
            return await query.OrderBy(l => l.PurchaseInvoiceId).ThenBy(l => l.DisplayOrder).Select(l => new ProfessionalInvoiceDetailDto(
                l.PurchaseInvoiceId, l.Id, l.Invoice!.ReturnOfInvoiceId != null || l.Invoice.ReversesInvoiceId != null ? ProfessionalReportSource.PurchaseReturn : ProfessionalReportSource.PurchaseInvoice,
                l.Invoice.InvoiceNumber, l.Item!.Code, l.Item.NameArabic,
                context.UnitsOfMeasure.Where(u => u.Id == l.UnitId && u.CompanyId == scope.CompanyId).Select(u => u.Code).First(),
                l.Quantity, l.UnitCost, l.DiscountAmount, l.TaxAmount, l.LineTotal, l.Invoice.CurrencyCode))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
