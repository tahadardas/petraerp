using PetraERP.Application.Common;
using PetraERP.Application.Productivity;

namespace PetraERP.Application.Tests;

public sealed class WorkspaceRulesTests
{
    private static readonly string[] ExpectedCommands = ["workspace.search", "accounts.browse", "general-journal.report", "account-ledger.report", "trial-balance.report", "journals.browse"];
    [Fact]
    public void CatalogContainsOnlyImplementedNavigationAndNoUnimplementedAccountingCommands()
    {
        Assert.Equal(ExpectedCommands, WorkspaceRules.Commands.Select(command => command.Id));
        Assert.Null(WorkspaceRules.Permission(SearchCategory.Invoices));
        Assert.Null(WorkspaceRules.Permission(SearchCategory.Items));
    }
    [Fact]
    public void ImplementedReportsCanBeFavoritedAndRequireTheReportPermission()
    {
        WorkspaceRules.Validate(WorkspacePreferences.Default with { FavoriteReports = ["trial-balance.report"] });
        Assert.Equal(PetraERP.Application.Security.PermissionCatalog.ReportsView,
            WorkspaceRules.CommandPermission(WorkspaceRules.Commands.Single(command => command.Id == "trial-balance.report")));
        Assert.Throws<RequestValidationException>(() => WorkspaceRules.Validate(WorkspacePreferences.Default with { FavoriteReports = ["trial-balance.report", "trial-balance.report"] }));
    }
    [Fact]
    public void ALayoutCannotContainExpressionsOrHiddenAccountingFields()
    {
        var preferences = WorkspacePreferences.Default with { Grids = [new("workspace.results", [new("Tax=0", 100, 0, true)])] };
        Assert.Throws<RequestValidationException>(() => WorkspaceRules.Validate(preferences));
    }
    [Fact]
    public void WidthOrderAndVisibilityAreBoundedAndKnownColumnsAreAccepted()
    {
        WorkspaceRules.Validate(WorkspacePreferences.Default with { Grids = [new("document.lines", [new("Debit", 100, 0, true)])] });
        Assert.Throws<RequestValidationException>(() => WorkspaceRules.Validate(WorkspacePreferences.Default with
            { Grids = [new("document.lines", [new("Debit", 99999, 0, true)])] }));
        Assert.Throws<RequestValidationException>(() => WorkspaceRules.Validate(WorkspacePreferences.Default with
            { Grids = [new("document.lines", [new("Debit", 100, 0, false)])] }));
    }
    [Fact]
    public void UnsupportedFavoriteCommandsAndReportsAreRejected()
    {
        Assert.Throws<RequestValidationException>(() => WorkspaceRules.Validate(WorkspacePreferences.Default with { FavoriteCommands = ["sales.new"] }));
        Assert.Throws<RequestValidationException>(() => WorkspaceRules.Validate(WorkspacePreferences.Default with { FavoriteReports = ["trial-balance"] }));
    }
    [Fact]
    public void LedgerScreenLayoutsAreWhitelistedPerGridAndUnknownGridsOrColumnsAreRejected()
    {
        WorkspaceRules.Validate(WorkspacePreferences.Default with
        {
            Grids = WorkspaceRules.GridColumns.Select(grid => new GridPreference(grid.Key, [new(grid.Value[0], 120, 0, true)])).ToArray(),
        });
        Assert.Throws<RequestValidationException>(() => WorkspaceRules.Validate(WorkspacePreferences.Default with
            { Grids = [new("ledger.trial-balance", [new("Formula", 100, 0, true)])] }));
        Assert.Throws<RequestValidationException>(() => WorkspaceRules.Validate(WorkspacePreferences.Default with
            { Grids = [new("sales.invoices", [new("Code", 100, 0, true)])] }));
        Assert.Throws<RequestValidationException>(() => WorkspaceRules.Validate(WorkspacePreferences.Default with
            { Grids = [new("ledger.cost-centers", [new("Code", 100, 3, true)])] }));
    }
}
