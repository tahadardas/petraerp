using System.Windows.Controls;
using System.Windows.Data;

namespace PetraERP.Desktop.Inventory;

public partial class InventoryOperationsView : UserControl
{
    public InventoryOperationsView() => InitializeComponent();

    private void AuditColumn(object? sender, DataGridAutoGeneratingColumnEventArgs args)
    {
        if (DataContext is not InventoryOperationsViewModel model) return;
        if (args.PropertyName is "WarehouseId" or "ItemId") { args.Cancel = true; return; }
        var caption = new TextBlock();
        caption.SetBinding(TextBlock.TextProperty, new Binding("Localization[INV_Audit_" + args.PropertyName + "]") { Source = model });
        args.Column.Header = caption;
        if (args.PropertyName == "Kind" && args.Column is DataGridTextColumn column)
            column.Binding = new Binding("Kind") { Converter = new InventoryEnumCaptionConverter(), ConverterParameter = "INV_Movement_" };
    }
}
