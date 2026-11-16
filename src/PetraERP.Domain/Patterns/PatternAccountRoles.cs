using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Patterns;

/// <summary>
/// The typed account roles a pattern may map, per operation. Roles are a closed catalog (no free-form keys, scripts or SQL):
/// legacy aliases are normalized to the canonical names the posting engines resolve, unknown or foreign roles are rejected,
/// and publishing requires the roles the operation's engine cannot post without.
/// </summary>
public static class PatternAccountRoles
{
    public const string Revenue = "Revenue";
    public const string Receivable = "Receivable";
    public const string Payable = "Payable";
    public const string Cogs = "COGS";
    public const string Inventory = "Inventory";
    public const string Cash = "Cash";
    public const string Bank = "Bank";
    public const string Discount = "Discount";
    public const string Tax = "Tax";
    public const string TransportRevenue = "TransportRevenue";
    /// <summary>Realized exchange gain/loss when a foreign-currency receivable is collected at a different rate.</summary>
    public const string FxGain = "FxGain";
    public const string FxLoss = "FxLoss";

    public const string RoleUnknown = "patterns.role_unknown";
    public const string RoleNotAllowed = "patterns.role_not_allowed";
    public const string RoleDuplicate = "patterns.role_duplicate";
    public const string RoleMissing = "patterns.role_missing";
    public const string AccountMissing = "patterns.account_missing";
    public const string NumberingInvalid = "patterns.numbering_invalid";
    public const string ApprovalInvalid = "patterns.approval_invalid";

    private static readonly Dictionary<string, string> Canonical = new(StringComparer.OrdinalIgnoreCase)
    {
        [Revenue] = Revenue, ["Sales"] = Revenue,
        [Receivable] = Receivable, ["Customer"] = Receivable,
        [Payable] = Payable, ["Supplier"] = Payable,
        [Cogs] = Cogs, ["CostOfGoods"] = Cogs,
        [Inventory] = Inventory, [Cash] = Cash, [Bank] = Bank, [Discount] = Discount, [Tax] = Tax, [TransportRevenue] = TransportRevenue,
        [FxGain] = FxGain, [FxLoss] = FxLoss,
    };

    /// <summary>Roles each runnable engine resolves; operations without an engine accept none until it exists.</summary>
    public static IReadOnlyCollection<string> AllowedFor(BusinessOperationKind kind) => kind switch
    {
        BusinessOperationKind.SalesInvoice => [Revenue, Receivable, Cash, Inventory, Cogs, Discount, Tax, TransportRevenue],
        BusinessOperationKind.PurchaseInvoice => [Inventory, Payable, Cash, Tax],
        BusinessOperationKind.CashReceipt => [Cash, Bank, Receivable, FxGain, FxLoss],
        BusinessOperationKind.CashPayment => [Cash, Bank, Payable, FxGain, FxLoss],
        _ => [],
    };

    /// <summary>Normalizes an alias to its canonical role name, or null when the role is not in the catalog.</summary>
    public static string? Normalize(string? role) =>
        role is not null && Canonical.TryGetValue(role.Trim(), out var canonical) ? canonical : null;

    /// <summary>
    /// Returns the configuration with canonical role names after rejecting unknown, disallowed, duplicate or empty mappings and
    /// invalid numbering/approval settings. Account existence and scope are checked by the caller against the company.
    /// </summary>
    public static PatternConfiguration Normalize(BusinessOperationKind kind, PatternConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var allowed = AllowedFor(kind);
        var mappings = new List<PatternAccountMapping>();
        foreach (var mapping in configuration.AccountMappings ?? [])
        {
            var role = Normalize(mapping.Role)
                ?? throw RuleViolation.Create(RoleUnknown, $"Account role '{mapping.Role}' is not a supported pattern role.");
            if (!allowed.Contains(role))
                throw RuleViolation.Create(RoleNotAllowed, $"Account role '{role}' is not used by operation '{kind}'.");
            if (mapping.AccountId == Guid.Empty)
                throw RuleViolation.Create(AccountMissing, $"Account role '{role}' has no account.");
            if (mappings.Any(m => m.Role == role))
                throw RuleViolation.Create(RoleDuplicate, $"Account role '{role}' is mapped more than once.");
            mappings.Add(mapping with { Role = role });
        }

        var numbering = configuration.NumberingPolicy ?? new PatternNumberingPolicy();
        if (numbering.MinimumDigits is < 1 or > 12 || (numbering.Prefix ?? string.Empty).Length > 16 || (numbering.Suffix ?? string.Empty).Length > 16
            || !Enum.IsDefined(numbering.Scope) || !Enum.IsDefined(numbering.ResetPolicy) || numbering.InitialSequence is < 1 or > 999999999999L
            || numbering.ResetPolicy == PatternNumberResetPolicy.Monthly && !(numbering.Prefix+numbering.Suffix).Contains("{yyyyMM}",StringComparison.Ordinal))
            throw RuleViolation.Create(NumberingInvalid, "Numbering needs 1–12 digits, a defined scope and a prefix/suffix of at most 16 characters.");
        var approval = configuration.ApprovalPolicy ?? new PatternApprovalPolicy();
        if (approval.ThresholdAmount is { } threshold && (threshold < 0m || decimal.Round(threshold, 4) != threshold))
            throw RuleViolation.Create(ApprovalInvalid, "The approval threshold must be a non-negative amount with at most four decimals.");

        return configuration with { AccountMappings = mappings, NumberingPolicy = numbering, ApprovalPolicy = approval };
    }

    /// <summary>Publishing fails closed when the operation's engine would be unable to post with this configuration.</summary>
    public static void RequireForPublish(BusinessOperationKind kind, PatternConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var mapped = (configuration.AccountMappings ?? []).Select(m => Normalize(m.Role)).Where(r => r is not null).ToHashSet();
        string[] required = kind switch
        {
            BusinessOperationKind.SalesInvoice => [Revenue, Inventory, Cogs],
            BusinessOperationKind.PurchaseInvoice => [Inventory],
            _ => [],
        };
        var missing = required.Where(r => !mapped.Contains(r)).ToList();
        if (kind is BusinessOperationKind.CashReceipt or BusinessOperationKind.CashPayment && !mapped.Contains(Cash) && !mapped.Contains(Bank))
            missing.Add(Cash + "/" + Bank);
        if (missing.Count > 0)
            throw RuleViolation.Create(RoleMissing, $"Publishing '{kind}' requires account roles: {string.Join(", ", missing)}.");
    }
}
