using PetraERP.Domain.Inventory;

namespace PetraERP.Application.Inventory;

public sealed record InventoryDocumentScope(Guid CompanyId, Guid BranchId, Guid UserId);
public sealed record InventoryDocumentLineInput(Guid ItemId, Guid UnitId, decimal Quantity)
{
    public decimal? OpeningUnitCost { get; init; }
}
public sealed record SaveInventoryDocumentCommand(InventoryDocumentKind Kind, Guid WarehouseId, Guid? TargetWarehouseId,
    DateTimeOffset DateUtc, string Reason, Guid? CostCenterId, Guid? InventoryAccountId, Guid? OffsetAccountId,
    IReadOnlyList<InventoryDocumentLineInput> Lines, string? ExpectedVersion)
{
    public string? CreationKey { get; init; }
}
public sealed record InventoryDocumentAction(string ExpectedVersion, string IdempotencyKey, string Reason);
public sealed record InventoryDocumentLineResult(Guid Id, Guid ItemId, string ItemCode, Guid UnitId, decimal Quantity,
    decimal BaseQuantity, decimal? SnapshotQuantity, long? SnapshotSequence, decimal? Cost)
{
    public decimal? OpeningUnitCost { get; init; }
}
public sealed record InventoryDocumentResult(Guid Id, string? Number, InventoryDocumentKind Kind, InventoryDocumentState State,
    Guid WarehouseId, Guid? TargetWarehouseId, DateTimeOffset DateUtc, string Reason, Guid? CostCenterId, Guid? InventoryAccountId,
    Guid? OffsetAccountId, Guid? JournalId, Guid? ReversalJournalId, Guid CreatedBy, DateTimeOffset CreatedAtUtc,
    Guid? PostedBy, DateTimeOffset? PostedAtUtc, string Version, IReadOnlyList<InventoryDocumentLineResult> Lines)
{
    public bool IsLegacy { get; init; }
    public string? PostReason { get; init; }
    public string? ReverseReason { get; init; }
    public Guid? ReversedBy { get; init; }
    public DateTimeOffset? ReversedAtUtc { get; init; }
}
public sealed record InventoryDocumentPage(int Page, int PageSize, int TotalCount, IReadOnlyList<InventoryDocumentResult> Items);
public sealed record InventoryBalanceResult(Guid WarehouseId, Guid ItemId, decimal Quantity, decimal Value, long LastSequence)
{
    public decimal? AuditQuantity { get; init; }
    public decimal? AuditValue { get; init; }
    public bool? MatchesAudit { get; init; }
    public string WarehouseCode { get; init; } = string.Empty;
    public string ItemCode { get; init; } = string.Empty;
}
public sealed record InventoryMovementAuditResult(Guid Id, long Sequence, Guid WarehouseId, Guid ItemId, StockMovementKind Kind,
    decimal Quantity, decimal Value, decimal? QuantityBefore, decimal? QuantityAfter, decimal? ValueBefore, decimal? ValueAfter,
    string DocumentType, Guid? DocumentId, string? Number, Guid ActorId, DateTimeOffset DateUtc, DateTimeOffset? RecordedAtUtc,
    string? Reason, Guid? ReversesMovementId)
{
    public string WarehouseCode { get; init; } = string.Empty;
    public string ItemCode { get; init; } = string.Empty;
}
public sealed record InventoryMovementAuditPage(int Page, int PageSize, int TotalCount, IReadOnlyList<InventoryMovementAuditResult> Items);

public interface IInventoryDocumentService
{
    Task<InventoryDocumentPage> ListAsync(InventoryDocumentScope scope, string? search, int page, int pageSize, CancellationToken token);
    Task<InventoryDocumentResult> GetAsync(InventoryDocumentScope scope, Guid id, CancellationToken token);
    Task<InventoryDocumentResult> SaveAsync(InventoryDocumentScope scope, Guid? id, SaveInventoryDocumentCommand command, CancellationToken token);
    Task<InventoryDocumentResult> PostAsync(InventoryDocumentScope scope, Guid id, InventoryDocumentAction command, CancellationToken token);
    Task<InventoryDocumentResult> ReverseAsync(InventoryDocumentScope scope, Guid id, InventoryDocumentAction command, CancellationToken token);
    Task<IReadOnlyList<InventoryBalanceResult>> BalancesAsync(InventoryDocumentScope scope, Guid? warehouseId, Guid? itemId, CancellationToken token);
    Task<InventoryMovementAuditPage> MovementsAsync(InventoryDocumentScope scope, Guid? warehouseId, Guid? itemId,
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int page, int pageSize, CancellationToken token);
}
