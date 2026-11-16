using PetraERP.ApiClient;
using PetraERP.Desktop.Inventory;

namespace PetraERP.Desktop.Connection;

public sealed class InventoryDocumentsClient(DesktopSession session) : IInventoryDocumentsClient
{
    public Task<InventoryDocumentPageResponse> ListAsync(Guid company, Guid branch, string? search, int page, CancellationToken token) =>
        session.Api.ListInventoryDocumentsAsync(company, branch, search, page, 50, token);
    public Task<InventoryDocumentDto> GetAsync(Guid company, Guid branch, Guid id, CancellationToken token) =>
        session.Api.GetInventoryDocumentAsync(company, branch, id, token);
    public Task<InventoryDocumentDto> SaveAsync(Guid company, Guid branch, Guid? id, SaveInventoryDocumentRequest request, CancellationToken token) =>
        id.HasValue ? session.Api.UpdateInventoryDocumentAsync(company, branch, id.Value, request, token)
            : session.Api.CreateInventoryDocumentAsync(company, branch, request, token);
    public Task<InventoryDocumentDto> PostAsync(Guid company, Guid branch, Guid id, InventoryDocumentActionRequest request, CancellationToken token) =>
        session.Api.PostInventoryDocumentAsync(company, branch, id, request, token);
    public Task<InventoryDocumentDto> ReverseAsync(Guid company, Guid branch, Guid id, InventoryDocumentActionRequest request, CancellationToken token) =>
        session.Api.ReverseInventoryDocumentAsync(company, branch, id, request, token);
    public Task<IReadOnlyList<InventoryBalanceDto>> BalancesAsync(Guid company, Guid branch, Guid? warehouse, Guid? item, CancellationToken token) =>
        session.Api.ListInventoryBalancesAsync(company, branch, warehouse, item, token);
    public Task<InventoryMovementAuditPageResponse> MovementsAsync(Guid company, Guid branch, Guid? warehouse, Guid? item,
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int page, CancellationToken token) =>
        session.Api.ListInventoryMovementAuditAsync(company, branch, warehouse, item, fromUtc, toUtc, page, 100, token);
}
