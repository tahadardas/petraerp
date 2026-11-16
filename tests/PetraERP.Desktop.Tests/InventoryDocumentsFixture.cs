using PetraERP.ApiClient;
using PetraERP.Desktop.Inventory;

namespace PetraERP.Desktop.Tests;

/// <summary>Empty warehouse-documents port that records the movement and balance filters; stock rules are proven against SQL.</summary>
internal sealed class InventoryDocumentsFixture : IInventoryDocumentsClient
{
    internal List<Guid?> MovementWarehouseQueries { get; } = [];
    internal List<Guid?> BalanceWarehouseQueries { get; } = [];

    public Task<InventoryDocumentPageResponse> ListAsync(Guid company, Guid branch, string? search, int page, CancellationToken token) =>
        Task.FromResult(new InventoryDocumentPageResponse { Page = page, PageSize = 50, TotalCount = 0, Items = [] });

    public Task<InventoryDocumentDto> GetAsync(Guid company, Guid branch, Guid id, CancellationToken token) => throw new NotSupportedException();

    public Task<InventoryDocumentDto> SaveAsync(Guid company, Guid branch, Guid? id, SaveInventoryDocumentRequest request, CancellationToken token) => throw new NotSupportedException();

    public Task<InventoryDocumentDto> PostAsync(Guid company, Guid branch, Guid id, InventoryDocumentActionRequest request, CancellationToken token) => throw new NotSupportedException();

    public Task<InventoryDocumentDto> ReverseAsync(Guid company, Guid branch, Guid id, InventoryDocumentActionRequest request, CancellationToken token) => throw new NotSupportedException();

    public Task<IReadOnlyList<InventoryBalanceDto>> BalancesAsync(Guid company, Guid branch, Guid? warehouse, Guid? item, CancellationToken token)
    {
        BalanceWarehouseQueries.Add(warehouse);
        return Task.FromResult<IReadOnlyList<InventoryBalanceDto>>([]);
    }

    public Task<InventoryMovementAuditPageResponse> MovementsAsync(Guid company, Guid branch, Guid? warehouse, Guid? item,
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int page, CancellationToken token)
    {
        MovementWarehouseQueries.Add(warehouse);
        return Task.FromResult(new InventoryMovementAuditPageResponse { Page = page, PageSize = 50, TotalCount = 0, Items = [] });
    }
}
