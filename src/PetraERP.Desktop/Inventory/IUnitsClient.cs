using PetraERP.ApiClient;

namespace PetraERP.Desktop.Inventory;

/// <summary>Typed-client seam for units of measure and conversions (the unified server conversion engine).</summary>
public interface IUnitsClient
{
    Task<IReadOnlyList<UnitOfMeasureDto>> ListUnitsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<UnitOfMeasureDto> CreateUnitAsync(Guid companyId, Guid branchId, CreateUnitRequest request, CancellationToken cancellationToken);
    Task<UnitOfMeasureDto> UpdateUnitAsync(Guid companyId, Guid branchId, Guid unitId, UpdateUnitRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<UnitConversionDto>> ListConversionsAsync(Guid companyId, Guid branchId, Guid? itemId, CancellationToken cancellationToken);
    Task<UnitConversionDto> CreateConversionAsync(Guid companyId, Guid branchId, CreateUnitConversionRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ItemLookupDto>> LookupItemsAsync(Guid companyId, Guid branchId, string? search, CancellationToken cancellationToken);
}

