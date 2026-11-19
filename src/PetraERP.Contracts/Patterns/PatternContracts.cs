using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Patterns;

public enum BusinessOperationKind
{
    SalesInvoice = 1,
    SalesReturn = 2,
    PurchaseInvoice = 3,
    PurchaseReturn = 4,
    InventoryIssue = 5,
    InventoryReceipt = 6,
    CashReceipt = 7,
    CashPayment = 8,
    CashJournal = 9,
    ManualJournal = 10,
    Assembly = 11,
    Disassembly = 12,
}

public enum PatternRevisionState
{
    Draft = 1,
    Validated = 2,
    Published = 3,
    Retired = 4,
}

public enum PatternNumberingScope
{
    Company = 1,
    Branch = 2,
    FiscalYear = 3,
    BranchAndFiscalYear = 4,
}


/// <summary>How a voucher pattern runs; independent of the accounting operation (receipt, payment, journal).</summary>
public enum VoucherFormKind
{
    GenericVoucher = 1,
    MultiMethodReceipt = 2,
    MultiMethodPayment = 3,
}

/// <summary>How the one-step save-and-process path posts a pattern's vouchers; a pattern can only be stricter than the company policy.</summary>
public enum VoucherPostingMode
{
    InheritFromCompany = 0,
    ManualOnly = 1,
}

/// <summary>Column captions only; never changes the direction of an entry.</summary>
public enum VoucherCaptionMode
{
    DebitCredit = 0,
    FromTo = 1,
}

/// <summary>
/// Voucher settings of a revision (schema version 2). Absent = defaults: GenericVoucher, DebitCredit. Options the server does not
/// enforce yet are rejected unless left at their default.
/// </summary>
public sealed record PatternLookupPolicyDto
{
    /// <summary>Accounts the lines may use: each listed account and everything under it. Empty = no restriction.</summary>
    public List<Guid> EligibleAccountRootIds { get; init; } = [];
    /// <summary>Cost centers the lines may use. Empty = no restriction.</summary>
    public List<Guid> EligibleCostCenterIds { get; init; } = [];
}

public sealed record PatternVoucherPolicyDto
{
    public PatternLookupPolicyDto? Lookup { get; init; }
    public VoucherFormKind FormKind { get; init; } = VoucherFormKind.GenericVoucher;
    public VoucherCaptionMode CaptionMode { get; init; } = VoucherCaptionMode.DebitCredit;
    /// <summary>Vouchers of this pattern dated on or before this day cannot be created, edited, deleted, posted or reversed.</summary>
    public DateOnly? DenyMutationsOnOrBefore { get; init; }
    /// <summary>Reserved; rejected when true until it is enforced.</summary>
    public bool PerLineJournal { get; init; }
    /// <summary>InheritFromCompany (default) or ManualOnly: hold this pattern's vouchers for a deliberate manual post.</summary>
    public VoucherPostingMode PostingMode { get; init; } = VoucherPostingMode.InheritFromCompany;
    /// <summary>Reserved; rejected when true (posting at approval time is not enforced).</summary>
    public bool AutoPost { get; init; }
}

public sealed record PatternConfigurationDto
{
    public int SchemaVersion { get; init; } = 1;
    public PatternVoucherPolicyDto? Voucher { get; init; }
    public Guid? DefaultWarehouseId { get; init; }
    public string? DefaultCurrencyCode { get; init; }
    public Guid? DefaultCostCenterId { get; init; }
    public List<PatternAccountMappingDto> AccountMappings { get; init; } = [];
    public List<string> AllowedPaymentMethods { get; init; } = [];
    public List<Guid> AllowedWarehouseIds { get; init; } = [];
    public List<PatternFieldRuleDto> LineFields { get; init; } = [];
    public List<string> OptionOverridesWhitelist { get; init; } = [];
    public PatternNumberingPolicyDto NumberingPolicy { get; init; } = new();
    public PatternApprovalPolicyDto ApprovalPolicy { get; init; } = new();
    public PatternPricingPolicyDto PricingPolicy { get; init; } = new();
    public PatternEntryPolicyDto EntryPolicy { get; init; } = new();
}

/// <summary>Al-Ameen "تثبيت" options of a pattern.</summary>
public sealed record PatternEntryPolicyDto
{
    /// <summary>The document date is the day it is added and cannot be edited.</summary>
    public bool LockDocumentDate { get; init; }
}

public sealed record PatternPricingPolicyDto
{
    public bool AllowManualPriceOverride { get; init; } = true;
    [RegularExpression(FinancialDecimalText.RatePattern)]
    public string? MaxDiscountPercentage { get; init; }
    public bool RequirePriceQuote { get; init; }
}

public sealed record PatternAccountMappingDto
{
    public string Role { get; init; } = string.Empty;
    public Guid AccountId { get; init; }
    public bool IsRequired { get; init; } = true;
    public bool AllowOverride { get; init; }
}

public sealed record PatternFieldRuleDto
{
    public string FieldName { get; init; } = string.Empty;
    public string CaptionArabic { get; init; } = string.Empty;
    public bool IsVisible { get; init; } = true;
    public bool IsRequired { get; init; }
    public int DisplayOrder { get; init; }
}

public enum PatternNumberResetPolicy { Never = 0, Monthly = 1 }
public sealed record PatternNumberingPolicyDto
{
    public long InitialSequence { get; init; } = 1;
    public PatternNumberResetPolicy ResetPolicy { get; init; }
    public string Prefix { get; init; } = string.Empty;
    public string Suffix { get; init; } = string.Empty;
    public int MinimumDigits { get; init; } = 6;
    public PatternNumberingScope Scope { get; init; } = PatternNumberingScope.BranchAndFiscalYear;
    public bool AllowManualNumbering { get; init; }
}

public sealed record PatternApprovalPolicyDto
{
    public bool RequiresApproval { get; init; }
    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? ThresholdAmount { get; init; }
}

/// <summary>The active published revision a branch runs, with the voucher block resolved. Clients read this; they never infer fields or accounts from names.</summary>
public sealed record EffectivePatternResponse
{
    public Guid PatternId { get; init; }
    public Guid RevisionId { get; init; }
    public int RevisionNumber { get; init; }
    public BusinessOperationKind OperationKind { get; init; }
    public string Code { get; init; } = string.Empty;
    public PatternConfigurationDto Configuration { get; init; } = new();
}

public sealed record PatternAccountLookupItemResponse
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
}

public sealed record DocumentPatternSummaryResponse
{
    public Guid Id { get; init; }
    public BusinessOperationKind OperationKind { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public string ShortCode { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public string? Group { get; init; }
    public bool IsActive { get; init; }
    public bool IsDefault { get; init; }
    public bool IsRunnable { get; init; }
    public int? ActiveRevisionNumber { get; init; }
    public string Version { get; init; } = string.Empty;
}

public sealed record DocumentPatternResponse
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public BusinessOperationKind OperationKind { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public string ShortCode { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public string? Group { get; init; }
    public bool IsActive { get; init; }
    public bool IsDefault { get; init; }
    public bool IsRunnable { get; init; }
    public string Version { get; init; } = string.Empty;
    public IReadOnlyList<Guid> BranchIds { get; init; } = [];
    public PatternRevisionResponse? ActiveRevision { get; init; }
    public IReadOnlyList<PatternRevisionResponse> Revisions { get; init; } = [];
}

public sealed record DocumentPatternPageResponse
{
    public IReadOnlyList<DocumentPatternSummaryResponse> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }
}

public sealed record PatternRevisionResponse
{
    public Guid Id { get; init; }
    public Guid PatternId { get; init; }
    public int RevisionNumber { get; init; }
    public PatternRevisionState State { get; init; }
    public DateTime? EffectiveFromUtc { get; init; }
    public DateTime? EffectiveToUtc { get; init; }
    public PatternConfigurationDto Configuration { get; init; } = new();
    public Guid ActorUserId { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
}

public sealed record CreateDocumentPatternRequest
{
    public BusinessOperationKind OperationKind { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public string ShortCode { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public string? Group { get; init; }
    public bool IsDefault { get; init; }
    public IReadOnlyList<Guid>? BranchIds { get; init; }
    public PatternConfigurationDto? InitialConfiguration { get; init; }
}

public sealed record UpdateDocumentPatternRequest
{
    public string ExpectedVersion { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public string ShortCode { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public string? Group { get; init; }
    public bool IsActive { get; init; }
    public bool IsDefault { get; init; }
    public IReadOnlyList<Guid>? BranchIds { get; init; }
}

public sealed record CreatePatternRevisionRequest
{
    public string ExpectedVersion { get; init; } = string.Empty;
    public PatternConfigurationDto Configuration { get; init; } = new();
    public string Reason { get; init; } = string.Empty;
}

public sealed record PublishPatternRevisionRequest
{
    public string ExpectedVersion { get; init; } = string.Empty;
    public Guid RevisionId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record RetirePatternRequest
{
    public string ExpectedVersion { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed record NextNumberResponse
{
    public long SequenceNumber { get; init; }
    public string FormattedNumber { get; init; } = string.Empty;
}

/// <summary>
/// The number a document would most likely receive, read without reserving it. Always an estimate: the authoritative
/// number is allocated by the server when the document is saved, so abandoning a draft never burns a number.
/// </summary>
public sealed record NextNumberPreviewResponse
{
    public bool IsEstimate { get; init; }
    public long SequenceNumber { get; init; }
    public string FormattedNumber { get; init; } = string.Empty;

    /// <summary>Stable reason no estimate is available, e.g. the fiscal year or document date is still missing.</summary>
    public string? UnavailableReason { get; init; }
}
