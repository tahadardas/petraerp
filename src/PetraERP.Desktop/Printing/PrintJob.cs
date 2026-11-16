using PetraERP.Reporting.OpenSource.Printing;

namespace PetraERP.Desktop.Printing;

/// <summary>
/// One document to preview or print: its kind, the values to print (already formatted: Latin digits, grouping, dates) and a window title.
/// A document view model builds the document-specific fields; the shell adds the company, branch, fiscal year, user and date.
/// </summary>
public sealed record PrintJob(PrintDocumentKind Kind, PrintData Data, string Title);

/// <summary>Where the print templates live and which store the shell uses. Tests replace <see cref="Store"/> with a temporary folder.</summary>
public static class DocumentPrinting
{
    private static PrintTemplateStore store = new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PetraERP", "PrintTemplates"));

    /// <summary>The template store (per company and document kind).</summary>
    public static PrintTemplateStore Store
    {
        get => store;
        set => store = value ?? throw new ArgumentNullException(nameof(value));
    }
}
