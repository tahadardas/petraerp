using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.Organizations;

namespace PetraERP.Domain.GeneralLedger;

/// <summary>Journal-series number allocated by the future transactional application service.</summary>
public sealed record JournalPostingNumber
{
    private JournalPostingNumber(Guid companyId, Guid branchId, Guid fiscalYearId, long sequence)
    {
        CompanyId = companyId;
        BranchId = branchId;
        FiscalYearId = fiscalYearId;
        Sequence = sequence;
    }

    public Guid CompanyId { get; }
    public Guid BranchId { get; }
    public Guid FiscalYearId { get; }
    public long Sequence { get; }

    public static JournalPostingNumber Create(BranchScope scope, FiscalYear year, long sequence)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(year);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        if (scope.CompanyId != year.CompanyId)
        {
            throw new ArgumentException("Number scope and fiscal year must belong to the same company.", nameof(year));
        }

        return new JournalPostingNumber(scope.CompanyId, scope.BranchId, year.Id, sequence);
    }

    public static JournalPostingNumber Reconstitute(Guid companyId, Guid branchId, Guid fiscalYearId, long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        return new JournalPostingNumber(companyId, branchId, fiscalYearId, sequence);
    }
}
