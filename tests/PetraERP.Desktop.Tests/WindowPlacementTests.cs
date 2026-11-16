using System.IO;
using System.Text.Json;
using PetraERP.ApiClient;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class WindowPlacementTests
{
    private static DetachedWindowPlacement Placement => new("\\\\.\\DISPLAY2", 100, 50, 800, 450, true);

    [Fact]
    public void GeometryFitsNegativeMonitorsTaskbarsChangedDpiAndVerySmallWorkAreas()
    {
        var monitor = new MonitorWorkArea("DISPLAY2", -1920, 40, 1920, 1040);
        foreach (var scale in new[] { 1d, 1.25d, 1.5d, 2d })
        {
            var bounds = WindowPlacementGeometry.Fit(Placement, monitor, scale, 1100, 720, 640, 350);
            Assert.Equal((int)(800 * scale), bounds.Width); Assert.Equal((int)(450 * scale), bounds.Height);
            Assert.Equal(-1920 + (int)(100 * scale), bounds.Left);
            Assert.Equal(40 + (int)(50 * scale), bounds.Top);
            Assert.InRange(bounds.Left + bounds.Width, monitor.Left, monitor.Left + monitor.Width);
            Assert.InRange(bounds.Top + bounds.Height, monitor.Top, monitor.Top + monitor.Height);
        }
        var tiny = new MonitorWorkArea("fallback", 0, 48, 320, 240);
        var fitted = WindowPlacementGeometry.Fit(Placement with { OffsetX = -1000, OffsetY = 9000, Width = 10000, Height = 10000 }, tiny, 2, 1100, 720, 640, 350);
        Assert.Equal(new WindowPixelBounds(0, 48, 320, 240), fitted);
    }

    [Fact]
    public void InvalidGeometryFallsBackWithoutNonfiniteOrExtremeNativeCoordinates()
    {
        var work = new MonitorWorkArea("current", 100, -1080, 1920, 1040);
        foreach (var invalid in new[] { Placement with { OffsetX = double.NaN }, Placement with { Height = double.PositiveInfinity },
            Placement with { OffsetY = double.MaxValue }, Placement with { Width = -1 }, Placement with { Height = 0 }, Placement with { Monitor = "" } })
        {
            Assert.False(invalid.IsValid);
            // Centred: 100 + (1920 - 1100) / 2 and -1080 + (1040 - 720) / 2.
            Assert.Equal(new WindowPixelBounds(510, -920, 1100, 720), WindowPlacementGeometry.Fit(invalid, work, 1, 1100, 720, 640, 350));
        }
    }

    [Fact]
    public void LocalPreferencesRoundTripGeometryAndReadOldFilesWithoutAddingContentOrTokens()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PetraERP_Placement_" + Guid.NewGuid().ToString("N"));
        try
        {
            var user = Guid.NewGuid(); var store = new ContextPreferenceStore(directory);
            var reference = new WorkspaceDocumentReference("account", null, Guid.NewGuid(), true, false, Placement);
            store.Save(user, new(Guid.NewGuid(), Guid.NewGuid(), [reference]));
            Assert.Equal(reference, Assert.Single(store.Read(user)!.Documents!)); Assert.Null(store.Read(Guid.NewGuid()));
            var file = Assert.Single(Directory.GetFiles(directory));
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            Assert.Equal(["Kind", "Route", "Id", "Detached", "Active", "Placement"], json.RootElement.GetProperty("Documents")[0].EnumerateObject().Select(item => item.Name));
            Assert.Equal(6, json.RootElement.GetProperty("Documents")[0].GetProperty(nameof(WorkspaceDocumentReference.Placement)).EnumerateObject().Count());
            File.WriteAllText(file, "{\"CompanyId\":\"" + Guid.NewGuid() + "\",\"BranchId\":\"" + Guid.NewGuid() + "\",\"Documents\":[{\"Kind\":\"route\",\"Route\":\"accounts\",\"Id\":null,\"Detached\":true,\"Active\":false}]}");
            Assert.Null(Assert.Single(store.Read(user)!.Documents!).Placement);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task GeometrySurvivesDisposalAndAuthorizedRestoreWhileDraftsAndRevokedDocumentsAreExcluded()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = ["Ledger.Journals.View", "Ledger.Journals.Manage"];
        var ledger = new FakeLedgerClient(); var saved = ledger.Add(fixture.Company.Id, fixture.Branch.Id, JournalState.Posted);
        var shell = fixture.Create(new AdministrationFixture(), ledger);
        try
        {
            shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
            await shell.NavigateToAsync("journals", new OpenJournalRequest(saved.Id)).ConfigureAwait(true);
            var document = shell.Documents.Active; shell.DetachDocumentCommand.Execute(document);
            shell.RememberWindowPlacement(document, Placement); shell.PersistWindowPlacements();
            Assert.Equal(Placement, Assert.Single(fixture.Saved!.Documents!).Placement);
            await shell.NavigateToAsync("journals", NewJournalRequest.Instance).ConfigureAwait(true);
            shell.RememberWindowPlacement(shell.Documents.Active, Placement with { Width = 900 }); shell.PersistWindowPlacements();
            Assert.Single(fixture.Saved.Documents!);
        }
        finally { shell.Dispose(); }
        Assert.Equal(Placement, Assert.Single(fixture.Saved!.Documents!).Placement);
        using var restored = fixture.Create(new AdministrationFixture(), ledger);
        restored.UserName = "admin"; restored.Password = "test"; await restored.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        var opened = Assert.Single(restored.Documents.Documents, item => item.IsClosable);
        Assert.Equal(saved.Id, Assert.IsType<PetraERP.Desktop.GeneralLedger.GeneralLedgerViewModel>(opened.Content).CurrentJournalId);
        Assert.Equal(Placement, opened.Placement); Assert.True(opened.IsDetached);
        fixture.User.Permissions = [];
        await restored.RefreshAsync().ConfigureAwait(true); Assert.DoesNotContain(opened, restored.Documents.Documents);
    }
}
