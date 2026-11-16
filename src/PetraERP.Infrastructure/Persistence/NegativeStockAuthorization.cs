namespace PetraERP.Infrastructure.Persistence;

/// <summary>
/// A single unit of work's explicit negative-stock exception. It names the exact company/branch and the
/// warehouse/item pairs the operator is allowed to drive below zero, so the flag can never become a blanket
/// bypass for every movement written by the same context.
/// </summary>
internal sealed class NegativeStockAuthorization(Guid companyId, Guid branchId, string reason)
{
    private readonly HashSet<(Guid WarehouseId, Guid ItemId)> lines = [];

    internal Guid CompanyId { get; } = companyId;
    internal Guid BranchId { get; } = branchId;
    internal string Reason { get; } = reason;

    internal void Include(Guid warehouseId, Guid itemId) => lines.Add((warehouseId, itemId));

    internal bool Covers(Guid company, Guid branch, Guid warehouse, Guid item) =>
        CompanyId == company && BranchId == branch && lines.Contains((warehouse, item));
}
