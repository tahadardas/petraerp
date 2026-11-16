namespace PetraERP.Desktop.Management;

/// <summary>
/// Where a catalogue report lives in the top menu. The catalogue (<c>ReportMasterCatalog</c>, served by the API) stays the only list of
/// reports; this only says which natural menu shows each report group, so reports are not copied anywhere.
/// </summary>
public static class ReportMenuPlacement
{
    /// <summary>Menus that get a live "reports" sub-menu (ids of <see cref="Shell.MenuRegistry"/> roots).</summary>
    public static readonly IReadOnlyList<string> Menus = ["invoices", "vouchers", "parties", "inventory", "accounts", "financial", "cost-centers"];

    /// <summary>The menu id for a catalogue group (the id prefix such as GL, ARAP, INV); unknown groups go to the tools menu.</summary>
    public static string MenuFor(string? group) => group switch
    {
        "GL" or "FX" => "accounts",
        "ARAP" => "parties",
        "INV" => "inventory",
        "SALES" or "BUY" => "invoices",
        "CASH" => "vouchers",
        "CC" => "cost-centers",
        "FIN" => "financial",
        _ => "tools",
    };

    /// <summary>Localization key of the top menu caption (ملف، فاتورة، سندات...).</summary>
    public static string TitleKey(string menuId) => menuId switch
    {
        "invoices" => "MN_Invoice",
        "vouchers" => "MN_Vouchers",
        "parties" => "MN_Customers",
        "inventory" => "MN_Materials",
        "accounts" => "MN_Accounts",
        "financial" => "MN_Finance",
        "cost-centers" => "MN_Centers",
        _ => "MN_Tools",
    };
}
