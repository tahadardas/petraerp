using System.IO;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Printing;
using PetraERP.Reporting.OpenSource.Printing;

namespace PetraERP.Desktop.Tests;

/// <summary>Printing of saved documents: the builder prints the server's strings, the view model previews through the chosen template.</summary>
public sealed class PrintingTests
{
    internal static SalesInvoiceDetailDto SampleInvoice() => new()
    {
        Id = Guid.NewGuid(), InvoiceNumber = "1024", InvoiceDateUtc = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero), CurrencyCode = "USD",
        CustomerName = "زبون المثال", CustomerCode = "C-1", PaymentMethod = PaymentMethod.Credit, Status = SalesInvoiceStatus.Approved, Notes = "ملاحظة",
        SubTotal = "1250.0000", DiscountTotal = "50.0000", GrandTotal = "1200.0000", SettledAmount = "200.0000", OutstandingAmount = "1000.0000",
        Lines =
        [
            new SalesInvoiceLineDto { DisplayOrder = 2, ItemCode = "B", ItemNameArabic = "مادة ب", UnitCode = "قطعة", Quantity = "3.0000", UnitPrice = "10.5000", DiscountAmount = "0.0000", LineTotal = "31.5000" },
            new SalesInvoiceLineDto { DisplayOrder = 1, ItemCode = "A", ItemNameArabic = "مادة أ", UnitCode = "كغ", Quantity = "1234.5000", UnitPrice = "1.0000", DiscountAmount = "2.0000", LineTotal = "1232.5000" },
        ],
    };

    private static readonly string[] AmountFields = ["Doc.Subtotal", "Doc.Discount", "Doc.Total", "Doc.Paid", "Doc.Remaining"];

    [Fact]
    public void SalesInvoiceBuilderPrintsTheServersRecordInLineOrderWithGroupedLatinNumbers()
    {
        var data = PrintDocumentBuilders.SalesInvoice(SampleInvoice());
        Assert.Equal("1024", data.Field("Doc.Number"));
        Assert.Equal("2026-10-31", data.Field("Doc.Date"));
        Assert.Equal("آجل", data.Field("Doc.PaymentMethod"));
        Assert.Equal("زبون المثال", data.Field("Party.Name"));
        Assert.Equal(2, data.Lines.Count);
        Assert.Equal("1", data.Lines[0]["Line.No"]);
        Assert.Equal("A", data.Lines[0]["Line.ItemCode"]);
        Assert.Equal("B", data.Lines[1]["Line.ItemCode"]);
        Assert.Matches("^[0-9,.]+$", data.Lines[0]["Line.Quantity"]);
        Assert.Contains("1,234.5", data.Lines[0]["Line.Quantity"], StringComparison.Ordinal);
        Assert.All(AmountFields, key => Assert.Matches("^[0-9,.]+$", data.Field(key)));
    }

    [Fact]
    public void ContextFieldsAreAddedWithoutTouchingTheDocumentValues()
    {
        var data = PrintDocumentBuilders.SalesInvoice(SampleInvoice());
        var withContext = PrintDocumentBuilders.WithContext(data, "شركة", "الفرع الرئيسي", "2026", "admin", new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero));
        Assert.Equal("الفرع الرئيسي", withContext.Field("Branch.Name"));
        Assert.Equal("2026", withContext.Field("FiscalYear.Name"));
        Assert.Equal("2026-10-31", withContext.Field("Print.Date"));
        Assert.Equal(data.Field("Doc.Total"), withContext.Field("Doc.Total"));
        Assert.False(data.Fields.ContainsKey("Company.Name"));
    }

    [Fact]
    public async Task ViewModelRendersThePreviewAndFollowsTheChosenTemplate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "petra-print-vm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var company = Guid.NewGuid();
            var store = new PrintTemplateStore(directory);
            var data = PrintDocumentBuilders.WithContext(PrintDocumentBuilders.SalesInvoice(SampleInvoice()), "شركة", "فرع", "2026", "admin", DateTimeOffset.UtcNow);
            var custom = store.Save(company, PrintTemplateStore.Copy(DefaultPrintTemplates.For(PrintDocumentKind.SalesInvoice) with { Page = new PrintPageSettings { Landscape = true } }, "أفقي"));
            await WpfApplicationHost.RunAsync(async () =>
            {
                var model = new DocumentPrintViewModel(new PrintJob(PrintDocumentKind.SalesInvoice, data, "فاتورة"), store, company, new LocalizationService("ar"));
                await model.RenderAsync().ConfigureAwait(true);
                Assert.Empty(model.Error);
                var portrait = Assert.Single(model.Pages);
                Assert.True(portrait.HeightDip > portrait.WidthDip);
                model.ReloadTemplates(custom.Id);
                await model.RenderAsync().ConfigureAwait(true);
                var landscape = Assert.Single(model.Pages);
                Assert.True(landscape.WidthDip > landscape.HeightDip);
                model.SetSelectedAsDefault();
                Assert.True(model.IsDefaultTemplate);
                Assert.Equal(custom.Id, store.Resolve(company, PrintDocumentKind.SalesInvoice).Id);
            }, TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
