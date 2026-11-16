using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Sales;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Sales;

public sealed class SqlSalesDocumentLinkService(FinancialDbContext context) : ISalesDocumentLinkService
{
    public async Task<SalesDocumentLinkPage> GetAsync(Guid companyId, Guid branchId, SalesLinkSource source,
        Guid sourceId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 200 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new RequestValidationException("sales.links.page", "Page must be positive and pageSize must be between 1 and 200.", "page");

        var invoices = context.SalesInvoices.AsNoTracking().Where(i => i.CompanyId == companyId && i.BranchId == branchId);
        var movements = context.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && m.BranchId == branchId);
        bool exists;
        switch (source)
        {
            case SalesLinkSource.Customer:
                exists = await context.Parties.AnyAsync(p => p.CompanyId == companyId && p.Id == sourceId, cancellationToken).ConfigureAwait(false);
                invoices = invoices.Where(i => i.CustomerId == sourceId);
                break;
            case SalesLinkSource.Item:
                exists = await context.Items.AnyAsync(i => i.CompanyId == companyId && i.Id == sourceId, cancellationToken).ConfigureAwait(false);
                invoices = invoices.Where(i => i.Lines.Any(l => l.ItemId == sourceId));
                break;
            case SalesLinkSource.Journal:
                exists = await context.JournalEntries.AnyAsync(j => j.CompanyId == companyId && j.BranchId == branchId && j.Id == sourceId, cancellationToken).ConfigureAwait(false);
                invoices = invoices.Where(i => i.JournalEntryId == sourceId);
                break;
            case SalesLinkSource.Movement:
                var movement = await movements.SingleOrDefaultAsync(m => m.Id == sourceId, cancellationToken).ConfigureAwait(false);
                exists = movement is not null;
                // A document id alone never establishes a Sales relationship (adjustments/transfers use other types).
                invoices = movement is not null && IsSalesMovement(movement)
                    ? invoices.Where(i => i.Id == movement.DocumentId && i.Lines.Any(l => l.ItemId == movement.ItemId))
                    : invoices.Where(i => false);
                break;
            case SalesLinkSource.Invoice:
                exists = await invoices.AnyAsync(i => i.Id == sourceId, cancellationToken).ConfigureAwait(false);
                if (!exists) throw new ResourceNotFoundException("sales invoice");
                var linked = movements.Where(m => m.DocumentId == sourceId &&
                    ((m.DocumentType == "SalesInvoice" && m.MovementKind == (int)StockMovementKind.SalesDelivery) ||
                     (m.DocumentType == "SalesInvoiceReturn" && m.MovementKind == (int)StockMovementKind.SalesReturn) ||
                     (m.DocumentType == "SalesReturn" && m.MovementKind == (int)StockMovementKind.SalesReturn)) &&
                    invoices.Any(i => i.Id == sourceId && i.Lines.Any(l => l.ItemId == m.ItemId)));
                var movementCount = await linked.CountAsync(cancellationToken).ConfigureAwait(false);
                var movementRows = await linked.OrderByDescending(m => m.MovementDateUtc).ThenBy(m => m.Id)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(m => new SalesDocumentLink(m.Id, m.ItemId, m.DocumentNumber, m.MovementDateUtc,
                        context.Items.Where(i => i.CompanyId == companyId && i.Id == m.ItemId).Select(i => i.NameArabic).First(),
                        m.TotalCost, context.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrencyCode).First(),
                        m.BaseQuantity)).ToListAsync(cancellationToken).ConfigureAwait(false);
                return new(page, pageSize, movementCount, movementRows);
            default:
                throw new RequestValidationException("Invalid link source.");
        }
        if (!exists) throw new ResourceNotFoundException("sales link source");
        var count = await invoices.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await invoices.OrderByDescending(i => i.InvoiceDateUtc).ThenBy(i => i.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new SalesDocumentLink(i.Id, null, i.InvoiceNumber, i.InvoiceDateUtc,
                i.Customer!.NameArabic, i.GrandTotal, i.CurrencyCode, null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new(page, pageSize, count, rows);
    }

    private static bool IsSalesMovement(StockMovementRow movement) =>
        (movement.DocumentType == "SalesInvoice" && movement.MovementKind == (int)StockMovementKind.SalesDelivery) ||
        ((movement.DocumentType is "SalesInvoiceReturn" or "SalesReturn") && movement.MovementKind == (int)StockMovementKind.SalesReturn);
}

