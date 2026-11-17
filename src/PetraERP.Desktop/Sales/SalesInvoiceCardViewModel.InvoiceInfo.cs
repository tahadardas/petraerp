using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Sales;

/// <summary>
/// Al-Ameen 10 Independent Invoice Information panel & window metrics:
/// instant profitability & COGS analysis, customer credit utilization before/after,
/// operational attribution, and direct drill-down commands.
/// </summary>
public sealed partial class SalesInvoiceCardViewModel
{
    private RelayCommand? popOutInvoiceInfoCommand;
    public RelayCommand PopOutInvoiceInfoCommand => popOutInvoiceInfoCommand ??= new(() =>
    {
        var win = new Shell.InvoiceInformationWindow
        {
            DataContext = this,
            Owner = System.Windows.Application.Current?.MainWindow
        };
        win.Show();
    });

    // --- Profitability & COGS (تحليل الربحية وتكلفة المبيعات) ---
    public decimal EstimatedTotalCost =>
        Lines.Where(l => !l.IsPristineBlank).Sum(l => l.EstimatedLineCost);

    public string EstimatedTotalCostDisplay =>
        EstimatedTotalCost > 0m ? EstimatedTotalCost.ToString("N2", CultureInfo.InvariantCulture) : "—";

    public decimal EstimatedGrossProfit
    {
        get
        {
            if (EstimatedTotalCost <= 0m) return 0m;
            var netSales = ParseDecimal(GrandTotalDisplay);
            return netSales - EstimatedTotalCost;
        }
    }

    public string EstimatedProfitDisplay =>
        EstimatedTotalCost > 0m ? EstimatedGrossProfit.ToString("N2", CultureInfo.InvariantCulture) : "—";

    public decimal EstimatedProfitMarginPercent
    {
        get
        {
            var netSales = ParseDecimal(GrandTotalDisplay);
            if (netSales <= 0m || EstimatedTotalCost <= 0m) return 0m;
            return Math.Round((EstimatedGrossProfit / netSales) * 100m, 1, MidpointRounding.AwayFromZero);
        }
    }

    public string EstimatedProfitMarginDisplay =>
        EstimatedTotalCost > 0m ? $"{EstimatedProfitMarginPercent:0.0}%" : "—";

    public bool HasCostData => EstimatedTotalCost > 0m;
    public bool IsProfitable => EstimatedTotalCost <= 0m || EstimatedGrossProfit >= 0m;
    public bool HasBelowCostWarning => Lines.Any(l => !l.IsPristineBlank && l.IsBelowCost);
    public int BelowCostItemsCount => Lines.Count(l => !l.IsPristineBlank && l.IsBelowCost);

    // --- Customer Financial Health & Credit Context (رصيد العميل وسقف الائتمان) ---
    public string CustomerBalanceBeforeDisplay
    {
        get
        {
            if (SelectedCustomer is null) return "—";
            var current = ParseDecimal(SelectedCustomer.CurrentBalance);
            if (Invoice?.Status == SalesInvoiceStatus.Posted && DraftPaymentMethod == PaymentMethod.Credit)
            {
                var invoiceNet = ParseDecimal(GrandTotalDisplay);
                return (current - invoiceNet).ToString("N2", CultureInfo.InvariantCulture);
            }
            return current.ToString("N2", CultureInfo.InvariantCulture);
        }
    }

    public string CustomerBalanceAfterDisplay
    {
        get
        {
            if (SelectedCustomer is null) return "—";
            var current = ParseDecimal(SelectedCustomer.CurrentBalance);
            if (Invoice?.Status != SalesInvoiceStatus.Posted && DraftPaymentMethod == PaymentMethod.Credit)
            {
                var invoiceNet = ParseDecimal(GrandTotalDisplay);
                return (current + invoiceNet).ToString("N2", CultureInfo.InvariantCulture);
            }
            return current.ToString("N2", CultureInfo.InvariantCulture);
        }
    }

    public decimal CustomerCreditUtilizationPercent
    {
        get
        {
            if (SelectedCustomer is null) return 0m;
            var limit = ParseDecimal(SelectedCustomer.CreditLimit);
            if (limit <= 0m) return 0m;
            var after = ParseDecimal(CustomerBalanceAfterDisplay);
            if (after <= 0m) return 0m;
            return Math.Min(100m, Math.Round((after / limit) * 100m, 1, MidpointRounding.AwayFromZero));
        }
    }

    public string CustomerCreditUtilizationDisplay =>
        HasCustomerCreditLimit ? $"{CustomerCreditUtilizationPercent:0.0}%" : "—";

    public bool IsCreditExceeded
    {
        get
        {
            if (SelectedCustomer is null || !HasCustomerCreditLimit) return false;
            var limit = ParseDecimal(SelectedCustomer.CreditLimit);
            var after = ParseDecimal(CustomerBalanceAfterDisplay);
            return after > limit;
        }
    }

    public string CreditExceededAmountDisplay
    {
        get
        {
            if (!IsCreditExceeded) return string.Empty;
            var limit = ParseDecimal(SelectedCustomer!.CreditLimit);
            var after = ParseDecimal(CustomerBalanceAfterDisplay);
            return (after - limit).ToString("N2", CultureInfo.InvariantCulture);
        }
    }

    // --- Operational Attribution (التنسيب والترحيل) ---
    public string DraftPatternNameDisplay =>
        DraftPattern?.Name ?? Invoice?.PatternName ?? "نمط افتراضي";

    public string DraftWarehouseNameDisplay =>
        WarehouseLookups.FirstOrDefault(w => w.Id == DraftWarehouseId)?.NameArabic ?? Invoice?.WarehouseCode ?? "—";

    public string DraftCostCenterNameDisplay =>
        CostCenterLookups.FirstOrDefault(c => c.Id == DraftCostCenterId)?.NameArabic ?? Invoice?.CostCenterCode ?? "بدون مركز كلفة";

    public string DraftFinancialAccountDisplay =>
        DraftPaymentMethod == PaymentMethod.Cash
            ? (CashAccountLookups.FirstOrDefault(a => a.Id == DraftCashAccountId)?.NameArabic ?? "الصندوق الرئيسي")
            : (SelectedCustomer is not null ? $"ذمم — {SelectedCustomer.NameArabic}" : "ذمم العملاء");

    public void NotifyInvoiceInfoProperties()
    {
        OnPropertyChanged(nameof(EstimatedTotalCost));
        OnPropertyChanged(nameof(EstimatedTotalCostDisplay));
        OnPropertyChanged(nameof(EstimatedProfit));
        OnPropertyChanged(nameof(EstimatedProfitDisplay));
        OnPropertyChanged(nameof(EstimatedProfitMarginPercent));
        OnPropertyChanged(nameof(EstimatedProfitMarginDisplay));
        OnPropertyChanged(nameof(HasCostData));
        OnPropertyChanged(nameof(IsProfitable));
        OnPropertyChanged(nameof(HasBelowCostWarning));
        OnPropertyChanged(nameof(BelowCostItemsCount));

        OnPropertyChanged(nameof(CustomerBalanceBeforeDisplay));
        OnPropertyChanged(nameof(CustomerBalanceAfterDisplay));
        OnPropertyChanged(nameof(CustomerCreditUtilizationPercent));
        OnPropertyChanged(nameof(CustomerCreditUtilizationDisplay));
        OnPropertyChanged(nameof(IsCreditExceeded));
        OnPropertyChanged(nameof(CreditExceededAmountDisplay));

        OnPropertyChanged(nameof(DraftPatternNameDisplay));
        OnPropertyChanged(nameof(DraftWarehouseNameDisplay));
        OnPropertyChanged(nameof(DraftCostCenterNameDisplay));
        OnPropertyChanged(nameof(DraftFinancialAccountDisplay));
    }
}
