using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Inventory;
using PetraERP.Application.Security;
using PetraERP.Contracts.Inventory;

namespace PetraERP.Api.Endpoints;

/// <summary>Warehouse accounting profile: parent warehouse, inventory / in-transit accounts and default cost center.</summary>
internal static class WarehouseAccountingEndpoints
{
    private const string Path = "/companies/{companyId:guid}/branches/{branchId:guid}/inventory/warehouses/{warehouseId:guid}/accounting";

    internal static void MapWarehouseAccountingEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet(Path, GetAsync).WithName("GetWarehouseAccounting").WithTags("Inventory").WithSummary("Reads the warehouse accounting profile.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.InventoryWarehousesView)).Produces<WarehouseAccountingDto>().ProducesStandardProblems();
        api.MapPut(Path, SetAsync).WithName("SetWarehouseAccounting").WithTags("Inventory").WithSummary("Sets the warehouse accounting profile (validated: same branch, no cycle, postable accounts).")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.InventoryWarehousesManage)).Produces<WarehouseAccountingDto>().ProducesStandardProblems();
    }

    private static async Task<IResult> GetAsync(Guid companyId, Guid branchId, Guid warehouseId, IWarehouseAccountingService service, CancellationToken ct) =>
        TypedResults.Ok(Map(await service.GetAsync(companyId, branchId, warehouseId, ct).ConfigureAwait(false)));

    private static async Task<IResult> SetAsync(Guid companyId, Guid branchId, Guid warehouseId, [FromBody] SetWarehouseAccountingRequest request, IWarehouseAccountingService service, CancellationToken ct) =>
        TypedResults.Ok(Map(await service.SetAsync(companyId, branchId, warehouseId,
            new SetWarehouseAccountingCommand(request.ParentWarehouseId, request.InventoryAccountId, request.InTransitAccountId, request.DefaultCostCenterId), ct).ConfigureAwait(false)));

    private static WarehouseAccountingDto Map(WarehouseAccountingResult r) => new()
    {
        WarehouseId = r.WarehouseId, ParentWarehouseId = r.ParentWarehouseId, InventoryAccountId = r.InventoryAccountId, InventoryAccountCode = r.InventoryAccountCode,
        InTransitAccountId = r.InTransitAccountId, InTransitAccountCode = r.InTransitAccountCode, DefaultCostCenterId = r.DefaultCostCenterId, DefaultCostCenterCode = r.DefaultCostCenterCode,
    };
}
