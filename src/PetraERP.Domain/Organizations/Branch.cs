using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Organizations;

public sealed class Branch
{
    private Branch(Guid id, Guid companyId, EntityCode code, BilingualName name, bool isActive)
    {
        Id = id;
        CompanyId = companyId;
        Code = code;
        Name = name;
        IsActive = isActive;
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public EntityCode Code { get; }
    public BilingualName Name { get; }
    public bool IsActive { get; }

    public static Branch Create(Company company, EntityCode code, BilingualName name, bool isActive = true)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        return new Branch(Guid.CreateVersion7(), company.Id, code, name, isActive);
    }

    public static Branch Reconstitute(Guid id, Guid companyId, EntityCode code, BilingualName name, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        return new Branch(id, companyId, code, name, isActive);
    }
}
