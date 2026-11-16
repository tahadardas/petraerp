using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Sales;

/// <summary>A document-level discount or addition with its own account (Al-Ameen's discounts-and-additions table).</summary>
internal sealed class SalesInvoiceAdjustmentRow
{
    public Guid Id { get; set; }
    public Guid SalesInvoiceId { get; set; }

    /// <summary>1 = Discount, 2 = Addition (<see cref="PetraERP.Domain.Sales.SalesInvoiceAdjustmentKind"/>).</summary>
    public int Kind { get; set; }
    public Guid AccountId { get; set; }

    /// <summary>Optional: the row is then booked between <see cref="AccountId"/> and this account, outside the customer's balance and the totals.</summary>
    public Guid? CounterAccountId { get; set; }

    /// <summary>Positive amount in the invoice currency.</summary>
    public decimal Amount { get; set; }

    /// <summary>Informational: the share of the lines' subtotal this row represents when it was entered as a percentage.</summary>
    public decimal Percentage { get; set; }
    public string? Notes { get; set; }
    public Guid? CostCenterId { get; set; }
    public int DisplayOrder { get; set; }

    public SalesInvoiceRow? Invoice { get; set; }
    public AccountRow? Account { get; set; }
}
