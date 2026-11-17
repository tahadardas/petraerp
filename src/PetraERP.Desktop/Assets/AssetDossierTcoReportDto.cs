namespace PetraERP.Desktop.Assets;

/// <summary>
/// Summary line for category breakdown in the asset dossier report.
/// </summary>
public sealed record AssetDossierExpenseSummaryLineDto
{
    public string CategoryName { get; init; } = string.Empty;
    public int TransactionCount { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal PercentageOfOperatingCost { get; init; }
}

/// <summary>
/// Comprehensive Asset Dossier and Total Cost of Ownership (TCO) Executive Report DTO.
/// </summary>
public sealed record AssetDossierTcoReportDto
{
    public Guid AssetId { get; init; }
    public string AssetCode { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public string PlateOrSerial { get; init; } = string.Empty;
    public string CurrentCustodianOrDriver { get; init; } = string.Empty;
    public DateTime PurchaseDate { get; init; }
    public decimal AcquisitionCost { get; init; }
    public decimal CapitalizedImprovements { get; init; }
    public decimal TotalCapitalCost => AcquisitionCost + CapitalizedImprovements;
    public decimal AccumulatedDepreciation { get; init; }
    public decimal CurrentNetBookValue => Math.Max(0m, TotalCapitalCost - AccumulatedDepreciation);

    // Operating & Maintenance Cost Breakdown
    public decimal TotalOilAndLubricantsCost { get; init; }
    public decimal TotalPreventiveServiceCost { get; init; }
    public decimal TotalEmergencyRepairsCost { get; init; }
    public decimal TotalSparePartsConsumedCost { get; init; }
    public decimal TotalFuelCost { get; init; }
    public decimal TotalInsuranceAndLicenseCost { get; init; }
    public decimal TotalOtherExpensesCost { get; init; }

    public decimal TotalOperatingCost =>
        TotalOilAndLubricantsCost + TotalPreventiveServiceCost + TotalEmergencyRepairsCost +
        TotalSparePartsConsumedCost + TotalFuelCost + TotalInsuranceAndLicenseCost + TotalOtherExpensesCost;

    public decimal GrandTotalCostOfOwnership => TotalCapitalCost + TotalOperatingCost;

    // Performance & Metrics
    public MeterType MeterType { get; init; } = MeterType.Kilometers;
    public decimal TotalMeterRecorded { get; init; }
    public decimal OperatingCostPerUnit =>
        TotalMeterRecorded > 0 ? Math.Round(TotalOperatingCost / TotalMeterRecorded, 2) : 0m;
    public decimal GrandTcoPerUnit =>
        TotalMeterRecorded > 0 ? Math.Round(GrandTotalCostOfOwnership / TotalMeterRecorded, 2) : 0m;

    public int TotalDowntimeDays { get; init; }
    public decimal NextScheduledServiceMeter { get; init; }
    public decimal MeterRemainingToService => Math.Max(0m, NextScheduledServiceMeter - TotalMeterRecorded);

    public IReadOnlyList<AssetDossierExpenseSummaryLineDto> ExpenseCategories { get; init; } = [];
}
