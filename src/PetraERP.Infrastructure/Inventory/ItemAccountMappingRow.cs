using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class ItemAccountMappingRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public byte OwnerType { get; set; } // 1 = Item, 2 = ItemGroup
    public Guid OwnerId { get; set; }
    public string Role { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public Guid? PatternId { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigations
    public AccountRow? Account { get; set; }
}
