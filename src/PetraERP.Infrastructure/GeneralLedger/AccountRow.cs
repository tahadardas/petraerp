using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class AccountRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string NameArabic { get; set; }
    public string? NameEnglish { get; set; }

    /// <summary>
    /// Maps to <see cref="PetraERP.Domain.GeneralLedger.AccountType"/> value.
    /// 1=Assets, 2=Liabilities, 3=Equity, 4=Revenue, 5=Expenses (CK_Accounts_AccountType)
    /// </summary>
    public byte AccountType { get; set; }

    public bool PostingAllowed { get; set; }

    /// <summary>
    /// Maps to <see cref="PetraERP.Domain.GeneralLedger.CostCenterPolicy"/> value.
    /// 1=NotAllowed, 2=Optional, 3=Required (CK_Accounts_CostCenterPolicy)
    /// </summary>
    public byte CostCenterPolicy { get; set; }

    /// <summary>
    /// Maps to <see cref="PetraERP.Domain.GeneralLedger.AccountCurrencyMode"/> value.
    /// 1=BaseCurrencyOnly, 2=AnyEnabledCurrency, 3=FixedCurrency (CK_Accounts_CurrencyMode)
    /// </summary>
    public byte CurrencyMode { get; set; }

    /// <summary>
    /// Maps to <see cref="PetraERP.Domain.GeneralLedger.FxMonetaryClassification"/> value.
    /// 1=MonetaryEligible, 2=NonMonetary, 3=NotApplicable, 4=RequiresReview (CK_Accounts_FxMonetaryEligibility)
    /// </summary>
    public byte FxMonetaryEligibility { get; set; } = 1;

    /// <summary>Non-null exactly when CurrencyMode=3 (FixedCurrency).</summary>
    public string? FixedCurrencyCode { get; set; }

    public Guid? ParentId { get; set; }
    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
