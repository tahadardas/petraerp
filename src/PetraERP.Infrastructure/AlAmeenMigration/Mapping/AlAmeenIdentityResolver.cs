using Microsoft.EntityFrameworkCore;
using PetraERP.Application.AlAmeenMigration.Mapping;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Registry;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.AlAmeenMigration.Mapping;

/// <summary>
/// Resolves Al-Ameen source identities to Petra identities by reviewed code, and records the binding centrally so a
/// later run compares the same objects. Nothing here writes accounting data: an unresolved or ambiguous source row is
/// returned as an issue, because a silently dropped row would make the reconciliation report a false match.
/// </summary>
public sealed class AlAmeenIdentityResolver(
    IAccountingDbContextFactory contextFactory,
    SystemDbContext systemContext,
    TimeProvider timeProvider)
{
    private readonly IAccountingDbContextFactory _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly SystemDbContext _systemContext = systemContext ?? throw new ArgumentNullException(nameof(systemContext));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<IdentityResolution> ResolveAsync(
        SourceSnapshot snapshot,
        Guid companyId,
        Guid fiscalYearId,
        Guid branchId,
        bool persistBindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (companyId == Guid.Empty || fiscalYearId == Guid.Empty || branchId == Guid.Empty)
            throw new BusinessRuleException("migration.scope_required", "الشركة والسنة المالية والفرع مطلوبة لمواءمة هويات الترحيل.");

        var bindings = new List<IdentityBinding>();
        var issues = new List<IdentityIssue>();

        using var context = await _contextFactory.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false);

        await ResolveAccountsAsync(context, snapshot, companyId, fiscalYearId, bindings, issues, persistBindings, cancellationToken).ConfigureAwait(false);
        await ResolvePartiesAsync(context, snapshot, companyId, fiscalYearId, bindings, issues, persistBindings, cancellationToken).ConfigureAwait(false);
        await ResolveWarehousesAsync(context, snapshot, companyId, fiscalYearId, branchId, bindings, issues, persistBindings, cancellationToken).ConfigureAwait(false);
        await ResolveItemsAndUnitsAsync(context, snapshot, companyId, fiscalYearId, bindings, issues, persistBindings, cancellationToken).ConfigureAwait(false);
        var currencyCodes = new Dictionary<Guid, string>();
        await ResolveCurrenciesAsync(context, snapshot, currencyCodes, issues, cancellationToken).ConfigureAwait(false);

        if (!await context.Branches.AnyAsync(b => b.CompanyId == companyId && b.Id == branchId, cancellationToken).ConfigureAwait(false))
            issues.Add(new IdentityIssue(MigrationEntityKind.Branch, branchId.ToString("N"), IdentityIssueKind.MissingFromTarget, "الفرع غير موجود في قاعدة بيانات السنة المالية المستهدفة."));

        if (persistBindings && bindings.Count > 0)
            await _systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new IdentityResolution(companyId, fiscalYearId, DateOnly.FromDateTime(snapshot.CutoffUtc), branchId, bindings, currencyCodes, issues);
    }

    private static void FlagDuplicateSourceCodes(List<IdentityIssue> issues, MigrationEntityKind kind, IEnumerable<(Guid Id, string Code)> rows)
    {
        foreach (var group in rows.Where(r => !string.IsNullOrWhiteSpace(r.Code))
                     .GroupBy(r => r.Code.Trim(), StringComparer.Ordinal)
                     .Where(g => g.Select(r => r.Id).Distinct().Count() > 1))
        {
            issues.Add(new IdentityIssue(kind, group.Key, IdentityIssueKind.DuplicateSourceCode,
                "كود المصدر يشاركه أكثر من سجل أمين واحد؛ لا يمكن الاعتماد على المواءمة بالكود."));
        }
    }

    private async Task ResolveAccountsAsync(
        AccountingDbContext context,
        SourceSnapshot snapshot,
        Guid companyId,
        Guid fiscalYearId,
        List<IdentityBinding> bindings,
        List<IdentityIssue> issues,
        bool persist,
        CancellationToken cancellationToken)
    {
        var rows = snapshot.Accounts.Select(a => (a.Id, a.Code)).ToList();
        FlagDuplicateSourceCodes(issues, MigrationEntityKind.Account, rows);
        var target = await context.Accounts.AsNoTracking()
            .Where(a => a.CompanyId == companyId)
            .Select(a => new TargetCodeRow(a.Id, a.Code.Trim()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        await ApplyAsync(MigrationEntityKind.Account, rows, target, companyId, fiscalYearId, bindings, issues, persist, cancellationToken).ConfigureAwait(false);
    }

    private async Task ResolvePartiesAsync(
        AccountingDbContext context,
        SourceSnapshot snapshot,
        Guid companyId,
        Guid fiscalYearId,
        List<IdentityBinding> bindings,
        List<IdentityIssue> issues,
        bool persist,
        CancellationToken cancellationToken)
    {
        var rows = snapshot.Parties.Select(p => (p.Id, p.Code)).ToList();
        FlagDuplicateSourceCodes(issues, MigrationEntityKind.Party, rows);
        var target = await context.Parties.AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Select(p => new TargetCodeRow(p.Id, p.Code.Trim()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        await ApplyAsync(MigrationEntityKind.Party, rows, target, companyId, fiscalYearId, bindings, issues, persist, cancellationToken).ConfigureAwait(false);
    }

    private async Task ResolveWarehousesAsync(
        AccountingDbContext context,
        SourceSnapshot snapshot,
        Guid companyId,
        Guid fiscalYearId,
        Guid branchId,
        List<IdentityBinding> bindings,
        List<IdentityIssue> issues,
        bool persist,
        CancellationToken cancellationToken)
    {
        var rows = snapshot.Warehouses.Select(w => (w.Id, w.Code)).ToList();
        FlagDuplicateSourceCodes(issues, MigrationEntityKind.Warehouse, rows);
        var target = await context.Warehouses.AsNoTracking()
            .Where(w => w.CompanyId == companyId && w.BranchId == branchId)
            .Select(w => new TargetCodeRow(w.Id, w.Code.Trim()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        await ApplyAsync(MigrationEntityKind.Warehouse, rows, target, companyId, fiscalYearId, bindings, issues, persist, cancellationToken).ConfigureAwait(false);
    }

    private async Task ResolveItemsAndUnitsAsync(
        AccountingDbContext context,
        SourceSnapshot snapshot,
        Guid companyId,
        Guid fiscalYearId,
        List<IdentityBinding> bindings,
        List<IdentityIssue> issues,
        bool persist,
        CancellationToken cancellationToken)
    {
        var rows = snapshot.Items.Select(i => (i.Id, i.Code)).ToList();
        FlagDuplicateSourceCodes(issues, MigrationEntityKind.Item, rows);
        var target = await context.Items.AsNoTracking()
            .Where(i => i.CompanyId == companyId)
            .Select(i => new TargetCodeRow(i.Id, i.Code.Trim()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        await ApplyAsync(MigrationEntityKind.Item, rows, target, companyId, fiscalYearId, bindings, issues, persist, cancellationToken).ConfigureAwait(false);

        var units = await ResolveUnitsAsync(context, snapshot, companyId, cancellationToken).ConfigureAwait(false);

        foreach (var item in snapshot.Items)
        {
            var code = item.Code.Trim();
            var unitName = item.UnitName?.Trim() ?? string.Empty;
            if (unitName.Length == 0)
            {
                issues.Add(new IdentityIssue(MigrationEntityKind.Unit, code, IdentityIssueKind.EmptySourceCode, "المادة بلا وحدة مصدر؛ لا يمكن تسجيل حركة مخزون بلا وحدة."));
                continue;
            }

            if (!units.TryGetValue(unitName, out var unitId))
            {
                issues.Add(new IdentityIssue(MigrationEntityKind.Unit, unitName, IdentityIssueKind.MissingFromTarget,
                    $"وحدة المادة \"{unitName}\" غير مسجلة في دليل وحدات الشركة المستهدفة."));
                continue;
            }

            bindings.Add(new IdentityBinding(MigrationEntityKind.Unit, item.Id, unitId, unitName));
        }
    }

    /// <summary>Al-Ameen stores the unit as free text on the item, so the unit binding is keyed by that text, not by a source GUID.</summary>
    private static async Task<Dictionary<string, Guid>> ResolveUnitsAsync(AccountingDbContext context, SourceSnapshot snapshot, Guid companyId, CancellationToken cancellationToken)
    {
        var wanted = snapshot.Items.Select(i => i.UnitName?.Trim() ?? string.Empty).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var rows = await context.UnitsOfMeasure.AsNoTracking()
            .Where(u => u.CompanyId == companyId)
            .Select(u => new { u.Id, u.Code, u.Symbol, u.NameArabic, u.NameEnglish })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var map = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var name in wanted)
        {
            var matches = rows.Where(u => new[] { u.Code, u.Symbol, u.NameArabic, u.NameEnglish }
                    .Any(value => string.Equals(value?.Trim(), name, StringComparison.Ordinal)))
                .Select(u => u.Id)
                .Distinct()
                .ToList();
            if (matches.Count == 1)
                map[name] = matches[0];
        }

        return map;
    }

    private static async Task ResolveCurrenciesAsync(
        AccountingDbContext context,
        SourceSnapshot snapshot,
        Dictionary<Guid, string> currencyCodes,
        List<IdentityIssue> issues,
        CancellationToken cancellationToken)
    {
        var rows = snapshot.Currencies.Select(c => (c.Id, c.Code)).ToList();
        FlagDuplicateSourceCodes(issues, MigrationEntityKind.Currency, rows);

        // Petra currencies are identified by their three-letter code, not by a GUID, so the binding is a code, not an id.
        var known = (await context.Currencies.AsNoTracking()
            .Select(c => c.Code.Trim())
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        foreach (var (sourceId, rawCode) in rows)
        {
            var code = rawCode.Trim();
            if (code.Length == 0)
                issues.Add(new IdentityIssue(MigrationEntityKind.Currency, rawCode, IdentityIssueKind.EmptySourceCode, "عملة مصدر بلا رمز."));
            else if (code.Length != 3 || !code.All(char.IsUpper))
                issues.Add(new IdentityIssue(MigrationEntityKind.Currency, code, IdentityIssueKind.MissingFromTarget,
                    "رمز العملة يجب أن يكون ثلاثة أحرف كبيرة ليستخدم في قيود الهدف."));
            else if (!known.Contains(code))
                issues.Add(new IdentityIssue(MigrationEntityKind.Currency, code, IdentityIssueKind.MissingFromTarget,
                    "العملة غير مفعلة في الشركة المستهدفة؛ لا يمكن ترحيل حركة بعملة غير معروفة."));
            else
                currencyCodes[sourceId] = code;
        }
    }

    private async Task ApplyAsync(
        MigrationEntityKind kind,
        IReadOnlyList<(Guid Id, string Code)> rows,
        IReadOnlyList<TargetCodeRow> target,
        Guid companyId,
        Guid fiscalYearId,
        List<IdentityBinding> bindings,
        List<IdentityIssue> issues,
        bool persist,
        CancellationToken cancellationToken)
    {
        var byCode = target.GroupBy(r => r.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var knownTargetIds = target.Select(r => r.Id).ToHashSet();
        var existing = await LoadExistingAsync(kind, companyId, fiscalYearId, cancellationToken).ConfigureAwait(false);

        foreach (var (sourceId, rawCode) in rows)
        {
            var code = rawCode.Trim();
            if (code.Length == 0)
            {
                issues.Add(new IdentityIssue(kind, rawCode, IdentityIssueKind.EmptySourceCode, "سجل مصدر بلا كود: لا يمكن مواءمته ولا مقارنته."));
                continue;
            }

            if (existing.TryGetValue(sourceId, out var mappedId))
            {
                if (!knownTargetIds.Contains(mappedId))
                {
                    issues.Add(new IdentityIssue(kind, code, IdentityIssueKind.ConflictingExistingMapping,
                        "ربط محفوظ سابقاً يشير إلى سجل هدف لم يعد موجوداً؛ يلزم مراجعة الربط."));
                    continue;
                }

                bindings.Add(new IdentityBinding(kind, sourceId, mappedId, code));
                continue;
            }

            if (!byCode.TryGetValue(code, out var candidates))
            {
                issues.Add(new IdentityIssue(kind, code, IdentityIssueKind.MissingFromTarget, "لا يوجد سجل هدف مطابق للكود؛ الترحيل مرفوض بدل تجاهل السجل."));
                continue;
            }

            if (candidates.Count > 1)
            {
                issues.Add(new IdentityIssue(kind, code, IdentityIssueKind.AmbiguousInTarget,
                    $"كود الهدف مكرر على {candidates.Count} سجلات؛ المواءمة بالكود غير آمنة."));
                continue;
            }

            var targetId = candidates[0].Id;
            bindings.Add(new IdentityBinding(kind, sourceId, targetId, code));
            if (persist)
                StoreNew(kind, sourceId, targetId, companyId, fiscalYearId);
        }
    }

    private async Task<Dictionary<Guid, Guid>> LoadExistingAsync(MigrationEntityKind kind, Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken)
    {
        var entityType = EntityType(kind);
        var rows = await _systemContext.GlobalEntityMappings.AsNoTracking()
            .Where(m => m.CompanyId == companyId && m.EntityType == entityType && m.FiscalYearId == fiscalYearId)
            .Select(m => new { m.GlobalEntityId, m.LocalEntityId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(r => r.GlobalEntityId).ToDictionary(g => g.Key, g => g.First().LocalEntityId);
    }

    private void StoreNew(MigrationEntityKind kind, Guid sourceId, Guid targetId, Guid companyId, Guid fiscalYearId)
    {
        var entityType = EntityType(kind);
        _systemContext.GlobalEntityMappings.Add(new GlobalEntityMappingRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            EntityType = entityType,
            GlobalEntityId = sourceId,
            LocalEntityId = targetId,
            FiscalYearId = fiscalYearId,
            CreatedAtUtc = _timeProvider.GetUtcNow(),
        });
    }

    /// <summary>A target master-data row reduced to the only two facts mapping needs: identity and code.</summary>
    public sealed record TargetCodeRow(Guid Id, string Code);

    private static string EntityType(MigrationEntityKind kind) => kind switch
    {
        MigrationEntityKind.Account => "ameen.account",
        MigrationEntityKind.Party => "ameen.party",
        MigrationEntityKind.Item => "ameen.item",
        MigrationEntityKind.Warehouse => "ameen.warehouse",
        MigrationEntityKind.Currency => "ameen.currency",
        MigrationEntityKind.Unit => "ameen.unit",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
