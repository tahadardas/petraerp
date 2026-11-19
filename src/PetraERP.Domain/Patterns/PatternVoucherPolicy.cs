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

    /// <summary>Reserved (AMN-VPAT-GATE-002): documents dated on or before this day become read-only. Not the same as <see cref="PatternEntryPolicy.LockDocumentDate"/>.</summary>
    public DateOnly? DenyMutationsOnOrBefore { get; init; }

    /// <summary>Reserved (AMN-VPAT-GATE-003): one journal entry per voucher line instead of one grouped entry.</summary>
    public bool PerLineJournal { get; init; }

    /// <summary>Reserved (AMN-VPAT-GATE-003): post right after approval. It will never bypass a required approval.</summary>
    public bool AutoPost { get; init; }
}

public static class VoucherPatternRules
{
    public const string BlockNotAllowed = "patterns.voucher_block_not_allowed";
    public const string FormUnsupported = "patterns.voucher_form_unsupported";
    public const string OptionNotEnforced = "patterns.voucher_option_not_enforced";
    public const string SchemaUnsupported = "patterns.schema_unsupported";

    /// <summary>The operations that run as vouchers: receipts, payments and the two journal forms.</summary>
    public static bool IsVoucherOperation(BusinessOperationKind kind) =>
        kind is BusinessOperationKind.CashReceipt or BusinessOperationKind.CashPayment
            or BusinessOperationKind.CashJournal or BusinessOperationKind.ManualJournal;

    /// <summary>What a revision runs with: its own block, or the defaults when it has none (every schema-1 revision).</summary>
    public static PatternVoucherPolicy Effective(PatternConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.Voucher ?? new PatternVoucherPolicy();
    }

    /// <summary>Returns the block when it is valid for the operation, null for none; otherwise a stable rule violation.</summary>
    public static PatternVoucherPolicy? Validate(BusinessOperationKind kind, PatternVoucherPolicy? policy)
    {
        if (policy is null) return null;
        if (!IsVoucherOperation(kind))
            throw RuleViolation.Create(BlockNotAllowed, $"Operation '{kind}' is not a voucher; it has no voucher settings.");
        if (!Enum.IsDefined(policy.FormKind) || !Enum.IsDefined(policy.CaptionMode))
            throw RuleViolation.Create(FormUnsupported, "The voucher form kind or caption mode is not defined.");
        if (policy.FormKind != VoucherFormKind.GenericVoucher)
            throw RuleViolation.Create(FormUnsupported, $"Voucher form '{policy.FormKind}' is not supported yet; only GenericVoucher runs.");
        if (policy.DenyMutationsOnOrBefore is not null)
            throw RuleViolation.Create(OptionNotEnforced, "The voucher cut-off date is not enforced yet and cannot be stored.");
        if (policy.PerLineJournal)
            throw RuleViolation.Create(OptionNotEnforced, "One journal entry per line is not enforced yet and cannot be stored.");
        if (policy.AutoPost)
            throw RuleViolation.Create(OptionNotEnforced, "Automatic posting by pattern is not enforced yet and cannot be stored.");
        return policy;
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
