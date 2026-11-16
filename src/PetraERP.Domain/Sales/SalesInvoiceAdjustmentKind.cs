namespace PetraERP.Domain.Sales;

/// <summary>
/// The kind of a document-level adjustment row (Al-Ameen's discounts-and-additions table): a discount lowers what the customer owes and is
/// booked to its own account; an addition raises it and is booked to its own account.
/// </summary>
public enum SalesInvoiceAdjustmentKind
{
    Discount = 1,
    Addition = 2,
}
