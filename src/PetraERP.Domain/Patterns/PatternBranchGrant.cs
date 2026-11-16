namespace PetraERP.Domain.Patterns;

public sealed class PatternBranchGrant
{
    private PatternBranchGrant() { }

    public PatternBranchGrant(Guid id, Guid patternId, Guid companyId, Guid branchId)
    {
        if (id == Guid.Empty) throw new ArgumentException("Grant ID cannot be empty.", nameof(id));
        if (patternId == Guid.Empty) throw new ArgumentException("Pattern ID cannot be empty.", nameof(patternId));
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID cannot be empty.", nameof(companyId));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID cannot be empty.", nameof(branchId));

        Id = id;
        PatternId = patternId;
        CompanyId = companyId;
        BranchId = branchId;
    }

    public Guid Id { get; private set; }
    public Guid PatternId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
}
