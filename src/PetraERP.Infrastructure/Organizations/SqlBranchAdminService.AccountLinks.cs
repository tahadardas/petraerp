using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Branches;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Currencies;

namespace PetraERP.Infrastructure.Organizations;

public sealed partial class SqlBranchAdminService
{
    public async Task<IReadOnlyList<BranchAccountLinkResult>> ListAccountLinksAsync(Guid companyId, Guid branchId, DateOnly? asOf, CancellationToken cancellationToken = default)
    {
        await RequireBranchAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);
        var links = context.BranchAccountLinks.AsNoTracking().Where(l => l.CompanyId == companyId && l.BranchId == branchId);
        if (asOf is { } day) links = links.Where(l => l.EffectiveFrom <= day && (l.EffectiveTo == null || l.EffectiveTo >= day));
        var rows = await (from l in links
                          join a in context.Accounts.AsNoTracking() on l.AccountId equals a.Id
                          orderby l.Role, l.EffectiveFrom
                          select new { l.Id, l.BranchId, l.Role, l.AccountId, a.Code, a.NameArabic, l.EffectiveFrom, l.EffectiveTo })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new BranchAccountLinkResult(r.Id, r.BranchId, (BranchAccountRole)r.Role, r.AccountId, r.Code, r.NameArabic, r.EffectiveFrom, r.EffectiveTo)).ToList();
    }

    public async Task<BranchAccountLinkResult> SetAccountLinkAsync(Guid companyId, Guid branchId, Guid userId, SetBranchAccountLinkCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Enum.IsDefined(command.Role))
            throw new BusinessRuleException("branch.link_role_invalid", "Unknown account role.", "role");
        var reason = Reason(command.Reason);
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        var branch = await context.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.CompanyId == companyId && b.Id == branchId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("Branch");
        if (!branch.IsActive) throw new BusinessRuleException("branch.inactive", "Account mappings cannot change on an inactive branch.", "branchId");
        var account = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && a.Id == command.AccountId)
            .Select(a => new { a.Id, a.Code, a.NameArabic, a.IsActive, a.PostingAllowed }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("branch.link_account_not_found", "The account was not found in this company.", "accountId");
        if (!account.IsActive || !account.PostingAllowed)
            throw new BusinessRuleException("branch.link_account_not_postable", "The account must be active and accept postings.", "accountId");

        var role = (byte)command.Role;
        var history = await context.BranchAccountLinks.Where(l => l.CompanyId == companyId && l.BranchId == branchId && l.Role == role)
            .OrderByDescending(l => l.EffectiveFrom).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (history.Count > 0 && history[0].EffectiveFrom >= command.EffectiveFrom)
            throw new BusinessRuleException("branch.link_not_after_latest",
                $"The new mapping must start after the latest one ({history[0].EffectiveFrom:yyyy-MM-dd}); mappings are never rewritten retroactively.", "effectiveFrom");
        var now = Clock.GetUtcNow();
        if (history.Count > 0 && history[0].AccountId == command.AccountId && history[0].EffectiveTo is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return (await ListAccountLinksAsync(companyId, branchId, null, cancellationToken).ConfigureAwait(false)).First(l => l.Id == history[0].Id);
        }
        if (history.Count > 0 && (history[0].EffectiveTo is null || history[0].EffectiveTo >= command.EffectiveFrom))
        {
            history[0].EffectiveTo = command.EffectiveFrom.AddDays(-1);
            history[0].UpdatedAtUtc = now;
        }
        var link = new BranchAccountLinkRow
        {
            Id = Guid.CreateVersion7(now), CompanyId = companyId, BranchId = branchId, Role = role, AccountId = command.AccountId,
            EffectiveFrom = command.EffectiveFrom, EffectiveTo = null, CreatedByUserId = userId, CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        context.BranchAccountLinks.Add(link);
        context.BranchAudit.Add(Audit(branch, BranchAuditRow.AccountLinkSet,
            string.Create(CultureInfo.InvariantCulture, $"role={command.Role};account={account.Code};from={command.EffectiveFrom:yyyy-MM-dd}"), reason, userId, now));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new BranchAccountLinkResult(link.Id, branchId, command.Role, account.Id, account.Code, account.NameArabic, link.EffectiveFrom, null);
    }

    public async Task<IReadOnlyList<BranchSubAccountPlan>> PreviewSubAccountsAsync(Guid companyId, Guid branchId, Guid modelBranchId, CancellationToken cancellationToken = default)
    {
        var target = await context.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.CompanyId == companyId && b.Id == branchId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("Branch");
        await RequireBranchAsync(companyId, modelBranchId, cancellationToken).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);
        var model = await (from l in context.BranchAccountLinks.AsNoTracking()
                           join a in context.Accounts.AsNoTracking() on l.AccountId equals a.Id
                           where l.CompanyId == companyId && l.BranchId == modelBranchId && l.EffectiveFrom <= today && (l.EffectiveTo == null || l.EffectiveTo >= today)
                           orderby l.Role
                           select new { l.Role, a.Code, a.NameArabic, a.NameEnglish, a.ParentId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var parentIds = model.Where(m => m.ParentId is not null).Select(m => m.ParentId!.Value).Distinct().ToList();
        var parents = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && parentIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, cancellationToken).ConfigureAwait(false);
        var used = (await context.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).Select(a => a.Code).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var plans = new List<BranchSubAccountPlan>();
        foreach (var m in model)
        {
            if (m.ParentId is not { } parent || !parents.TryGetValue(parent, out var parentCode)) continue;
            var baseCode = $"{parentCode}-{target.Code}";
            var code = baseCode;
            for (var i = 2; !used.Add(code); i++) code = string.Create(CultureInfo.InvariantCulture, $"{baseCode}-{i}");
            plans.Add(new BranchSubAccountPlan((BranchAccountRole)m.Role, parent, parentCode, code, $"{m.NameArabic} - {target.NameArabic}",
                m.NameEnglish is null || target.NameEnglish is null ? null : $"{m.NameEnglish} - {target.NameEnglish}"));
        }
        return plans;
    }

    public async Task<Guid?> ResolveAccountAsync(Guid companyId, Guid branchId, BranchAccountRole role, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var value = (byte)role;
        return await context.BranchAccountLinks.AsNoTracking()
            .Where(l => l.CompanyId == companyId && l.BranchId == branchId && l.Role == value && l.EffectiveFrom <= asOf && (l.EffectiveTo == null || l.EffectiveTo >= asOf))
            .OrderByDescending(l => l.EffectiveFrom).Select(l => (Guid?)l.AccountId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireBranchAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken)
    {
        if (!await context.Branches.AnyAsync(b => b.CompanyId == companyId && b.Id == branchId, cancellationToken).ConfigureAwait(false))
            throw new ResourceNotFoundException("Branch");
    }
}
