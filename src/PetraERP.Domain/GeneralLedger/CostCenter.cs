using PetraERP.Domain.Organizations;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.GeneralLedger;

public sealed class CostCenter
{
    private CostCenter(Guid id, Guid companyId, EntityCode code, BilingualName name, bool isActive,
        Guid? parentId = null, byte centerType = 1, string? description = null, DateOnly? startDate = null, DateOnly? endDate = null, string? responsiblePerson = null)
    {
        Id = id;
        CompanyId = companyId;
        Code = code;
        Name = name;
        IsActive = isActive;
        ParentId = parentId;
        CenterType = centerType;
        Description = description;
        StartDate = startDate;
        EndDate = endDate;
        ResponsiblePerson = responsiblePerson;
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public EntityCode Code { get; }
    public BilingualName Name { get; }
    public bool IsActive { get; }
    public Guid? ParentId { get; }
    public byte CenterType { get; } // 1 = Ordinary, 2 = Parent, 3 = Aggregation, 4 = Distribution
    public string? Description { get; }
    public DateOnly? StartDate { get; }
    public DateOnly? EndDate { get; }
    public string? ResponsiblePerson { get; }

    public static CostCenter Create(Company company, EntityCode code, BilingualName name, bool isActive = true,
        Guid? parentId = null, byte centerType = 1, string? description = null, DateOnly? startDate = null, DateOnly? endDate = null, string? responsiblePerson = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        if (parentId.HasValue && parentId.Value == Guid.Empty) parentId = null;
        if (centerType is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(centerType));
        return new CostCenter(Guid.CreateVersion7(), company.Id, code, name, isActive, parentId, centerType, description, startDate, endDate, responsiblePerson);
    }

    /// <summary>The code is fixed once journal lines reference the center; names stay editable.</summary>
    public CostCenter Revise(EntityCode code, BilingualName name, bool hasJournalLines,
        Guid? parentId = null, byte centerType = 1, string? description = null, DateOnly? startDate = null, DateOnly? endDate = null, string? responsiblePerson = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        if (hasJournalLines && code != Code)
        {
            throw RuleViolation.Create(RuleViolation.CostCenterInUse, "The code is fixed once journal lines reference the cost center.");
        }
        if (parentId.HasValue && parentId.Value == Id)
        {
            throw RuleViolation.Create("ledger.cost_center_parent_self", "A cost center cannot be its own parent.");
        }

        return new CostCenter(Id, CompanyId, code, name, IsActive, parentId, centerType, description, startDate, endDate, responsiblePerson);
    }

    public CostCenter WithActivation(bool isActive) => new(Id, CompanyId, Code, Name, isActive, ParentId, CenterType, Description, StartDate, EndDate, ResponsiblePerson);

    public static CostCenter Reconstitute(Guid id, Guid companyId, EntityCode code, BilingualName name, bool isActive,
        Guid? parentId = null, byte centerType = 1, string? description = null, DateOnly? startDate = null, DateOnly? endDate = null, string? responsiblePerson = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        return new CostCenter(id, companyId, code, name, isActive, parentId, centerType, description, startDate, endDate, responsiblePerson);
    }
}
