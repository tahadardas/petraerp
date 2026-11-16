using Microsoft.EntityFrameworkCore;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports;

namespace PetraERP.Infrastructure.Reports;

public sealed partial class SqlCommercialReportService
{
    public async Task<SalesAnalysisResult> GetSalesAnalysisAsync(LedgerReportScope scope, DateOnly fromDate, DateOnly toDate, SalesAnalysisGrouping grouping,
        Guid? patternRevisionId, Guid? customerId, Guid? itemId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        EnsureRange(fromDate, toDate);
        if (!Enum.IsDefined(grouping)) throw new Application.Common.RequestValidationException("reports.grouping_invalid", "Unknown grouping.", "groupBy");
        var start = StartUtc(fromDate); var end = EndUtc(toDate);
        var documents = SalesInScope(scope).Where(i => i.JournalEntryId != null && i.InvoiceDateUtc >= start && i.InvoiceDateUtc < end);
        if (patternRevisionId is { } revision) documents = documents.Where(i => i.PatternRevisionId == revision);
        if (customerId is { } customer) documents = documents.Where(i => i.CustomerId == customer);
        var lines = await documents.SelectMany(i => i.Lines.Where(l => itemId == null || l.ItemId == itemId), (i, l) => new
        {
            i.Id, i.JournalEntryId, i.ExchangeRate, Return = i.ReturnOfInvoiceId != null || i.ReversesInvoiceId != null,
            PatternId = i.PatternRevision!.PatternId, PatternCode = i.PatternRevision.Pattern!.Code, PatternName = i.PatternRevision.Pattern.NameArabic,
            i.CustomerId, CustomerCode = i.Customer!.Code, CustomerName = i.Customer.NameArabic,
            l.ItemId, ItemCode = l.Item!.Code, ItemName = l.Item.NameArabic, UnitCode = l.Unit!.Code,
            l.Quantity, l.UnitPrice, l.DiscountAmount, l.TaxAmount, l.PumpCost, l.TransportCost,
        }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var rows = lines.Select(l =>
        {
            var sign = l.Return ? -1m : 1m;
            var net = decimal.Round(l.Quantity * l.UnitPrice, 4, MidpointRounding.AwayFromZero) - l.DiscountAmount;
            (Guid? Id, string Code, string Name, string? Unit) key = grouping switch
            {
                SalesAnalysisGrouping.Pattern => (l.PatternId, l.PatternCode, l.PatternName, null),
                SalesAnalysisGrouping.Customer => (l.CustomerId, l.CustomerCode, l.CustomerName, null),
                _ => (l.ItemId, l.ItemCode, l.ItemName, l.UnitCode),
            };
            return new
            {
                Key = key, l.Id, Quantity = sign * l.Quantity,
                Net = sign * ToBase(net, l.ExchangeRate), Additions = sign * ToBase(l.PumpCost + l.TransportCost, l.ExchangeRate),
                Tax = sign * ToBase(l.TaxAmount, l.ExchangeRate),
            };
        }).GroupBy(x => x.Key).OrderBy(g => g.Key.Code, StringComparer.Ordinal)
            .Select(g => new SalesAnalysisRow(g.Key.Id, g.Key.Code, g.Key.Name, g.Key.Unit,
                grouping == SalesAnalysisGrouping.Item ? g.Sum(x => x.Quantity) : 0m, g.Select(x => x.Id).Distinct().Count(),
                g.Sum(x => x.Net), g.Sum(x => x.Additions), g.Sum(x => x.Tax), g.Sum(x => x.Net + x.Additions + x.Tax)))
            .ToList();

        var netBase = rows.Sum(r => r.NetBase); var additionsBase = rows.Sum(r => r.AdditionsBase); var taxBase = rows.Sum(r => r.TaxBase);
        decimal? glRevenue = null;
        if (itemId is null)
        {
            var journals = lines.Select(l => l.JournalEntryId!.Value).Distinct().ToList();
            glRevenue = await context.JournalLines.AsNoTracking()
                .Where(l => l.CompanyId == scope.CompanyId && journals.Contains(l.JournalId)
                    && context.Accounts.Any(a => a.Id == l.AccountId && a.AccountType == Revenue))
                .SumAsync(l => l.BaseCredit - l.BaseDebit, cancellationToken).ConfigureAwait(false);
        }
        return new(fromDate, toDate, grouping, await BaseCurrencyAsync(scope.CompanyId, cancellationToken).ConfigureAwait(false), rows, netBase, additionsBase, taxBase,
            netBase + additionsBase + taxBase, glRevenue, glRevenue - (netBase + additionsBase));
    }

    public async Task<PurchaseAnalysisResult> GetPurchaseAnalysisAsync(LedgerReportScope scope, DateOnly fromDate, DateOnly toDate, PurchaseAnalysisGrouping grouping,
        Guid? patternRevisionId, Guid? supplierId, Guid? itemId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        EnsureRange(fromDate, toDate);
        if (!Enum.IsDefined(grouping)) throw new Application.Common.RequestValidationException("reports.grouping_invalid", "Unknown grouping.", "groupBy");
        var start = StartUtc(fromDate); var end = EndUtc(toDate);
        var documents = PurchasesInScope(scope).Where(i => i.JournalEntryId != null && i.InvoiceDateUtc >= start && i.InvoiceDateUtc < end);
        if (patternRevisionId is { } revision) documents = documents.Where(i => i.PatternRevisionId == revision);
        if (supplierId is { } supplier) documents = documents.Where(i => i.SupplierId == supplier);
        var lines = await documents.SelectMany(i => i.Lines.Where(l => itemId == null || l.ItemId == itemId), (i, l) => new
        {
            i.Id, LineId = l.Id, i.ExchangeRate, Return = i.ReturnOfInvoiceId != null || i.ReversesInvoiceId != null,
            PatternId = i.PatternRevision!.PatternId, PatternCode = i.PatternRevision.Pattern!.Code, PatternName = i.PatternRevision.Pattern.NameArabic,
            i.SupplierId, SupplierCode = i.Supplier!.Code, SupplierName = i.Supplier.NameArabic,
            l.ItemId, ItemCode = l.Item!.Code, ItemName = l.Item.NameArabic, BaseUnitCode = l.Item.BaseUnit!.Code,
            l.Quantity, l.UnitCost, l.DiscountAmount, l.TaxAmount,
        }).ToListAsync(cancellationToken).ConfigureAwait(false);

        // Base quantities and stock cost come from the authoritative receipt/return movements of each line (ton purchases count in kg).
        var lineIds = lines.Select(l => (Guid?)l.LineId).ToList();
        var stock = await context.StockMovements.AsNoTracking()
            .Where(m => m.CompanyId == scope.CompanyId && lineIds.Contains(m.DocumentLineId)
                && (m.DocumentType == Purchases.SqlPurchaseInvoiceService.InvoiceDocumentType || m.DocumentType == Purchases.SqlPurchaseInvoiceService.ReturnDocumentType))
            .GroupBy(m => m.DocumentLineId!.Value)
            .Select(g => new { g.Key, Quantity = g.Sum(m => m.BaseQuantity), Cost = g.Sum(m => m.BaseQuantity < 0 ? -m.TotalCost : m.TotalCost) })
            .ToDictionaryAsync(g => g.Key, cancellationToken).ConfigureAwait(false);

        var rows = lines.Select(l =>
        {
            var sign = l.Return ? -1m : 1m;
            var net = decimal.Round(l.Quantity * l.UnitCost, 4, MidpointRounding.AwayFromZero) - l.DiscountAmount;
            (Guid? Id, string Code, string Name, string? Unit) key = grouping switch
            {
                PurchaseAnalysisGrouping.Pattern => (l.PatternId, l.PatternCode, l.PatternName, null),
                PurchaseAnalysisGrouping.Supplier => (l.SupplierId, l.SupplierCode, l.SupplierName, null),
                _ => (l.ItemId, l.ItemCode, l.ItemName, l.BaseUnitCode),
            };
            stock.TryGetValue(l.LineId, out var movement);
            return new
            {
                Key = key, l.Id, BaseQuantity = movement?.Quantity ?? 0m, Cost = movement?.Cost ?? 0m,
                Net = sign * ToBase(net, l.ExchangeRate), Tax = sign * ToBase(l.TaxAmount, l.ExchangeRate),
            };
        }).GroupBy(x => x.Key).OrderBy(g => g.Key.Code, StringComparer.Ordinal)
            .Select(g => new PurchaseAnalysisRow(g.Key.Id, g.Key.Code, g.Key.Name, g.Key.Unit,
                grouping == PurchaseAnalysisGrouping.Item ? g.Sum(x => x.BaseQuantity) : 0m, g.Select(x => x.Id).Distinct().Count(),
                g.Sum(x => x.Net), g.Sum(x => x.Tax), g.Sum(x => x.Net + x.Tax), g.Sum(x => x.Cost)))
            .ToList();
        return new(fromDate, toDate, grouping, await BaseCurrencyAsync(scope.CompanyId, cancellationToken).ConfigureAwait(false), rows, rows.Sum(r => r.NetBase),
            rows.Sum(r => r.TaxBase), rows.Sum(r => r.TotalBase), rows.Sum(r => r.StockCostBase));
    }

    /// <summary>Document money in base currency with the ledger's conversion rule (4 decimals, banker's rounding).</summary>
    private static decimal ToBase(decimal amount, decimal rate) => decimal.Round(amount * rate, 4, MidpointRounding.ToEven);
}
