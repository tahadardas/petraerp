namespace PetraERP.Domain.Inventory;

/// <summary>What is left of one receipt layer: quantity and the value still carried by it.</summary>
public sealed record FifoLayerBalance(Guid LayerId, decimal QuantityRemaining, decimal ValueRemaining);

/// <summary>The part of one layer an issue consumes, with its cost.</summary>
public sealed record FifoSlice(Guid LayerId, decimal Quantity, decimal Cost);

/// <summary>
/// First-in-first-out issue costing over receipt layers at decimal(19,4). Layers are consumed in the order supplied
/// (posting order). A layer that is consumed completely releases exactly the value it still carries, so no rounding
/// residue is left behind and the sum of all issue costs plus the remaining layers equals the receipts' value.
/// </summary>
public static class FifoCosting
{
    public static bool TryIssue(IReadOnlyList<FifoLayerBalance> layersOldestFirst, decimal quantity, out IReadOnlyList<FifoSlice> slices, out decimal totalCost)
    {
        ArgumentNullException.ThrowIfNull(layersOldestFirst);
        slices = [];
        totalCost = 0m;
        if (quantity <= 0m || quantity > WeightedAverageCosting.MaximumStoredValue || decimal.Round(quantity, 4) != quantity) return false;
        var result = new List<FifoSlice>();
        var needed = quantity;
        var total = 0m;
        foreach (var layer in layersOldestFirst)
        {
            if (needed == 0m) break;
            if (layer.QuantityRemaining <= 0m || layer.ValueRemaining < 0m) continue;
            var take = Math.Min(needed, layer.QuantityRemaining);
            var cost = take == layer.QuantityRemaining
                ? layer.ValueRemaining
                : decimal.Round(layer.ValueRemaining / layer.QuantityRemaining * take, 4, MidpointRounding.ToEven);
            result.Add(new FifoSlice(layer.LayerId, take, cost));
            total += cost;
            needed -= take;
        }
        if (needed != 0m || total > WeightedAverageCosting.MaximumStoredValue) return false;
        slices = result;
        totalCost = total;
        return true;
    }
}
