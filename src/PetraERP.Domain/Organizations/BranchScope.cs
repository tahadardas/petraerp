using PetraERP.Domain.Primitives;
namespace PetraERP.Domain.Organizations;

/// <summary>Validated ownership/activity; this is not proof of user authorization.</summary>
public sealed record BranchScope
{
    private BranchScope(Guid companyId, Guid branchId)
    {
        CompanyId = companyId;
        BranchId = branchId;
    }

    public Guid CompanyId { get; }
    public Guid BranchId { get; }

    public static BranchScope Create(Company company, Branch branch)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(branch);
        if (company.Id != branch.CompanyId)
        {
            throw new ArgumentException("Branch belongs to a different company.", nameof(branch));
        }

        if (!company.IsActive || !branch.IsActive)
        {
            throw RuleViolation.Create(RuleViolation.ScopeInactive, "Inactive companies or branches cannot provide a posting scope.");
        }

        return new BranchScope(company.Id, branch.Id);
    }

    public static BranchScope Reconstitute(Guid companyId, Guid branchId) =>
        new(companyId, branchId);
}
