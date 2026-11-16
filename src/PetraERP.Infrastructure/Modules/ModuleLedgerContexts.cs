using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Workspaces;
using PetraERP.Modules.Abstractions;

namespace PetraERP.Infrastructure.Modules;

/// <summary>A database context opened for one module step; disposing it releases the context only if the factory created it.</summary>
public sealed class ModuleLedgerContext(FinancialDbContext db, IAsyncDisposable? owner) : IAsyncDisposable
{
    internal FinancialDbContext Db { get; } = db;

    public ValueTask DisposeAsync() => owner?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>
/// Opens the annual database for a module call. Financial writes in Petra are authorized per operation (draft, approve,
/// post are different operations with different permissions), so the gateways open one context per step rather than
/// sharing a request's context, and the database layer checks the real user's permission for every step.
/// </summary>
public interface IModuleLedgerContexts
{
    /// <summary>Opens the company's fiscal-year database; <paramref name="operation"/> null opens it for reading only.</summary>
    Task<ModuleLedgerContext> OpenAsync(ModuleRequestContext context, AccountingOperation? operation, CancellationToken cancellationToken);
}

/// <summary>Production implementation: a fresh annual context per step, bound to exactly one write operation.</summary>
public sealed class AccountingModuleLedgerContexts(IAccountingDbContextFactory factory) : IModuleLedgerContexts
{
    public async Task<ModuleLedgerContext> OpenAsync(ModuleRequestContext context, AccountingOperation? operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var db = await factory.CreateDbContextAsync(context.CompanyId, context.FiscalYearId, cancellationToken).ConfigureAwait(false);
        try
        {
            if (operation is { } write)
                db.BindWriteScope(new AccountingWriteScope(context.CompanyId, context.FiscalYearId, context.UserId, write, context.BranchId));
            return new ModuleLedgerContext(db, db);
        }
        catch
        {
            await db.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>Uses one existing context for every step. For hosts and tests whose context does not enforce per-operation write scopes.</summary>
public sealed class SharedModuleLedgerContexts(FinancialDbContext db) : IModuleLedgerContexts
{
    public Task<ModuleLedgerContext> OpenAsync(ModuleRequestContext context, AccountingOperation? operation, CancellationToken cancellationToken) =>
        Task.FromResult(new ModuleLedgerContext(db, null));
}
