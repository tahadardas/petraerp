using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Inventory;

public sealed class ItemGroup
{
    private ItemGroup() { }

    public ItemGroup(
        Guid id,
        Guid companyId,
        EntityCode code,
        BilingualName name,
        Guid? parentId = null,
        bool isActive = true,
        string? description = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Group ID cannot be empty.", nameof(id));
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID cannot be empty.", nameof(companyId));
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        if (parentId.HasValue && parentId.Value == id)
            throw new ArgumentException("Group cannot be its own parent.", nameof(parentId));

        Id = id;
        CompanyId = companyId;
        Code = code;
        Name = name;
        ParentId = parentId;
        IsActive = isActive;
        Description = description;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public EntityCode Code { get; private set; } = null!;
    public BilingualName Name { get; private set; } = null!;
    public Guid? ParentId { get; private set; }
    public bool IsActive { get; private set; }
    public string? Description { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Update(BilingualName name, Guid? parentId, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (parentId.HasValue && parentId.Value == Id)
            throw new ArgumentException("Group cannot be its own parent.", nameof(parentId));

        Name = name;
        ParentId = parentId;
        Description = description;
    }

    public void ValidateNoCycle(Func<Guid, Guid?> getParentId)
    {
        ArgumentNullException.ThrowIfNull(getParentId);
        if (!ParentId.HasValue) return;

        var visited = new HashSet<Guid> { Id };
        var current = ParentId;
        while (current.HasValue)
        {
            if (!visited.Add(current.Value))
            {
                throw RuleViolation.Create("inventory.circular_group", "Circular item group hierarchy detected.");
            }
            current = getParentId(current.Value);
        }
    }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;
}
