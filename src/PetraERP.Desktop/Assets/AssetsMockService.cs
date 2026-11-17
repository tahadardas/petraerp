using PetraERP.Contracts.Assets;
using PetraERP.Domain.Assets;

namespace PetraERP.Desktop.Assets;

/// <summary>
/// In-memory implementation of <see cref="IAssetsClient"/> providing rich realistic fleet,
/// machinery, custody, maintenance, and fuel distribution data.
/// </summary>
public sealed class AssetsMockService : IAssetsClient
{
    private readonly List<AssetDto> assets = [];
    private readonly List<AssetCustodyVoucherDto> custodyVouchers = [];
    private readonly List<AssetCustomerRentalDto> customerRentals = [];
    private readonly List<AssetMaintenanceExpenseBatchDto> maintenanceBatches = [];
    private readonly List<AssetFuelDispensingVoucherDto> fuelVouchers = [];

    // Fuel Warehouses (Tanks)
    private readonly List<(Guid Id, string Name, decimal MovingAverageCost, decimal CurrentStockLiters)> fuelWarehouses =
    [
        (Guid.Parse("11111111-1111-1111-1111-111111111111"), "خزان المازوت الرئيسي - الورشة المركزية", 11200m, 32500m),
        (Guid.Parse("22222222-2222-2222-2222-222222222222"), "خزان ديزل موقع الكسارة والمقلع", 11450m, 14200m),
        (Guid.Parse("33333333-3333-3333-3333-333333333333"), "محطة وقود المستودع العام (بنزين)", 14500m, 8600m),
    ];

    // Petty Cash & Payment Accounts
    private readonly List<(Guid Id, string Code, string Name)> paymentAccounts =
    [
        (Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "110201", "عهدة مسؤول الورشة (أبو خالد)"),
        (Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "110202", "عهدة مشرف الصيانة الميدانية (م. سعيد)"),
        (Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "110203", "عهدة مسؤول الحركة والنقليات (أبو فهد)"),
        (Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), "110101", "الصندوق الرئيسي للإدارة العامة"),
        (Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), "110301", "البنك التجاري - حساب جاري رئيسي"),
    ];

    public AssetsMockService()
    {
        InitializeSampleData();
    }

    private void InitializeSampleData()
    {
        var komatsuWheelLoader = new AssetDto
        {
            Id = Guid.Parse("00000001-0000-0000-0000-000000000001"),
            AssetCode = "FA-MC-0014",
            NameArabic = "تركس كوماتسو WA470 عجلات",
            NameEnglish = "Komatsu WA470 Wheel Loader",
            CategoryCode = "1201",
            CategoryName = "آليات ومعدات ثقيلة",
            AssetType = AssetType.MachineryVehicle,
            Status = AssetStatus.InService,
            BarcodeOrQr = "QR-KOM-WA470-014",
            SerialNumber = "SN-KOM-984210",
            LocationOrBranch = "ورشة الكسارة المركزية",
            CurrentCustodian = "عثمان إبراهيم (مشغّل)",
            PurchaseDate = new DateTime(2023, 3, 15),
            AcquisitionCost = 380000m,
            SalvageValue = 38000m,
            UsefulLifeMonths = 120,
            DepreciationMethod = AssetDepreciationMethod.UnitsOfProduction,
            AccumulatedDepreciation = 98750m,
            MachineryDetails = new MachineryVehicleDetailsDto
            {
                PlateNumber = "معدات 8492",
                ChassisNumberVin = "KM470W-889102X",
                EngineNumber = "ENG-KOM-7712",
                Manufacturer = "Komatsu",
                ModelName = "WA470-8",
                ModelYear = 2023,
                MeterType = MeterType.OperatingHours,
                CurrentMeterReading = 3420m,
                FuelType = FuelType.Diesel,
                StandardFuelConsumptionRate = 18m, // 18 لتر / ساعة عمل
                OilChangeIntervalMeter = 250m,
                LastOilChangeMeter = 3250m,
                CurrentDriverOrOperator = "عثمان إبراهيم",
                AssignedProjectOrSite = "مشروع المقلع الشمالي",
                InsuranceExpiryDate = DateTime.Today.AddDays(45),
                LicenseExpiryDate = DateTime.Today.AddDays(120),
                InspectionExpiryDate = DateTime.Today.AddDays(15),
            }
        };

        var mercedesTruck = new AssetDto
        {
            Id = Guid.Parse("00000002-0000-0000-0000-000000000002"),
            AssetCode = "FA-TR-0088",
            NameArabic = "شاحنة مرسيدس أكتروس قلاب",
            NameEnglish = "Mercedes Actros Tipper Truck",
            CategoryCode = "1202",
            CategoryName = "سيارات وشاحنات ونقليات",
            AssetType = AssetType.MachineryVehicle,
            Status = AssetStatus.InService,
            BarcodeOrQr = "QR-MB-ACT-088",
            SerialNumber = "SN-WDB-963001",
            LocationOrBranch = "أسطول النقليات العام",
            CurrentCustodian = "محمد حسن (سائق)",
            PurchaseDate = new DateTime(2022, 6, 1),
            AcquisitionCost = 290000m,
            SalvageValue = 30000m,
            UsefulLifeMonths = 96,
            DepreciationMethod = AssetDepreciationMethod.StraightLine,
            AccumulatedDepreciation = 86660m,
            MachineryDetails = new MachineryVehicleDetailsDto
            {
                PlateNumber = "4591-نقل",
                ChassisNumberVin = "WDB9634031L789123",
                EngineNumber = "OM501LA-88124",
                Manufacturer = "Mercedes-Benz",
                ModelName = "Actros 3340",
                ModelYear = 2022,
                MeterType = MeterType.Kilometers,
                CurrentMeterReading = 142500m,
                FuelType = FuelType.Diesel,
                StandardFuelConsumptionRate = 32m, // 32 لتر / 100 كم
                OilChangeIntervalMeter = 10000m,
                LastOilChangeMeter = 138000m,
                CurrentDriverOrOperator = "محمد حسن",
                AssignedProjectOrSite = "خط توريد وتوزيع المواد",
                InsuranceExpiryDate = DateTime.Today.AddDays(80),
                LicenseExpiryDate = DateTime.Today.AddDays(30),
            }
        };

        var catExcavator = new AssetDto
        {
            Id = Guid.Parse("00000003-0000-0000-0000-000000000003"),
            AssetCode = "FA-EX-0021",
            NameArabic = "حفارة كاتر بيلار CAT 320 هيدروليك",
            NameEnglish = "Caterpillar 320 Hydraulic Excavator",
            CategoryCode = "1201",
            CategoryName = "آليات ومعدات ثقيلة",
            AssetType = AssetType.MachineryVehicle,
            Status = AssetStatus.InService,
            BarcodeOrQr = "QR-CAT-320-021",
            SerialNumber = "SN-CAT-320DL-991",
            LocationOrBranch = "موقع الإنشاءات 3",
            CurrentCustodian = "راشد القحطاني (مشغّل)",
            PurchaseDate = new DateTime(2021, 10, 10),
            AcquisitionCost = 450000m,
            SalvageValue = 45000m,
            UsefulLifeMonths = 120,
            DepreciationMethod = AssetDepreciationMethod.UnitsOfProduction,
            AccumulatedDepreciation = 182000m,
            MachineryDetails = new MachineryVehicleDetailsDto
            {
                PlateNumber = "معدات 3310",
                ChassisNumberVin = "CAT0320DLBRC11029",
                EngineNumber = "C6.4ACERT-9018",
                Manufacturer = "Caterpillar",
                ModelName = "320D",
                ModelYear = 2021,
                MeterType = MeterType.OperatingHours,
                CurrentMeterReading = 8110m,
                FuelType = FuelType.Diesel,
                StandardFuelConsumptionRate = 22m, // 22 لتر / ساعة عمل
                OilChangeIntervalMeter = 250m,
                LastOilChangeMeter = 8000m,
                CurrentDriverOrOperator = "راشد القحطاني",
                AssignedProjectOrSite = "مشروع برج العاصمة",
                InsuranceExpiryDate = DateTime.Today.AddDays(150),
                LicenseExpiryDate = DateTime.Today.AddDays(200),
            }
        };

        var generatorPerkins = new AssetDto
        {
            Id = Guid.Parse("00000004-0000-0000-0000-000000000004"),
            AssetCode = "FA-GN-0005",
            NameArabic = "مولدة كهربائية بيركنز 500 KVA",
            NameEnglish = "Perkins 500 KVA Diesel Generator",
            CategoryCode = "1201",
            CategoryName = "آليات ومعدات ثقيلة",
            AssetType = AssetType.MachineryVehicle,
            Status = AssetStatus.InService,
            BarcodeOrQr = "QR-PRK-500-005",
            SerialNumber = "SN-PRK-500KVA-77",
            LocationOrBranch = "محطة التوليد الاحتياطية بالمصنع",
            CurrentCustodian = "سمير فني الكهرباء",
            PurchaseDate = new DateTime(2023, 1, 1),
            AcquisitionCost = 140000m,
            SalvageValue = 15000m,
            UsefulLifeMonths = 96,
            DepreciationMethod = AssetDepreciationMethod.UnitsOfProduction,
            AccumulatedDepreciation = 38000m,
            MachineryDetails = new MachineryVehicleDetailsDto
            {
                PlateNumber = "GEN-05",
                ChassisNumberVin = "PRK500-STATIONARY",
                EngineNumber = "ENG-PRK-2506A",
                Manufacturer = "Perkins",
                ModelName = "500KVA Soundproof",
                ModelYear = 2023,
                MeterType = MeterType.OperatingHours,
                CurrentMeterReading = 4120m,
                FuelType = FuelType.Diesel,
                StandardFuelConsumptionRate = 45m, // 45 لتر / ساعة عند الحمل الكامل
                OilChangeIntervalMeter = 200m,
                LastOilChangeMeter = 4050m,
                CurrentDriverOrOperator = "سمير خليل",
                AssignedProjectOrSite = "المصنع الرئيسي",
            }
        };

        var laptopDell = new AssetDto
        {
            Id = Guid.Parse("00000005-0000-0000-0000-000000000005"),
            AssetCode = "FA-IT-0089",
            NameArabic = "محطة عمل لابتوب Dell Precision 7780",
            NameEnglish = "Dell Precision 7780 Workstation",
            CategoryCode = "1203",
            CategoryName = "أجهزة وتقنية معلومات",
            AssetType = AssetType.Movable,
            Status = AssetStatus.InCustodyEmployee,
            BarcodeOrQr = "QR-DELL-PR7780-89",
            SerialNumber = "SN-892140-LK",
            LocationOrBranch = "قسم تكنولوجيا المعلومات والمشاريع",
            CurrentCustodian = "م. خالد عبد الرحمن",
            PurchaseDate = new DateTime(2025, 2, 10),
            AcquisitionCost = 18500m,
            SalvageValue = 1500m,
            UsefulLifeMonths = 36,
            DepreciationMethod = AssetDepreciationMethod.StraightLine,
            AccumulatedDepreciation = 4250m,
        };

        var surveyStation = new AssetDto
        {
            Id = Guid.Parse("00000006-0000-0000-0000-000000000006"),
            AssetCode = "FA-SV-0012",
            NameArabic = "محطة مساحة ليزرية توتال ستيشن Leica TS16",
            NameEnglish = "Leica TS16 Total Station",
            CategoryCode = "1201",
            CategoryName = "آليات ومعدات ثقيلة",
            AssetType = AssetType.Movable,
            Status = AssetStatus.InCustodyCustomer,
            BarcodeOrQr = "QR-LEICA-TS16-012",
            SerialNumber = "SN-776102-LC",
            LocationOrBranch = "موقع العميل: شركة النور للمقاولات",
            CurrentCustodian = "شركة النور للمقاولات (تأجير أمانة)",
            PurchaseDate = new DateTime(2024, 5, 20),
            AcquisitionCost = 45000m,
            SalvageValue = 5000m,
            UsefulLifeMonths = 60,
            DepreciationMethod = AssetDepreciationMethod.StraightLine,
            AccumulatedDepreciation = 11330m,
        };

        assets.AddRange([komatsuWheelLoader, mercedesTruck, catExcavator, generatorPerkins, laptopDell, surveyStation]);

        // Custody record sample
        custodyVouchers.Add(new AssetCustodyVoucherDto
        {
            VoucherNumber = "CUST-2026-001",
            TransactionDate = DateTime.Today.AddDays(-10),
            HandoverType = CustodyHandoverType.NewAssignment,
            AssetId = laptopDell.Id,
            AssetCode = laptopDell.AssetCode,
            AssetName = laptopDell.NameArabic,
            SerialNumber = laptopDell.SerialNumber,
            CustodianName = "م. خالد عبد الرحمن",
            EmployeeCode = "EMP-1082",
            DepartmentOrSite = "إدارة المشاريع الإنشائية",
            PhoneNumber = "0501234567",
            Condition = AssetConditionRating.BrandNew,
            AccessoriesChecklist = "حقيبة ظهر أصلية، شاحن 240W، ماوس بلوتوث، كابل HDMI",
            TechnicalNotes = "تم تسليم الجهاز مع كافة البرامج الهندسية المعتمدة والتأكد من سلامته",
            LegalDeclarationText = "أقر أنا المستلم بأنني استلمت الأصل بحالة ممتازة وأتعهد بالمحافظة عليه وإعادته عند الطلب...",
            CustodianSignatureName = "خالد عبد الرحمن",
            WarehouseKeeperSignatureName = "عمر سليم",
            DepartmentManagerSignatureName = "د. ياسر ناصر"
        });

        // Customer rental sample
        customerRentals.Add(new AssetCustomerRentalDto
        {
            VoucherNumber = "RENT-2026-004",
            AssetId = surveyStation.Id,
            AssetCode = surveyStation.AssetCode,
            AssetName = surveyStation.NameArabic,
            SerialNumber = surveyStation.SerialNumber,
            CustomerId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            CustomerName = "شركة النور للمقاولات الهندسية",
            CustomerProjectOrSite = "مشروع جسر الشام الحديث",
            DispatchDate = DateTime.Today.AddDays(-30),
            ExpectedReturnDate = DateTime.Today.AddDays(30),
            DailyOrMonthlyRentalRate = 3500m,
            SecurityDeposit = 15000m,
            ConditionOnDispatch = AssetConditionRating.Excellent,
            Status = CustomerRentalStatus.ActiveOnLoan,
            Notes = "عقد إيجار تشغيلي شهري شامل التأمين ضد التلف مع فحص معايرة مسبق"
        });
    }

    public Task<IReadOnlyList<AssetDto>> ListAssetsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AssetDto>>(assets.AsReadOnly());

    public Task<AssetDto?> GetAssetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(assets.FirstOrDefault(a => a.Id == id));

    public Task<IReadOnlyList<AssetCustodyVoucherDto>> ListCustodyVouchersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AssetCustodyVoucherDto>>(custodyVouchers.AsReadOnly());

    public Task<AssetCustodyVoucherDto> SaveCustodyVoucherAsync(AssetCustodyVoucherDto voucher, CancellationToken cancellationToken = default)
    {
        custodyVouchers.Add(voucher);
        // Update asset custody state
        var asset = assets.FirstOrDefault(a => a.Id == voucher.AssetId);
        if (asset != null)
        {
            var idx = assets.IndexOf(asset);
            assets[idx] = asset with
            {
                Status = voucher.HandoverType == CustodyHandoverType.ReturnToWarehouse ? AssetStatus.InService : AssetStatus.InCustodyEmployee,
                CurrentCustodian = voucher.HandoverType == CustodyHandoverType.ReturnToWarehouse ? null : voucher.CustodianName
            };
        }
        return Task.FromResult(voucher);
    }

    public Task<IReadOnlyList<AssetCustomerRentalDto>> ListCustomerRentalsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AssetCustomerRentalDto>>(customerRentals.AsReadOnly());

    public Task<AssetCustomerRentalDto> SaveCustomerRentalAsync(AssetCustomerRentalDto rental, CancellationToken cancellationToken = default)
    {
        customerRentals.Add(rental);
        var asset = assets.FirstOrDefault(a => a.Id == rental.AssetId);
        if (asset != null)
        {
            var idx = assets.IndexOf(asset);
            assets[idx] = asset with
            {
                Status = rental.Status == CustomerRentalStatus.ActiveOnLoan ? AssetStatus.InCustodyCustomer : AssetStatus.InService,
                CurrentCustodian = rental.Status == CustomerRentalStatus.ActiveOnLoan ? rental.CustomerName : null
            };
        }
        return Task.FromResult(rental);
    }

    public Task<AssetMaintenanceExpenseBatchDto> SaveMaintenanceBatchAsync(AssetMaintenanceExpenseBatchDto batch, CancellationToken cancellationToken = default)
    {
        maintenanceBatches.Add(batch);
        // Update meters and oil change for any machine in the lines
        foreach (var line in batch.Lines)
        {
            var asset = assets.FirstOrDefault(a => a.Id == line.AssetId);
            if (asset?.MachineryDetails != null && line.CurrentMeterReading > asset.MachineryDetails.CurrentMeterReading)
            {
                var idx = assets.IndexOf(asset);
                var lastOil = line.ExpenseType == MaintenanceExpenseType.OilAndFilterChange ? line.CurrentMeterReading : asset.MachineryDetails.LastOilChangeMeter;
                assets[idx] = asset with
                {
                    MachineryDetails = asset.MachineryDetails with
                    {
                        CurrentMeterReading = line.CurrentMeterReading,
                        LastOilChangeMeter = lastOil
                    }
                };
            }
        }
        return Task.FromResult(batch);
    }

    public Task<IReadOnlyList<AssetFuelDispensingVoucherDto>> ListFuelVouchersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AssetFuelDispensingVoucherDto>>(fuelVouchers.AsReadOnly());

    public Task<AssetFuelDispensingVoucherDto> SaveFuelDispensingVoucherAsync(AssetFuelDispensingVoucherDto voucher, CancellationToken cancellationToken = default)
    {
        fuelVouchers.Add(voucher);
        // Update meters on assets
        foreach (var line in voucher.Lines)
        {
            var asset = assets.FirstOrDefault(a => a.Id == line.AssetId);
            if (asset?.MachineryDetails != null && line.CurrentMeterReading > asset.MachineryDetails.CurrentMeterReading)
            {
                var idx = assets.IndexOf(asset);
                assets[idx] = asset with
                {
                    MachineryDetails = asset.MachineryDetails with
                    {
                        CurrentMeterReading = line.CurrentMeterReading
                    }
                };
            }
        }
        return Task.FromResult(voucher);
    }

    public Task<AssetDossierTcoReportDto> GetAssetDossierTcoReportAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = assets.FirstOrDefault(a => a.Id == assetId) ?? assets.First();
        var isTruck = asset.AssetCode.Contains("TR");
        var isLoader = asset.AssetCode.Contains("MC");

        var oil = isLoader ? 18900m : (isTruck ? 14200m : 5500m);
        var parts = isLoader ? 24400m : (isTruck ? 18600m : 6800m);
        var repairs = isLoader ? 11200m : (isTruck ? 9500m : 4200m);
        var fuel = isLoader ? 68500m : (isTruck ? 92000m : 22000m);
        var insurance = isLoader ? 9000m : (isTruck ? 12000m : 3500m);
        var other = 3000m;

        var totalOp = oil + parts + repairs + fuel + insurance + other;
        var meter = asset.MachineryDetails?.CurrentMeterReading ?? 3420m;
        var meterType = asset.MachineryDetails?.MeterType ?? MeterType.OperatingHours;

        var report = new AssetDossierTcoReportDto
        {
            AssetId = asset.Id,
            AssetCode = asset.AssetCode,
            AssetName = asset.NameArabic,
            CategoryName = asset.CategoryName,
            PlateOrSerial = asset.MachineryDetails?.PlateNumber ?? asset.SerialNumber,
            CurrentCustodianOrDriver = asset.MachineryDetails?.CurrentDriverOrOperator ?? asset.CurrentCustodian ?? "غير مسند",
            PurchaseDate = asset.PurchaseDate,
            AcquisitionCost = asset.AcquisitionCost,
            CapitalizedImprovements = 15000m,
            AccumulatedDepreciation = asset.AccumulatedDepreciation,
            TotalOilAndLubricantsCost = oil,
            TotalPreventiveServiceCost = oil * 0.4m,
            TotalEmergencyRepairsCost = repairs,
            TotalSparePartsConsumedCost = parts,
            TotalFuelCost = fuel,
            TotalInsuranceAndLicenseCost = insurance,
            TotalOtherExpensesCost = other,
            MeterType = meterType,
            TotalMeterRecorded = meter,
            TotalDowntimeDays = 6,
            NextScheduledServiceMeter = asset.MachineryDetails?.NextOilChangeMeter ?? (meter + 80m),
            ExpenseCategories =
            [
                new() { CategoryName = "تغيير زيوت وفلاتر وتشحيم", TransactionCount = 14, TotalAmount = oil, PercentageOfOperatingCost = Math.Round(oil / totalOp * 100m, 1) },
                new() { CategoryName = "قطع غيار مستهلكة من المستودع", TransactionCount = 8, TotalAmount = parts, PercentageOfOperatingCost = Math.Round(parts / totalOp * 100m, 1) },
                new() { CategoryName = "إصلاحات وورش خارجية (أعطال)", TransactionCount = 3, TotalAmount = repairs, PercentageOfOperatingCost = Math.Round(repairs / totalOp * 100m, 1) },
                new() { CategoryName = "وقود ومحروقات (مازوت مسجل)", TransactionCount = 42, TotalAmount = fuel, PercentageOfOperatingCost = Math.Round(fuel / totalOp * 100m, 1) },
                new() { CategoryName = "رسوم وتأمين وفحص فني", TransactionCount = 2, TotalAmount = insurance, PercentageOfOperatingCost = Math.Round(insurance / totalOp * 100m, 1) },
                new() { CategoryName = "مصاريف تشغيلية وغسيل أخرى", TransactionCount = 10, TotalAmount = other, PercentageOfOperatingCost = Math.Round(other / totalOp * 100m, 1) },
            ]
        };

        return Task.FromResult(report);
    }

    public Task<AssetDepreciationRunDto> CalculateDepreciationRunAsync(int fiscalYear, int periodNumber, CancellationToken cancellationToken = default)
    {
        var lines = new List<AssetDepreciationRunLineDto>();
        foreach (var asset in assets.Where(a => a.Status != AssetStatus.Suspended && a.Status != AssetStatus.Disposed))
        {
            decimal depAmount = 0m;
            if (asset.DepreciationMethod == AssetDepreciationMethod.StraightLine)
            {
                depAmount = AssetCalculationEngine.CalculateStraightLine(asset.AcquisitionCost, asset.SalvageValue, asset.UsefulLifeMonths, 1);
            }
            else if (asset.DepreciationMethod == AssetDepreciationMethod.UnitsOfProduction)
            {
                // Typical 150 hours or 1000 KM per month
                var units = asset.MachineryDetails?.MeterType == MeterType.OperatingHours ? 120m : 1500m;
                var totalExpected = asset.MachineryDetails?.MeterType == MeterType.OperatingHours ? 10000m : 300000m;
                depAmount = AssetCalculationEngine.CalculateUnitsOfProduction(asset.AcquisitionCost, asset.SalvageValue, totalExpected, units);
            }
            else if (asset.DepreciationMethod == AssetDepreciationMethod.DecliningBalance)
            {
                depAmount = AssetCalculationEngine.CalculateDecliningBalance(asset.CurrentBookValue, asset.SalvageValue, 20m, 1);
            }

            lines.Add(new AssetDepreciationRunLineDto
            {
                AssetId = asset.Id,
                AssetCode = asset.AssetCode,
                AssetName = asset.NameArabic,
                CategoryName = asset.CategoryName,
                BookValueBefore = asset.CurrentBookValue,
                DepreciationAmount = depAmount,
                CostCenterName = asset.LocationOrBranch,
                Notes = $"قسط اهتلاك شهر {periodNumber}/{fiscalYear}"
            });
        }

        var run = new AssetDepreciationRunDto
        {
            VoucherNumber = $"DEP-{fiscalYear}-{periodNumber:D2}",
            RunDate = DateTime.Today,
            FiscalYear = fiscalYear,
            PeriodNumber = periodNumber,
            Description = $"إهلاك الأصول الدوري لشهر {periodNumber} لسنة {fiscalYear}",
            Lines = lines,
            IsPosted = false
        };

        return Task.FromResult(run);
    }

    public Task<bool> PostDepreciationRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        // When posted, updates accumulated depreciation on all assets
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<(Guid Id, string Name, decimal MovingAverageCost, decimal CurrentStockLiters)>> ListFuelWarehousesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<(Guid Id, string Name, decimal MovingAverageCost, decimal CurrentStockLiters)>>(fuelWarehouses.AsReadOnly());

    public Task<IReadOnlyList<(Guid Id, string Code, string Name)>> ListPaymentAccountsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<(Guid Id, string Code, string Name)>>(paymentAccounts.AsReadOnly());
}
