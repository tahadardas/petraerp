using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Branches;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Organizations;

/// <summary>
/// Branch card, tree and account mappings of one company-year database. Branches are never deleted: an unused or retired
/// branch is deactivated. The parent chain can never loop, the company of a branch never changes, and every change is
/// recorded in the append-only branch audit with its reason.
/// </summary>
public sealed partial class SqlBranchAdminService(FinancialDbContext context, CentralDataContext? central = null, TimeProvider? clock = null) : IBranchAdminService
{
    private static readonly Regex PrefixPattern = new("^[A-Z0-9]{1,8}$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private TimeProvider Clock => clock ?? TimeProvider.System;

    public async Task<IReadOnlyList<BranchCardResult>> ListAsync(Guid companyId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        var rows = await context.Branches.AsNoTracking().Where(b => b.CompanyId == companyId && (includeInactive || b.IsActive))
            .OrderBy(b => b.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<BranchTreeNode>> GetTreeAsync(Guid companyId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        var cards = await ListAsync(companyId, includeInactive, cancellationToken).ConfigureAwait(false);
        var ids = cards.Select(c => c.Id).ToHashSet();
        var byParent = cards.Where(c => c.ParentBranchId is { } p && ids.Contains(p)).ToLookup(c => c.ParentBranchId!.Value);
        // A node whose parent is hidden (inactive) is shown as a root so that no active branch disappears from the tree.
        var roots = cards.Where(c => c.ParentBranchId is null || !ids.Contains(c.ParentBranchId.Value));
        return roots.Select(r => Build(r, 0, byParent)).ToList();
    }

    private static BranchTreeNode Build(BranchCardResult node, int depth, ILookup<Guid, BranchCardResult> byParent) =>
        new(node, depth, byParent[node.Id].Select(c => Build(c, depth + 1, byParent)).ToList());

    public async Task<BranchCardResult> GetAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken = default) =>
        Map(await context.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.CompanyId == companyId && b.Id == branchId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("Branch"));

    public async Task<BranchCardResult> CreateAsync(Guid companyId, Guid userId, SaveBranchCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        var now = Clock.GetUtcNow();
        var card = await NormalizeAsync(companyId, null, command, cancellationToken).ConfigureAwait(false);
        if (card.DefaultWarehouseId is not null || card.DefaultCostCenterId is not null)
            throw new BusinessRuleException("branch.defaults_after_create", "The default warehouse and cost center are set after the branch exists.", "defaultWarehouseId");
        var row = new BranchRow { Id = Guid.CreateVersion7(now), CompanyId = companyId, Code = card.Code, NameArabic = card.NameArabic, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        Apply(row, card);
        context.Branches.Add(row);
        context.BranchAudit.Add(Audit(row, BranchAuditRow.Created, Detail(card), "إنشاء فرع / branch created", userId, now));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        context.ChangeTracker.Clear();
        await SyncToCentralAsync(companyId, row.Id, cancellationToken).ConfigureAwait(false);
        return await GetAsync(companyId, row.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BranchCardResult> UpdateAsync(Guid companyId, Guid branchId, Guid userId, UpdateBranchCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Card);
        var reason = Reason(command.Reason);
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        var row = await LoadForUpdateAsync(companyId, branchId, command.ExpectedVersion, cancellationToken).ConfigureAwait(false);
        var card = await NormalizeAsync(companyId, row, command.Card, cancellationToken).ConfigureAwait(false);
        var now = Clock.GetUtcNow();
        row.Code = card.Code;
        row.NameArabic = card.NameArabic;
        Apply(row, card);
        row.UpdatedAtUtc = now;
        context.BranchAudit.Add(Audit(row, BranchAuditRow.Updated, Detail(card), reason, userId, now));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        context.ChangeTracker.Clear();
        await SyncToCentralAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);
        return await GetAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BranchCardResult> SetActiveAsync(Guid companyId, Guid branchId, Guid userId, SetBranchActiveCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = Reason(command.Reason);
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        var row = await LoadForUpdateAsync(companyId, branchId, command.ExpectedVersion, cancellationToken).ConfigureAwait(false);
        if (row.IsActive == command.IsActive)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Map(row);
        }
        if (command.IsActive)
        {
            if (row.ParentBranchId is { } parentId
                && !await context.Branches.AnyAsync(b => b.CompanyId == companyId && b.Id == parentId && b.IsActive, cancellationToken).ConfigureAwait(false))
                throw new BusinessRuleException("branch.parent_inactive", "A branch cannot be activated while its parent branch is inactive.", "parentBranchId");
        }
        else
        {
            if (await context.Branches.AnyAsync(b => b.CompanyId == companyId && b.ParentBranchId == branchId && b.IsActive, cancellationToken).ConfigureAwait(false))
                throw new BusinessRuleException("branch.has_active_children", "Deactivate or move the child branches first.", "branchId");
            if (!await context.Branches.AnyAsync(b => b.CompanyId == companyId && b.Id != branchId && b.IsActive, cancellationToken).ConfigureAwait(false))
                throw new BusinessRuleException("branch.last_active", "The last active branch of a company cannot be deactivated.", "branchId");
        }
        var now = Clock.GetUtcNow();
        row.IsActive = command.IsActive;
        row.UpdatedAtUtc = now;
        context.BranchAudit.Add(Audit(row, command.IsActive ? BranchAuditRow.Activated : BranchAuditRow.Deactivated, $"code={row.Code}", reason, userId, now));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        context.ChangeTracker.Clear();
        await SyncToCentralAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);
        return await GetAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BranchRow> LoadForUpdateAsync(Guid companyId, Guid branchId, ConcurrencyToken expected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var row = await context.Branches.SingleOrDefaultAsync(b => b.CompanyId == companyId && b.Id == branchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Branch");
        if (!expected.Matches(row.RowVersion)) throw new ConcurrencyConflictException("branch");
        context.Entry(row).Property(static r => r.RowVersion).OriginalValue = expected.ToBytes();
        return row;
    }

    private async Task<SaveBranchCommand> NormalizeAsync(Guid companyId, BranchRow? existing, SaveBranchCommand c, CancellationToken cancellationToken)
    {
        var code = (c.Code ?? string.Empty).Trim();
        if (code.Length is 0 or > 64 || code.Any(char.IsWhiteSpace))
            throw new BusinessRuleException("branch.code_invalid", "رمز الفرع مطلوب (حتى 64 محرفاً بلا فراغات) / Branch code is required (up to 64 characters, no spaces).", "code");
        var arabic = (c.NameArabic ?? string.Empty).Trim();
        if (arabic.Length is 0 or > 200)
            throw new BusinessRuleException("branch.name_required", "اسم الفرع بالعربية مطلوب (حتى 200 محرف) / The Arabic branch name is required.", "nameArabic");
        var english = Optional(c.NameEnglish, 200, "nameEnglish");
        string? prefix = null;
        if (!string.IsNullOrWhiteSpace(c.NumberPrefix))
        {
            prefix = c.NumberPrefix.Trim().ToUpperInvariant();
            if (!PrefixPattern.IsMatch(prefix))
                throw new BusinessRuleException("branch.prefix_invalid", "Number prefix must be 1 to 8 letters or digits.", "numberPrefix");
        }
        var id = existing?.Id;
        if (await context.Branches.AnyAsync(b => b.CompanyId == companyId && b.Code == code && b.Id != id, cancellationToken).ConfigureAwait(false))
            throw new ResourceConflictException("branch.code_exists", $"A branch with code '{code}' already exists.");
        if (prefix is not null && await context.Branches.AnyAsync(b => b.CompanyId == companyId && b.NumberPrefix == prefix && b.Id != id, cancellationToken).ConfigureAwait(false))
            throw new ResourceConflictException("branch.prefix_exists", $"The number prefix '{prefix}' is used by another branch.");
        await EnsureParentAsync(companyId, id, c.ParentBranchId, cancellationToken).ConfigureAwait(false);
        await EnsureDefaultsAsync(companyId, id, c, cancellationToken).ConfigureAwait(false);
        return c with
        {
            Code = code, NameArabic = arabic, NameEnglish = english, NumberPrefix = prefix,
            Address = Optional(c.Address, 400, "address"), Phone = Optional(c.Phone, 50, "phone"),
            Email = Optional(c.Email, 200, "email"), Notes = Optional(c.Notes, 1000, "notes"),
        };
    }

    private async Task EnsureParentAsync(Guid companyId, Guid? branchId, Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is not { } parent) return;
        if (branchId == parent) throw new BusinessRuleException("branch.parent_self", "A branch cannot be its own parent.", "parentBranchId");
        var seen = new HashSet<Guid>();
        Guid? cursor = parent;
        while (cursor is { } current)
        {
            if (current == branchId) throw new BusinessRuleException("branch.parent_loop", "The parent chain would form a loop.", "parentBranchId");
            if (!seen.Add(current)) throw new BusinessRuleException("branch.parent_loop", "The parent chain is already inconsistent.", "parentBranchId");
            var next = await context.Branches.AsNoTracking().Where(b => b.CompanyId == companyId && b.Id == current)
                .Select(b => new { b.IsActive, b.ParentBranchId }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new BusinessRuleException("branch.parent_not_found", "The parent branch was not found in this company.", "parentBranchId");
            if (current == parent && !next.IsActive)
                throw new BusinessRuleException("branch.parent_inactive", "The parent branch is inactive.", "parentBranchId");
            cursor = next.ParentBranchId;
        }
    }

    private async Task EnsureDefaultsAsync(Guid companyId, Guid? branchId, SaveBranchCommand c, CancellationToken cancellationToken)
    {
        if (c.DefaultWarehouseId is { } warehouse
            && !await context.Warehouses.AnyAsync(w => w.CompanyId == companyId && w.Id == warehouse && w.IsActive && w.BranchId == branchId, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("branch.default_warehouse_invalid", "The default warehouse must be an active warehouse of this branch.", "defaultWarehouseId");
        if (c.DefaultCostCenterId is { } center
            && !await context.CostCenters.AnyAsync(x => x.CompanyId == companyId && x.Id == center && x.IsActive, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("branch.default_cost_center_invalid", "The default cost center must be an active cost center of this company.", "defaultCostCenterId");
        foreach (var (accountId, field) in new[] { (c.DefaultCashAccountId, "defaultCashAccountId"), (c.DefaultBankAccountId, "defaultBankAccountId") })
            if (accountId is { } account && !await context.Accounts.AnyAsync(a => a.CompanyId == companyId && a.Id == account && a.IsActive && a.PostingAllowed, cancellationToken).ConfigureAwait(false))
                throw new BusinessRuleException("branch.default_account_invalid", "The default account must be an active posting account of this company.", field);
    }

    /// <summary>
    /// The central database authorizes branch scope, so it must carry the same card. The annual copy is authoritative and is
    /// written first; this idempotent upsert runs after it and can be repeated safely when the central database was unreachable.
    /// </summary>
    public async Task SyncToCentralAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken = default)
    {
        if (central is null) return;
        var source = await context.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.CompanyId == companyId && b.Id == branchId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("Branch");
        try
        {
            var target = await central.Set<BranchRow>().SingleOrDefaultAsync(b => b.CompanyId == companyId && b.Id == branchId, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                target = new BranchRow { Id = source.Id, CompanyId = source.CompanyId, Code = source.Code, NameArabic = source.NameArabic, CreatedAtUtc = source.CreatedAtUtc, UpdatedAtUtc = source.UpdatedAtUtc };
                CopyCard(source, target);
                central.Add(target);
            }
            else
            {
                CopyCard(source, target);
                target.UpdatedAtUtc = source.UpdatedAtUtc;
            }
            await central.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            throw new BusinessRuleException("branch.central_sync_failed",
                "تم حفظ الفرع في قاعدة السنة لكن تعذّر تحديث القاعدة المركزية؛ أعد المزامنة / The branch was saved in the year database but the central copy could not be updated; run the sync again.");
        }
    }

    private static void CopyCard(BranchRow from, BranchRow to)
    {
        Apply(to, new SaveBranchCommand(from.Code, from.NameArabic, from.NameEnglish, from.ParentBranchId, from.NumberPrefix, from.OpenedOn, from.Address,
            from.Phone, from.Email, from.Notes, from.DefaultWarehouseId, from.DefaultCashAccountId, from.DefaultBankAccountId, from.DefaultCostCenterId));
        to.IsActive = from.IsActive;
    }

    private static void Apply(BranchRow row, SaveBranchCommand c)
    {
        row.Code = c.Code;
        row.NameArabic = c.NameArabic;
        row.NameEnglish = c.NameEnglish;
        row.ParentBranchId = c.ParentBranchId;
        row.NumberPrefix = c.NumberPrefix;
        row.OpenedOn = c.OpenedOn;
        row.Address = c.Address;
        row.Phone = c.Phone;
        row.Email = c.Email;
        row.Notes = c.Notes;
        row.DefaultWarehouseId = c.DefaultWarehouseId;
        row.DefaultCashAccountId = c.DefaultCashAccountId;
        row.DefaultBankAccountId = c.DefaultBankAccountId;
        row.DefaultCostCenterId = c.DefaultCostCenterId;
    }

    private static string? Optional(string? value, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > max) throw new BusinessRuleException("branch.field_too_long", $"'{field}' is limited to {max} characters.", field);
        return trimmed;
    }

    private static string Reason(string? reason)
    {
        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > 400)
            throw new BusinessRuleException("branch.reason_required", "سبب التعديل مطلوب (حتى 400 محرف) / A reason is required (up to 400 characters).", "reason");
        return trimmed;
    }

    private static string Detail(SaveBranchCommand c) =>
        string.Create(CultureInfo.InvariantCulture, $"code={c.Code};parent={c.ParentBranchId};prefix={c.NumberPrefix};opened={c.OpenedOn:yyyy-MM-dd}");

    private static BranchAuditRow Audit(BranchRow row, byte action, string detail, string reason, Guid userId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now), CompanyId = row.CompanyId, BranchId = row.Id, Action = action,
        Detail = detail.Length > 2000 ? detail[..2000] : detail, Reason = reason, ActorUserId = userId, OccurredAtUtc = now,
    };

    private static BranchCardResult Map(BranchRow b) => new(b.Id, b.CompanyId, b.Code, b.NameArabic, b.NameEnglish, b.IsActive, b.ParentBranchId,
        b.NumberPrefix, b.OpenedOn, b.Address, b.Phone, b.Email, b.Notes, b.DefaultWarehouseId, b.DefaultCashAccountId, b.DefaultBankAccountId,
        b.DefaultCostCenterId, b.RowVersion, b.UpdatedAtUtc);
}
