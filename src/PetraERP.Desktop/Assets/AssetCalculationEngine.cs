namespace PetraERP.Desktop.Assets;

/// <summary>
/// Domain calculation engine for asset depreciation, inventory weighted-average costing, and fleet metrics.
/// Adheres strictly to decimal arithmetic with zero binary floating-point representation.
/// </summary>
public static class AssetCalculationEngine
{
    /// <summary>
    /// Calculates Straight-Line depreciation for a given period in months.
    /// Formula: ((Cost - SalvageValue) / UsefulLifeMonths) * MonthsInPeriod.
    /// </summary>
    public static decimal CalculateStraightLine(decimal acquisitionCost, decimal salvageValue, int usefulLifeMonths, int monthsInPeriod = 1)
    {
        if (usefulLifeMonths <= 0) return 0m;
        if (acquisitionCost <= salvageValue) return 0m;
        if (monthsInPeriod <= 0) return 0m;

        var depreciableBase = acquisitionCost - salvageValue;
        var monthlyRate = depreciableBase / usefulLifeMonths;
        var periodDepreciation = monthlyRate * monthsInPeriod;

        return Math.Round(periodDepreciation, 4);
    }

    /// <summary>
    /// Calculates Declining Balance depreciation for a given period.
    /// Formula: BookValue * (AnnualRate / 12) * MonthsInPeriod, clamped so BookValue does not fall below SalvageValue.
    /// </summary>
    public static decimal CalculateDecliningBalance(decimal currentBookValue, decimal salvageValue, decimal annualRatePercent, int monthsInPeriod = 1)
    {
        if (currentBookValue <= salvageValue) return 0m;
        if (annualRatePercent <= 0m || monthsInPeriod <= 0) return 0m;

        var monthlyFactor = (annualRatePercent / 100m) / 12m;
        var theoreticalDepreciation = currentBookValue * monthlyFactor * monthsInPeriod;

        // Ensure book value never falls below salvage value
        var maxPossibleDepreciation = currentBookValue - salvageValue;
        var periodDepreciation = Math.Min(theoreticalDepreciation, maxPossibleDepreciation);

        return Math.Round(periodDepreciation, 4);
    }

    /// <summary>
    /// Calculates Units of Production (or usage hours / mileage) depreciation.
    /// Formula: ((Cost - SalvageValue) / TotalExpectedUnits) * UnitsConsumed.
    /// </summary>
    public static decimal CalculateUnitsOfProduction(decimal acquisitionCost, decimal salvageValue, decimal totalExpectedUnits, decimal unitsConsumedInPeriod)
    {
        if (totalExpectedUnits <= 0m) return 0m;
        if (acquisitionCost <= salvageValue) return 0m;
        if (unitsConsumedInPeriod <= 0m) return 0m;

        var depreciableBase = acquisitionCost - salvageValue;
        var unitRate = depreciableBase / totalExpectedUnits;
        var periodDepreciation = unitRate * unitsConsumedInPeriod;

        return Math.Round(periodDepreciation, 4);
    }

    /// <summary>
    /// Computes the new Moving Weighted Average Cost when purchasing or adding stock to a warehouse.
    /// Formula: (ExistingValue + PurchaseValue) / (ExistingQty + PurchaseQty).
    /// </summary>
    public static decimal CalculateMovingWeightedAverageCost(decimal existingStockQty, decimal existingAverageCost, decimal newQty, decimal newUnitPrice)
    {
        if (newQty <= 0m) return existingAverageCost;

        var totalQty = existingStockQty + newQty;
        if (totalQty <= 0m) return 0m;

        var existingValue = existingStockQty * existingAverageCost;
        var newValue = newQty * newUnitPrice;
        var totalValue = existingValue + newValue;

        return Math.Round(totalValue / totalQty, 4);
    }

    /// <summary>
    /// Calculates the fuel consumption rate per distance or hour (e.g. Liters per 100 KM or Liters per Operating Hour).
    /// </summary>
    public static decimal CalculateFuelConsumptionRate(decimal litersIssued, decimal currentMeter, decimal previousMeter, bool isHours = false)
    {
        if (litersIssued <= 0m) return 0m;
        var meterDelta = currentMeter - previousMeter;
        if (meterDelta <= 0m) return 0m;

        if (isHours)
        {
            // معدل لتر لكل ساعة تشغيل: Liters / Hours
            return Math.Round(litersIssued / meterDelta, 3);
        }

        // معدل لتر لكل 100 كم: (Liters / KM) * 100
        return Math.Round((litersIssued / meterDelta) * 100m, 3);
    }

    /// <summary>
    /// Detects potential fuel leakage or theft by comparing actual consumption rate against standard specification with tolerance.
    /// </summary>
    public static bool IsFuelConsumptionExcessive(decimal actualRate, decimal standardRate, decimal tolerancePercent = 15m)
    {
        if (standardRate <= 0m || actualRate <= 0m) return false;
        var threshold = standardRate * (1m + (tolerancePercent / 100m));
        return actualRate > threshold;
    }

    /// <summary>
    /// Calculates total operating cost per unit (KM or Hour).
    /// </summary>
    public static decimal CalculateOperatingCostPerUnit(decimal totalOperatingCost, decimal totalMeterUnits)
    {
        if (totalMeterUnits <= 0m || totalOperatingCost <= 0m) return 0m;
        return Math.Round(totalOperatingCost / totalMeterUnits, 2);
    }
}
