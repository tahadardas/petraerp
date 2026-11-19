using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;

namespace PetraERP.Infrastructure.Patterns;

public sealed partial class SqlPatternService
{
    /// <summary>
    /// Normalizes and validates a revision configuration against the company: the closed role catalog for the operation, every
    /// mapped account active and posting-allowed in this company, and defaults (cost center, warehouse, currency) in scope.
    /// Applied when a revision is stored and again at publish, so a since-deactivated account cannot be published.
    /// </summary>
    private async Task<PatternConfiguration> ValidateConfigurationAsync(Guid companyId, BusinessOperationKind kind, PatternConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var normalized = Rule(() => PatternAccountRoles.Normalize(kind, configuration), "configuration");

        var accountIds = normalized.AccountMappings.Select(m => m.AccountId).Distinct().ToList();
        if (accountIds.Count > 0)
        {
            var valid = await context.Accounts.AsNoTracking()
                .Where(a => a.CompanyId == companyId && accountIds.Contains(a.Id) && a.IsActive && a.PostingAllowed)
                .Select(a => a.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            var invalid = normalized.AccountMappings.FirstOrDefault(m => !valid.Contains(m.AccountId));
            if (invalid is not null)
                throw new BusinessRuleException("patterns.account_invalid",
                    $"The account for role '{invalid.Role}' must be an active posting account of this company.", "configuration.accountMappings");
        }

        if (normalized.DefaultCostCenterId is { } center
            && !await context.CostCenters.AnyAsync(c => c.Id == center && c.CompanyId == companyId && c.IsActive, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("patterns.default_cost_center_invalid", "The default cost center must be active in this company.",
                "configuration.defaultCostCenterId");

        if (normalized.DefaultWarehouseId is { } warehouse
            && !await context.Warehouses.AnyAsync(w => w.Id == warehouse && w.CompanyId == companyId && w.IsActive, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("patterns.default_warehouse_invalid", "The default warehouse must be active in this company.",
                "configuration.defaultWarehouseId");

        if (!string.IsNullOrWhiteSpace(normalized.DefaultCurrencyCode))
        {
            var currency = normalized.DefaultCurrencyCode.Trim().ToUpperInvariant();
            var enabled = await context.Companies.AnyAsync(c => c.Id == companyId && c.BaseCurrencyCode == currency, cancellationToken).ConfigureAwait(false)
                || await context.CompanyAdditionalCurrencies.AnyAsync(c => c.CompanyId == companyId && c.CurrencyCode == currency, cancellationToken).ConfigureAwait(false);
            if (!enabled)
                throw new BusinessRuleException("patterns.default_currency_invalid", "The default currency is not enabled for this company.",
                    "configuration.defaultCurrencyCode");
            normalized = normalized with { DefaultCurrencyCode = currency };
        }

        if (normalized.Voucher is { } voucher)
        {
            var roots = voucher.Lookup.EligibleAccountRootIds;
            if (roots.Count > 0
                && await context.Accounts.AsNoTracking().CountAsync(a => a.CompanyId == companyId && a.IsActive && roots.Contains(a.Id), cancellationToken).ConfigureAwait(false) != roots.Count)
                throw new BusinessRuleException(VoucherPatternRules.LookupInvalid, "Every eligible account root must be an active account of this company.",
                    "configuration.voucher.lookup.eligibleAccountRootIds");
            var centers = voucher.Lookup.EligibleCostCenterIds;
            if (centers.Count > 0
                && await context.CostCenters.AsNoTracking().CountAsync(c => c.CompanyId == companyId && c.IsActive && centers.Contains(c.Id), cancellationToken).ConfigureAwait(false) != centers.Count)
                throw new BusinessRuleException(VoucherPatternRules.LookupInvalid, "Every eligible cost center must be an active cost center of this company.",
                    "configuration.voucher.lookup.eligibleCostCenterIds");
        }

        if (normalized.AllowedWarehouseIds is { Count: > 0 })
        {
            var distinct = normalized.AllowedWarehouseIds.Distinct().ToList();
            var count = await context.Warehouses.AsNoTracking()
                .CountAsync(w => w.CompanyId == companyId && distinct.Contains(w.Id) && w.IsActive, cancellationToken)
                .ConfigureAwait(false);
            if (count != distinct.Count)
                throw new BusinessRuleException("patterns.allowed_warehouse_invalid", "Every allowed warehouse must be active in this company.",
                    "configuration.allowedWarehouseIds");
        }

        if (normalized.PricingPolicy.MaxDiscountPercentage is { } maxDiscount)
        {
            if (maxDiscount < 0m || maxDiscount > 100m)
                throw new BusinessRuleException("patterns.max_discount_invalid", "Max discount percentage must be between 0 and 100.",
                    "configuration.pricingPolicy.maxDiscountPercentage");
        }

        return normalized;
    }

    /// <summary>Re-validates the stored revision at publish and requires the roles the operation's posting engine needs.</summary>
    private async Task ValidateForPublishAsync(Guid companyId, BusinessOperationKind kind, PatternRevisionRow revision, CancellationToken cancellationToken)
    {
        PatternConfiguration configuration;
        try
        {
            configuration = JsonSerializer.Deserialize<PatternConfiguration>(revision.ConfigurationJson, JsonOptions) ?? new PatternConfiguration();
        }
        catch (JsonException exception)
        {
            throw new BusinessRuleException("patterns.configuration_invalid", "The revision configuration cannot be read (" + exception.GetType().Name + "); create a new revision.", "revisionId");
        }

        var normalized = await ValidateConfigurationAsync(companyId, kind, configuration, cancellationToken).ConfigureAwait(false);
        Rule(() => { PatternAccountRoles.RequireForPublish(kind, normalized); return 0; }, "configuration.accountMappings");
    }

    /// <summary>Branch grants may only name branches of the pattern's company (no cross-company grant).</summary>
    private async Task ValidateBranchGrantsAsync(Guid companyId, IReadOnlyList<Guid>? branchIds, CancellationToken cancellationToken)
    {
        if (branchIds is not { Count: > 0 }) return;
        var distinct = branchIds.Distinct().ToList();
        var known = await context.Branches.CountAsync(b => b.CompanyId == companyId && distinct.Contains(b.Id), cancellationToken).ConfigureAwait(false);
        if (known != distinct.Count)
            throw new BusinessRuleException("patterns.branch_invalid", "Every granted branch must belong to this company.", "branchIds");
    }

    private static T Rule<T>(Func<T> action, string field)
    {
        try { return action(); }
        catch (InvalidOperationException exception) when (RuleViolation.CodeOf(exception) is { } code)
        {
            throw new BusinessRuleException(code, exception.Message, field);
        }
    }
}
