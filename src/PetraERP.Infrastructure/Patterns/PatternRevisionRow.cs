using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Patterns;

internal sealed class PatternRevisionRow
{
    public Guid Id { get; set; }
    public Guid PatternId { get; set; }
    public int RevisionNumber { get; set; }
    public int State { get; set; }
    public DateTimeOffset? EffectiveFromUtc { get; set; }
    public DateTimeOffset? EffectiveToUtc { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public Guid CreatedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }

    public DocumentPatternRow? Pattern { get; set; }
}
