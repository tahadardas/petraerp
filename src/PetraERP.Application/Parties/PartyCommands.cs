using PetraERP.Domain.Parties;

namespace PetraERP.Application.Parties;

public sealed record CreatePartyCommand(
    string Code,
    string NameArabic,
    string? NameEnglish,
    PartyRole Role,
    string? TaxNumber = null,
    string? CommercialRegistration = null,
    string? DefaultCurrencyCode = null,
    Guid? ReceivableAccountId = null,
    Guid? PayableAccountId = null,
    decimal CreditLimit = 0m,
    int PaymentTermDays = 0,
    CreditPolicyKind PolicyKind = CreditPolicyKind.NoLimit,
    IReadOnlyList<PartyContactInput>? Contacts = null,
    IReadOnlyList<PartyAddressInput>? Addresses = null,
    PartyProfileData? Profile = null)
{
    public bool IsCreditBlocked { get; init; }
    /// <summary>Trusted caller context, never bound from a request body.</summary>
    public Guid ChangedByUserId { get; init; }
    public Guid ChangedInBranchId { get; init; }
}

public sealed record UpdatePartyCommand(
    string NameArabic,
    string? NameEnglish,
    PartyRole Role,
    string? TaxNumber = null,
    string? CommercialRegistration = null,
    string? DefaultCurrencyCode = null,
    Guid? ReceivableAccountId = null,
    Guid? PayableAccountId = null,
    decimal CreditLimit = 0m,
    int PaymentTermDays = 0,
    CreditPolicyKind PolicyKind = CreditPolicyKind.NoLimit,
    string? ExpectedVersion = null,
    IReadOnlyList<PartyContactInput>? Contacts = null,
    IReadOnlyList<PartyAddressInput>? Addresses = null,
    PartyProfileData? Profile = null)
{
    public bool IsCreditBlocked { get; init; }
    /// <summary>Trusted caller context, never bound from a request body.</summary>
    public Guid ChangedByUserId { get; init; }
    public Guid ChangedInBranchId { get; init; }
}

public sealed record PartyContactInput(
    Guid? Id,
    string Name,
    string? Role = null,
    string? Phone = null,
    string? Mobile = null,
    string? Email = null);

public sealed record PartyAddressInput(
    Guid? Id,
    string Label,
    string? City = null,
    string? Street = null,
    string? PostalCode = null,
    string? Notes = null);

public sealed record PartySearchQuery(
    string? SearchText = null,
    PartyRole? RoleFilter = null,
    bool? IsActiveFilter = null,
    int PageNumber = 1,
    int PageSize = 50);
