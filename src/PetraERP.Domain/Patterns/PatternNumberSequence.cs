namespace PetraERP.Domain.Patterns;

public sealed class PatternNumberSequence
{
    private PatternNumberSequence() { }

    public PatternNumberSequence(Guid id, Guid companyId, Guid branchId, Guid patternId, Guid? fiscalYearId, long initialNumber = 0)
    {
        if (id == Guid.Empty) throw new ArgumentException("Sequence ID cannot be empty.", nameof(id));
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID cannot be empty.", nameof(companyId));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID cannot be empty.", nameof(branchId));
        if (patternId == Guid.Empty) throw new ArgumentException("Pattern ID cannot be empty.", nameof(patternId));

        Id = id;
        CompanyId = companyId;
        BranchId = branchId;
        PatternId = patternId;
        FiscalYearId = fiscalYearId;
        CurrentNumber = initialNumber;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid PatternId { get; private set; }
    public Guid? FiscalYearId { get; private set; }
    public long CurrentNumber { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public long Increment() => ++CurrentNumber;
}
