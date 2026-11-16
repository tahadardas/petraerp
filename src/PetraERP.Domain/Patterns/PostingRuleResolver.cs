using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Patterns;

public enum AccountResolutionSource
{
    EnforcedRule,
    DocumentOverride,
    ItemOverride,
    GroupContract,
    AncestorGroupContract,
    PartyContract,
    BranchLink,
    PatternDefault,
    CompanyDefault,
}

public sealed record ResolvedAccount
{
    public Guid AccountId { get; init; }
    public AccountResolutionSource Source { get; init; }
    public string Role { get; init; } = string.Empty;
}

/// <summary>
/// Authoritative posting rule resolver enforcing explicit priority:
/// 1. Enforced accounting rule (system invariants)
/// 2. Authorized document override (only if whitelisted by pattern configuration)
/// 3. Material specific account mapping
/// 4. Immediate Material Group account mapping
/// 5. Ancestor Material Group account mapping
/// 6. Party contract mapping
/// 7. Branch account link (effective-dated mapping of the posting branch; more specific than the pattern/company defaults,
///    less specific than anything tied to the item, its groups or the party)
/// 8. Pattern configuration default mapping
/// 9. Company default mapping
/// </summary>
public static class PostingRuleResolver
{
    /// <summary>
    /// True when the pattern lets a document name its own account for the role: the role is in the override whitelist, or its
    /// mapping is not enforced. The same rule decides at save time (early refusal) and at posting (the authority).
    /// </summary>
    public static bool AllowsDocumentOverride(string role, PatternConfiguration patternConfig)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(patternConfig);
        return patternConfig.OptionOverridesWhitelist.Any(w => string.Equals(w, role, StringComparison.OrdinalIgnoreCase))
            || patternConfig.AccountMappings.Any(m => string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase) && m.AllowOverride);
    }

    public static ResolvedAccount Resolve(
        string role,
        PatternConfiguration patternConfig,
        Guid? enforcedAccountId = null,
        Guid? documentOverrideAccountId = null,
        Guid? partyContractAccountId = null,
        Guid? companyDefaultAccountId = null,
        Guid? itemContractAccountId = null,
        Guid? groupContractAccountId = null,
        Guid? ancestorGroupContractAccountId = null,
        Guid? branchLinkAccountId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(patternConfig);

        var mapping = patternConfig.AccountMappings.FirstOrDefault(m => string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase));
        var isRequired = mapping?.IsRequired ?? true;

        // 1. Enforced system rule
        if (enforcedAccountId.HasValue && enforcedAccountId.Value != Guid.Empty)
        {
            return new ResolvedAccount
            {
                AccountId = enforcedAccountId.Value,
                Source = AccountResolutionSource.EnforcedRule,
                Role = role,
            };
        }

        // 2. Permitted document override
        if (documentOverrideAccountId.HasValue && documentOverrideAccountId.Value != Guid.Empty)
        {
            if (!AllowsDocumentOverride(role, patternConfig))
            {
                throw RuleViolation.Create(RuleViolation.PatternOverrideNotAllowed, $"Account override for role '{role}' is not permitted by pattern configuration whitelist.");
            }

            return new ResolvedAccount
            {
                AccountId = documentOverrideAccountId.Value,
                Source = AccountResolutionSource.DocumentOverride,
                Role = role,
            };
        }

        // 3. Item specific assignment
        if (itemContractAccountId.HasValue && itemContractAccountId.Value != Guid.Empty)
        {
            return new ResolvedAccount
            {
                AccountId = itemContractAccountId.Value,
                Source = AccountResolutionSource.ItemOverride,
                Role = role,
            };
        }

        // 4. Immediate group assignment
        if (groupContractAccountId.HasValue && groupContractAccountId.Value != Guid.Empty)
        {
            return new ResolvedAccount
            {
                AccountId = groupContractAccountId.Value,
                Source = AccountResolutionSource.GroupContract,
                Role = role,
            };
        }

        // 5. Ancestor group assignment
        if (ancestorGroupContractAccountId.HasValue && ancestorGroupContractAccountId.Value != Guid.Empty)
        {
            return new ResolvedAccount
            {
                AccountId = ancestorGroupContractAccountId.Value,
                Source = AccountResolutionSource.AncestorGroupContract,
                Role = role,
            };
        }

        // 6. Party contract
        if (partyContractAccountId.HasValue && partyContractAccountId.Value != Guid.Empty)
        {
            return new ResolvedAccount
            {
                AccountId = partyContractAccountId.Value,
                Source = AccountResolutionSource.PartyContract,
                Role = role,
            };
        }

        // 7. Branch account link
        if (branchLinkAccountId.HasValue && branchLinkAccountId.Value != Guid.Empty)
        {
            return new ResolvedAccount
            {
                AccountId = branchLinkAccountId.Value,
                Source = AccountResolutionSource.BranchLink,
                Role = role,
            };
        }

        // 8. Pattern default
        if (mapping is not null && mapping.AccountId != Guid.Empty)
        {
            return new ResolvedAccount
            {
                AccountId = mapping.AccountId,
                Source = AccountResolutionSource.PatternDefault,
                Role = role,
            };
        }

        // 9. Company default
        if (companyDefaultAccountId.HasValue && companyDefaultAccountId.Value != Guid.Empty)
        {
            return new ResolvedAccount
            {
                AccountId = companyDefaultAccountId.Value,
                Source = AccountResolutionSource.CompanyDefault,
                Role = role,
            };
        }

        if (isRequired)
        {
            throw RuleViolation.Create(RuleViolation.PatternAccountUnresolved, $"Required account mapping for role '{role}' could not be resolved from any configuration level.");
        }

        return new ResolvedAccount
        {
            AccountId = Guid.Empty,
            Source = AccountResolutionSource.PatternDefault,
            Role = role,
        };
    }
}
