using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Patterns;

internal sealed class PatternNumberSequenceRow : ITrackedStorageRow
{
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid PatternId { get; set; }
    public Guid FiscalYearId { get; set; }
    public long LastSequence { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
