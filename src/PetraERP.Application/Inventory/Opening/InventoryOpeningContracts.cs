namespace PetraERP.Application.Inventory.Opening;

public sealed record OpeningStockLineInput(
    Guid WarehouseId,
    Guid ItemId,
    Guid UnitId,
    decimal Quantity,
    decimal TotalValue,
    string? Notes);

public sealed record CreateOpeningStockBatchCommand(
    Guid BranchId,
    DateTimeOffset OpeningDateUtc,
    string? SourceReference,
    string IdempotencyKey,
    IReadOnlyList<OpeningStockLineInput> Lines);

public sealed record OpeningStockBatchDto(
    Guid BatchId,
    Guid CompanyId,
    Guid BranchId,
    DateTimeOffset OpeningDateUtc,
    string? SourceReference,
    decimal TotalQuantity,
    decimal TotalValue,
    IReadOnlyList<OpeningStockLineDto> Lines);

public sealed record OpeningStockLineDto(
    Guid MovementId,
    Guid WarehouseId,
    string WarehouseName,
    Guid ItemId,
    string ItemCode,
    string ItemNameArabic,
    Guid UnitId,
    string UnitCode,
    decimal Quantity,
    decimal UnitCost,
    decimal TotalCost,
    string? Notes);

public interface IInventoryOpeningService
{
    Task<OpeningStockBatchDto> PostOpeningStockBatchAsync(Guid companyId, Guid actorId, CreateOpeningStockBatchCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpeningStockLineDto>> GetOpeningStockAsync(Guid companyId, Guid? warehouseId, CancellationToken cancellationToken = default);
}
