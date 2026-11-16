using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Patterns;

internal sealed class DocumentPatternRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public int OperationKind { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameArabic { get; set; } = string.Empty;
    public string? NameEnglish { get; set; }
    public string ShortCode { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? Group { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public Guid? ActiveRevisionId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<PatternRevisionRow> Revisions { get; set; } = [];
    public ICollection<PatternBranchGrantRow> BranchGrants { get; set; } = [];
}
