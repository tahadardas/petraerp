using PetraERP.Domain.Parties;

namespace PetraERP.Application.Parties;

public sealed record PartyContactResult(
    Guid Id,
    string Name,
    string? Role,
    string? Phone,
    string? Mobile,
    string? Email);

public sealed record PartyAddressResult(
    Guid Id,
    string Label,
    string? City,
    string? Street,
    string? PostalCode,
    string? Notes);

public sealed record PartySummaryResult(
    Guid Id,
    Guid CompanyId,
    string Code,
    string NameArabic,
    string? NameEnglish,
    PartyRole Role,
    bool IsActive,
    string? TaxNumber,
    string? DefaultCurrencyCode,
    Guid? ReceivableAccountId,
    Guid? PayableAccountId,
    decimal CreditLimit,
    int PaymentTermDays,
    CreditPolicyKind PolicyKind,
    decimal CurrentBalance,
    decimal AvailableCredit)
{
    public bool IsCreditBlocked { get; init; }
    public bool IsCashCustomer { get; init; }
    public decimal ReceivableBalance { get; init; }
    public decimal PayableBalance { get; init; }
}

public sealed record PartyDetailResult(
    Guid Id,
    Guid CompanyId,
    string Code,
    string NameArabic,
    string? NameEnglish,
    PartyRole Role,
    bool IsActive,
    string? TaxNumber,
    string? CommercialRegistration,
    string? DefaultCurrencyCode,
    Guid? ReceivableAccountId,
    Guid? PayableAccountId,
    decimal CreditLimit,
    int PaymentTermDays,
    CreditPolicyKind PolicyKind,
    decimal CurrentBalance,
    decimal AvailableCredit,
    IReadOnlyList<PartyContactResult> Contacts,
    IReadOnlyList<PartyAddressResult> Addresses,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string VersionToken)
{
    public bool IsCreditBlocked { get; init; }
    public bool IsCashCustomer { get; init; }
    public PartyProfileData? Profile { get; init; }
    public decimal ReceivableBalance { get; init; }
    public decimal PayableBalance { get; init; }
    public IReadOnlyList<PartyPolicyAuditResult> PolicyHistory { get; init; } = [];
}

public sealed record PartyLookupItem(
    Guid Id,
    string Code,
    string NameArabic,
    string? NameEnglish,
    PartyRole Role,
    bool IsActive,
    Guid? ReceivableAccountId,
    Guid? PayableAccountId,
    decimal CreditLimit,
    decimal CurrentBalance,
    decimal AvailableCredit)
{
    public bool IsCreditBlocked { get; init; }
    public bool IsCashCustomer { get; init; }
    public decimal ReceivableBalance { get; init; }
    public decimal PayableBalance { get; init; }
}

public sealed record PartyPageResult(
    IReadOnlyList<PartySummaryResult> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed record PartyCreditExposureResult(
    Guid PartyId,
    decimal CreditLimit,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal CurrentBalance,
    decimal AvailableCredit,
    CreditPolicyKind PolicyKind,
    bool IsExceeded);

public sealed record PartyPolicyAuditResult(Guid Id, Guid ActorId, decimal BeforeCreditLimit, decimal AfterCreditLimit,
    int BeforePolicy, int AfterPolicy, bool BeforeBlocked, bool AfterBlocked, DateTimeOffset DateUtc);

/// <summary>
/// One side (receivable or payable) of the plan for a party saved without an account: the heading the new account goes under and
/// the code it would get. <see cref="ParentAccountId"/> is null when the heading itself does not exist yet and would be created
/// by the chart convention.
/// </summary>
public sealed record PartyAccountPlanSide(Guid? ParentAccountId, string ParentCode, string ParentNameArabic, string NextAccountCode,
    bool FromCompanyPolicy, bool ParentWillBeCreated);

/// <summary>What the server would create for a new customer and a new supplier that name no account. Read-only preview.</summary>
public sealed record PartyAccountPlanResult(PartyAccountPlanSide Receivable, PartyAccountPlanSide Payable);
