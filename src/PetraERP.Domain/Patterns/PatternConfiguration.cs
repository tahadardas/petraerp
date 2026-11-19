namespace PetraERP.Domain.Patterns;

/// <summary>
/// Strictly typed, schema-versioned configuration model stored in a pattern revision.
/// Evaluated dynamically by the posting engine; no dynamic SQL or unvalidated scripting allowed.
/// </summary>
public sealed record PatternConfiguration
{
    /// <summary>Version 2 adds the optional <see cref="Voucher"/> block; version-1 revisions stay valid and run with its defaults.</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = 1;

    /// <summary>Voucher run settings; null = defaults (see <see cref="VoucherPatternRules.Effective"/>). Only for voucher operations.</summary>
    public PatternVoucherPolicy? Voucher { get; init; }

    public Guid? DefaultWarehouseId { get; init; }

    public string? DefaultCurrencyCode { get; init; }

    public Guid? DefaultCostCenterId { get; init; }

    public List<PatternAccountMapping> AccountMappings { get; init; } = [];

    public List<string> AllowedPaymentMethods { get; init; } = [];

    public List<Guid> AllowedWarehouseIds { get; init; } = [];

    public List<PatternFieldRule> LineFields { get; init; } = [];

    public List<string> OptionOverridesWhitelist { get; init; } = [];

    public PatternNumberingPolicy NumberingPolicy { get; init; } = new();

    public PatternApprovalPolicy ApprovalPolicy { get; init; } = new();

    public PatternPricingPolicy PricingPolicy { get; init; } = new();

    public PatternEntryPolicy EntryPolicy { get; init; } = new();
}

/// <summary>
/// Al-Ameen "تثبيت" options of a pattern. The branch is the working branch and the number is assigned by the server (manual numbering is
/// <see cref="PatternNumberingPolicy.AllowManualNumbering"/>), so the one option left to fix is the date.
/// </summary>
public sealed record PatternEntryPolicy
{
    /// <summary>The document date is the day the document is added and cannot be edited (Al-Ameen "تثبيت تاريخ إضافة السند").</summary>
    public bool LockDocumentDate { get; init; }

    /// <summary>
    /// Al-Ameen journal detail: one revenue/discount/tax (sales) or inventory (purchases) line per document line inside the single
    /// document journal, instead of lines merged per account and cost center. Totals and balances are identical either way;
    /// default false keeps existing patterns posting exactly as before.
    /// </summary>
    public bool DetailedJournalLines { get; init; }
}

public sealed record PatternPricingPolicy
{
    public bool AllowManualPriceOverride { get; init; } = true;

    public decimal? MaxDiscountPercentage { get; init; }

    public bool RequirePriceQuote { get; init; }
}

public sealed record PatternAccountMapping
{
    public string Role { get; init; } = string.Empty;

    public Guid AccountId { get; init; }

    public bool IsRequired { get; init; } = true;

    public bool AllowOverride { get; init; }
}

public sealed record PatternFieldRule
{
    public string FieldName { get; init; } = string.Empty;

    /// <summary>Column header shown for this field on the document's grid (Al-Ameen: field captions come from the pattern); empty = the standard caption.</summary>
    public string CaptionArabic { get; init; } = string.Empty;

    public bool IsVisible { get; init; } = true;

    public bool IsRequired { get; init; }

    public int DisplayOrder { get; init; }
}

public enum PatternNumberingScope
{
    Company = 1,
    Branch = 2,
    FiscalYear = 3,
    BranchAndFiscalYear = 4,
}

public enum PatternNumberResetPolicy { Never = 0, Monthly = 1 }

public sealed record PatternNumberingPolicy
{
    public long InitialSequence { get; init; } = 1;
    public PatternNumberResetPolicy ResetPolicy { get; init; } = PatternNumberResetPolicy.Never;
    public string Prefix { get; init; } = string.Empty;

    public string Suffix { get; init; } = string.Empty;

    public int MinimumDigits { get; init; } = 6;

    public PatternNumberingScope Scope { get; init; } = PatternNumberingScope.BranchAndFiscalYear;

    public bool AllowManualNumbering { get; init; }
}

public sealed record PatternApprovalPolicy
{
    public bool RequiresApproval { get; init; }

    public decimal? ThresholdAmount { get; init; }
}
