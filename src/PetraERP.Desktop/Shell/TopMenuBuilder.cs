using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using PetraERP.Desktop.Management;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// Builds the WPF menu bar from <see cref="NavigationService.TopMenu"/>, the resolved single menu description. Routes run
/// <see cref="DesktopViewModel.NavigateCommand"/>; shell commands run <see cref="DesktopViewModel.MenuCommand"/>. A node
/// whose command is unknown is skipped, so the bar never shows an entry that does nothing. The two live lists (open
/// windows, program appearance) are filled when the menu opens or from the live collection.
/// </summary>
public static class TopMenuBuilder
{
    private const string OpenWindowTag = "open-window-entry";

    /// <summary>Replaces the whole menu bar content; call after sign-in, a permission change and a language change.</summary>
    public static void Rebuild(Menu menu, DesktopViewModel model)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(model);
        menu.Items.Clear();
        foreach (var node in model.Navigation.TopMenu)
            if (Create(node, model) is { } item)
            {
                menu.Items.Add(item);
            }
    }

    private static MenuItem? Create(TopMenuNode node, DesktopViewModel model)
    {
        var item = new MenuItem
        {
            Header = node.Command == "theme" ? model.ThemeLabel : node.Label,
            InputGestureText = node.Shortcut ?? string.Empty,
            Tag = node.Id,
        };
        AutomationProperties.SetName(item, node.Label);

        if (node.Route is not null)
        {
            item.Command = model.NavigateCommand;
            item.CommandParameter = node.Route;
            return item;
        }

        if (node.Command == MenuRegistry.SkinListCommand)
        {
            foreach (var skin in model.SkinItems) item.Items.Add(CreateSkinItem(skin));
            return item.Items.Count == 0 ? null : item;
        }

        if (node.Command is not null)
        {
            if (model.MenuCommand(node.Command) is not { } command) return null;
            item.Command = command;
            return item;
        }

        var hasOpenWindowList = false;
        foreach (var child in node.Children)
        {
            if (child.Command == MenuRegistry.OpenWindowsCommand) { hasOpenWindowList = true; continue; }
            if (Create(child, model) is not { } childItem) continue;
            if (child.SeparatorBefore && item.Items.Count > 0) item.Items.Add(new Separator());
            item.Items.Add(childItem);
        }

        if (hasOpenWindowList) item.SubmenuOpened += (_, _) => RefreshOpenWindows(item, model);
        // Reports live under their natural menu too: a live sub-menu fed by the report catalogue (never a second copy of it).
        if (ReportMenuPlacement.Menus.Contains(node.Id) && model.Pages.Management is not null && model.Navigation.Has(ReportsCenterViewModel.ViewPermission))
        {
            if (item.Items.Count > 0) item.Items.Add(new Separator());
            item.Items.Add(CreateReportsMenu(node.Id, model));
        }

        return item;
    }

    private static MenuItem CreateReportsMenu(string menuId, DesktopViewModel model)
    {
        var menu = new MenuItem { Header = model.Localization["D_Menu_Sub_reports"], Tag = "reports-menu" };
        AutomationProperties.SetName(menu, model.Localization["D_Menu_Sub_reports"]);
        // A sub-menu without an item never opens (and so never raises SubmenuOpened): start with a disabled placeholder.
        menu.Items.Add(new MenuItem { Header = model.Localization["REPM_Loading"], IsEnabled = false });
        menu.SubmenuOpened += (_, args) =>
        {
            if (ReferenceEquals(args.OriginalSource, menu)) _ = PopulateReportsAsync(menu, menuId, model);
        };
        return menu;
    }

    /// <summary>Fills the reports sub-menu with the implemented, permitted catalogue reports of one menu; says so plainly when there are none.</summary>
    private static async Task PopulateReportsAsync(MenuItem menu, string menuId, DesktopViewModel model)
    {
        var center = model.Pages.Management?.ReportsCenter;
        var company = model.Company;
        var branch = model.Branch;
        var loaded = false;
        if (center is not null && company is not null && branch is not null)
        {
            try { loaded = await center.EnsureCatalogAsync(company.Id, branch.Id, CancellationToken.None).ConfigureAwait(true); }
            catch (Exception) { loaded = false; }
        }

        menu.Items.Clear();
        var rows = loaded && center is not null ? center.OpenableForMenu(menuId) : [];
        if (rows.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = model.Localization[loaded ? "REPM_None" : "REPM_Unavailable"], IsEnabled = false });
            return;
        }

        foreach (var row in rows)
        {
            menu.Items.Add(new MenuItem
            {
                Header = row.Id + " — " + row.Name,
                Command = model.OpenCatalogReportCommand,
                CommandParameter = row,
                ToolTip = string.IsNullOrWhiteSpace(row.Entry.Note) ? null : row.Entry.Note,
            });
        }
    }

    private static MenuItem CreateSkinItem(DesktopViewModel.SkinMenuItem skin)
    {
        var entry = new MenuItem { Command = skin.Command, IsCheckable = false };
        entry.SetBinding(HeaderedItemsControl.HeaderProperty, new Binding(nameof(DesktopViewModel.SkinMenuItem.Label)) { Source = skin, Mode = BindingMode.OneWay });
        entry.SetBinding(MenuItem.IsCheckedProperty, new Binding(nameof(DesktopViewModel.SkinMenuItem.IsChecked)) { Source = skin, Mode = BindingMode.OneWay });
        return entry;
    }

    /// <summary>Appends one entry per open document (the active one checked) under a separator, replacing the previous list.</summary>
    private static void RefreshOpenWindows(MenuItem window, DesktopViewModel model)
    {
        for (var index = window.Items.Count - 1; index >= 0; index--)
            if (window.Items[index] is FrameworkElement { Tag: OpenWindowTag })
            {
                window.Items.RemoveAt(index);
            }

        if (model.Documents.Documents.Count == 0) return;
        window.Items.Add(new Separator { Tag = OpenWindowTag });
        foreach (var document in model.Documents.Documents)
        {
            window.Items.Add(new MenuItem
            {
                Header = document.Title,
                IsChecked = document.IsActive,
                Command = model.ActivateDocumentCommand,
                CommandParameter = document,
                Tag = OpenWindowTag,
            });
        }
    }
}
