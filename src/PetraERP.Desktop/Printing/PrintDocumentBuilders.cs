using System.Globalization;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Reporting.OpenSource.Printing;

namespace PetraERP.Desktop.Printing;

/// <summary>
/// Turns a saved document (the server's authoritative record, never an unsaved draft) into the strings a print template shows. Amounts and
/// quantities use the program's number formatting (Latin digits, grouping), dates are ISO. Nothing is recalculated here.
/// </summary>
public static class PrintDocumentBuilders
{
    /// <summary>The fields of a saved sales invoice and its lines.</summary>
    public static PrintData SalesInvoice(SalesInvoiceDetailDto invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Doc.Title"] = "فاتورة مبيعات",
            ["Doc.Number"] = invoice.InvoiceNumber ?? string.Empty,
            ["Doc.Date"] = Date(invoice.InvoiceDateUtc),
            ["Doc.Currency"] = invoice.CurrencyCode ?? string.Empty,
            ["Doc.Status"] = invoice.Status?.ToString() ?? string.Empty,
            ["Doc.Notes"] = invoice.Notes ?? string.Empty,
            ["Party.Name"] = invoice.CustomerIsCashCustomer == true && string.IsNullOrWhiteSpace(invoice.CustomerName) ? "زبون نقدي" : invoice.CustomerName ?? string.Empty,
            ["Party.Code"] = invoice.CustomerCode ?? string.Empty,
            ["Doc.PaymentMethod"] = invoice.PaymentMethod == PaymentMethod.Cash ? "نقدًا" : invoice.PaymentMethod == PaymentMethod.Credit ? "آجل" : string.Empty,
            ["Doc.Subtotal"] = Money(invoice.SubTotal),
            ["Doc.Discount"] = Money(invoice.DiscountTotal),
            ["Doc.Total"] = Money(invoice.GrandTotal),
            ["Doc.Paid"] = Money(invoice.SettledAmount),
            ["Doc.Remaining"] = Money(invoice.OutstandingAmount),
        };
        var lines = new List<IReadOnlyDictionary<string, string>>();
        var number = 0;
        foreach (var line in (invoice.Lines ?? []).OrderBy(line => line.DisplayOrder ?? int.MaxValue))
        {
            number++;
            lines.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Line.No"] = number.ToString(CultureInfo.InvariantCulture),
                ["Line.ItemCode"] = line.ItemCode ?? string.Empty,
                ["Line.ItemName"] = line.ItemNameArabic ?? string.Empty,
                ["Line.Unit"] = line.UnitCode ?? string.Empty,
                ["Line.Quantity"] = PetraNumberFormat.Format(line.Quantity, NumberKind.Quantity),
                ["Line.Price"] = PetraNumberFormat.Format(line.UnitPrice, NumberKind.Money),
                ["Line.Discount"] = PetraNumberFormat.Format(line.DiscountAmount, NumberKind.Money),
                ["Line.Total"] = PetraNumberFormat.Format(line.LineTotal, NumberKind.Money),
                ["Line.Notes"] = line.Notes ?? string.Empty,
            });
        }

        return new PrintData(fields, lines);
    }

    /// <summary>The fields of a saved purchase invoice and its lines.</summary>
    public static PrintData PurchaseInvoice(PurchaseDetailDto invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Doc.Title"] = "فاتورة مشتريات",
            ["Doc.Number"] = invoice.InvoiceNumber ?? string.Empty,
            ["Doc.Date"] = Date(invoice.InvoiceDateUtc),
            ["Doc.Currency"] = invoice.CurrencyCode ?? string.Empty,
            ["Doc.Status"] = invoice.Status?.ToString() ?? string.Empty,
            ["Doc.Notes"] = invoice.Notes ?? string.Empty,
            ["Party.Name"] = invoice.SupplierName ?? string.Empty,
            ["Party.Code"] = invoice.SupplierCode ?? string.Empty,
            ["Doc.PaymentMethod"] = invoice.PaymentMethod == PaymentMethod.Cash ? "نقدًا" : invoice.PaymentMethod == PaymentMethod.Credit ? "آجل" : string.Empty,
            ["Doc.Subtotal"] = Money(invoice.SubTotal),
            ["Doc.Discount"] = Money(invoice.DiscountTotal),
            ["Doc.Total"] = Money(invoice.GrandTotal),
            ["Doc.Paid"] = Money(invoice.SettledAmount),
            ["Doc.Remaining"] = Money(invoice.OutstandingAmount),
        };
        var lines = new List<IReadOnlyDictionary<string, string>>();
        var number = 0;
        foreach (var line in (invoice.Lines ?? []).OrderBy(line => line.DisplayOrder ?? int.MaxValue))
        {
            number++;
            lines.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Line.No"] = number.ToString(CultureInfo.InvariantCulture),
                ["Line.ItemCode"] = line.ItemCode ?? string.Empty,
                ["Line.ItemName"] = line.ItemName ?? string.Empty,
                ["Line.Unit"] = line.UnitCode ?? string.Empty,
                ["Line.Quantity"] = PetraNumberFormat.Format(line.Quantity, NumberKind.Quantity),
                ["Line.Price"] = PetraNumberFormat.Format(line.UnitCost, NumberKind.Money),
                ["Line.Discount"] = PetraNumberFormat.Format(line.DiscountAmount, NumberKind.Money),
                ["Line.Total"] = PetraNumberFormat.Format(line.LineTotal, NumberKind.Money),
                ["Line.Notes"] = line.Notes ?? string.Empty,
            });
        }

        return new PrintData(fields, lines);
    }

    /// <summary>The kind of document a voucher prints as.</summary>
    public static PrintDocumentKind VoucherKindOf(VoucherDetailDto voucher) =>
        voucher?.Kind == VoucherKind.Payment ? PrintDocumentKind.PaymentVoucher : PrintDocumentKind.ReceiptVoucher;

    /// <summary>The fields of a saved voucher: its invoice settlements first, then its account lines.</summary>
    public static PrintData Voucher(VoucherDetailDto voucher)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        var receipt = voucher.Kind != VoucherKind.Payment;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Doc.Title"] = receipt ? "سند قبض" : "سند دفع",
            ["Doc.Number"] = voucher.VoucherNumber ?? string.Empty,
            ["Doc.Date"] = Date(voucher.VoucherDateUtc),
            ["Doc.Currency"] = voucher.CurrencyCode ?? string.Empty,
            ["Doc.Status"] = voucher.Status?.ToString() ?? string.Empty,
            ["Doc.Notes"] = voucher.Notes ?? string.Empty,
            ["Party.Name"] = voucher.PartyName ?? string.Empty,
            ["Party.Code"] = voucher.PartyCode ?? string.Empty,
            ["Doc.Account"] = (voucher.Lines ?? []).Select(line => line.AccountCode).FirstOrDefault(code => !string.IsNullOrWhiteSpace(code)) ?? string.Empty,
            ["Doc.Method"] = voucher.SettlementMethod == SettlementMethod.Bank ? "بنكي" : voucher.SettlementMethod == SettlementMethod.Cash ? "نقدًا" : string.Empty,
            ["Doc.Amount"] = Money(voucher.Amount),
        };
        var lines = new List<IReadOnlyDictionary<string, string>>();
        var number = 0;
        foreach (var allocation in voucher.Allocations ?? [])
        {
            number++;
            lines.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Line.No"] = number.ToString(CultureInfo.InvariantCulture),
                ["Line.Invoice"] = allocation.InvoiceNumber ?? string.Empty,
                ["Line.Description"] = "تسديد فاتورة " + (allocation.InvoiceNumber ?? string.Empty) + (allocation.InvoiceDateUtc is { } at ? " بتاريخ " + Date(at) : string.Empty),
                ["Line.Amount"] = Money(allocation.Amount),
            });
        }

        foreach (var line in (voucher.Lines ?? []).OrderBy(line => line.DisplayOrder ?? int.MaxValue))
        {
            number++;
            lines.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Line.No"] = number.ToString(CultureInfo.InvariantCulture),
                ["Line.Invoice"] = string.Empty,
                ["Line.Description"] = string.Join(" — ", new[] { line.AccountCode, line.PartyName, line.Description }.Where(text => !string.IsNullOrWhiteSpace(text))),
                ["Line.Amount"] = Money(line.Amount),
            });
        }

        return new PrintData(fields, lines);
    }

    /// <summary>Adds the company, branch, fiscal year, user and print date that every template can show.</summary>
    public static PrintData WithContext(PrintData data, string company, string branch, string fiscalYear, string user, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        var fields = new Dictionary<string, string>(data.Fields, StringComparer.Ordinal)
        {
            ["Company.Name"] = company,
            ["Branch.Name"] = branch,
            ["FiscalYear.Name"] = fiscalYear,
            ["User.Name"] = user,
            ["Print.Date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        return data with { Fields = fields };
    }

    private static string Date(DateTimeOffset? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Money(string? wire) => string.IsNullOrWhiteSpace(wire) ? string.Empty : PetraNumberFormat.Format(wire, NumberKind.Money);
}
