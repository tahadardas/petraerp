using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Assets;

/// <summary>
/// Custody management workstation: Handover, Transfer, Return to warehouse, and Universal Certificate generator.
/// </summary>
public sealed partial class AssetCustodyViewModel : PageViewModel
{
    private readonly IAssetsClient client;
    private readonly NotificationCenter notifications;

    [ObservableProperty] private string voucherNumber = "CUST-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
    [ObservableProperty] private DateTime transactionDate = DateTime.Today;
    [ObservableProperty] private CustodyHandoverType handoverType = CustodyHandoverType.NewAssignment;

    // Asset selection
    [ObservableProperty] private AssetDto? selectedAsset;
    [ObservableProperty] private string serialNumber = string.Empty;

    // Custodian Details
    [ObservableProperty] private string custodianName = string.Empty;
    [ObservableProperty] private string employeeCode = string.Empty;
    [ObservableProperty] private string departmentOrSite = string.Empty;
    [ObservableProperty] private string phoneNumber = string.Empty;
    [ObservableProperty] private string? previousCustodian;

    // Technical & Condition
    [ObservableProperty] private AssetConditionRating condition = AssetConditionRating.BrandNew;
    [ObservableProperty] private string accessoriesChecklist = "حقيبة حمل، شاحن كهربائي، كابل بيانات، ملحقات أساسية";
    [ObservableProperty] private string technicalNotes = "تم فحص الأصل بحضور المستلم والتأكد من سلامته وجاهزيته للعمل";

    // Legal Declaration
    [ObservableProperty] private string legalDeclarationText =
        "أقر أنا الموقع أدناه بأنني قد استلمت الأصل الموضح بياناته وملحقاته أعلاه بحالة سليمة وصالحة للعمل، " +
        "وأتعهد بالمحافظة عليه واستخدامه في أغراض العمل المخصصة فقط، وأتحمل المسؤولية الكاملة عن أي تلف ناتج عن سوء الاستخدام أو الإهمال، " +
        "مع التزامي برده فور طلب الإدارة أو عند انتهاء علاقتي بالعمل.";

    // Signatures
    [ObservableProperty] private string custodianSignature = string.Empty;
    [ObservableProperty] private string warehouseKeeperSignature = "عمر سليم (أمين العهد)";
    [ObservableProperty] private string departmentManagerSignature = "د. ياسر ناصر (المدير المباشر)";

    [ObservableProperty] private string generatedCertificateText = string.Empty;
    [ObservableProperty] private bool isCertificateVisible;

    public ObservableCollection<AssetDto> MovableAssets { get; } = [];
    public ObservableCollection<AssetCustodyVoucherDto> VouchersHistory { get; } = [];

    public AssetCustodyViewModel(IAssetsClient client, NotificationCenter notifications, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.notifications = notifications;

        SaveVoucherCommand = new AsyncRelayCommand(SaveVoucherAsync, () => SelectedAsset != null && !string.IsNullOrWhiteSpace(CustodianName));
        GenerateCertificateCommand = new RelayCommand(GenerateCertificate);
        ClearCommand = new RelayCommand(ClearForm);
    }

    protected override string RequiredPermission => "Ledger.Accounts.View";

    public IAsyncRelayCommand SaveVoucherCommand { get; }
    public IRelayCommand GenerateCertificateCommand { get; }
    public IRelayCommand ClearCommand { get; }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        var assets = await client.ListAssetsAsync(cancellationToken);
        MovableAssets.Clear();
        foreach (var a in assets.Where(a => a.AssetType != AssetType.Fixed))
        {
            MovableAssets.Add(a);
        }

        SelectedAsset = MovableAssets.FirstOrDefault();

        var history = await client.ListCustodyVouchersAsync(cancellationToken);
        VouchersHistory.Clear();
        foreach (var v in history) VouchersHistory.Add(v);
    }

    partial void OnSelectedAssetChanged(AssetDto? value)
    {
        if (value != null)
        {
            SerialNumber = value.SerialNumber;
            PreviousCustodian = value.CurrentCustodian;
        }
        SaveVoucherCommand.NotifyCanExecuteChanged();
    }

    partial void OnCustodianNameChanged(string value)
    {
        CustodianSignature = value;
        SaveVoucherCommand.NotifyCanExecuteChanged();
    }

    public void GenerateCertificate()
    {
        if (SelectedAsset == null) return;

        var typeStr = HandoverType switch
        {
            CustodyHandoverType.NewAssignment => "تسليم عهدة جديد",
            CustodyHandoverType.CustodyTransfer => "نقل عهدة من موظف لآخر",
            CustodyHandoverType.ReturnToWarehouse => "استرداد عهدة للمستودع (براءة ذمة)",
            _ => "فحص ومطابقة جرد"
        };

        var condStr = Condition switch
        {
            AssetConditionRating.BrandNew => "جديد تماماً (Brand New)",
            AssetConditionRating.Excellent => "ممتاز (Excellent)",
            AssetConditionRating.Good => "جيد (Good)",
            AssetConditionRating.NeedsMaintenance => "بحاجة صيانة (Needs Maintenance)",
            _ => "تالف / خردة"
        };

        GeneratedCertificateText = $"""
        ================================================================================
                                 شركة [اسم المؤسسة / الشركة]
                                  محضر تسليم واستلام عهدة
                           Custody Handover & Receipt Certificate
        ================================================================================
        رقم السند: {VoucherNumber,-20} التاريخ: {TransactionDate:yyyy/MM/dd}
        نوع الحركة: {typeStr,-20} القسم/الموقع: {DepartmentOrSite}
        --------------------------------------------------------------------------------
        1. بيانات المستلم:
        اسم المستلم: {CustodianName,-22} الرقم الوظيفي: {EmployeeCode}
        رقم الهاتف:  {PhoneNumber,-22} العهدة السابقة: {PreviousCustodian ?? "لا يوجد (جديد)"}
        --------------------------------------------------------------------------------
        2. تفاصيل الأصل المسلّم:
        رمز الأصل:   {SelectedAsset.AssetCode,-22} اسم الأصل: {SelectedAsset.NameArabic}
        الرقم التسلسلي: {SerialNumber,-20} الباركود:   {SelectedAsset.BarcodeOrQr}
        الحالة الفنية عند التسليم: {condStr}
        --------------------------------------------------------------------------------
        3. قائمة الملحقات والتجهيزات المسلّمة:
        {AccessoriesChecklist}
        ملاحظات فنية: {TechnicalNotes}
        --------------------------------------------------------------------------------
        4. الإقرار والتعهد القانوني:
        "{LegalDeclarationText}"
        --------------------------------------------------------------------------------
        5. التوقيعات والاعتمادات:
           توقيع المستلم                   أمين العهدة                    مدير الإدارة
           {CustodianSignature,-30} {WarehouseKeeperSignature,-30} {DepartmentManagerSignature}
        ================================================================================
        """;

        IsCertificateVisible = true;
    }

    private async Task SaveVoucherAsync()
    {
        if (SelectedAsset == null || string.IsNullOrWhiteSpace(CustodianName)) return;

        var voucher = new AssetCustodyVoucherDto
        {
            VoucherNumber = VoucherNumber,
            TransactionDate = TransactionDate,
            HandoverType = HandoverType,
            AssetId = SelectedAsset.Id,
            AssetCode = SelectedAsset.AssetCode,
            AssetName = SelectedAsset.NameArabic,
            SerialNumber = SerialNumber,
            PreviousCustodian = PreviousCustodian,
            CustodianName = CustodianName,
            EmployeeCode = EmployeeCode,
            DepartmentOrSite = DepartmentOrSite,
            PhoneNumber = PhoneNumber,
            Condition = Condition,
            AccessoriesChecklist = AccessoriesChecklist,
            TechnicalNotes = TechnicalNotes,
            LegalDeclarationText = LegalDeclarationText,
            CustodianSignatureName = CustodianSignature,
            WarehouseKeeperSignatureName = WarehouseKeeperSignature,
            DepartmentManagerSignatureName = DepartmentManagerSignature
        };

        await client.SaveCustodyVoucherAsync(voucher);
        VouchersHistory.Insert(0, voucher);
        GenerateCertificate();

        notifications.Text(NotificationKind.Success, "تم حفظ العهدة", $"تم إصدار محضر العهدة برقم {VoucherNumber} للموظف {CustodianName}");
    }

    protected override void ClearPage() => ClearForm();

    private void ClearForm()
    {
        VoucherNumber = "CUST-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
        TransactionDate = DateTime.Today;
        CustodianName = string.Empty;
        EmployeeCode = string.Empty;
        PhoneNumber = string.Empty;
        IsCertificateVisible = false;
        GeneratedCertificateText = string.Empty;
    }
}
