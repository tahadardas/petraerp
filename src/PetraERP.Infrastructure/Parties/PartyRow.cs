using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Parties;

internal sealed class PartyRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameArabic { get; set; } = string.Empty;
    public string? NameEnglish { get; set; }
    public string NormalizedSearchText { get; set; } = string.Empty;
    public int Role { get; set; } // 1=Customer, 2=Supplier, 3=Both
    public bool IsActive { get; set; } = true;
    public bool IsCashCustomer { get; set; }
    public bool IsCreditBlocked { get; set; }
    public string? Title { get; set; }
    public string? Suffix { get; set; }
    public string? Barcode { get; set; }
    public bool HideInSearch { get; set; }
    public decimal DiscountPercent { get; set; }
    public string SalesPriceType { get; set; } = "CONSUMER";
    public Guid? ConditionalDiscountAccountId { get; set; }
    public Guid? VatAccountId { get; set; }
    public string? Notes { get; set; }
    public string? TaxNumber { get; set; }
    public string? CommercialRegistration { get; set; }
    public string? DefaultCurrencyCode { get; set; }
    public Guid? ReceivableAccountId { get; set; }
    public Guid? PayableAccountId { get; set; }
    public decimal CreditLimit { get; set; }
    public int PaymentTermDays { get; set; }
    public int CreditPolicyKind { get; set; } // 1=AllowWithWarning, 2=BlockOnExceed, 3=NoLimit
    public string? ContactsJson { get; set; }
    public string? AddressesJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigations
    public AccountRow? ReceivableAccount { get; set; }
    public AccountRow? PayableAccount { get; set; }
}
