namespace PetraERP.Desktop.Assets;

/// <summary>
/// Operational workflow status for customer and workshop maintenance work orders.
/// Aligns with standard commercial service center lifecycles.
/// </summary>
public enum WorkOrderStatus
{
    Received = 1,              // مستلم (تم استلام الجهاز/الآلية وفحص الملحقات)
    UnderInspection = 2,       // قيد الفحص والتشخيص الفني
    AwaitingApproval = 3,      // بانتظار موافقة العميل على التكلفة التقديرية
    AwaitingParts = 4,         // بانتظار توفر قطع الغيار المطلوبة
    InRepair = 5,              // قيد الإصلاح الفني في الورشة
    ReadyForDelivery = 6,      // تم الإصلاح بنجاح وجاهز للتسليم
    DeliveredAndInvoiced = 7,  // تم التسليم للعميل وصدرت فاتورة الصيانة
    CancelledOrRejected = 8,   // ملغى أو تعذر الإصلاح لعدم الجدوى الفنية
}

/// <summary>
/// Warranty and coverage policy for the maintenance work order.
/// </summary>
public enum WarrantyStatus
{
    UnderWarranty = 1,        // ضمن الكفالة والضمان (قطع وأجور مجانية على حساب الشركة)
    OutOfWarranty = 2,        // خارج الكفالة (مأجور بالكامل على العميل)
    PartsOnlyWarranty = 3,    // كفالة قطع غيار فقط (أجور اليد مأجورة على العميل)
    GoodwillCourtesy = 4,     // صيانة مجانية استثنائية (بموافقة الإدارة / خدمة ولاء)
}

/// <summary>
/// Operational priority of the repair ticket.
/// </summary>
public enum MaintenancePriority
{
    Normal = 1,               // عادي (حسب الدور المعتاد)
    Urgent = 2,               // مستعجل (أولوية مرتفعة)
    CriticalEmergency = 3,    // طارئ / فوري (تعطل خط إنتاج أو آلية حيوية)
}

/// <summary>
/// One spare part line issued from warehouse to the work order.
/// </summary>
public sealed record MaintenanceWorkOrderPartLineDto
{
    public Guid LineId { get; init; } = Guid.NewGuid();
    public string ItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public string WarehouseName { get; init; } = string.Empty;
    public decimal Quantity { get; init; } = 1m;
    public string UnitOfMeasure { get; init; } = "قطعة";
    public decimal UnitCost { get; init; }
    public decimal UnitPrice { get; init; }
    public bool IsCoveredByWarranty { get; init; }
    public string Notes { get; init; } = string.Empty;

    public decimal TotalCost => Quantity * UnitCost;
    public decimal CustomerTotal => IsCoveredByWarranty ? 0m : Quantity * UnitPrice;
    public decimal CompanyWarrantyCost => IsCoveredByWarranty ? TotalCost : 0m;
}

/// <summary>
/// One labor / service charge line performed by a technician on the work order.
/// </summary>
public sealed record MaintenanceWorkOrderLaborLineDto
{
    public Guid LineId { get; init; } = Guid.NewGuid();
    public string ServiceCode { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string TechnicianName { get; init; } = string.Empty;
    public decimal LaborHours { get; init; } = 1m;
    public decimal HourlyRate { get; init; } = 0m;
    public decimal LaborAmount { get; init; } = 0m;
    public decimal TechnicianCommissionRate { get; init; } = 0m; // %
    public bool IsCoveredByWarranty { get; init; }
    public string Notes { get; init; } = string.Empty;

    public decimal TechnicianCommissionAmount => Math.Round(LaborAmount * (TechnicianCommissionRate / 100m), 2);
    public decimal CustomerCharge => IsCoveredByWarranty ? 0m : LaborAmount;
}

/// <summary>
/// Complete maintenance work order ticket for commercial service centers and workshops.
/// </summary>
public sealed record MaintenanceWorkOrderDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string OrderNumber { get; init; } = string.Empty;
    public DateTime OrderDate { get; init; } = DateTime.UtcNow;
    public DateTime? ExpectedDeliveryDate { get; init; }
    public DateTime? ActualDeliveryDate { get; init; }

    // Customer Information
    public Guid? CustomerId { get; init; }
    public string CustomerCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerPhone { get; init; } = string.Empty;
    public string CustomerAddress { get; init; } = string.Empty;

    // Equipment / Device / Vehicle Information
    public string EquipmentType { get; init; } = string.Empty; // سيارة، شاحنة، مولد، جهاز إلكتروني، آلة صناعية
    public string Brand { get; init; } = string.Empty;         // الصانع / الماركة
    public string Model { get; init; } = string.Empty;         // الموديل / الطراز
    public string SerialNumberOrChassis { get; init; } = string.Empty; // رقم الشاسيه أو السيريال نبر
    public string PlateOrDeviceNumber { get; init; } = string.Empty;   // رقم اللوحة أو المعرف الداخلي
    public decimal MeterReading { get; init; }                 // قراءة العداد (كم أو ساعات تشغيل)
    public string ReceivedAccessories { get; init; } = string.Empty;   // الملحقات المستلمة مع الجهاز
    public string VisualCondition { get; init; } = string.Empty;       // حالة الهيكل الخارجي (خدوش، كسور...)

    // Diagnosis & Workflow
    public string CustomerReportedIssue { get; init; } = string.Empty; // شكوى العميل والعطل المذكور
    public string TechnicalDiagnosis { get; init; } = string.Empty;     // التشخيص الفني الأولي
    public WorkOrderStatus Status { get; init; } = WorkOrderStatus.Received;
    public MaintenancePriority Priority { get; init; } = MaintenancePriority.Normal;
    public WarrantyStatus Warranty { get; init; } = WarrantyStatus.OutOfWarranty;
    public string AssignedTechnician { get; init; } = string.Empty;
    public string SupervisorNotes { get; init; } = string.Empty;

    // Financial & Execution Lines
    public IReadOnlyList<MaintenanceWorkOrderPartLineDto> PartsLines { get; init; } = [];
    public IReadOnlyList<MaintenanceWorkOrderLaborLineDto> LaborLines { get; init; } = [];

    public decimal EstimatedCost { get; init; }
    public decimal DiscountAmount { get; init; }
    public string? InvoiceNumber { get; init; }
    public bool IsDelivered { get; init; }
    public string? DeliveryReceiptNumber { get; init; }
    public string Notes { get; init; } = string.Empty;

    // Calculated Totals
    public decimal PartsTotalCost => PartsLines.Sum(p => p.TotalCost);
    public decimal PartsTotalCustomerCharge => PartsLines.Sum(p => p.CustomerTotal);
    public decimal PartsTotalWarrantyCost => PartsLines.Sum(p => p.CompanyWarrantyCost);

    public decimal LaborTotalCharge => LaborLines.Sum(l => l.CustomerCharge);
    public decimal TotalTechnicianCommissions => LaborLines.Sum(l => l.TechnicianCommissionAmount);

    public decimal GrossCustomerTotal => PartsTotalCustomerCharge + LaborTotalCharge;
    public decimal NetCustomerTotal => Math.Max(0m, GrossCustomerTotal - DiscountAmount);
    public decimal TotalCompanyWarrantyCost => PartsTotalWarrantyCost + LaborLines.Where(l => l.IsCoveredByWarranty).Sum(l => l.LaborAmount);
}
