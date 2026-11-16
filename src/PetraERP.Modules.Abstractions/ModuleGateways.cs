namespace PetraERP.Modules.Abstractions;

/// <summary>
/// A module never writes journals, ledgers or balances directly. Every financial effect goes through this gateway so the
/// host can enforce the same company/branch scope, authorization, closed-period, idempotency and audit rules that the
/// built-in documents obey.
/// </summary>
public interface IFinancialPostingGateway
{
    /// <summary>Validates a proposed journal without writing anything. The result is advisory, not a reservation.</summary>
    Task<PostingProposalResult> ValidateAsync(ModuleRequestContext context, PostingProposal proposal, CancellationToken cancellationToken);

    /// <summary>
    /// Creates, approves and posts through the core engine. Replaying the same <see cref="PostingProposal.IdempotencyKey"/>
    /// for the same company and year must return the original receipt, never a second financial effect.
    /// </summary>
    Task<PostingReceipt> PostAsync(ModuleRequestContext context, PostingProposal proposal, CancellationToken cancellationToken);

    /// <summary>Posted records are immutable: corrections are linked reversals, and only this method performs them.</summary>
    Task<PostingReceipt> ReverseAsync(
        ModuleRequestContext context,
        Guid journalId,
        Guid idempotencyKey,
        string reason,
        CancellationToken cancellationToken);
}

public sealed record PostingProposalLine(
    Guid AccountId,
    string CurrencyCode,
    decimal Debit,
    decimal Credit,
    decimal Rate,
    string Description,
    Guid? CostCenterId);

public sealed record PostingProposal(
    DateOnly AccountingDate,
    string Description,
    string ExternalReference,
    Guid IdempotencyKey,
    IReadOnlyList<PostingProposalLine> Lines);

public sealed record PostingProposalResult(bool IsValid, string? Reason, IReadOnlyList<string> Errors);

public sealed record PostingReceipt(Guid JournalId, DateTimeOffset PostedAtUtc, long PostingSequence, bool WasAlreadyPosted);

/// <summary>Stock movements are equally gated: a module cannot insert into inventory tables or restate a cost layer.</summary>
public interface IInventoryMovementGateway
{
    Task<InventoryBalanceResult> QueryBalanceAsync(
        ModuleRequestContext context,
        Guid itemId,
        Guid warehouseId,
        CancellationToken cancellationToken);

    Task<InventoryMovementReceipt> MoveAsync(
        ModuleRequestContext context,
        InventoryMovementRequest movement,
        CancellationToken cancellationToken);
}

public sealed record InventoryMovementRequest(
    Guid ItemId,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    Guid UnitId,
    decimal Quantity,
    DateOnly MovementDate,
    string Reason,
    Guid IdempotencyKey);

public sealed record InventoryBalanceResult(IReadOnlyList<InventoryBalanceLine> Lines);

public sealed record InventoryBalanceLine(Guid WarehouseId, Guid UnitId, decimal Quantity, decimal Value, decimal? UnitCost);

public sealed record InventoryMovementReceipt(Guid MovementId, DateTimeOffset RecordedAtUtc, bool WasAlreadyRecorded);

/// <summary>Read-only reference data, scoped to the caller's company and branch. Writes are not offered by design.</summary>
public interface IReferenceDataQuery
{
    Task<IReadOnlyList<ReferenceItem>> FindAccountsAsync(ModuleRequestContext context, string? search, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceItem>> FindItemsAsync(ModuleRequestContext context, string? search, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceItem>> FindPartiesAsync(ModuleRequestContext context, string? search, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceItem>> FindWarehousesAsync(ModuleRequestContext context, string? search, int take, CancellationToken cancellationToken);
}

public sealed record ReferenceItem(Guid Id, string Code, string NameArabic, string NameEnglish, bool IsActive);

/// <summary>Every module action that changes or reads sensitive data is written to the same audit trail as core actions.</summary>
public interface IAuditTrail
{
    Task RecordAsync(ModuleRequestContext context, string action, string targetType, string targetId, string details, CancellationToken cancellationToken);
}
