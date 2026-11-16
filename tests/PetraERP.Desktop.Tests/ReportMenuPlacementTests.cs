using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Management;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>Catalogue reports are shown under their natural menus without copying the catalogue.</summary>
public sealed class ReportMenuPlacementTests
{
    [Fact]
    public void EveryMenuWithReportsIsARootOfTheSingleMenuDescription()
    {
        var roots = MenuRegistry.RootGroups.Select(node => node.Id).ToArray();
        Assert.All(ReportMenuPlacement.Menus, menu => Assert.Contains(menu, roots));
    }

    [Theory]
    [InlineData("GL", "accounts")]
    [InlineData("FX", "accounts")]
    [InlineData("ARAP", "parties")]
    [InlineData("INV", "inventory")]
    [InlineData("SALES", "invoices")]
    [InlineData("BUY", "invoices")]
    [InlineData("CASH", "vouchers")]
    [InlineData("CC", "cost-centers")]
    [InlineData("FIN", "financial")]
    [InlineData("SYSTEM", "tools")]
    [InlineData("SOMETHING-NEW", "tools")]
    public void GroupsLandInTheirNaturalMenu(string group, string menu) => Assert.Equal(menu, ReportMenuPlacement.MenuFor(group));

    [Fact]
    public void MenuCaptionsAreTheArabicMenuNames()
    {
        var localization = new LocalizationService("ar");
        var captions = ReportMenuPlacement.Menus.Select(menu => localization[ReportMenuPlacement.TitleKey(menu)]).ToArray();
        Assert.Equal(["فاتورة", "سندات", "زبائن", "مواد", "حسابات", "مالية", "مراكز"], captions);
    }
}
