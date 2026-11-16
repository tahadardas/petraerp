using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class WorkspaceFiscalYearIsolationTests
{
    [Fact]
    public void RouteKeysForDifferentFiscalYearsDoNotCollide()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var year2025 = Guid.NewGuid();
        var year2026 = Guid.NewGuid();

        var key2025 = WorkspaceDocument.RouteKey("journals", companyId, branchId, year2025);
        var key2026 = WorkspaceDocument.RouteKey("journals", companyId, branchId, year2026);

        Assert.NotEqual(key2025, key2026);
        Assert.Contains(year2025.ToString("N"), key2025);
        Assert.Contains(year2026.ToString("N"), key2026);
    }

    [Fact]
    public void DocumentKeysForDifferentFiscalYearsAreIsolated()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var year2025 = Guid.NewGuid();
        var year2026 = Guid.NewGuid();
        var docId = Guid.NewGuid();

        var journal2025 = WorkspaceDocument.JournalKey(companyId, branchId, docId, year2025);
        var journal2026 = WorkspaceDocument.JournalKey(companyId, branchId, docId, year2026);
        Assert.NotEqual(journal2025, journal2026);

        var account2025 = WorkspaceDocument.AccountKey(companyId, branchId, docId, year2025);
        var account2026 = WorkspaceDocument.AccountKey(companyId, branchId, docId, year2026);
        Assert.NotEqual(account2025, account2026);

        var invoice2025 = WorkspaceDocument.SalesInvoiceKey(companyId, branchId, docId, year2025);
        var invoice2026 = WorkspaceDocument.SalesInvoiceKey(companyId, branchId, docId, year2026);
        Assert.NotEqual(invoice2025, invoice2026);
    }

    [Fact]
    public void SavedFilterScopedToFiscalYearDoesNotLeakAcrossYears()
    {
        var year2025 = Guid.NewGuid();
        var year2026 = Guid.NewGuid();

        var filter2025 = new SavedFilter(Guid.NewGuid(), "Filter-2025", "account-ledger", Guid.NewGuid(), FiscalYearId: year2025);

        Assert.Equal(year2025, filter2025.FiscalYearId);
        Assert.NotEqual(year2026, filter2025.FiscalYearId);
    }

    [Fact]
    public void ScopedDocumentsIdentifyCompanyAndFiscalYearOwnership()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var yearId = Guid.NewGuid();

        var doc = new WorkspaceDocument(
            "Route/test",
            WorkspaceDocumentKind.Route,
            "journals",
            null,
            "Test Journal",
            "",
            companyId,
            branchId,
            yearId);

        Assert.True(doc.IsScoped);
        Assert.Equal(companyId, doc.CompanyId);
        Assert.Equal(branchId, doc.BranchId);
        Assert.Equal(yearId, doc.FiscalYearId);
    }
}
