using PetraERP.ApiClient;
using PetraERP.Desktop.Inventory;

namespace PetraERP.Desktop.Connection;

public sealed class UnitsClient(DesktopSession session) : IUnitsClient
{
    public async Task<IReadOnlyList<UnitOfMeasureDto>> ListUnitsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        (await session.Api.ListUnitsAsync(companyId, branchId, cancellationToken).ConfigureAwait(false)).ToArray();

    public Task<UnitOfMeasureDto> CreateUnitAsync(Guid companyId, Guid branchId, CreateUnitRequest request, CancellationToken cancellationToken) =>
        session.Api.CreateUnitAsync(companyId, branchId, request, cancellationToken);

    public Task<UnitOfMeasureDto> UpdateUnitAsync(Guid companyId, Guid branchId, Guid unitId, UpdateUnitRequest request, CancellationToken cancellationToken) =>
        session.Api.UpdateUnitAsync(companyId, branchId, unitId, request, cancellationToken);

    public async Task<IReadOnlyList<UnitConversionDto>> ListConversionsAsync(Guid companyId, Guid branchId, Guid? itemId, CancellationToken cancellationToken) =>
        (await session.Api.ListUnitConversionsAsync(companyId, branchId, itemId, cancellationToken).ConfigureAwait(false)).ToArray();

    public Task<UnitConversionDto> CreateConversionAsync(Guid companyId, Guid branchId, CreateUnitConversionRequest request, CancellationToken cancellationToken) =>
        session.Api.CreateUnitConversionAsync(companyId, branchId, request, cancellationToken);

    public async Task<IReadOnlyList<ItemLookupDto>> LookupItemsAsync(Guid companyId, Guid branchId, string? search, CancellationToken cancellationToken) =>
        (await session.Api.LookupItemsAsync(companyId, branchId, 100, search, cancellationToken).ConfigureAwait(false)).ToArray();
}
