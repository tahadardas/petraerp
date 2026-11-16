using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Parties;

public sealed class Party
{
    private readonly List<PartyContact> contacts = [];
    private readonly List<PartyAddress> addresses = [];

    private Party() { }

    public Party(
        Guid id,
        Guid companyId,
        EntityCode code,
        BilingualName name,
        PartyRole role,
        bool isActive = true,
        string? taxNumber = null,
        string? commercialRegistration = null,
        string? defaultCurrencyCode = null,
        Guid? receivableAccountId = null,
        Guid? payableAccountId = null,
        PartyCreditPolicy? creditPolicy = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Party ID cannot be empty.", nameof(id));
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID cannot be empty.", nameof(companyId));
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role), "Unknown party role.");

        ValidateAccountMappings(role, receivableAccountId, payableAccountId);

        Id = id;
        CompanyId = companyId;
        Code = code;
        Name = name;
        Role = role;
        IsActive = isActive;
        TaxNumber = string.IsNullOrWhiteSpace(taxNumber) ? null : taxNumber.Trim();
        CommercialRegistration = string.IsNullOrWhiteSpace(commercialRegistration) ? null : commercialRegistration.Trim();
        DefaultCurrencyCode = string.IsNullOrWhiteSpace(defaultCurrencyCode) ? null : defaultCurrencyCode.Trim().ToUpperInvariant();
        ReceivableAccountId = receivableAccountId;
        PayableAccountId = payableAccountId;
        CreditPolicy = creditPolicy ?? PartyCreditPolicy.Default;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public EntityCode Code { get; private set; } = null!;
    public BilingualName Name { get; private set; } = null!;
    public PartyRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public string? TaxNumber { get; private set; }
    public string? CommercialRegistration { get; private set; }
    public string? DefaultCurrencyCode { get; private set; }
    public Guid? ReceivableAccountId { get; private set; }
    public Guid? PayableAccountId { get; private set; }
    public PartyCreditPolicy CreditPolicy { get; private set; } = PartyCreditPolicy.Default;
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<PartyContact> Contacts => contacts.AsReadOnly();
    public IReadOnlyList<PartyAddress> Addresses => addresses.AsReadOnly();

    public void Update(
        BilingualName name,
        PartyRole role,
        string? taxNumber,
        string? commercialRegistration,
        string? defaultCurrencyCode,
        Guid? receivableAccountId,
        Guid? payableAccountId,
        PartyCreditPolicy creditPolicy)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        ArgumentNullException.ThrowIfNull(creditPolicy);

        ValidateAccountMappings(role, receivableAccountId, payableAccountId);

        Name = name;
        Role = role;
        TaxNumber = string.IsNullOrWhiteSpace(taxNumber) ? null : taxNumber.Trim();
        CommercialRegistration = string.IsNullOrWhiteSpace(commercialRegistration) ? null : commercialRegistration.Trim();
        DefaultCurrencyCode = string.IsNullOrWhiteSpace(defaultCurrencyCode) ? null : defaultCurrencyCode.Trim().ToUpperInvariant();
        ReceivableAccountId = receivableAccountId;
        PayableAccountId = payableAccountId;
        CreditPolicy = creditPolicy;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Reactivate()
    {
        IsActive = true;
    }

    public void SetContacts(IEnumerable<PartyContact> newContacts)
    {
        ArgumentNullException.ThrowIfNull(newContacts);
        contacts.Clear();
        contacts.AddRange(newContacts);
    }

    public void SetAddresses(IEnumerable<PartyAddress> newAddresses)
    {
        ArgumentNullException.ThrowIfNull(newAddresses);
        addresses.Clear();
        addresses.AddRange(newAddresses);
    }

    public string BuildSearchTarget()
    {
        var parts = new List<string>(5)
        {
            Code.Value,
            Name.Arabic
        };

        if (!string.IsNullOrWhiteSpace(Name.English))
        {
            parts.Add(Name.English);
        }

        if (!string.IsNullOrWhiteSpace(TaxNumber))
        {
            parts.Add(TaxNumber);
        }

        if (!string.IsNullOrWhiteSpace(CommercialRegistration))
        {
            parts.Add(CommercialRegistration);
        }

        return string.Join(" ", parts);
    }

    private static void ValidateAccountMappings(PartyRole role, Guid? receivableAccountId, Guid? payableAccountId)
    {
        if (role == PartyRole.Customer && payableAccountId.HasValue)
        {
            throw RuleViolation.Create(
                RuleViolation.PartyRoleInvalid,
                "A party with Customer-only role cannot have a payable account assigned.");
        }

        if (role == PartyRole.Supplier && receivableAccountId.HasValue)
        {
            throw RuleViolation.Create(
                RuleViolation.PartyRoleInvalid,
                "A party with Supplier-only role cannot have a receivable account assigned.");
        }
    }
}
