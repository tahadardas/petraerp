using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Parties;

public enum PartyRole
{
    Customer = 1,
    Supplier = 2,
    Both = 3
}

public enum CreditPolicyKind
{
    AllowWithWarning = 1,
    BlockOnExceed = 2,
    NoLimit = 3
}

public sealed record PartyContactDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    [Required]
    public string Name { get; init; } = string.Empty;
    public string? Role { get; init; }
    public string? Phone { get; init; }
    public string? Mobile { get; init; }
    public string? Email { get; init; }
}

public sealed record PartyAddressDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    [Required]
    public string Label { get; init; } = string.Empty;
    public string? City { get; init; }
    public string? Street { get; init; }
    public string? PostalCode { get; init; }
    public string? Notes { get; init; }
}

public sealed record PartySummaryDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public PartyRole Role { get; init; }
    public bool IsActive { get; init; }
    public string? TaxNumber { get; init; }
    public string? DefaultCurrencyCode { get; init; }
    public Guid? ReceivableAccountId { get; init; }
    public Guid? PayableAccountId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CreditLimit { get; init; } = "0.0000";

    public int PaymentTermDays { get; init; }
    public CreditPolicyKind PolicyKind { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CurrentBalance { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string AvailableCredit { get; init; } = "0.0000";
    public bool IsCreditBlocked { get; init; }
    public bool IsCashCustomer { get; init; }
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string ReceivableBalance { get; init; } = "0.0000";
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string PayableBalance { get; init; } = "0.0000";

}

public sealed record PartyDetailDto
{
    public PartyProfileDto? Profile { get; init; }

    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public PartyRole Role { get; init; }
    public bool IsActive { get; init; }
    public string? TaxNumber { get; init; }
    public string? CommercialRegistration { get; init; }
    public string? DefaultCurrencyCode { get; init; }
    public Guid? ReceivableAccountId { get; init; }
    public Guid? PayableAccountId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CreditLimit { get; init; } = "0.0000";

    public int PaymentTermDays { get; init; }
    public CreditPolicyKind PolicyKind { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CurrentBalance { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string AvailableCredit { get; init; } = "0.0000";

    public List<PartyContactDto> Contacts { get; init; } = [];
    public List<PartyAddressDto> Addresses { get; init; } = [];
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public string VersionToken { get; init; } = string.Empty;
    public bool IsCreditBlocked { get; init; }
    public bool IsCashCustomer { get; init; }
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string ReceivableBalance { get; init; } = "0.0000";
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string PayableBalance { get; init; } = "0.0000";

    public IReadOnlyList<PartyPolicyAuditDto> PolicyHistory { get; init; } = [];

}

public sealed record CreatePartyRequest
{
    public PartyProfileDto? Profile { get; init; }

    [Required, MaxLength(64)]
    public string Code { get; init; } = string.Empty;

    [Required, MaxLength(200)]
    public string NameArabic { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? NameEnglish { get; init; }

    public PartyRole Role { get; init; } = PartyRole.Customer;

    [MaxLength(64)]
    public string? TaxNumber { get; init; }

    [MaxLength(64)]
    public string? CommercialRegistration { get; init; }

    [MaxLength(3)]
    public string? DefaultCurrencyCode { get; init; }

    public Guid? ReceivableAccountId { get; init; }
    public Guid? PayableAccountId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CreditLimit { get; init; } = "0.0000";

    public int PaymentTermDays { get; init; }
    public CreditPolicyKind PolicyKind { get; init; } = CreditPolicyKind.NoLimit;

    public List<PartyContactDto>? Contacts { get; init; }
    public List<PartyAddressDto>? Addresses { get; init; }
    public bool IsCreditBlocked { get; init; }

}

public sealed record UpdatePartyRequest
{
    public PartyProfileDto? Profile { get; init; }

    [Required, MaxLength(200)]
    public string NameArabic { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? NameEnglish { get; init; }

    public PartyRole Role { get; init; }

    [MaxLength(64)]
    public string? TaxNumber { get; init; }

    [MaxLength(64)]
    public string? CommercialRegistration { get; init; }

    [MaxLength(3)]
    public string? DefaultCurrencyCode { get; init; }

    public Guid? ReceivableAccountId { get; init; }
    public Guid? PayableAccountId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CreditLimit { get; init; } = "0.0000";

    public int PaymentTermDays { get; init; }
    public CreditPolicyKind PolicyKind { get; init; }

    [Required, StringLength(12, MinimumLength = 12)]
    public string? ExpectedVersion { get; init; }

    public List<PartyContactDto>? Contacts { get; init; }
    public List<PartyAddressDto>? Addresses { get; init; }
    public bool IsCreditBlocked { get; init; }

}

public sealed record PartyLookupDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public PartyRole Role { get; init; }
    public bool IsActive { get; init; }
    public Guid? ReceivableAccountId { get; init; }
    public Guid? PayableAccountId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CreditLimit { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CurrentBalance { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string AvailableCredit { get; init; } = "0.0000";
    public bool IsCreditBlocked { get; init; }
    public bool IsCashCustomer { get; init; }
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string ReceivableBalance { get; init; } = "0.0000";
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string PayableBalance { get; init; } = "0.0000";

}

public sealed record PartyCreditExposureDto
{
    public Guid PartyId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CreditLimit { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string TotalDebit { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string TotalCredit { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CurrentBalance { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string AvailableCredit { get; init; } = "0.0000";

    public CreditPolicyKind PolicyKind { get; init; }
    public bool IsExceeded { get; init; }
}

public sealed record PartyListResponse
{
    public List<PartySummaryDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
}

public sealed record PartyPolicyAuditDto
{
    public Guid Id { get; init; }
    public Guid ActorId { get; init; }
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string BeforeCreditLimit { get; init; } = "0.0000";
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string AfterCreditLimit { get; init; } = "0.0000";
    public int BeforePolicy { get; init; }
    public int AfterPolicy { get; init; }
    public bool BeforeBlocked { get; init; }
    public bool AfterBlocked { get; init; }
    public DateTimeOffset DateUtc { get; init; }
}

/// <summary>One side of the account plan for a party saved without an account.</summary>
public sealed record PartyAccountPlanSideDto
{
    /// <summary>The heading the new account goes under; null when the heading itself would be created by the chart convention.</summary>
    public Guid? ParentAccountId { get; init; }
    public string ParentCode { get; init; } = string.Empty;
    public string ParentNameArabic { get; init; } = string.Empty;
    public string NextAccountCode { get; init; } = string.Empty;
    public bool FromCompanyPolicy { get; init; }
    public bool ParentWillBeCreated { get; init; }
}

/// <summary>Read-only preview of the accounts a new customer and a new supplier would get when none is chosen.</summary>
public sealed record PartyAccountPlanDto
{
    public PartyAccountPlanSideDto Receivable { get; init; } = new();
    public PartyAccountPlanSideDto Payable { get; init; } = new();
}
