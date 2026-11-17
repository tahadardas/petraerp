using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Contracts.Assets;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Assets;

/// <summary>
/// Editable UI row item for spare parts issued to a work order.
/// </summary>
public sealed partial class WorkOrderPartItem : ObservableObject
{
    public Guid LineId { get; init; } = Guid.NewGuid();
    [ObservableProperty] private string itemCode = string.Empty;
    [ObservableProperty] private string itemName = string.Empty;
    [ObservableProperty] private string warehouseName = string.Empty;
    [ObservableProperty] private decimal quantity = 1m;
    [ObservableProperty] private decimal unitCost;
    [ObservableProperty] private decimal unitPrice;
    [ObservableProperty] private bool isCoveredByWarranty;
    [ObservableProperty] private string notes = string.Empty;

    public decimal TotalCost => Quantity * UnitCost;
    public decimal CustomerTotal => IsCoveredByWarranty ? 0m : Quantity * UnitPrice;
    public decimal CompanyWarrantyCost => IsCoveredByWarranty ? TotalCost : 0m;

    partial void OnQuantityChanged(decimal value) => OnPropertyChanged(nameof(CustomerTotal));
    partial void OnUnitPriceChanged(decimal value) => OnPropertyChanged(nameof(CustomerTotal));
    partial void OnIsCoveredByWarrantyChanged(bool value) => OnPropertyChanged(nameof(CustomerTotal));
}

/// <summary>
/// Editable UI row item for labor / technical service charges on a work order.
/// </summary>
public sealed partial class WorkOrderLaborItem : ObservableObject
{
    public Guid LineId { get; init; } = Guid.NewGuid();
    [ObservableProperty] private string serviceCode = string.Empty;
    [ObservableProperty] private string serviceName = string.Empty;
    [ObservableProperty] private string technicianName = string.Empty;
    [ObservableProperty] private decimal laborHours = 1m;
    [ObservableProperty] private decimal hourlyRate = 50m;
    [ObservableProperty] private decimal laborAmount = 50m;
    [ObservableProperty] private decimal technicianCommissionRate = 15m;
    [ObservableProperty] private bool isCoveredByWarranty;
    [ObservableProperty] private string notes = string.Empty;

    public decimal CustomerCharge => IsCoveredByWarranty ? 0m : LaborAmount;
    public decimal TechnicianCommissionAmount => Math.Round(LaborAmount * (TechnicianCommissionRate / 100m), 2);

    partial void OnLaborHoursChanged(decimal value)
    {
        LaborAmount = value * HourlyRate;
        OnPropertyChanged(nameof(CustomerCharge));
        OnPropertyChanged(nameof(TechnicianCommissionAmount));
    }

    partial void OnHourlyRateChanged(decimal value)
    {
        LaborAmount = LaborHours * value;
        OnPropertyChanged(nameof(CustomerCharge));
        OnPropertyChanged(nameof(TechnicianCommissionAmount));
    }

    partial void OnLaborAmountChanged(decimal value)
    {
        OnPropertyChanged(nameof(CustomerCharge));
        OnPropertyChanged(nameof(TechnicianCommissionAmount));
    }

    partial void OnTechnicianCommissionRateChanged(decimal value) => OnPropertyChanged(nameof(TechnicianCommissionAmount));
    partial void OnIsCoveredByWarrantyChanged(bool value) => OnPropertyChanged(nameof(CustomerCharge));
}

/// <summary>
/// Comprehensive Customer Maintenance Center & Workshop Management ViewModel.
/// Implements full Al-Ameen 10 parity and beyond: Ticket intake, diagnosis, spare parts, labor,
/// technician commissions, warranty claims, and one-click final invoice generation.
/// </summary>
public sealed partial class MaintenanceWorkOrderViewModel : PageViewModel
{
    private readonly IAssetsClient client;
    private readonly NotificationCenter notifications;

    // Filter and search
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private WorkOrderStatus? statusFilter;

    // Active Work Order Header
    [ObservableProperty] private Guid activeOrderId = Guid.NewGuid();
    [ObservableProperty] private string orderNumber = "WO-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
    [ObservableProperty] private DateTime orderDate = DateTime.Today;
    [ObservableProperty] private DateTime expectedDeliveryDate = DateTime.Today.AddDays(2);
    [ObservableProperty] private DateTime? actualDeliveryDate;
    [ObservableProperty] private WorkOrderStatus status = WorkOrderStatus.Received;
    [ObservableProperty] private MaintenancePriority priority = MaintenancePriority.Normal;
    [ObservableProperty] private WarrantyStatus warranty = WarrantyStatus.OutOfWarranty;

    // Customer Information
    [ObservableProperty] private string customerName = string.Empty;
    [ObservableProperty] private string customerPhone = string.Empty;
    [ObservableProperty] private string customerAddress = string.Empty;

    // Equipment / Vehicle Information
    [ObservableProperty] private string equipmentType = "شاحنة / آلية ثقيلة";
    [ObservableProperty] private string brand = string.Empty;
    [ObservableProperty] private string model = string.Empty;
    [ObservableProperty] private string serialNumberOrChassis = string.Empty;
    [ObservableProperty] private string plateOrDeviceNumber = string.Empty;
    [ObservableProperty] private decimal meterReading;
    [ObservableProperty] private string receivedAccessories = "المفاتيح الأصلية";
    [ObservableProperty] private string visualCondition = "هيكل خارجي بحالة جيدة";

    // Issue & Technical Diagnosis
    [ObservableProperty] private string customerReportedIssue = string.Empty;
    [ObservableProperty] private string technicalDiagnosis = string.Empty;
    [ObservableProperty] private string assignedTechnician = string.Empty;
    [ObservableProperty] private string supervisorNotes = string.Empty;

    // Financial & Invoicing Summary
    [ObservableProperty] private decimal estimatedCost = 1500m;
    [ObservableProperty] private decimal partsCustomerTotal;
    [ObservableProperty] private decimal laborCustomerTotal;
    [ObservableProperty] private decimal discountAmount;
    [ObservableProperty] private decimal netCustomerTotal;
    [ObservableProperty] private decimal companyWarrantyCost;
    [ObservableProperty] private decimal totalTechnicianCommissions;
    [ObservableProperty] private string? invoiceNumber;
    [ObservableProperty] private bool isDelivered;
    [ObservableProperty] private string? deliveryReceiptNumber;

    // Lookups & Part Selection
    [ObservableProperty] private (string ItemCode, string ItemName, decimal Cost, decimal SellPrice, string Warehouse)? selectedSparePart;
    [ObservableProperty] private decimal newPartQuantity = 1m;
    [ObservableProperty] private bool newPartWarranty;

    // Master List & Tables
    public ObservableCollection<MaintenanceWorkOrderDto> AllOrders { get; } = [];
    public ObservableCollection<MaintenanceWorkOrderDto> FilteredOrders { get; } = [];
    public ObservableCollection<WorkOrderPartItem> PartsList { get; } = [];
    public ObservableCollection<WorkOrderLaborItem> LaborList { get; } = [];
    public ObservableCollection<string> AvailableTechnicians { get; } = [];
    public ObservableCollection<(string ItemCode, string ItemName, decimal Cost, decimal SellPrice, string Warehouse)> AvailableSpareParts { get; } = [];

    // Selected order from list
    [ObservableProperty] private MaintenanceWorkOrderDto? selectedOrderInList;

    public MaintenanceWorkOrderViewModel(IAssetsClient client, NotificationCenter notifications, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.notifications = notifications;

        NewOrderCommand = new RelayCommand(NewOrder);
        SaveOrderCommand = new AsyncRelayCommand(SaveOrderAsync, () => !string.IsNullOrWhiteSpace(CustomerName) && !string.IsNullOrWhiteSpace(EquipmentType));
        AddPartCommand = new RelayCommand(AddSparePart, () => SelectedSparePart != null && NewPartQuantity > 0);
        RemovePartCommand = new RelayCommand<WorkOrderPartItem>(RemoveSparePart);
        AddLaborCommand = new RelayCommand(AddLaborLine);
        RemoveLaborCommand = new RelayCommand<WorkOrderLaborItem>(RemoveLaborLine);
        AdvanceStatusCommand = new AsyncRelayCommand<WorkOrderStatus>(AdvanceStatusAsync);
        DeliverAndInvoiceCommand = new AsyncRelayCommand(DeliverAndInvoiceAsync, () => !IsDelivered);
        PrintTicketCommand = new RelayCommand(PrintReceiptTicket);
    }

    protected override string RequiredPermission => "Sales.Invoices.View";

    public IRelayCommand NewOrderCommand { get; }
    public IAsyncRelayCommand SaveOrderCommand { get; }
    public IRelayCommand AddPartCommand { get; }
    public IRelayCommand<WorkOrderPartItem> RemovePartCommand { get; }
    public IRelayCommand AddLaborCommand { get; }
    public IRelayCommand<WorkOrderLaborItem> RemoveLaborCommand { get; }
    public IAsyncRelayCommand<WorkOrderStatus> AdvanceStatusCommand { get; }
    public IAsyncRelayCommand DeliverAndInvoiceCommand { get; }
    public IRelayCommand PrintTicketCommand { get; }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        // 1. Technicians
        var techs = await client.ListTechniciansAsync(cancellationToken);
        AvailableTechnicians.Clear();
        foreach (var t in techs) AvailableTechnicians.Add(t);
        if (string.IsNullOrEmpty(AssignedTechnician))
        {
            AssignedTechnician = AvailableTechnicians.FirstOrDefault() ?? string.Empty;
        }

        // 2. Spare Parts
        var parts = await client.ListAvailableSparePartsAsync(cancellationToken);
        AvailableSpareParts.Clear();
        foreach (var p in parts) AvailableSpareParts.Add(p);
        SelectedSparePart = AvailableSpareParts.FirstOrDefault();

        // 3. Work Orders
        var orders = await client.ListWorkOrdersAsync(cancellationToken);
        AllOrders.Clear();
        foreach (var o in orders) AllOrders.Add(o);

        ApplyFilter();

        if (SelectedOrderInList == null && FilteredOrders.Count > 0)
        {
            SelectedOrderInList = FilteredOrders.First();
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(WorkOrderStatus? value) => ApplyFilter();

    partial void OnCustomerNameChanged(string value) => SaveOrderCommand.NotifyCanExecuteChanged();
    partial void OnEquipmentTypeChanged(string value) => SaveOrderCommand.NotifyCanExecuteChanged();
    partial void OnSelectedSparePartChanged((string ItemCode, string ItemName, decimal Cost, decimal SellPrice, string Warehouse)? value) => AddPartCommand.NotifyCanExecuteChanged();
    partial void OnNewPartQuantityChanged(decimal value) => AddPartCommand.NotifyCanExecuteChanged();
    partial void OnDiscountAmountChanged(decimal value) => RecalculateTotals();
    partial void OnWarrantyChanged(WarrantyStatus value)
    {
        if (value == WarrantyStatus.UnderWarranty)
        {
            foreach (var p in PartsList) p.IsCoveredByWarranty = true;
            foreach (var l in LaborList) l.IsCoveredByWarranty = true;
        }
        RecalculateTotals();
    }

    partial void OnSelectedOrderInListChanged(MaintenanceWorkOrderDto? value)
    {
        if (value != null)
        {
            LoadOrderDetails(value);
        }
    }

    private void ApplyFilter()
    {
        FilteredOrders.Clear();
        var q = AllOrders.AsEnumerable();

        if (StatusFilter.HasValue)
        {
            q = q.Where(o => o.Status == StatusFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            q = q.Where(o =>
                o.OrderNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                o.CustomerName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                o.Brand.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                o.SerialNumberOrChassis.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                o.PlateOrDeviceNumber.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in q)
        {
            FilteredOrders.Add(item);
        }
    }

    private void LoadOrderDetails(MaintenanceWorkOrderDto dto)
    {
        ActiveOrderId = dto.Id;
        OrderNumber = dto.OrderNumber;
        OrderDate = dto.OrderDate;
        ExpectedDeliveryDate = dto.ExpectedDeliveryDate ?? DateTime.Today.AddDays(2);
        ActualDeliveryDate = dto.ActualDeliveryDate;
        Status = dto.Status;
        Priority = dto.Priority;
        Warranty = dto.Warranty;

        CustomerName = dto.CustomerName;
        CustomerPhone = dto.CustomerPhone;
        CustomerAddress = dto.CustomerAddress;

        EquipmentType = dto.EquipmentType;
        Brand = dto.Brand;
        Model = dto.Model;
        SerialNumberOrChassis = dto.SerialNumberOrChassis;
        PlateOrDeviceNumber = dto.PlateOrDeviceNumber;
        MeterReading = dto.MeterReading;
        ReceivedAccessories = dto.ReceivedAccessories;
        VisualCondition = dto.VisualCondition;

        CustomerReportedIssue = dto.CustomerReportedIssue;
        TechnicalDiagnosis = dto.TechnicalDiagnosis;
        AssignedTechnician = dto.AssignedTechnician;
        SupervisorNotes = dto.SupervisorNotes;

        EstimatedCost = dto.EstimatedCost;
        DiscountAmount = dto.DiscountAmount;
        InvoiceNumber = dto.InvoiceNumber;
        IsDelivered = dto.IsDelivered;
        DeliveryReceiptNumber = dto.DeliveryReceiptNumber;

        PartsList.Clear();
        foreach (var p in dto.PartsLines)
        {
            var row = new WorkOrderPartItem
            {
                LineId = p.LineId,
                ItemCode = p.ItemCode,
                ItemName = p.ItemName,
                WarehouseName = p.WarehouseName,
                Quantity = p.Quantity,
                UnitCost = p.UnitCost,
                UnitPrice = p.UnitPrice,
                IsCoveredByWarranty = p.IsCoveredByWarranty,
                Notes = p.Notes
            };
            row.PropertyChanged += (s, e) => RecalculateTotals();
            PartsList.Add(row);
        }

        LaborList.Clear();
        foreach (var l in dto.LaborLines)
        {
            var row = new WorkOrderLaborItem
            {
                LineId = l.LineId,
                ServiceCode = l.ServiceCode,
                ServiceName = l.ServiceName,
                TechnicianName = l.TechnicianName,
                LaborHours = l.LaborHours,
                HourlyRate = l.HourlyRate,
                LaborAmount = l.LaborAmount,
                TechnicianCommissionRate = l.TechnicianCommissionRate,
                IsCoveredByWarranty = l.IsCoveredByWarranty,
                Notes = l.Notes
            };
            row.PropertyChanged += (s, e) => RecalculateTotals();
            LaborList.Add(row);
        }

        RecalculateTotals();
        DeliverAndInvoiceCommand.NotifyCanExecuteChanged();
    }

    public void RecalculateTotals()
    {
        PartsCustomerTotal = PartsList.Sum(p => p.CustomerTotal);
        LaborCustomerTotal = LaborList.Sum(l => l.CustomerCharge);
        CompanyWarrantyCost = PartsList.Sum(p => p.CompanyWarrantyCost) + LaborList.Where(l => l.IsCoveredByWarranty).Sum(l => l.LaborAmount);
        TotalTechnicianCommissions = LaborList.Sum(l => l.TechnicianCommissionAmount);

        var gross = PartsCustomerTotal + LaborCustomerTotal;
        NetCustomerTotal = Math.Max(0m, gross - DiscountAmount);
    }

    public void NewOrder()
    {
        ActiveOrderId = Guid.NewGuid();
        OrderNumber = "WO-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
        OrderDate = DateTime.Today;
        ExpectedDeliveryDate = DateTime.Today.AddDays(2);
        ActualDeliveryDate = null;
        Status = WorkOrderStatus.Received;
        Priority = MaintenancePriority.Normal;
        Warranty = WarrantyStatus.OutOfWarranty;

        CustomerName = string.Empty;
        CustomerPhone = string.Empty;
        CustomerAddress = string.Empty;

        EquipmentType = "شاحنة / مركبة تجارية";
        Brand = string.Empty;
        Model = string.Empty;
        SerialNumberOrChassis = string.Empty;
        PlateOrDeviceNumber = string.Empty;
        MeterReading = 0m;
        ReceivedAccessories = "المفاتيح الأصلية";
        VisualCondition = "سليم دون خدوش";

        CustomerReportedIssue = string.Empty;
        TechnicalDiagnosis = string.Empty;
        AssignedTechnician = AvailableTechnicians.FirstOrDefault() ?? string.Empty;
        SupervisorNotes = string.Empty;

        EstimatedCost = 1000m;
        DiscountAmount = 0m;
        InvoiceNumber = null;
        IsDelivered = false;
        DeliveryReceiptNumber = null;

        PartsList.Clear();
        LaborList.Clear();
        RecalculateTotals();
        DeliverAndInvoiceCommand.NotifyCanExecuteChanged();

        notifications.Publish(new NotificationMessage(NotificationSeverity.Info, "أمر صيانة جديد", "تم فتح بطاقة أمر صيانة فارغة للاستلام"));
    }

    private void AddSparePart()
    {
        if (SelectedSparePart == null || NewPartQuantity <= 0) return;

        var part = SelectedSparePart.Value;
        var isCovered = Warranty == WarrantyStatus.UnderWarranty || NewPartWarranty;

        var line = new WorkOrderPartItem
        {
            ItemCode = part.ItemCode,
            ItemName = part.ItemName,
            WarehouseName = part.Warehouse,
            Quantity = NewPartQuantity,
            UnitCost = part.Cost,
            UnitPrice = part.SellPrice,
            IsCoveredByWarranty = isCovered,
            Notes = isCovered ? "مشمول بالكفالة" : "صرف مستودعي مأجور"
        };
        line.PropertyChanged += (s, e) => RecalculateTotals();
        PartsList.Add(line);
        RecalculateTotals();

        NewPartQuantity = 1m;
        NewPartWarranty = false;
    }

    private void RemoveSparePart(WorkOrderPartItem? item)
    {
        if (item == null) return;
        PartsList.Remove(item);
        RecalculateTotals();
    }

    private void AddLaborLine()
    {
        var isCovered = Warranty == WarrantyStatus.UnderWarranty;
        var line = new WorkOrderLaborItem
        {
            ServiceCode = $"SRV-LBR-{LaborList.Count + 1:D2}",
            ServiceName = "صيانة وفك وتركيب ومعايرة فنية",
            TechnicianName = AssignedTechnician,
            LaborHours = 2m,
            HourlyRate = 50m,
            LaborAmount = 100m,
            TechnicianCommissionRate = 20m,
            IsCoveredByWarranty = isCovered,
            Notes = "أجور يد الورشة"
        };
        line.PropertyChanged += (s, e) => RecalculateTotals();
        LaborList.Add(line);
        RecalculateTotals();
    }

    private void RemoveLaborLine(WorkOrderLaborItem? item)
    {
        if (item == null) return;
        LaborList.Remove(item);
        RecalculateTotals();
    }

    private async Task AdvanceStatusAsync(WorkOrderStatus newStatus)
    {
        Status = newStatus;
        if (newStatus == WorkOrderStatus.DeliveredAndInvoiced)
        {
            await DeliverAndInvoiceAsync();
            return;
        }

        await SaveOrderAsync();
        notifications.Publish(new NotificationMessage(
            NotificationSeverity.Success,
            "تحديث حالة أمر الصيانة",
            $"تم تحديث حالة الأمر {OrderNumber} إلى ({GetStatusArabic(newStatus)})"));
    }

    private async Task DeliverAndInvoiceAsync()
    {
        if (IsDelivered) return;

        var result = await client.DeliverAndInvoiceWorkOrderAsync(ActiveOrderId, DiscountAmount);
        InvoiceNumber = result.InvoiceNumber;
        IsDelivered = true;
        Status = WorkOrderStatus.DeliveredAndInvoiced;
        ActualDeliveryDate = DateTime.Today;
        DeliveryReceiptNumber = $"DEL-{DateTime.Now:HHmmss}";

        // Update in AllOrders
        var idx = AllOrders.IndexOf(AllOrders.FirstOrDefault(o => o.Id == ActiveOrderId)!);
        if (idx >= 0)
        {
            AllOrders[idx] = AllOrders[idx] with
            {
                Status = WorkOrderStatus.DeliveredAndInvoiced,
                IsDelivered = true,
                InvoiceNumber = InvoiceNumber,
                ActualDeliveryDate = ActualDeliveryDate,
                DeliveryReceiptNumber = DeliveryReceiptNumber,
                DiscountAmount = DiscountAmount
            };
            ApplyFilter();
        }

        DeliverAndInvoiceCommand.NotifyCanExecuteChanged();

        notifications.Publish(new NotificationMessage(
            NotificationSeverity.Success,
            "تم التسليم والفوترة بنجاح",
            $"تم تسليم الجهاز/الآلية وإصدار فاتورة الصيانة برقم [{InvoiceNumber}] بمبلغ صافي {NetCustomerTotal:N0} ل.س"));
    }

    private async Task SaveOrderAsync()
    {
        var dto = new MaintenanceWorkOrderDto
        {
            Id = ActiveOrderId,
            OrderNumber = OrderNumber,
            OrderDate = OrderDate,
            ExpectedDeliveryDate = ExpectedDeliveryDate,
            ActualDeliveryDate = ActualDeliveryDate,
            CustomerName = CustomerName,
            CustomerPhone = CustomerPhone,
            CustomerAddress = CustomerAddress,
            EquipmentType = EquipmentType,
            Brand = Brand,
            Model = Model,
            SerialNumberOrChassis = SerialNumberOrChassis,
            PlateOrDeviceNumber = PlateOrDeviceNumber,
            MeterReading = MeterReading,
            ReceivedAccessories = ReceivedAccessories,
            VisualCondition = VisualCondition,
            CustomerReportedIssue = CustomerReportedIssue,
            TechnicalDiagnosis = TechnicalDiagnosis,
            Status = Status,
            Priority = Priority,
            Warranty = Warranty,
            AssignedTechnician = AssignedTechnician,
            SupervisorNotes = SupervisorNotes,
            EstimatedCost = EstimatedCost,
            DiscountAmount = DiscountAmount,
            InvoiceNumber = InvoiceNumber,
            IsDelivered = IsDelivered,
            DeliveryReceiptNumber = DeliveryReceiptNumber,
            PartsLines = PartsList.Select(p => new MaintenanceWorkOrderPartLineDto
            {
                LineId = p.LineId,
                ItemCode = p.ItemCode,
                ItemName = p.ItemName,
                WarehouseName = p.WarehouseName,
                Quantity = p.Quantity,
                UnitCost = p.UnitCost,
                UnitPrice = p.UnitPrice,
                IsCoveredByWarranty = p.IsCoveredByWarranty,
                Notes = p.Notes
            }).ToList(),
            LaborLines = LaborList.Select(l => new MaintenanceWorkOrderLaborLineDto
            {
                LineId = l.LineId,
                ServiceCode = l.ServiceCode,
                ServiceName = l.ServiceName,
                TechnicianName = l.TechnicianName,
                LaborHours = l.LaborHours,
                HourlyRate = l.HourlyRate,
                LaborAmount = l.LaborAmount,
                TechnicianCommissionRate = l.TechnicianCommissionRate,
                IsCoveredByWarranty = l.IsCoveredByWarranty,
                Notes = l.Notes
            }).ToList()
        };

        var saved = await client.SaveWorkOrderAsync(dto);

        // Update lists
        var existing = AllOrders.FirstOrDefault(o => o.Id == saved.Id);
        if (existing != null)
        {
            var idx = AllOrders.IndexOf(existing);
            AllOrders[idx] = saved;
        }
        else
        {
            AllOrders.Insert(0, saved);
        }
        ApplyFilter();
        SelectedOrderInList = saved;

        notifications.Publish(new NotificationMessage(
            NotificationSeverity.Success,
            "حفظ أمر الصيانة",
            $"تم حفظ أمر الصيانة [{saved.OrderNumber}] للعميل ({saved.CustomerName}) بنجاح"));
    }

    private void PrintReceiptTicket()
    {
        var msg = $"[بطاقة استلام وصيانة جهاز]\n" +
                  $"رقم الأمر: {OrderNumber}\n" +
                  $"العميل: {CustomerName} ({CustomerPhone})\n" +
                  $"المعدة: {Brand} {Model} (سيريال: {SerialNumberOrChassis})\n" +
                  $"الملحقات: {ReceivedAccessories}\n" +
                  $"العطل المذكور: {CustomerReportedIssue}\n" +
                  $"الكفالة: {GetWarrantyArabic(Warranty)}\n" +
                  $"التكلفة المقدرة: {EstimatedCost:N0} ل.س - التسليم المتوقع: {ExpectedDeliveryDate:yyyy/MM/dd}";

        notifications.Publish(new NotificationMessage(NotificationSeverity.Info, "طباعة بطاقة الاستلام", msg));
    }

    public static string GetStatusArabic(WorkOrderStatus s) => s switch
    {
        WorkOrderStatus.Received => "مستلم",
        WorkOrderStatus.UnderInspection => "قيد الفحص والتشخيص",
        WorkOrderStatus.AwaitingApproval => "بانتظار موافقة العميل",
        WorkOrderStatus.AwaitingParts => "بانتظار قطع الغيار",
        WorkOrderStatus.InRepair => "قيد الإصلاح الفني",
        WorkOrderStatus.ReadyForDelivery => "تم الإصلاح (جاهز للتسليم)",
        WorkOrderStatus.DeliveredAndInvoiced => "تم التسليم وتمت الفوترة",
        WorkOrderStatus.CancelledOrRejected => "ملغى / تعذر الإصلاح",
        _ => s.ToString()
    };

    public static string GetWarrantyArabic(WarrantyStatus w) => w switch
    {
        WarrantyStatus.UnderWarranty => "ضمن الكفالة (مجاني بالكامل)",
        WarrantyStatus.OutOfWarranty => "خارج الكفالة (مأجور)",
        WarrantyStatus.PartsOnlyWarranty => "كفالة قطع فقط",
        WarrantyStatus.GoodwillCourtesy => "صيانة مجانية استثنائية",
        _ => w.ToString()
    };
}
