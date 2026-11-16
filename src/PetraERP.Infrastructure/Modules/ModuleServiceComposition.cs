using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Modules.Abstractions;

namespace PetraERP.Infrastructure.Modules;

/// <summary>
/// A module is activated once but runs for the life of the server, while the database context belongs to one request. These
/// wrappers open a fresh scope for every call, so a module never holds on to a context, a connection or another user's scope.
/// </summary>
public static class ModuleServiceComposition
{
    public static ModuleServiceReferences Create(IServiceScopeFactory scopes) => new(
        new ScopedPosting(scopes), new ScopedInventory(scopes), new ScopedReferences(scopes), new ScopedAudit(scopes));

    private static async Task<T> InScopeAsync<TService, T>(IServiceScopeFactory scopes, Func<TService, Task<T>> action) where TService : notnull
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            return await action(scope.ServiceProvider.GetRequiredService<TService>()).ConfigureAwait(false);
        }
    }

    private sealed class ScopedPosting(IServiceScopeFactory scopes) : IFinancialPostingGateway
    {
        public Task<PostingProposalResult> ValidateAsync(ModuleRequestContext context, PostingProposal proposal, CancellationToken cancellationToken) =>
            InScopeAsync<SqlFinancialPostingGateway, PostingProposalResult>(scopes, g => g.ValidateAsync(context, proposal, cancellationToken));

        public Task<PostingReceipt> PostAsync(ModuleRequestContext context, PostingProposal proposal, CancellationToken cancellationToken) =>
            InScopeAsync<SqlFinancialPostingGateway, PostingReceipt>(scopes, g => g.PostAsync(context, proposal, cancellationToken));

        public Task<PostingReceipt> ReverseAsync(ModuleRequestContext context, Guid journalId, Guid idempotencyKey, string reason, CancellationToken cancellationToken) =>
            InScopeAsync<SqlFinancialPostingGateway, PostingReceipt>(scopes, g => g.ReverseAsync(context, journalId, idempotencyKey, reason, cancellationToken));
    }

    private sealed class ScopedInventory(IServiceScopeFactory scopes) : IInventoryMovementGateway
    {
        public Task<InventoryBalanceResult> QueryBalanceAsync(ModuleRequestContext context, Guid itemId, Guid warehouseId, CancellationToken cancellationToken) =>
            InScopeAsync<SqlInventoryMovementGateway, InventoryBalanceResult>(scopes, g => g.QueryBalanceAsync(context, itemId, warehouseId, cancellationToken));

        public Task<InventoryMovementReceipt> MoveAsync(ModuleRequestContext context, InventoryMovementRequest movement, CancellationToken cancellationToken) =>
            InScopeAsync<SqlInventoryMovementGateway, InventoryMovementReceipt>(scopes, g => g.MoveAsync(context, movement, cancellationToken));
    }

    private sealed class ScopedReferences(IServiceScopeFactory scopes) : IReferenceDataQuery
    {
        public Task<IReadOnlyList<ReferenceItem>> FindAccountsAsync(ModuleRequestContext context, string? search, int take, CancellationToken cancellationToken) =>
            InScopeAsync<SqlReferenceDataQuery, IReadOnlyList<ReferenceItem>>(scopes, q => q.FindAccountsAsync(context, search, take, cancellationToken));

        public Task<IReadOnlyList<ReferenceItem>> FindItemsAsync(ModuleRequestContext context, string? search, int take, CancellationToken cancellationToken) =>
            InScopeAsync<SqlReferenceDataQuery, IReadOnlyList<ReferenceItem>>(scopes, q => q.FindItemsAsync(context, search, take, cancellationToken));

        public Task<IReadOnlyList<ReferenceItem>> FindPartiesAsync(ModuleRequestContext context, string? search, int take, CancellationToken cancellationToken) =>
            InScopeAsync<SqlReferenceDataQuery, IReadOnlyList<ReferenceItem>>(scopes, q => q.FindPartiesAsync(context, search, take, cancellationToken));

        public Task<IReadOnlyList<ReferenceItem>> FindWarehousesAsync(ModuleRequestContext context, string? search, int take, CancellationToken cancellationToken) =>
            InScopeAsync<SqlReferenceDataQuery, IReadOnlyList<ReferenceItem>>(scopes, q => q.FindWarehousesAsync(context, search, take, cancellationToken));
    }

    private sealed class ScopedAudit(IServiceScopeFactory scopes) : IAuditTrail
    {
        public Task RecordAsync(ModuleRequestContext context, string action, string targetType, string targetId, string details, CancellationToken cancellationToken) =>
            InScopeAsync<SqlModuleAuditTrail, bool>(scopes, async a =>
            {
                await a.RecordAsync(context, action, targetType, targetId, details, cancellationToken).ConfigureAwait(false);
                return true;
            });
    }
}
