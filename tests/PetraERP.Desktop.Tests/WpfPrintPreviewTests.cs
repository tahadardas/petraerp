using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Printing;
using PetraERP.Reporting.OpenSource.Printing;

namespace PetraERP.Desktop.Tests;

public sealed partial class WpfFoundationTests
{
    /// <summary>Real WPF: the print preview window shows the rendered pages of a saved invoice and a screenshot is kept for review.</summary>
    private static void VerifyPrintPreviewWindow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "petra-print-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var data = PrintDocumentBuilders.WithContext(PrintDocumentBuilders.SalesInvoice(PrintingTests.SampleInvoice()), "شركة المثال التجارية", "الفرع الرئيسي", "2026", "admin", DateTimeOffset.UtcNow);
            var model = new DocumentPrintViewModel(new PrintJob(PrintDocumentKind.SalesInvoice, data, "فاتورة مبيعات 1024"), new PrintTemplateStore(directory), Guid.NewGuid(), new LocalizationService("ar"));
            var window = new DocumentPrintWindow(model) { Width = 960, Height = 780, Left = -10000, ShowActivated = false };
            window.Show();
            try
            {
                for (var attempt = 0; attempt < 200 && model.Pages.Count == 0; attempt++) { Thread.Sleep(25); Pump(); }
                window.UpdateLayout(); Pump();
                Assert.Single(model.Pages);
                Assert.Empty(model.Error);
                Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);
                Assert.Single(Descendants<Image>(window), image => image.Source is not null);
                var bitmap = new RenderTargetBitmap(960, 780, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                var output = Path.Combine(FindRoot(), ".artifacts", "viewport-acceptance", "visual"); Directory.CreateDirectory(output);
                using var stream = File.Create(Path.Combine(output, "print-preview-ar.png"));
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
            }
            finally { window.Close(); }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>Real WPF: the template designer opens on the built-in invoice, draws every band with its elements and the table, and a screenshot is kept.</summary>
    private static void VerifyPrintTemplateDesigner()
    {
        var directory = Path.Combine(Path.GetTempPath(), "petra-designer-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var data = PrintDocumentBuilders.WithContext(PrintDocumentBuilders.SalesInvoice(PrintingTests.SampleInvoice()), "شركة المثال التجارية", "الفرع الرئيسي", "2026", "admin", DateTimeOffset.UtcNow);
            var template = DefaultPrintTemplates.For(PrintDocumentKind.SalesInvoice);
            var window = new PrintTemplateDesignerWindow(template, new PrintTemplateStore(directory), Guid.NewGuid(), data) { Width = 1280, Height = 800, Left = -10000, ShowActivated = false };
            window.Show();
            try
            {
                window.UpdateLayout(); Pump();
                Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);
                Assert.NotEmpty(Descendants<Canvas>(window));
                var bitmap = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                var output = Path.Combine(FindRoot(), ".artifacts", "viewport-acceptance", "visual"); Directory.CreateDirectory(output);
                using var stream = File.Create(Path.Combine(output, "print-designer-ar.png"));
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
            }
            finally { window.Close(); }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
