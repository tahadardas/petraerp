namespace PetraERP.Contracts.Assets;

/// <summary>
/// Status of customer rental or loan dispatch.
/// </summary>
public enum CustomerRentalStatus
{
    ActiveOnLoan = 1,       // معار / مؤجر حالياً لدى الزبون
    ReturnedInGoodOrder = 2,// تم الاسترجاع بحالة سليمة
    ReturnedWithDamages = 3,// تم الاسترجاع مع وجود أضرار وفروقات
    Overdue = 4,            // متأخر عن موعد الإرجاع المحدد
}

/// <summary>
/// Record of equipment rented to or held in trust by a customer.
/// </summary>
public sealed record AssetCustomerRentalDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string VoucherNumber { get; init; } = string.Empty;
    public Guid AssetId { get; init; }
    public string AssetCode { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerProjectOrSite { get; init; } = string.Empty;
    public DateTime DispatchDate { get; init; }
    public DateTime ExpectedReturnDate { get; init; }
    public DateTime? ActualReturnDate { get; init; }
    public decimal DailyOrMonthlyRentalRate { get; init; }
    public decimal SecurityDeposit { get; init; }
    public AssetConditionRating ConditionOnDispatch { get; init; } = AssetConditionRating.Excellent;
    public AssetConditionRating? ConditionOnReturn { get; init; }
    public decimal DamageRepairCost { get; init; }
    public decimal TotalBilledAmount { get; init; }
    public CustomerRentalStatus Status { get; init; } = CustomerRentalStatus.ActiveOnLoan;
    public string Notes { get; init; } = string.Empty;
}
