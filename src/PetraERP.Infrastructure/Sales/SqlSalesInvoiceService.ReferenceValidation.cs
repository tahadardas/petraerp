using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Sales;

namespace PetraERP.Infrastructure.Sales;

public sealed partial class SqlSalesInvoiceService
{
    /// <summary>Validate all supplied header/line references before mutating any tracked invoice state.</summary>
    private async Task ValidateDraftReferencesAsync(Guid companyId, Guid branchId, Guid warehouseId, Guid? costCenterId,
        IReadOnlyList<CreateSalesInvoiceLineCommand>? lines, CancellationToken cancellationToken)
    {
        var warehouses = (lines ?? []).Where(line => line.WarehouseId.HasValue).Select(line => line.WarehouseId!.Value)
            .Append(warehouseId).Distinct().ToArray();
        var knownWarehouses = await context.Warehouses.AsNoTracking()
            .CountAsync(row => warehouses.Contains(row.Id) && row.CompanyId == companyId && row.BranchId == branchId && row.IsActive,
                cancellationToken).ConfigureAwait(false);
        if (knownWarehouses != warehouses.Length)
            throw new BusinessRuleException("sales.warehouse_invalid", "Every warehouse must be active in the invoice company and branch.");
        var centers = (lines ?? []).Where(line => line.CostCenterId.HasValue).Select(line => line.CostCenterId!.Value)
            .Concat(costCenterId.HasValue ? [costCenterId.Value] : Array.Empty<Guid>()).Distinct().ToArray();
        if (centers.Length == 0) return;
        var knownCenters = await context.CostCenters.AsNoTracking()
            .CountAsync(row => centers.Contains(row.Id) && row.CompanyId == companyId && row.IsActive, cancellationToken).ConfigureAwait(false);
        if (knownCenters != centers.Length)
            throw new BusinessRuleException("sales.cost_center_invalid", "Every cost center must be active in the invoice company.");
    }
}
