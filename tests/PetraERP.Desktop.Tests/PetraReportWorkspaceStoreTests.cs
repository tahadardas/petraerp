using System.IO;
using PetraERP.Desktop.Reporting;

namespace PetraERP.Desktop.Tests;

public sealed class PetraReportWorkspaceStoreTests
{
    [Fact]
    public async Task WorkspaceIsVersionedScopedBoundedAndDeletable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PetraERP.Tests", "ReportWorkspace", Guid.NewGuid().ToString("N"));
        try
        {
            var user = Guid.NewGuid();
            var company = Guid.NewGuid();
            var year = Guid.NewGuid();
            var store = new PetraReportWorkspaceStore(directory);
            var workspace = new PetraReportWorkspace(
                PetraReportWorkspaceStore.CurrentVersion,
                "تحليل المبيعات",
                user,
                company,
                year,
                "AccountLedger",
                [new PetraReportColumnState("Description", 0, 240, true, true)],
                ["Description"],
                [new PetraReportWorkspaceFilter("Description", PetraReportFilterOperator.Contains, "إسمنت", null, PetraReportFilterLogic.And, false)],
                "Description",
                System.ComponentModel.ListSortDirection.Ascending,
                "Standard",
                null,
                DateTimeOffset.UtcNow);

            await store.SaveAsync(workspace, TestContext.Current.CancellationToken);
            var loaded = await store.LoadAsync(user, company, year, "AccountLedger", TestContext.Current.CancellationToken);
            var saved = Assert.Single(loaded);
            Assert.Equal("تحليل المبيعات", saved.Name);
            Assert.Equal("إسمنت", Assert.Single(saved.Filters).Value);
            Assert.Empty(await store.LoadAsync(user, company, Guid.NewGuid(), "AccountLedger", TestContext.Current.CancellationToken));

            await store.DeleteAsync(saved, TestContext.Current.CancellationToken);
            Assert.Empty(await store.LoadAsync(user, company, year, "AccountLedger", TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task WorkspaceRejectsInvalidScopeAndOversizedConditionSet()
    {
        var store = new PetraReportWorkspaceStore(Path.Combine(Path.GetTempPath(), "PetraERP.Tests", "InvalidWorkspace", Guid.NewGuid().ToString("N")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadAsync(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(),
            "AccountLedger", TestContext.Current.CancellationToken));
        var filters = Enumerable.Range(0, PetraReportFilterEngine.MaximumConditions + 1)
            .Select(index => new PetraReportWorkspaceFilter("Description", PetraReportFilterOperator.Contains,
                index.ToString(System.Globalization.CultureInfo.InvariantCulture), null, PetraReportFilterLogic.And, false)).ToArray();
        var workspace = new PetraReportWorkspace(PetraReportWorkspaceStore.CurrentVersion, "Too many", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "AccountLedger", [], [], filters, null, null, "Standard", null, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(workspace, TestContext.Current.CancellationToken));
    }
}
