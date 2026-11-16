using PetraERP.Application.Common;
using PetraERP.Application.Sales;
using PetraERP.Domain.Sales;

namespace PetraERP.Infrastructure.Sales;

/// <summary>
/// Document-level discounts and additions (Al-Ameen's discounts-and-additions table). Each row names its own account; its amount
/// joins the invoice's discount or additions total so the grand total, the customer's open item and every report stay consistent
/// with the journal the posting builds from the same rows.
/// </summary>
public sealed partial class SqlSalesInvoiceService
{
    private sealed record BuiltAdjustments(List<SalesInvoiceAdjustmentRow> Rows, decimal Discounts, decimal Additions);

    /// <summary>Validates the supplied rows against the company's chart of accounts and returns the stored rows with their totals.</summary>
    private async Task<BuiltAdjustments> BuildAdjustmentsAsync(Guid companyId, Guid invoiceId, string invoiceCurrency,
        IReadOnlyList<CreateSalesInvoiceAdjustmentCommand>? commands, CancellationToken cancellationToken)
    {
        var rows = new List<SalesInvoiceAdjustmentRow>();
        if (commands is null || commands.Count == 0) return new(rows, 0m, 0m);
        await InvoiceAdjustmentRules.ValidateAsync(context, companyId, invoiceCurrency, "sales",
            commands.Select(c => new AdjustmentInput(c.Kind, c.AccountId, c.CounterAccountId, c.Amount, c.Percentage, c.CostCenterId)).ToList(), cancellationToken).ConfigureAwait(false);
        var order = 0;
        foreach (var command in commands)
        {
            rows.Add(new SalesInvoiceAdjustmentRow
            {
                Id = Guid.NewGuid(), SalesInvoiceId = invoiceId, Kind = (int)command.Kind, AccountId = command.AccountId, CounterAccountId = command.CounterAccountId,
                Amount = command.Amount, Percentage = command.Percentage, Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim(),
                CostCenterId = command.CostCenterId, DisplayOrder = command.DisplayOrder != 0 ? command.DisplayOrder : ++order,
            });
        }

        // Rows with a counter account are booked between two accounts and never touch the customer's balance or the totals.
        var counted = rows.Where(r => r.CounterAccountId is null).ToList();
        return new(rows,
            counted.Where(r => r.Kind == (int)SalesInvoiceAdjustmentKind.Discount).Sum(r => r.Amount),
            counted.Where(r => r.Kind == (int)SalesInvoiceAdjustmentKind.Addition).Sum(r => r.Amount));
    }

    private static decimal AdjustmentDiscounts(SalesInvoiceRow invoice) => invoice.Adjustments.Where(a => a.CounterAccountId is null && a.Kind == (int)SalesInvoiceAdjustmentKind.Discount).Sum(a => a.Amount);

    private static decimal AdjustmentAdditions(SalesInvoiceRow invoice) => invoice.Adjustments.Where(a => a.CounterAccountId is null && a.Kind == (int)SalesInvoiceAdjustmentKind.Addition).Sum(a => a.Amount);

    private static void RequireNonNegativeTotal(decimal grandTotal)
    {
        if (grandTotal < 0m)
            throw new BusinessRuleException("sales.adjustments_exceed_total", "The discounts exceed the invoice total.", "adjustments");
    }

    private static SalesInvoiceAdjustmentResult MapAdjustment(SalesInvoiceAdjustmentRow row) =>
        new(row.Id, (SalesInvoiceAdjustmentKind)row.Kind, row.AccountId, row.Account?.Code ?? string.Empty, row.Account?.NameArabic ?? string.Empty,
            row.Amount, row.Percentage, row.Notes, row.CostCenterId, row.DisplayOrder) { CounterAccountId = row.CounterAccountId };
}
