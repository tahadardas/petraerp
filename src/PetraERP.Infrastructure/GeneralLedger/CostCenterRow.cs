using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class CostCenterRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? ParentId { get; set; }
    public byte CenterType { get; set; } = 1; // 1 = Ordinary, 2 = Parent, 3 = Aggregation, 4 = Distribution
    public required string Code { get; set; }
    public required string NameArabic { get; set; }
    public string? NameEnglish { get; set; }
    public string? Description { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? ResponsiblePerson { get; set; }
    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigations
    public CostCenterRow? Parent { get; set; }
    public ICollection<CostCenterRow> Children { get; set; } = [];
}
