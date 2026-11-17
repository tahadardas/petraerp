using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Purchases;

/// <summary>
/// Al-Ameen 10 Independent Invoice Information panel & window metrics for Purchases:
/// Supplier balance & credit utilization before/after, operational attribution,
/// average item cost, and direct drill-down commands.
/// </summary>
public sealed partial class PurchasesViewModel
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

    // --- Supplier Financial & Credit Health (رصيد المورد وسقف الائتمان) ---
    public string SupplierBalanceBeforeDisplay
    {
        get
        {
            if (SelectedSupplier is null) return "—";
            var current = ParseDecimal(SelectedSupplier.CurrentBalance);
            if (Current?.Status == PurchaseInvoiceStatus.Posted && DraftPaymentMethod == PaymentMethod.Credit)
            {
                var invoiceNet = ParseDecimal(GrandTotalDisplay);
                return (current - invoiceNet).ToString("N2", CultureInfo.InvariantCulture);
            }
            return current.ToString("N2", CultureInfo.InvariantCulture);
        }
    }

    public string SupplierBalanceAfterDisplay
    {
        get
        {
            if (SelectedSupplier is null) return "—";
            var current = ParseDecimal(SelectedSupplier.CurrentBalance);
            if (Current?.Status != PurchaseInvoiceStatus.Posted && DraftPaymentMethod == PaymentMethod.Credit)
            {
                var invoiceNet = ParseDecimal(GrandTotalDisplay);
                return (current + invoiceNet).ToString("N2", CultureInfo.InvariantCulture);
            }
            return current.ToString("N2", CultureInfo.InvariantCulture);
        }
    }

    public decimal SupplierCreditUtilizationPercent
    {
        get
        {
            if (SelectedSupplier is null) return 0m;
            var limit = ParseDecimal(SelectedSupplier.CreditLimit);
            if (limit <= 0m) return 0m;
            var after = ParseDecimal(SupplierBalanceAfterDisplay);
            if (after <= 0m) return 0m;
            return Math.Min(100m, Math.Round((after / limit) * 100m, 1, MidpointRounding.AwayFromZero));
        }
    }

    public string SupplierCreditUtilizationDisplay =>
        HasSupplierCreditLimit ? $"{SupplierCreditUtilizationPercent:0.0}%" : "—";

    public bool IsSupplierCreditExceeded
    {
        get
        {
            if (SelectedSupplier is null || !HasSupplierCreditLimit) return false;
            var limit = ParseDecimal(SelectedSupplier.CreditLimit);
            var after = ParseDecimal(SupplierBalanceAfterDisplay);
            return after > limit;
        }
    }

    public string SupplierCreditExceededAmountDisplay
    {
        get
        {
            if (!IsSupplierCreditExceeded) return string.Empty;
            var limit = ParseDecimal(SelectedSupplier!.CreditLimit);
            var after = ParseDecimal(SupplierBalanceAfterDisplay);
            return (after - limit).ToString("N2", CultureInfo.InvariantCulture);
        }
    }

    // --- Operational Attribution (التنسيب والترحيل) ---
    public string DraftPatternNameDisplay =>
        PurchasePatterns.FirstOrDefault(p => p.RevisionId == DraftPatternRevisionId)?.Name ?? Current?.PatternName ?? "نمط افتراضي";

    public string DraftWarehouseNameDisplay =>
        Warehouses.FirstOrDefault(w => w.Id == DraftWarehouseId)?.NameArabic ?? Current?.WarehouseCode ?? "—";

    public string DraftCostCenterNameDisplay =>
        CostCenters.FirstOrDefault(c => c.Id == DraftCostCenterId)?.NameArabic ?? "بدون مركز كلفة";

    public string DraftFinancialAccountDisplay =>
        DraftPaymentMethod == PaymentMethod.Cash
            ? "الصندوق الرئيسي"
            : (SelectedSupplier is not null ? $"ذمم — {SelectedSupplier.NameArabic}" : "ذمم الموردين");

    public string AverageLineAmountDisplay
    {
        get
        {
            var filled = Lines.Count(l => !l.IsPristineBlank);
            if (filled == 0) return "0.00";
            var grand = ParseDecimal(GrandTotalDisplay);
            return (grand / filled).ToString("N2", CultureInfo.InvariantCulture);
        }
    }

    private static decimal ParseDecimal(string? text) =>
        decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;

    public void NotifyInvoiceInfoProperties()
    {
        OnPropertyChanged(nameof(SupplierBalanceBeforeDisplay));
        OnPropertyChanged(nameof(SupplierBalanceAfterDisplay));
        OnPropertyChanged(nameof(SupplierCreditUtilizationPercent));
        OnPropertyChanged(nameof(SupplierCreditUtilizationDisplay));
        OnPropertyChanged(nameof(IsSupplierCreditExceeded));
        OnPropertyChanged(nameof(SupplierCreditExceededAmountDisplay));

        OnPropertyChanged(nameof(DraftPatternNameDisplay));
        OnPropertyChanged(nameof(DraftWarehouseNameDisplay));
        OnPropertyChanged(nameof(DraftCostCenterNameDisplay));
        OnPropertyChanged(nameof(DraftFinancialAccountDisplay));
        OnPropertyChanged(nameof(AverageLineAmountDisplay));
    }
}
