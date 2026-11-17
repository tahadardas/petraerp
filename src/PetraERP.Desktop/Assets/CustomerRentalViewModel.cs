using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Assets;

/// <summary>
/// Workstation for customer rented equipment, equipment on loan, jobsite custody, and return inspection.
/// </summary>
public sealed partial class CustomerRentalViewModel : PageViewModel
{
    private readonly IAssetsClient client;
    private readonly NotificationCenter notifications;

    [ObservableProperty] private string voucherNumber = "RENT-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
    [ObservableProperty] private AssetDto? selectedAsset;
    [ObservableProperty] private string customerName = string.Empty;
    [ObservableProperty] private string projectOrSite = string.Empty;
    [ObservableProperty] private DateTime dispatchDate = DateTime.Today;
    [ObservableProperty] private DateTime expectedReturnDate = DateTime.Today.AddDays(30);
    [ObservableProperty] private DateTime? actualReturnDate;
    [ObservableProperty] private decimal rentalRate = 3500m;
    [ObservableProperty] private decimal securityDeposit = 10000m;
    [ObservableProperty] private AssetConditionRating conditionOnDispatch = AssetConditionRating.Excellent;
    [ObservableProperty] private AssetConditionRating conditionOnReturn = AssetConditionRating.Good;
    [ObservableProperty] private decimal damageRepairCost;
    [ObservableProperty] private decimal totalBilledAmount;
    [ObservableProperty] private CustomerRentalStatus status = CustomerRentalStatus.ActiveOnLoan;
    [ObservableProperty] private string notes = "عقد إيجار تشغيلي للمعدات مع تأمين مسترد";

    public ObservableCollection<AssetDto> RentableAssets { get; } = [];
    public ObservableCollection<AssetCustomerRentalDto> RentalHistory { get; } = [];

    public CustomerRentalViewModel(IAssetsClient client, NotificationCenter notifications, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.notifications = notifications;

        SaveDispatchCommand = new AsyncRelayCommand(SaveDispatchAsync, () => SelectedAsset != null && !string.IsNullOrWhiteSpace(CustomerName));
        CalculateReturnBillingCommand = new RelayCommand(CalculateReturnBilling);
        ClearCommand = new RelayCommand(ClearForm);
    }

    protected override string RequiredPermission => "Sales.Invoices.View";

    public IAsyncRelayCommand SaveDispatchCommand { get; }
    public IRelayCommand CalculateReturnBillingCommand { get; }
    public IRelayCommand ClearCommand { get; }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        var assets = await client.ListAssetsAsync(cancellationToken);
        RentableAssets.Clear();
        foreach (var a in assets.Where(a => a.AssetType != AssetType.Fixed))
        {
            RentableAssets.Add(a);
        }

        SelectedAsset = RentableAssets.FirstOrDefault();

        var history = await client.ListCustomerRentalsAsync(cancellationToken);
        RentalHistory.Clear();
        foreach (var r in history) RentalHistory.Add(r);
    }

    partial void OnSelectedAssetChanged(AssetDto? value) => SaveDispatchCommand.NotifyCanExecuteChanged();
    partial void OnCustomerNameChanged(string value) => SaveDispatchCommand.NotifyCanExecuteChanged();

    public void CalculateReturnBilling()
    {
        ActualReturnDate = DateTime.Today;
        var days = Math.Max(1, (int)(ActualReturnDate.Value - DispatchDate).TotalDays);
        // If monthly rate: days * (rate / 30)
        var baseRent = Math.Round(days * (RentalRate / 30m), 2);
        TotalBilledAmount = baseRent + DamageRepairCost;

        Status = DamageRepairCost > 0 ? CustomerRentalStatus.ReturnedWithDamages : CustomerRentalStatus.ReturnedInGoodOrder;
    }

    private async Task SaveDispatchAsync()
    {
        if (SelectedAsset == null || string.IsNullOrWhiteSpace(CustomerName)) return;

        var rental = new AssetCustomerRentalDto
        {
            VoucherNumber = VoucherNumber,
            AssetId = SelectedAsset.Id,
            AssetCode = SelectedAsset.AssetCode,
            AssetName = SelectedAsset.NameArabic,
            SerialNumber = SelectedAsset.SerialNumber,
            CustomerId = Guid.NewGuid(),
            CustomerName = CustomerName,
            CustomerProjectOrSite = ProjectOrSite,
            DispatchDate = DispatchDate,
            ExpectedReturnDate = ExpectedReturnDate,
            ActualReturnDate = ActualReturnDate,
            DailyOrMonthlyRentalRate = RentalRate,
            SecurityDeposit = SecurityDeposit,
            ConditionOnDispatch = ConditionOnDispatch,
            ConditionOnReturn = ActualReturnDate.HasValue ? ConditionOnReturn : null,
            DamageRepairCost = DamageRepairCost,
            TotalBilledAmount = TotalBilledAmount,
            Status = Status,
            Notes = Notes
        };

        await client.SaveCustomerRentalAsync(rental);
        RentalHistory.Insert(0, rental);

        notifications.Text(NotificationKind.Success, "تم حفظ العقد", $"تم تسجيل حركة الأصل للزبون {CustomerName} برقم {VoucherNumber}");
        ClearForm();
    }

    protected override void ClearPage() => ClearForm();

    private void ClearForm()
    {
        VoucherNumber = "RENT-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
        CustomerName = string.Empty;
        ProjectOrSite = string.Empty;
        ActualReturnDate = null;
        DamageRepairCost = 0m;
        TotalBilledAmount = 0m;
        Status = CustomerRentalStatus.ActiveOnLoan;
    }
}
