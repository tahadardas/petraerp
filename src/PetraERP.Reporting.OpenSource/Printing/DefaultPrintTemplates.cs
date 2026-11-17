namespace PetraERP.Reporting.OpenSource.Printing;

/// <summary>
/// The built-in templates used until a company designs its own. They are ordinary templates (the designer can open, change and save them
/// under another name); nothing in the renderer depends on them.
/// </summary>
public static class DefaultPrintTemplates
{
    /// <summary>The identifier of the built-in template of a kind (stable, so "default" survives updates).</summary>
    public static string IdFor(PrintDocumentKind kind) => "builtin-" + kind.ToString().ToUpperInvariant();

    public static PrintTemplate For(PrintDocumentKind kind) => kind switch
    {
        PrintDocumentKind.SalesInvoice => Invoice(kind, "نمط فاتورة مبيعات افتراضي", "الزبون"),
        PrintDocumentKind.PurchaseInvoice => Invoice(kind, "نمط فاتورة مشتريات افتراضي", "المورد"),
        PrintDocumentKind.ReceiptVoucher => Voucher(kind, "نمط سند قبض افتراضي", "استلمنا من"),
        _ => Voucher(kind, "نمط سند دفع افتراضي", "دفعنا إلى"),
    };

    private static PrintTemplate Invoice(PrintDocumentKind kind, string name, string party) => new()
    {
        Id = IdFor(kind), Name = name, Kind = kind,
        PageHeader = Header(),
        DocumentHeader = new PrintBand
        {
            HeightMm = 32,
            Elements =
            [
                Text("[Doc.Title]", 0, 0, 190, 9, 15, bold: true, align: PrintAlign.Center, color: "#1E3A8A"),
                Text("رقم المستند: [Doc.Number]", 100, 12, 90, 7, 10, bold: true),
                Text("التاريخ: [Doc.Date]", 0, 12, 90, 7, 10, align: PrintAlign.Left),
                Text(party + ": [Party.Name]", 100, 20, 90, 7, 10),
                Text("العملة: [Doc.Currency]    الدفع: [Doc.PaymentMethod]", 0, 20, 98, 7, 10, align: PrintAlign.Left),
            ],
        },
        Table = new PrintTable
        {
            HeaderFill = "#F1F5F9",
            Borders = true,
            Columns =
            [
                new("Line.No", "م", 10, PrintAlign.Center),
                new("Line.ItemCode", "الرمز", 25, PrintAlign.Right),
                new("Line.ItemName", "المادة", 63, PrintAlign.Right),
                new("Line.Unit", "الوحدة", 18, PrintAlign.Center),
                new("Line.Quantity", "الكمية", 20, PrintAlign.Center),
                new("Line.Price", "السعر", 26, PrintAlign.Center),
                new("Line.Total", "المجموع", 28, PrintAlign.Center),
            ],
        },
        DocumentFooter = new PrintBand
        {
            HeightMm = 46,
            Elements =
            [
                Text("المجموع: [Doc.Subtotal]", 0, 2, 70, 7, 10, align: PrintAlign.Left),
                Text("الخصم: [Doc.Discount]", 0, 9, 70, 7, 10, align: PrintAlign.Left),
                Text("الصافي: [Doc.Total]", 0, 16, 70, 8, 11, bold: true, align: PrintAlign.Left, color: "#1E3A8A"),
                Text("المدفوع: [Doc.Paid]    المتبقي: [Doc.Remaining]", 0, 24, 90, 7, 9, align: PrintAlign.Left),
                Text("ملاحظات: [Doc.Notes]", 100, 2, 90, 20, 9, color: "#475569"),
                Text("المستلم", 140, 36, 50, 7, 9, align: PrintAlign.Center, color: "#475569"),
                Text("المحاسب", 70, 36, 50, 7, 9, align: PrintAlign.Center, color: "#475569"),
                Line(140, 35, 50, "#94A3B8"),
                Line(70, 35, 50, "#94A3B8"),
            ],
        },
        PageFooter = Footer(),
    };

    private static PrintTemplate Voucher(PrintDocumentKind kind, string name, string party) => new()
    {
        Id = IdFor(kind), Name = name, Kind = kind,
        PageHeader = Header(),
        DocumentHeader = new PrintBand
        {
            HeightMm = 40,
            Elements =
            [
                Text("[Doc.Title]", 0, 0, 190, 9, 15, bold: true, align: PrintAlign.Center, color: "#1E3A8A"),
                Text("رقم السند: [Doc.Number]", 100, 12, 90, 7, 10, bold: true),
                Text("التاريخ: [Doc.Date]", 0, 12, 90, 7, 10, align: PrintAlign.Left),
                Text(party + ": [Party.Name]", 100, 20, 90, 7, 10),
                Text("الصندوق / الحساب: [Doc.Account]", 0, 20, 98, 7, 10, align: PrintAlign.Left),
                Text("طريقة الدفع: [Doc.Method]", 100, 28, 90, 7, 10),
                Text("المبلغ: [Doc.Amount] [Doc.Currency]", 0, 28, 98, 8, 12, bold: true, align: PrintAlign.Left, color: "#1E3A8A"),
            ],
        },
        Table = new PrintTable
        {
            HeaderFill = "#F1F5F9",
            Borders = true,
            Columns =
            [
                new("Line.No", "م", 10, PrintAlign.Center),
                new("Line.Invoice", "الفاتورة", 30, PrintAlign.Center),
                new("Line.Description", "البيان", 110, PrintAlign.Right),
                new("Line.Amount", "المبلغ", 40, PrintAlign.Center),
            ],
        },
        DocumentFooter = new PrintBand
        {
            HeightMm = 40,
            Elements =
            [
                Text("ملاحظات: [Doc.Notes]", 100, 2, 90, 16, 9, color: "#475569"),
                Text("المستلم", 140, 32, 50, 7, 9, align: PrintAlign.Center, color: "#475569"),
                Text("المحاسب", 75, 32, 50, 7, 9, align: PrintAlign.Center, color: "#475569"),
                Text("المدير", 10, 32, 50, 7, 9, align: PrintAlign.Center, color: "#475569"),
                Line(140, 31, 50, "#94A3B8"),
                Line(75, 31, 50, "#94A3B8"),
                Line(10, 31, 50, "#94A3B8"),
            ],
        },
        PageFooter = Footer(),
    };

    private static PrintBand Header() => new()
    {
        HeightMm = 28,
        Elements =
        [
            Text("[Company.Name]", 0, 2, 190, 10, 16, bold: true, color: "#1E3A8A"),
            Text("الفرع: [Branch.Name]     السنة المالية: [FiscalYear.Name]", 0, 13, 190, 7, 9, color: "#475569"),
            Line(0, 25, 190, "#1E3A8A"),
        ],
    };

    private static PrintBand Footer() => new()
    {
        HeightMm = 12,
        Elements =
        [
            Line(0, 0, 190, "#E2E8F0"),
            Text("[User.Name] — [Print.Date]", 0, 2, 90, 7, 8, align: PrintAlign.Left, color: "#64748B"),
            Text("صفحة [Page#] من [TotalPages#]", 100, 2, 90, 7, 8, align: PrintAlign.Right, color: "#64748B"),
        ],
    };

    private static PrintElement Text(string text, double x, double y, double width, double height, double size, bool bold = false, PrintAlign align = PrintAlign.Right, string color = "#000000") => new()
    {
        Type = PrintElementType.Text, Text = text, XMm = x, YMm = y, WidthMm = width, HeightMm = height, FontSize = size, Bold = bold, Align = align, Color = color,
    };

    private static PrintElement Line(double x, double y, double width, string color = "#7A8A93") => new()
    {
        Type = PrintElementType.Line, XMm = x, YMm = y, WidthMm = width, HeightMm = 0, Color = color,
    };
}
