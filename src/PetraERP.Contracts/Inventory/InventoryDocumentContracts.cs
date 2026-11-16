using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Inventory;

public enum InventoryDocumentKind { Transfer = 1, Count = 2, InternalIssue = 3 }
public enum InventoryDocumentState { Draft = 0, Posted = 1, Reversed = 2 }
public sealed record InventoryDocumentLineInputDto
{
    [RegularExpression(FinancialDecimalText.MoneyPattern)] public string? OpeningUnitCost { get; init; }
    public Guid ItemId { get; init; }
    public Guid UnitId { get; init; }
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)] public string Quantity { get; init; } = string.Empty;
}
public sealed record SaveInventoryDocumentRequest
{
    [MaxLength(128)] public string? CreationKey { get; init; }
    public InventoryDocumentKind Kind { get; init; }
    public Guid WarehouseId { get; init; }
    public Guid? TargetWarehouseId { get; init; }
    public DateTimeOffset DateUtc { get; init; }
    [Required, MaxLength(500)] public string Reason { get; init; } = string.Empty;
    public Guid? CostCenterId { get; init; }
    public Guid? InventoryAccountId { get; init; }
    public Guid? OffsetAccountId { get; init; }
    public string? ExpectedVersion { get; init; }
    [Required] public IReadOnlyList<InventoryDocumentLineInputDto> Lines { get; init; } = [];
}
public sealed record InventoryDocumentActionRequest
{
    [Required] public string ExpectedVersion { get; init; } = string.Empty;
    [Required, MaxLength(128)] public string IdempotencyKey { get; init; } = string.Empty;
    [Required, MaxLength(500)] public string Reason { get; init; } = string.Empty;
}
public sealed record InventoryDocumentLineDto
{
    public string? OpeningUnitCost { get; init; }
    public Guid Id { get; init; }
    public Guid ItemId { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public Guid UnitId { get; init; }
    public string Quantity { get; init; } = string.Empty;
    public string BaseQuantity { get; init; } = string.Empty;
    public string? SnapshotQuantity { get; init; }
    public long? SnapshotSequence { get; init; }
    public string? Cost { get; init; }
}
public sealed record InventoryDocumentDto
{
    public bool IsLegacy { get; init; }
    public string? PostReason { get; init; }
    public string? ReverseReason { get; init; }
    public Guid? ReversedBy { get; init; }
    public DateTimeOffset? ReversedAtUtc { get; init; }
    public Guid Id { get; init; }
    public string? Number { get; init; }
    public InventoryDocumentKind Kind { get; init; }
    public InventoryDocumentState State { get; init; }
    public Guid WarehouseId { get; init; }
    public Guid? TargetWarehouseId { get; init; }
    public DateTimeOffset DateUtc { get; init; }
    public string Reason { get; init; } = string.Empty;
    public Guid? CostCenterId { get; init; }
    public Guid? InventoryAccountId { get; init; }
    public Guid? OffsetAccountId { get; init; }
    public Guid? JournalId { get; init; }
    public Guid? ReversalJournalId { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public Guid? PostedBy { get; init; }
    public DateTimeOffset? PostedAtUtc { get; init; }
    public string Version { get; init; } = string.Empty;
    public IReadOnlyList<InventoryDocumentLineDto> Lines { get; init; } = [];
}
public sealed record InventoryDocumentPageResponse
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public IReadOnlyList<InventoryDocumentDto> Items { get; init; } = [];
}
public sealed record InventoryBalanceDto
{
    public string? AuditQuantity { get; init; }
    public string? AuditValue { get; init; }
    public bool? MatchesAudit { get; init; }
    public string WarehouseCode { get; init; } = string.Empty;
    public string ItemCode { get; init; } = string.Empty;
    public Guid WarehouseId { get; init; }
    public Guid ItemId { get; init; }
    public string Quantity { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public long LastSequence { get; init; }
}
public sealed record InventoryMovementAuditDto
{
    public string WarehouseCode { get; init; } = string.Empty;
    public string ItemCode { get; init; } = string.Empty;
    public Guid Id { get; init; }
    public long Sequence { get; init; }
    public Guid WarehouseId { get; init; }
    public Guid ItemId { get; init; }
    public StockMovementKind Kind { get; init; }
    public string Quantity { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? QuantityBefore { get; init; }
    public string? QuantityAfter { get; init; }
    public string? ValueBefore { get; init; }
    public string? ValueAfter { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public Guid? DocumentId { get; init; }
    public string? Number { get; init; }
    public Guid ActorId { get; init; }
    public DateTimeOffset DateUtc { get; init; }
    public DateTimeOffset? RecordedAtUtc { get; init; }
    public string? Reason { get; init; }
    public Guid? ReversesMovementId { get; init; }
}
public sealed record InventoryMovementAuditPageResponse
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public IReadOnlyList<InventoryMovementAuditDto> Items { get; init; } = [];
}
