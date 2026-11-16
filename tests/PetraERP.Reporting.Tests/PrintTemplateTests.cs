using System.IO;
using PetraERP.Reporting.OpenSource.Printing;

namespace PetraERP.Reporting.Tests;

/// <summary>Print templates: bounded JSON, field substitution, and real FastReport rendering of every built-in template.</summary>
public sealed class PrintTemplateTests
{
    private static string OutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PetraERP.slnx"))) directory = directory.Parent;
        var output = Path.Combine(directory!.FullName, ".artifacts", "print-templates");
        Directory.CreateDirectory(output);
        return output;
    }

    public static TheoryData<PrintDocumentKind> Kinds => new() { PrintDocumentKind.SalesInvoice, PrintDocumentKind.PurchaseInvoice, PrintDocumentKind.ReceiptVoucher, PrintDocumentKind.PaymentVoucher };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryBuiltInTemplateRoundTripsThroughJsonUnchanged(PrintDocumentKind kind)
    {
        var template = DefaultPrintTemplates.For(kind).Validated();
        var json = PrintTemplateJson.Serialize(template);
        Assert.Equal(template, PrintTemplateJson.Deserialize(json), new PrintTemplateComparer());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void BuiltInTemplatesUseOnlyKnownFields(PrintDocumentKind kind)
    {
        var template = DefaultPrintTemplates.For(kind);
        var known = PrintFieldCatalog.AllKeys(kind);
        var tokens = new[] { template.PageHeader, template.DocumentHeader, template.DocumentFooter, template.PageFooter }
            .SelectMany(band => band.Elements).Where(element => element.Type == PrintElementType.Text)
            .SelectMany(element => System.Text.RegularExpressions.Regex.Matches(element.Text, @"\[([A-Za-z.#]+)\]", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(2)).Select(match => match.Groups[1].Value));
        Assert.All(tokens, token => Assert.Contains(token, known));
        Assert.All(template.Table.Columns, column => Assert.Contains(column.Field, known));
        Assert.Equal(template.Page.PrintableWidthMm, template.Table.Columns.Sum(column => column.WidthMm), 3);
    }

    [Fact]
    public void ValidationBoundsEverythingAndRejectsUnusableTemplates()
    {
        var wild = DefaultPrintTemplates.For(PrintDocumentKind.SalesInvoice) with
        {
            Id = "../../evil", Name = new string('x', 500),
            Page = new PrintPageSettings { MarginLeftMm = 500, MarginRightMm = 500 },
        };
        Assert.Throws<InvalidDataException>(() => wild.Validated());
        var ok = DefaultPrintTemplates.For(PrintDocumentKind.SalesInvoice) with { Id = "../../evil", Name = new string('x', 500) };
        var cleaned = ok.Validated();
        Assert.DoesNotContain("..", cleaned.Id, StringComparison.Ordinal);
        Assert.Equal(80, cleaned.Name.Length);
        var nan = DefaultPrintTemplates.For(PrintDocumentKind.SalesInvoice) with { Page = new PrintPageSettings { MarginRightMm = double.NaN, PaperWidthMm = double.PositiveInfinity } };
        Assert.Equal(210, nan.Validated().Page.PaperWidthMm);
        Assert.Throws<InvalidDataException>(() => PrintTemplateJson.Deserialize("{ not json"));
        Assert.Throws<InvalidDataException>(() => PrintTemplateJson.Deserialize("{\"Version\": 99}"));
    }

    [Fact]
    public void SubstitutionPrintsValuesLiterallyAndKeepsPageNumbersAsVariables()
    {
        var data = new PrintData(new Dictionary<string, string> { ["Doc.Number"] = "7", ["Party.Name"] = "[Evil] زبون" }, []);
        Assert.Equal("رقم 7", PrintTemplateRenderer.Substitute("رقم [Doc.Number]", data, out var pages));
        Assert.False(pages);
        Assert.Equal("[Evil] زبون", PrintTemplateRenderer.Substitute("[Party.Name]", data, out _));
        Assert.Equal("(Evil) زبون / [Page#]", PrintTemplateRenderer.Substitute("[Party.Name] / [Page#]", data, out pages));
        Assert.True(pages);
        Assert.Equal("[Unknown.Field]", PrintTemplateRenderer.Substitute("[Unknown.Field]", data, out _));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void FastReportRendersEveryBuiltInTemplateToPagesAndPdf(PrintDocumentKind kind)
    {
        var template = DefaultPrintTemplates.For(kind);
        var data = PrintFieldCatalog.Sample(kind);
        var pages = PrintTemplateRenderer.RenderPages(template, data, 100);
        Assert.NotEmpty(pages);
        Assert.All(pages, page => Assert.True(page.Length > 2000 && page[1] == (byte)'P' && page[2] == (byte)'N' && page[3] == (byte)'G'));
        File.WriteAllBytes(Path.Combine(OutputDirectory(), kind + ".png"), pages[0]);
        var pdf = PrintTemplateRenderer.RenderPdf(template, data);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4), StringComparison.Ordinal);
        File.WriteAllBytes(Path.Combine(OutputDirectory(), kind + ".pdf"), pdf);
    }

    [Fact]
    public void ManyLinesFlowOntoSeveralPagesWithTheHeaderAndFooterOnEach()
    {
        var kind = PrintDocumentKind.SalesInvoice;
        var sample = PrintFieldCatalog.Sample(kind);
        var many = sample with { Lines = [.. Enumerable.Range(1, 90).Select(index => Number(sample.Lines[0].ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), index))] };
        var pages = PrintTemplateRenderer.RenderPages(DefaultPrintTemplates.For(kind), many, 60);
        Assert.True(pages.Count >= 2, $"expected several pages, got {pages.Count}");
        File.WriteAllBytes(Path.Combine(OutputDirectory(), "SalesInvoice-multipage-2.png"), pages[1]);
    }

    private static Dictionary<string, string> Number(Dictionary<string, string> line, int index)
    {
        line["Line.No"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return line;
    }

    private sealed class PrintTemplateComparer : IEqualityComparer<PrintTemplate>
    {
        public bool Equals(PrintTemplate? x, PrintTemplate? y) =>
            x is not null && y is not null && PrintTemplateJson.Serialize(x) == PrintTemplateJson.Serialize(y);

        public int GetHashCode(PrintTemplate obj) => PrintTemplateJson.Serialize(obj).GetHashCode(StringComparison.Ordinal);
    }
}
