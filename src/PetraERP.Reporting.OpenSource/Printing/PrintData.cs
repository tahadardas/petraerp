namespace PetraERP.Reporting.OpenSource.Printing;

/// <summary>A value a template can show: <c>[Key]</c> in a text element, or a column of the lines table.</summary>
public sealed record PrintField(string Key, string Arabic, string English, string Group, string Sample);

/// <summary>
/// The fields each document kind offers to the designer and the renderer. Keys are stable identifiers stored in templates; captions are what
/// the designer lists. <c>Page#</c> and <c>TotalPages#</c> are computed by the renderer.
/// </summary>
public static class PrintFieldCatalog
{
    public const string PageNumber = "Page#";
    public const string TotalPages = "TotalPages#";

    private static readonly PrintField[] Common =
    [
        new("Company.Name", "اسم الشركة", "Company name", "الشركة والسياق", "شركة المثال التجارية"),
        new("Branch.Name", "الفرع", "Branch", "الشركة والسياق", "الفرع الرئيسي"),
        new("FiscalYear.Name", "السنة المالية", "Fiscal year", "الشركة والسياق", "2026"),
        new("User.Name", "المستخدم", "User", "الشركة والسياق", "admin"),
        new("Print.Date", "تاريخ الطباعة", "Print date", "الشركة والسياق", "2026-10-31"),
        new("Doc.Title", "عنوان المستند", "Document title", "المستند", "فاتورة"),
        new("Doc.Number", "رقم المستند", "Document number", "المستند", "1024"),
        new("Doc.Date", "تاريخ المستند", "Document date", "المستند", "2026-10-31"),
        new("Doc.Currency", "العملة", "Currency", "المستند", "USD"),
        new("Doc.Status", "الحالة", "Status", "المستند", "مرحّل"),
        new("Doc.Notes", "ملاحظات", "Notes", "المستند", "ملاحظات المستند"),
    ];

    private static readonly PrintField[] Invoice =
    [
        new("Party.Name", "الزبون / المورد", "Customer / supplier", "المستند", "زبون المثال"),
        new("Party.Code", "رمز الجهة", "Party code", "المستند", "C-0001"),
        new("Doc.PaymentMethod", "طريقة الدفع", "Payment method", "المستند", "آجل"),
        new("Doc.Subtotal", "المجموع", "Subtotal", "الإجماليات", "1,250.00"),
        new("Doc.Discount", "الخصم", "Discount", "الإجماليات", "50.00"),
        new("Doc.Total", "الصافي", "Total", "الإجماليات", "1,200.00"),
        new("Doc.Paid", "المدفوع", "Paid", "الإجماليات", "200.00"),
        new("Doc.Remaining", "المتبقي", "Remaining", "الإجماليات", "1,000.00"),
    ];

    private static readonly PrintField[] Voucher =
    [
        new("Party.Name", "الجهة", "Party", "المستند", "زبون المثال"),
        new("Doc.Account", "الصندوق / الحساب", "Cash / account", "المستند", "الصندوق الرئيسي"),
        new("Doc.Method", "طريقة الدفع", "Method", "المستند", "نقدًا"),
        new("Doc.Amount", "المبلغ", "Amount", "الإجماليات", "1,000.00"),
    ];

    private static readonly PrintField[] InvoiceLines =
    [
        new("Line.No", "م", "No.", "الأسطر", "1"),
        new("Line.ItemCode", "رمز المادة", "Item code", "الأسطر", "A-100"),
        new("Line.ItemName", "اسم المادة", "Item name", "الأسطر", "مادة المثال"),
        new("Line.Unit", "الوحدة", "Unit", "الأسطر", "قطعة"),
        new("Line.Quantity", "الكمية", "Quantity", "الأسطر", "10.00"),
        new("Line.Price", "السعر", "Price", "الأسطر", "125.00"),
        new("Line.Discount", "الحسم", "Discount", "الأسطر", "0.00"),
        new("Line.Total", "المجموع", "Total", "الأسطر", "1,250.00"),
        new("Line.Notes", "ملاحظات", "Notes", "الأسطر", ""),
    ];

    private static readonly PrintField[] VoucherLines =
    [
        new("Line.No", "م", "No.", "الأسطر", "1"),
        new("Line.Invoice", "الفاتورة", "Invoice", "الأسطر", "1024"),
        new("Line.Description", "البيان", "Description", "الأسطر", "تسديد فاتورة"),
        new("Line.Amount", "المبلغ", "Amount", "الأسطر", "1,000.00"),
    ];

    /// <summary>Header, footer and body fields of a document kind (not the line columns).</summary>
    public static IReadOnlyList<PrintField> DocumentFields(PrintDocumentKind kind) =>
        [.. Common, .. kind is PrintDocumentKind.SalesInvoice or PrintDocumentKind.PurchaseInvoice ? Invoice : Voucher];

    /// <summary>The columns the lines table can show for a document kind.</summary>
    public static IReadOnlyList<PrintField> LineFields(PrintDocumentKind kind) =>
        kind is PrintDocumentKind.SalesInvoice or PrintDocumentKind.PurchaseInvoice ? InvoiceLines : VoucherLines;

    /// <summary>Every field key valid in a template of this kind, including the computed page numbers.</summary>
    public static IReadOnlySet<string> AllKeys(PrintDocumentKind kind) =>
        new HashSet<string>(DocumentFields(kind).Concat(LineFields(kind)).Select(field => field.Key).Append(PageNumber).Append(TotalPages), StringComparer.Ordinal);

    /// <summary>A realistic sample document (illustrative values, not data) for the designer's live preview.</summary>
    public static PrintData Sample(PrintDocumentKind kind)
    {
        var fields = DocumentFields(kind).ToDictionary(field => field.Key, field => field.Sample, StringComparer.Ordinal);
        fields["Doc.Title"] = kind switch
        {
            PrintDocumentKind.SalesInvoice => "فاتورة مبيعات",
            PrintDocumentKind.PurchaseInvoice => "فاتورة مشتريات",
            PrintDocumentKind.ReceiptVoucher => "سند قبض",
            _ => "سند دفع",
        };
        var lines = Enumerable.Range(1, 8).Select(index =>
        {
            var line = LineFields(kind).ToDictionary(field => field.Key, field => field.Sample, StringComparer.Ordinal);
            line["Line.No"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return (IReadOnlyDictionary<string, string>)line;
        }).ToArray();
        return new PrintData(fields, lines);
    }
}

/// <summary>
/// The values of one document, already formatted for printing (Latin digits, grouping, dates), keyed by <see cref="PrintField.Key"/>.
/// The renderer never formats or calculates; it prints exactly these strings.
/// </summary>
public sealed record PrintData(IReadOnlyDictionary<string, string> Fields, IReadOnlyList<IReadOnlyDictionary<string, string>> Lines)
{
    public string Field(string key) => Fields.TryGetValue(key, out var value) ? value : string.Empty;
}
