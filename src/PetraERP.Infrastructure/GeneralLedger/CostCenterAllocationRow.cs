using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class CostCenterAllocationRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SourceCostCenterId { get; set; }
    public Guid TargetCostCenterId { get; set; }
    public decimal Percentage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigations
    public CostCenterRow? SourceCostCenter { get; set; }
    public CostCenterRow? TargetCostCenter { get; set; }
}
