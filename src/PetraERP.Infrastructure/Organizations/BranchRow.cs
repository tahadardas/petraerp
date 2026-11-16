using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Organizations;

internal sealed class BranchRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string NameArabic { get; set; }
    public string? NameEnglish { get; set; }
    public bool IsActive { get; set; }
    public Guid? ParentBranchId { get; set; }
    public string? NumberPrefix { get; set; }
    public DateOnly? OpenedOn { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public Guid? DefaultWarehouseId { get; set; }
    public Guid? DefaultCashAccountId { get; set; }
    public Guid? DefaultBankAccountId { get; set; }
    public Guid? DefaultCostCenterId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
