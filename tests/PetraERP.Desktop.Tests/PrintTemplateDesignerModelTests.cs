using PetraERP.Desktop.Printing;
using PetraERP.Reporting.OpenSource.Printing;

namespace PetraERP.Desktop.Tests;

/// <summary>The designer's rules: snapping, staying inside the band and the printable width, field insertion, validation and the round trip to a template.</summary>
public sealed class PrintTemplateDesignerModelTests
{
    private static PrintTemplateDesignerModel Create() => new(DefaultPrintTemplates.For(PrintDocumentKind.SalesInvoice));

    [Fact]
    public void MovingSnapsToHalfMillimeterAndStaysInsideTheBand()
    {
        var model = Create();
        var element = model.AddElement(PrintElementType.Text, PrintBandKind.DocumentHeader);
        model.MoveTo(element, 12.3, 3.8);
        Assert.Equal(12.5, element.X);
        Assert.Equal(4.0, element.Y);

        model.MoveTo(element, -50, -50);
        Assert.Equal(0, element.X);
        Assert.Equal(0, element.Y);

        model.MoveTo(element, 10_000, 10_000);
        Assert.Equal(model.PrintableWidthMm - element.Width, element.X);
        Assert.Equal(model.Band(PrintBandKind.DocumentHeader).HeightMm - element.Height, element.Y);
    }

    [Fact]
    public void ResizingKeepsMinimumSizeAndStaysInsideTheBand()
    {
        var model = Create();
        var element = model.AddElement(PrintElementType.Rectangle, PrintBandKind.PageHeader);
        model.ResizeTo(element, 0, 0);
        Assert.Equal(3, element.Width);
        Assert.Equal(2, element.Height);

        model.ResizeTo(element, 10_000, 10_000);
        Assert.Equal(model.PrintableWidthMm - element.X, element.Width);
        Assert.Equal(model.Band(PrintBandKind.PageHeader).HeightMm - element.Y, element.Height);
    }

    [Fact]
    public void ALineKeepsZeroHeightWhenResized()
    {
        var model = Create();
        var line = model.AddElement(PrintElementType.Line, PrintBandKind.DocumentFooter);
        model.ResizeTo(line, 40, 20);
        Assert.Equal(0, line.Height);
        Assert.Equal(40, line.Width);
    }

    [Fact]
    public void InsertingAFieldAppendsToTheSelectedTextOrAddsAnElement()
    {
        var model = Create();
        var added = model.InsertField("Doc.Number", PrintBandKind.DocumentHeader);
        Assert.Equal("[Doc.Number]", added.Text);
        Assert.Same(added, model.SelectedElement);

        var same = model.InsertField("Doc.Total", PrintBandKind.DocumentHeader);
        Assert.Same(added, same);
        Assert.Equal("[Doc.Number][Doc.Total]", added.Text);
    }

    [Fact]
    public void ValidationFlagsUnknownFieldsAndEmptyColumnsButNotTheDefaultTemplate()
    {
        var model = Create();
        Assert.Empty(model.Validate());

        model.AddElement(PrintElementType.Text, PrintBandKind.DocumentHeader, "[Nope.Field]");
        Assert.Contains(model.Validate(), problem => problem.Contains("Nope.Field", StringComparison.Ordinal));

        model.Columns.Clear();
        Assert.Contains(model.Validate(), problem => problem.Contains("أعمدة", StringComparison.Ordinal));
    }

    [Fact]
    public void ColumnsCanBeReorderedAndFitToThePrintableWidth()
    {
        var model = Create();
        var first = model.Columns[0];
        model.SelectedColumn = first;
        model.MoveSelectedColumn(1);
        Assert.Same(first, model.Columns[1]);

        model.FitColumnsToWidth();
        Assert.InRange(model.Columns.Sum(column => column.WidthMm), model.PrintableWidthMm - 1, model.PrintableWidthMm + 1);

        model.RemoveSelectedColumn();
        Assert.DoesNotContain(first, model.Columns);
    }

    [Fact]
    public void TheTemplateRoundTripsAndEditsAreTracked()
    {
        var model = Create();
        Assert.False(model.IsDirty);
        var element = model.AddElement(PrintElementType.Text, PrintBandKind.PageFooter, "تذييل");
        element.Text = "الفرع: [Branch.Name] — السنة: [FiscalYear.Name]";
        Assert.True(model.IsDirty);

        var template = model.ToTemplate().Validated();
        var again = new PrintTemplateDesignerModel(template);
        Assert.Contains(again.Band(PrintBandKind.PageFooter).Elements, item => item.Text.Contains("[Branch.Name]", StringComparison.Ordinal));
        Assert.Empty(again.Validate());
    }

    [Fact]
    public void DuplicatingOffsetsTheCopyAndSelectsIt()
    {
        var model = Create();
        var original = model.AddElement(PrintElementType.Text, PrintBandKind.DocumentHeader);
        var copy = model.DuplicateSelected();
        Assert.NotNull(copy);
        Assert.NotSame(original, copy);
        Assert.Equal(original.X + 5, copy.X);
        Assert.Same(copy, model.SelectedElement);
    }
}
