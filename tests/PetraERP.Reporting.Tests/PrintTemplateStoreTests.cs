using System.IO;
using PetraERP.Reporting.OpenSource.Printing;

namespace PetraERP.Reporting.Tests;

/// <summary>The template store keeps templates per company and kind, protects the built-in one and never trusts file names.</summary>
public sealed class PrintTemplateStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "petra-templates-" + Guid.NewGuid().ToString("N"));
    private static readonly Guid Company = Guid.NewGuid();

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void BuiltInIsAlwaysFirstAndResolvedUntilACompanyChoosesAnother()
    {
        var store = new PrintTemplateStore(root);
        var kind = PrintDocumentKind.SalesInvoice;
        Assert.Equal(DefaultPrintTemplates.IdFor(kind), Assert.Single(store.List(Company, kind)).Id);
        var mine = store.Save(Company, PrintTemplateStore.Copy(DefaultPrintTemplates.For(kind), "نمط الشركة"));
        var list = store.List(Company, kind);
        Assert.Equal(2, list.Count);
        Assert.Equal(DefaultPrintTemplates.IdFor(kind), list[0].Id);
        Assert.Equal(DefaultPrintTemplates.IdFor(kind), store.Resolve(Company, kind).Id);
        store.SetDefault(Company, kind, mine.Id);
        Assert.Equal(mine.Id, store.Resolve(Company, kind).Id);
        store.Delete(Company, kind, mine.Id);
        Assert.Equal(DefaultPrintTemplates.IdFor(kind), store.Resolve(Company, kind).Id);
        Assert.Null(store.DefaultId(Company, kind));
    }

    [Fact]
    public void TemplatesAreScopedByCompanyAndKind()
    {
        var store = new PrintTemplateStore(root);
        store.Save(Company, PrintTemplateStore.Copy(DefaultPrintTemplates.For(PrintDocumentKind.SalesInvoice), "A"));
        Assert.Single(store.List(Guid.NewGuid(), PrintDocumentKind.SalesInvoice));
        Assert.Single(store.List(Company, PrintDocumentKind.ReceiptVoucher));
    }

    [Fact]
    public void TheBuiltInTemplateCannotBeOverwrittenAndBadIdsAreRefused()
    {
        var store = new PrintTemplateStore(root);
        Assert.Throws<InvalidOperationException>(() => store.Save(Company, DefaultPrintTemplates.For(PrintDocumentKind.SalesInvoice)));
        store.Delete(Company, PrintDocumentKind.SalesInvoice, DefaultPrintTemplates.IdFor(PrintDocumentKind.SalesInvoice));
        Assert.Throws<ArgumentException>(() => store.Delete(Company, PrintDocumentKind.SalesInvoice, "..\\..\\x"));
        Assert.Throws<ArgumentException>(() => store.List(Guid.Empty, PrintDocumentKind.SalesInvoice));
    }

    [Fact]
    public void DamagedFilesAreSkippedAndExportImportGivesANewIdentity()
    {
        var store = new PrintTemplateStore(root);
        var kind = PrintDocumentKind.PaymentVoucher;
        var saved = store.Save(Company, PrintTemplateStore.Copy(DefaultPrintTemplates.For(kind), "سند"));
        File.WriteAllText(Path.Combine(root, Company.ToString("N"), kind.ToString(), "broken.json"), "{ not json");
        Assert.Equal(2, store.List(Company, kind).Count);

        var file = Path.Combine(root, "exported.json");
        PrintTemplateStore.Export(saved, file);
        var imported = PrintTemplateStore.Import(file, kind);
        Assert.NotEqual(saved.Id, imported.Id);
        Assert.Equal(saved.Name, imported.Name);
        Assert.Throws<InvalidDataException>(() => PrintTemplateStore.Import(file, PrintDocumentKind.SalesInvoice));
    }
}
