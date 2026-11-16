using System.Globalization;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Inventory;

/// <summary>
/// The Al-Ameen warning for a quantity that takes an item above the maximum recorded on its card. It is advisory: the caller asks the user
/// whether to continue, and a failed read never blocks the document. Only lines in the item's base unit are compared, so no wrong
/// unit conversion can produce a false warning.
/// </summary>
public static class MaxStockGuard
{
    public static bool Exceeds(decimal currentStock, decimal incoming, decimal? maximum) => maximum is > 0m && currentStock + incoming > maximum;

    public static async Task<bool> AnyExceedsAsync(IInventoryClient inventory, Guid companyId, Guid branchId, IEnumerable<(Guid ItemId, Guid UnitId, decimal Quantity)> lines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(lines);
        foreach (var item in lines.GroupBy(line => line.ItemId))
        {
            try
            {
                var detail = await inventory.GetItemAsync(companyId, branchId, item.Key, cancellationToken).ConfigureAwait(true);
                if (!decimal.TryParse(detail.MaxStock, NumberStyles.Number, CultureInfo.InvariantCulture, out var maximum)) continue;
                var incoming = item.Where(line => line.UnitId == detail.BaseUnitId).Sum(line => line.Quantity);
                var current = decimal.TryParse(detail.CurrentStock, NumberStyles.Number, CultureInfo.InvariantCulture, out var stock) ? stock : 0m;
                if (Exceeds(current, incoming, maximum)) return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Information only: whatever goes wrong reading a card (network, permission, a missing card) never blocks the invoice.
            }
        }

        return false;
    }
}
