using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>
/// Atomic sequence counter for journal posting numbers, scoped to company + branch + fiscal year.
/// The counter is incremented via an atomic UPDATE with OUTPUT; no application-level race condition
/// is permitted. This row is created on first posting in a given scope and year.
/// </summary>
internal sealed class PostingSequenceRow : ITrackedStorageRow
{
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid FiscalYearId { get; set; }

    /// <summary>Current highest allocated sequence number. Starts at 0 (unallocated).</summary>
    public long LastSequence { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
