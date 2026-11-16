using PetraERP.ApiClient;

namespace PetraERP.Desktop.Inventory;

public interface IInventoryDocumentsClient
{
    Task<InventoryDocumentPageResponse> ListAsync(Guid company, Guid branch, string? search, int page, CancellationToken token);
    Task<InventoryDocumentDto> GetAsync(Guid company, Guid branch, Guid id, CancellationToken token);
    Task<InventoryDocumentDto> SaveAsync(Guid company, Guid branch, Guid? id, SaveInventoryDocumentRequest request, CancellationToken token);
    Task<InventoryDocumentDto> PostAsync(Guid company, Guid branch, Guid id, InventoryDocumentActionRequest request, CancellationToken token);
    Task<InventoryDocumentDto> ReverseAsync(Guid company, Guid branch, Guid id, InventoryDocumentActionRequest request, CancellationToken token);
    Task<IReadOnlyList<InventoryBalanceDto>> BalancesAsync(Guid company, Guid branch, Guid? warehouse, Guid? item, CancellationToken token);
    Task<InventoryMovementAuditPageResponse> MovementsAsync(Guid company, Guid branch, Guid? warehouse, Guid? item,
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int page, CancellationToken token);
}
