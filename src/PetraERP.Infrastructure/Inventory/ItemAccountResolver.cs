using Microsoft.EntityFrameworkCore;
using PetraERP.Domain.Patterns;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

public sealed class ItemAccountResolver
{
    private readonly Dictionary<Guid, Guid?> _itemGroups;
    private readonly Dictionary<Guid, Guid?> _groupParents;
    private readonly List<ItemAccountMappingRow> _mappings;
    private readonly PatternConfiguration? _patternConfig;
    private readonly Dictionary<byte, Guid> _branchLinks;

    private ItemAccountResolver(
        Dictionary<Guid, Guid?> itemGroups,
        Dictionary<Guid, Guid?> groupParents,
        List<ItemAccountMappingRow> mappings,
        PatternConfiguration? patternConfig,
        Dictionary<byte, Guid> branchLinks)
    {
        _branchLinks = branchLinks;
        _itemGroups = itemGroups;
        _groupParents = groupParents;
        _mappings = mappings;
        _patternConfig = patternConfig;
    }

    public static Task<ItemAccountResolver> CreateAsync(
        FinancialDbContext context,
        Guid companyId,
        IEnumerable<Guid> itemIds,
        PatternConfiguration? patternConfig,
        CancellationToken cancellationToken = default) =>
        CreateAsync(context, companyId, itemIds, patternConfig, null, null, cancellationToken);

    /// <summary>
    /// Branch-aware resolver. The branch links effective on <paramref name="asOf"/> (the document's accounting date) are read once;
    /// links are effective-dated and never rewritten retroactively, so the same document date always resolves to the same account.
    /// </summary>
    public static async Task<ItemAccountResolver> CreateAsync(
        FinancialDbContext context,
        Guid companyId,
        IEnumerable<Guid> itemIds,
        PatternConfiguration? patternConfig,
        Guid? branchId,
        DateOnly? asOf,
        CancellationToken cancellationToken = default)
    {
        var distinctItemIds = itemIds.Distinct().ToList();

        var items = await context.Items.AsNoTracking()
            .Where(i => i.CompanyId == companyId && distinctItemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.GroupId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var itemGroups = items.ToDictionary(i => i.Id, i => i.GroupId);

        var allGroups = await context.ItemGroups.AsNoTracking()
            .Where(g => g.CompanyId == companyId)
            .Select(g => new { g.Id, g.ParentId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var groupParents = allGroups.ToDictionary(g => g.Id, g => g.ParentId);

        var mappings = await context.ItemAccountMappings.AsNoTracking()
            .Where(m => m.CompanyId == companyId && m.IsActive
                && ((m.OwnerType == 1 && distinctItemIds.Contains(m.OwnerId)) || m.OwnerType == 2))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var branchLinks = new Dictionary<byte, Guid>();
        if (branchId is { } branch && asOf is { } day)
        {
            var links = await context.BranchAccountLinks.AsNoTracking()
                .Where(l => l.CompanyId == companyId && l.BranchId == branch && l.EffectiveFrom <= day && (l.EffectiveTo == null || l.EffectiveTo >= day))
                .OrderBy(l => l.EffectiveFrom).Select(l => new { l.Role, l.AccountId }).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var link in links) branchLinks[link.Role] = link.AccountId; // latest effective start wins
        }

        return new ItemAccountResolver(itemGroups, groupParents, mappings, patternConfig, branchLinks);
    }

    public Guid Resolve(
        Guid itemId,
        string role,
        Guid? fallbackAccountId = null,
        Guid? documentOverrideAccountId = null,
        Guid? partyContractAccountId = null)
    {
        var config = _patternConfig ?? new PatternConfiguration
        {
            AccountMappings = fallbackAccountId.HasValue
                ? [new PatternAccountMapping { Role = role, AccountId = fallbackAccountId.Value, IsRequired = true }]
                : []
        };

        var roleAliases = GetRoleAliases(role);

        // 1. Item-specific contract
        Guid? itemContractAccountId = null;
        foreach (var alias in roleAliases)
        {
            var m = _mappings.FirstOrDefault(x => x.OwnerType == 1 && x.OwnerId == itemId && string.Equals(x.Role, alias, StringComparison.OrdinalIgnoreCase));
            if (m is not null)
            {
                itemContractAccountId = m.AccountId;
                break;
            }
        }

        // 2. Immediate group contract
        Guid? groupContractAccountId = null;
        Guid? ancestorGroupContractAccountId = null;

        if (_itemGroups.TryGetValue(itemId, out var groupId) && groupId.HasValue)
        {
            foreach (var alias in roleAliases)
            {
                var m = _mappings.FirstOrDefault(x => x.OwnerType == 2 && x.OwnerId == groupId.Value && string.Equals(x.Role, alias, StringComparison.OrdinalIgnoreCase));
                if (m is not null)
                {
                    groupContractAccountId = m.AccountId;
                    break;
                }
            }

            // 3. Ancestor group contract
            var currentParentId = _groupParents.GetValueOrDefault(groupId.Value);
            while (currentParentId.HasValue)
            {
                foreach (var alias in roleAliases)
                {
                    var m = _mappings.FirstOrDefault(x => x.OwnerType == 2 && x.OwnerId == currentParentId.Value && string.Equals(x.Role, alias, StringComparison.OrdinalIgnoreCase));
                    if (m is not null)
                    {
                        ancestorGroupContractAccountId = m.AccountId;
                        break;
                    }
                }

                if (ancestorGroupContractAccountId.HasValue)
                    break;

                currentParentId = _groupParents.GetValueOrDefault(currentParentId.Value);
            }
        }

        var resolved = PostingRuleResolver.Resolve(
            role,
            config,
            documentOverrideAccountId: documentOverrideAccountId,
            partyContractAccountId: partyContractAccountId,
            companyDefaultAccountId: fallbackAccountId,
            itemContractAccountId: itemContractAccountId,
            groupContractAccountId: groupContractAccountId,
            ancestorGroupContractAccountId: ancestorGroupContractAccountId,
            branchLinkAccountId: BranchLinkFor(role));

        return resolved.AccountId;
    }

    /// <summary>Posting role → branch account role (Branches/BranchAccountRole numeric values).</summary>
    private Guid? BranchLinkFor(string role)
    {
        byte? key = role switch
        {
            "Cash" => 1, "Bank" => 2, "Revenue" => 3, "SalesReturns" => 4, "Purchases" => 5, "PurchaseReturns" => 6,
            "Inventory" => 7, "COGS" => 8, "Receivable" => 9, "Payable" => 10, _ => null,
        };
        return key is { } k && _branchLinks.TryGetValue(k, out var account) ? account : null;
    }

    private static string[] GetRoleAliases(string role) => role switch
    {
        "Discount" => ["SalesDiscount", "PurchaseDiscount", "Discount"],
        "SalesDiscount" => ["SalesDiscount", "Discount"],
        "PurchaseDiscount" => ["PurchaseDiscount", "Discount"],
        "Tax" => ["SalesTax", "PurchaseTax", "Tax"],
        "SalesTax" => ["SalesTax", "Tax"],
        "PurchaseTax" => ["PurchaseTax", "Tax"],
        _ => [role]
    };
}
