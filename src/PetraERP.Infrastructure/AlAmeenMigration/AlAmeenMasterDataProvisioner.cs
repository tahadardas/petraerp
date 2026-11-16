using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.AlAmeenMigration;

/// <summary>
/// Creates the Petra master-data records an Al-Ameen capture needs, keyed by reviewed code. It is deliberately separate
/// from the financial cutover: identities must be resolvable and durable before any amount is written, so reconciliation
/// and cutover provably operate on the same objects.
/// </summary>
public sealed class AlAmeenMasterDataProvisioner(
    IAccountingDbContextFactory contextFactory,
    TimeProvider timeProvider)
{
    private readonly IAccountingDbContextFactory _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly Dictionary<Guid, Guid> _accountIdsBySource = [];

    public async Task<int> ProvisionAsync(
        SourceSnapshot snapshot,
        Guid companyId,
        Guid fiscalYearId,
        Guid branchId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (companyId == Guid.Empty || fiscalYearId == Guid.Empty || branchId == Guid.Empty)
            throw new BusinessRuleException("migration.scope_required", "الشركة والسنة المالية والفرع مطلوبة لتهيئة بيانات الأساس.");

        var created = 0;
        using var context = await _contextFactory.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false);
        context.BindProvisioningScope(new AccountingWriteScope(companyId, fiscalYearId, actorUserId, PermissionCatalog.DatabaseManage));

        foreach (var currency in snapshot.Currencies)
        {
            if (!await context.Currencies.AnyAsync(c => c.Code == currency.Code, cancellationToken).ConfigureAwait(false))
            {
                context.Currencies.Add(new CurrencyRow
                {
                    Code = currency.Code,
                    NameArabic = currency.Name,
                    NameEnglish = currency.Name,
                    IsActive = true,
                });
                created++;
            }
        }

        // Every non-base source currency must be enabled for the company, or the ledger refuses postings in it.
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var baseCurrencyCode = await context.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrencyCode).SingleAsync(cancellationToken).ConfigureAwait(false);
        var enabledNow = _timeProvider.GetUtcNow();
        foreach (var code in snapshot.Currencies.Select(c => c.Code).Where(c => c != baseCurrencyCode).Distinct(StringComparer.Ordinal))
        {
            if (await context.CompanyAdditionalCurrencies.AnyAsync(c => c.CompanyId == companyId && c.CurrencyCode == code, cancellationToken).ConfigureAwait(false))
                continue;
            context.CompanyAdditionalCurrencies.Add(new PetraERP.Infrastructure.Organizations.CompanyAdditionalCurrencyRow { CompanyId = companyId, CurrencyCode = code, CreatedAtUtc = enabledNow, UpdatedAtUtc = enabledNow });
            created++;
        }

        foreach (var account in OrderParentsFirst(snapshot.Accounts, a => a.Id, a => a.ParentId))
        {
            if (await context.Accounts.AnyAsync(a => a.CompanyId == companyId && a.Code == account.Code, cancellationToken).ConfigureAwait(false))
                continue;

            var row = new GeneralLedger.AccountRow
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                Code = account.Code,
                NameArabic = account.Name,
                NameEnglish = account.Name,
                ParentId = account.ParentId is Guid sourceParent && _accountIdsBySource.TryGetValue(sourceParent, out var resolvedParent) ? resolvedParent : null,
                AccountType = (byte)(account.AccountType switch { 0 => 1, 1 => 2, 2 => 3, 3 => 4, 4 => 5, _ => 1 }),
                CostCenterPolicy = 1,
                CurrencyMode = 2,
                FxMonetaryEligibility = 1,
                PostingAllowed = account.FinalAccount != 0,
                IsActive = true,
            };
            context.Accounts.Add(row);
            _accountIdsBySource[account.Id] = row.Id;
            created++;
        }

        foreach (var warehouse in snapshot.Warehouses)
        {
            if (await context.Warehouses.AnyAsync(w => w.CompanyId == companyId && w.Code == warehouse.Code, cancellationToken).ConfigureAwait(false))
                continue;

            context.Warehouses.Add(new Inventory.WarehouseRow
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                BranchId = branchId,
                Code = warehouse.Code,
                NameArabic = warehouse.Name,
                NameEnglish = warehouse.Name,
                IsActive = true,
            });
            created++;
        }

        foreach (var unitName in snapshot.Items.Select(i => i.UnitName?.Trim() ?? string.Empty).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal))
        {
            if (await context.UnitsOfMeasure.AnyAsync(u => u.CompanyId == companyId && u.Code == unitName, cancellationToken).ConfigureAwait(false))
                continue;

            context.UnitsOfMeasure.Add(new Inventory.UnitOfMeasureRow
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                Code = unitName,
                Symbol = unitName,
                NameArabic = unitName,
                NameEnglish = unitName,
                IsActive = true,
            });
            created++;
        }

        // The item loop looks each unit up in the database, so units must be committed before it runs.
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var item in OrderParentsFirst(snapshot.Items, i => i.Id, i => i.ParentId))
        {
            if (await context.Items.AnyAsync(i => i.CompanyId == companyId && i.Code == item.Code, cancellationToken).ConfigureAwait(false))
                continue;

            var baseUnit = await context.UnitsOfMeasure.FirstOrDefaultAsync(u => u.CompanyId == companyId && u.Code == item.UnitName, cancellationToken).ConfigureAwait(false)
                ?? throw new BusinessRuleException("migration.unit_missing", $"وحدة المادة \"{item.UnitName}\" غير متاحة بعد تهيئة دليل الوحدات.");

            context.Items.Add(new Inventory.ItemRow
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                Code = item.Code,
                NameArabic = item.Name,
                NameEnglish = item.Name,
                NormalizedSearchText = $"{item.Code} {item.Name}".ToUpperInvariant(),
                BaseUnitId = baseUnit.Id,
                CostMethod = 1,
                IsActive = true,
            });
            created++;
        }

        foreach (var party in snapshot.Parties)
        {
            if (await context.Parties.AnyAsync(p => p.CompanyId == companyId && p.Code == party.Code, cancellationToken).ConfigureAwait(false))
                continue;

            context.Parties.Add(new Parties.PartyRow
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                Code = party.Code,
                NameArabic = party.Name,
                NameEnglish = party.Name,
                NormalizedSearchText = $"{party.Code} {party.Name}".ToUpperInvariant(),
                Role = party.IsCustomer && party.IsSupplier ? 3 : (party.IsCustomer ? 1 : 2),
                CreditPolicyKind = 3,
                IsActive = true,
            });
            created++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }

    /// <summary>Parents are created before children so the hierarchy foreign key resolves inside one save.</summary>
    private static List<T> OrderParentsFirst<T>(
        IReadOnlyList<T> rows,
        Func<T, Guid> idSelector,
        Func<T, Guid?> parentSelector)
        where T : class
    {
        var ordered = new List<T>();
        var placed = new HashSet<Guid>();
        var pending = rows.ToList();

        while (pending.Count > 0)
        {
            var ready = pending
                .Where(r =>
                {
                    var parentId = parentSelector(r);
                    return parentId is null || parentId == Guid.Empty || placed.Contains(parentId.Value);
                })
                .ToList();
            if (ready.Count == 0)
                throw new BusinessRuleException("migration.hierarchy_cycle", "دليل المصدر يحتوي حلقة في التسلسل الهرمي؛ تعذرت التهيئة بترتيب آمن.");

            foreach (var row in ready)
            {
                ordered.Add(row);
                placed.Add(idSelector(row));
            }

            pending = pending.Except(ready).ToList();
        }

        return ordered;
    }
}
