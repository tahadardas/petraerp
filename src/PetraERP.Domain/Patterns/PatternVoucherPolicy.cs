using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Patterns;

/// <summary>
/// How a voucher pattern runs, independent of its accounting meaning (<see cref="BusinessOperationKind"/>). Al-Ameen's "نوع السند":
/// a plain voucher, or a receipt/payment split over several settlement methods.
/// </summary>
public enum VoucherFormKind
{
    GenericVoucher = 1,
    MultiMethodReceipt = 2,
    MultiMethodPayment = 3,
}

/// <summary>
/// How a pattern's vouchers are posted by the one-step "save and process" path. A pattern can only be STRICTER than the company policy
/// (<c>DocumentPostingMode</c>): it can hold its vouchers for a manual post, never post what the company policy keeps for approval.
/// Manual posting through the post action is always available to whoever holds the posting permission.
/// </summary>
public enum VoucherPostingMode
{
    /// <summary>Follow the company's document posting mode (the behavior of every pattern without a voucher block).</summary>
    InheritFromCompany = 0,

    /// <summary>Save (and approve where required) but never post automatically; posting is a deliberate manual action.</summary>
    ManualOnly = 1,
}

/// <summary>Column captions only: "مدين/دائن" or "من/إلى". It never changes the direction of an entry.</summary>
public enum VoucherCaptionMode
{
    DebitCredit = 0,
    FromTo = 1,
}

/// <summary>
/// The voucher block of a pattern revision (schema version 2). A revision without the block (every schema-1 revision) runs with the
/// defaults of this record, so no stored revision has to be rewritten.
/// <para>
/// Options whose effect is not enforced by the server yet are accepted only at their default value and rejected otherwise
/// (<see cref="VoucherPatternRules.OptionNotEnforced"/>): the schema never stores a promise the posting engine does not keep.
/// </para>
/// </summary>
public sealed record PatternVoucherPolicy
{
    public VoucherFormKind FormKind { get; init; } = VoucherFormKind.GenericVoucher;

    public VoucherCaptionMode CaptionMode { get; init; } = VoucherCaptionMode.DebitCredit;

    /// <summary>
    /// Documents of this pattern dated on or before this day cannot be created, edited, deleted, posted or reversed (Al-Ameen "تاريخ تثبيت
    /// السندات"). Not the same as <see cref="PatternEntryPolicy.LockDocumentDate"/>. Enforced by the voucher service (AMN-VPAT-GATE-002).
    /// </summary>
    public DateOnly? DenyMutationsOnOrBefore { get; init; }

    /// <summary>Which accounts and cost centers the voucher's lines may use; empty lists = no restriction beyond the company's own rules.</summary>
    public PatternLookupPolicy Lookup { get; init; } = new();

    /// <summary>Reserved (AMN-VPAT-GATE-003): one journal entry per voucher line instead of one grouped entry.</summary>
    public bool PerLineJournal { get; init; }

    /// <summary>Whether the one-step save-and-process path may post this pattern's vouchers (see <see cref="VoucherPostingMode"/>).</summary>
    public VoucherPostingMode PostingMode { get; init; } = VoucherPostingMode.InheritFromCompany;

    /// <summary>Reserved: post by itself the moment an approval is recorded. Not enforced (needs an approval event hook); rejected when true.</summary>
    public bool AutoPost { get; init; }
}

/// <summary>
/// Al-Ameen "تصفية نوافذ اختيار الحسابات ومراكز الكلفة". Accounts are chosen by subtree (a root and everything under it); cost centers
/// are listed. The server applies it to the lookup AND to the saved lines, so a forged id is refused exactly like a hidden one.
/// </summary>
public sealed record PatternLookupPolicy
{
    public List<Guid> EligibleAccountRootIds { get; init; } = [];

    public List<Guid> EligibleCostCenterIds { get; init; } = [];

    public bool RestrictsAccounts => EligibleAccountRootIds is { Count: > 0 };

    public bool RestrictsCostCenters => EligibleCostCenterIds is { Count: > 0 };
}

public static class VoucherPatternRules
{
    public const string LookupInvalid = "patterns.voucher_lookup_invalid";
    public const int MaximumLookupEntries = 200;

    public const string BlockNotAllowed = "patterns.voucher_block_not_allowed";
    public const string FormUnsupported = "patterns.voucher_form_unsupported";
    public const string OptionNotEnforced = "patterns.voucher_option_not_enforced";
    public const string SchemaUnsupported = "patterns.schema_unsupported";
    public const string CutoffInvalid = "patterns.voucher_cutoff_invalid";

    /// <summary>
    /// True when a document dated <paramref name="documentDate"/> falls in the pattern's frozen period (on or before the cut-off day).
    /// Independent of <see cref="PatternEntryPolicy.LockDocumentDate"/>, which only fixes a NEW document's date to the day it is added.
    /// </summary>
    public static bool IsFrozen(PatternConfiguration configuration, DateOnly documentDate) =>
        Effective(configuration).DenyMutationsOnOrBefore is { } cutoff && documentDate <= cutoff;

    /// <summary>The operations that run as vouchers: receipts, payments and the two journal forms.</summary>
    public static bool IsVoucherOperation(BusinessOperationKind kind) =>
        kind is BusinessOperationKind.CashReceipt or BusinessOperationKind.CashPayment
            or BusinessOperationKind.CashJournal or BusinessOperationKind.ManualJournal;

    /// <summary>What a revision runs with: its own block, or the defaults when it has none (every schema-1 revision).</summary>
    public static PatternVoucherPolicy Effective(PatternConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var policy = configuration.Voucher ?? new PatternVoucherPolicy();
        return policy.Lookup is null ? policy with { Lookup = new PatternLookupPolicy() } : policy;
    }

    /// <summary>
    /// The approval rule of a pattern for a document of <paramref name="amount"/>: approval is required when the pattern requires it and
    /// the amount reaches the threshold (no threshold = every document). Same comparison the sales documents use, in document currency.
    /// </summary>
    public static bool ApprovalRequired(PatternConfiguration configuration, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var approval = configuration.ApprovalPolicy ?? new PatternApprovalPolicy();
        return approval.RequiresApproval && (approval.ThresholdAmount is not { } threshold || amount >= threshold);
    }

    public const string LineFieldRequired = "line_field_required";
    public const string LineFieldHidden = "line_field_hidden";

    /// <summary>
    /// Checks a voucher line against the pattern's field rules for the two optional line fields the voucher contract carries:
    /// "CostCenter" and "Description" (alias "Notes"). A required visible field must be filled; a hidden field must stay empty.
    /// Returns (code, fieldName) of the first violation or null. Fields the rules do not name are never enforced.
    /// </summary>
    public static (string Code, string Field)? CheckLineFields(PatternConfiguration configuration, bool hasCostCenter, bool hasDescription)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        foreach (var rule in configuration.LineFields ?? [])
        {
            var present = rule.FieldName switch
            {
                "CostCenter" => hasCostCenter,
                "Description" or "Notes" => hasDescription,
                _ => (bool?)null,
            };
            if (present is not { } filled) continue;
            if (!rule.IsVisible && filled) return (LineFieldHidden, rule.FieldName);
            if (rule.IsVisible && rule.IsRequired && !filled) return (LineFieldRequired, rule.FieldName);
        }

        return null;
    }

    /// <summary>Returns the block when it is valid for the operation, null for none; otherwise a stable rule violation.</summary>
    public static PatternVoucherPolicy? Validate(BusinessOperationKind kind, PatternVoucherPolicy? policy)
    {
        if (policy is null) return null;
        if (!IsVoucherOperation(kind))
            throw RuleViolation.Create(BlockNotAllowed, $"Operation '{kind}' is not a voucher; it has no voucher settings.");
        if (!Enum.IsDefined(policy.FormKind) || !Enum.IsDefined(policy.CaptionMode) || !Enum.IsDefined(policy.PostingMode))
            throw RuleViolation.Create(FormUnsupported, "The voucher form kind, caption mode or posting mode is not defined.");
        if (policy.FormKind != VoucherFormKind.GenericVoucher)
            throw RuleViolation.Create(FormUnsupported, $"Voucher form '{policy.FormKind}' is not supported yet; only GenericVoucher runs.");
        if (policy.DenyMutationsOnOrBefore is { } cutoff && (cutoff.Year < 2000 || cutoff.Year > 2100))
            throw RuleViolation.Create(CutoffInvalid, "The voucher cut-off date must be a real calendar date between 2000 and 2100.");
        if (policy.PerLineJournal)
            throw RuleViolation.Create(OptionNotEnforced, "One journal entry per line is not enforced yet and cannot be stored.");
        if (policy.AutoPost)
            throw RuleViolation.Create(OptionNotEnforced, "Posting by itself at approval time is not enforced and cannot be stored; use PostingMode to hold vouchers for manual posting.");
        var lookup = NormalizeLookup(policy.Lookup);
        return policy with { Lookup = lookup };
    }

    /// <summary>Distinct, non-empty ids, bounded in number. Existence in the company is checked by the caller.</summary>
    public static PatternLookupPolicy NormalizeLookup(PatternLookupPolicy? lookup)
    {
        var accounts = (lookup?.EligibleAccountRootIds ?? []).Distinct().ToList();
        var centers = (lookup?.EligibleCostCenterIds ?? []).Distinct().ToList();
        if (accounts.Contains(Guid.Empty) || centers.Contains(Guid.Empty))
            throw RuleViolation.Create(LookupInvalid, "A lookup restriction cannot name an empty id.");
        if (accounts.Count > MaximumLookupEntries || centers.Count > MaximumLookupEntries)
            throw RuleViolation.Create(LookupInvalid, $"At most {MaximumLookupEntries} accounts and {MaximumLookupEntries} cost centers can be listed.");
        return new PatternLookupPolicy { EligibleAccountRootIds = accounts, EligibleCostCenterIds = centers };
    }

    /// <summary>
    /// The ids of every account under the restriction's roots (the roots included). <paramref name="parents"/> maps account id to parent
    /// id for the whole company; cycles and unknown roots simply contribute nothing.
    /// </summary>
    public static HashSet<Guid> EligibleAccounts(PatternLookupPolicy lookup, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(parents);
        var roots = new HashSet<Guid>(lookup.EligibleAccountRootIds ?? []);
        var eligible = new HashSet<Guid>();
        foreach (var (id, _) in parents)
        {
            var current = (Guid?)id;
            var guard = 0;
            while (current is { } step && guard++ < 64)
            {
                if (roots.Contains(step)) { eligible.Add(id); break; }
                current = parents.TryGetValue(step, out var parent) ? parent : null;
            }
        }

        return eligible;
    }
}

/// <summary>
/// Schema upgrade, in memory and non-destructive: version 1 becomes version 2 with every value kept and the voucher block left
/// absent (= defaults). A stored revision is never rewritten; a new draft is stored at the current version.
/// </summary>
public static class PatternConfigurationUpgrader
{
    public static PatternConfiguration ToCurrent(PatternConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.SchemaVersion is < 1 or > PatternConfiguration.CurrentSchemaVersion)
            throw RuleViolation.Create(VoucherPatternRules.SchemaUnsupported,
                $"Pattern schema version {configuration.SchemaVersion} is not supported (1..{PatternConfiguration.CurrentSchemaVersion}).");
        return configuration.SchemaVersion == PatternConfiguration.CurrentSchemaVersion
            ? configuration
            : configuration with { SchemaVersion = PatternConfiguration.CurrentSchemaVersion };
    }
}
