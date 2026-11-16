using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Inventory;

public sealed class Warehouse
{
    private Warehouse() { }

    public Warehouse(
        Guid id,
        Guid companyId,
        Guid branchId,
        EntityCode code,
        BilingualName name,
        string? location = null,
        bool isActive = true)
    {
        if (id == Guid.Empty) throw new ArgumentException("Warehouse ID cannot be empty.", nameof(id));
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID cannot be empty.", nameof(companyId));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID cannot be empty.", nameof(branchId));
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);

        Id = id;
        CompanyId = companyId;
        BranchId = branchId;
        Code = code;
        Name = name;
        Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        IsActive = isActive;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public EntityCode Code { get; private set; } = null!;
    public BilingualName Name { get; private set; } = null!;
    public string? Location { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Update(BilingualName name, string? location)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
    }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;
}
