using PetraERP.Infrastructure.Organizations;

namespace PetraERP.Infrastructure.Patterns;

internal sealed class PatternBranchGrantRow
{
    public Guid Id { get; set; }
    public Guid PatternId { get; set; }
    public Guid BranchId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public DocumentPatternRow? Pattern { get; set; }
}
