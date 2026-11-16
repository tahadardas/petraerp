using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory;

namespace PetraERP.Infrastructure.Inventory;

public sealed partial class SqlInventoryService
{
    public async Task<ItemPriceAdjustmentResult> AdjustItemPricesAsync(Guid companyId, ItemPriceAdjustmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Percent == 0m || command.Percent < -90m || command.Percent > 1000m)
            throw new BusinessRuleException("inventory.price_adjust_percent", "النسبة يجب أن تكون غير صفرية وبين -90 و1000 / The percentage must be non-zero and between -90 and 1000.", "percent");
        if (command.RoundingScale is < 0 or > 4)
            throw new BusinessRuleException("inventory.price_adjust_rounding", "التقريب بين 0 و4 خانات / Rounding must be between 0 and 4 digits.", "roundingScale");

        var list = string.IsNullOrWhiteSpace(command.PriceListCode) ? null : command.PriceListCode.Trim().ToUpperInvariant();
        List<Guid>? groups = null;
        if (command.GroupId is { } rootId)
        {
            var nodes = await context.ItemGroups.AsNoTracking().Where(g => g.CompanyId == companyId)
                .Select(g => new { g.Id, g.ParentId }).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (!nodes.Exists(n => n.Id == rootId)) throw new ResourceNotFoundException("ItemGroup");
            var included = new HashSet<Guid> { rootId };
            bool grew;
            do
            {
                grew = false;
                foreach (var node in nodes)
                {
                    if (node.ParentId is { } parent && included.Contains(parent) && included.Add(node.Id)) grew = true;
                }
            }
            while (grew);
            groups = [.. included];
        }

        var query = context.ItemPrices.Where(p => p.CompanyId == companyId && p.PartyId == null && p.PatternId == null && p.Item!.IsActive);
        if (list is not null) query = query.Where(p => p.PriceListCode == list);
        if (groups is not null) query = query.Where(p => p.Item!.GroupId != null && groups.Contains(p.Item.GroupId.Value));
        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

        var factor = 1m + command.Percent / 100m;
        var now = Clock.GetUtcNow();
        var changed = new List<ItemPriceRow>();
        foreach (var row in rows)
        {
            var next = decimal.Round(row.Price * factor, command.RoundingScale, MidpointRounding.AwayFromZero);
            if (next == row.Price) continue;
            changed.Add(row);
            if (command.PreviewOnly) continue;
            row.Price = next;
            row.UpdatedAtUtc = now;
        }

        var result = new ItemPriceAdjustmentResult(changed.Select(r => r.ItemId).Distinct().Count(), changed.Count, !command.PreviewOnly && changed.Count > 0);
        if (result.Applied) await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }
}
