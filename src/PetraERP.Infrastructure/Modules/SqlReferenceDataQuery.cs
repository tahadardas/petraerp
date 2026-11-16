using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Modules.Abstractions;

namespace PetraERP.Infrastructure.Modules;

/// <summary>
/// Read-only reference lookups for modules, always filtered to the caller's company (warehouses also to the branch).
/// There is deliberately no write method: a module that needs a new account or item asks the operator to create it.
/// </summary>
public sealed class SqlReferenceDataQuery(IModuleLedgerContexts contexts) : IReferenceDataQuery
{
    private const int MaximumTake = 100;

    public Task<IReadOnlyList<ReferenceItem>> FindAccountsAsync(ModuleRequestContext context, string? search, int take, CancellationToken cancellationToken)
    {
        var term = Term(context, search);
        return ReadAsync(context, db => db.Accounts.AsNoTracking()
            .Where(a => a.CompanyId == context.CompanyId && a.IsActive && (term == null || a.Code.Contains(term) || a.NameArabic.Contains(term) || (a.NameEnglish != null && a.NameEnglish.Contains(term))))
            .OrderBy(a => a.Code).Take(Clamp(take))
            .Select(a => new ReferenceItem(a.Id, a.Code, a.NameArabic, a.NameEnglish ?? string.Empty, a.IsActive)), cancellationToken);
    }

    public Task<IReadOnlyList<ReferenceItem>> FindItemsAsync(ModuleRequestContext context, string? search, int take, CancellationToken cancellationToken)
    {
        var term = Term(context, search);
        return ReadAsync(context, db => db.Items.AsNoTracking()
            .Where(i => i.CompanyId == context.CompanyId && i.IsActive && (term == null || i.Code.Contains(term) || i.NameArabic.Contains(term) || (i.NameEnglish != null && i.NameEnglish.Contains(term))))
            .OrderBy(i => i.Code).Take(Clamp(take))
            .Select(i => new ReferenceItem(i.Id, i.Code, i.NameArabic, i.NameEnglish ?? string.Empty, i.IsActive)), cancellationToken);
    }

    public Task<IReadOnlyList<ReferenceItem>> FindPartiesAsync(ModuleRequestContext context, string? search, int take, CancellationToken cancellationToken)
    {
        var term = Term(context, search);
        return ReadAsync(context, db => db.Parties.AsNoTracking()
            .Where(p => p.CompanyId == context.CompanyId && p.IsActive && (term == null || p.Code.Contains(term) || p.NameArabic.Contains(term) || (p.NameEnglish != null && p.NameEnglish.Contains(term))))
            .OrderBy(p => p.Code).Take(Clamp(take))
            .Select(p => new ReferenceItem(p.Id, p.Code, p.NameArabic, p.NameEnglish ?? string.Empty, p.IsActive)), cancellationToken);
    }

    public Task<IReadOnlyList<ReferenceItem>> FindWarehousesAsync(ModuleRequestContext context, string? search, int take, CancellationToken cancellationToken)
    {
        var term = Term(context, search);
        return ReadAsync(context, db => db.Warehouses.AsNoTracking()
            .Where(w => w.CompanyId == context.CompanyId && w.BranchId == context.BranchId && w.IsActive && (term == null || w.Code.Contains(term) || w.NameArabic.Contains(term) || (w.NameEnglish != null && w.NameEnglish.Contains(term))))
            .OrderBy(w => w.Code).Take(Clamp(take))
            .Select(w => new ReferenceItem(w.Id, w.Code, w.NameArabic, w.NameEnglish ?? string.Empty, w.IsActive)), cancellationToken);
    }

    private async Task<IReadOnlyList<ReferenceItem>> ReadAsync(ModuleRequestContext context, Func<FinancialDbContext, IQueryable<ReferenceItem>> query, CancellationToken cancellationToken)
    {
        var read = await contexts.OpenAsync(context, null, cancellationToken).ConfigureAwait(false);
        await using (read.ConfigureAwait(false))
        {
            return await query(read.Db).ToListAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? Term(ModuleRequestContext context, string? search)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.CompanyId == Guid.Empty) throw new BusinessRuleException("module.reference_invalid", "company is required.");
        return string.IsNullOrWhiteSpace(search) ? null : search.Trim();
    }

    private static int Clamp(int take) => Math.Clamp(take, 1, MaximumTake);
}
