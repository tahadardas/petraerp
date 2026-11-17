using PetraERP.Domain.Assets;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;
using Xunit;

namespace PetraERP.Domain.Tests;

public class AssetsTests
{
    [Fact]
    public void StraightLineDepreciation_CalculatesAccurately()
    {
        // 120,000 cost, 12,000 salvage, 36 months life -> (108,000 / 36) = 3,000 per month
        var monthlyDep = AssetCalculationEngine.CalculateStraightLine(120000m, 12000m, 36, 1);
        Assert.Equal(3000m, monthlyDep);

        // For a full year (12 months): 3,000 * 12 = 36,000
        var yearlyDep = AssetCalculationEngine.CalculateStraightLine(120000m, 12000m, 36, 12);
        Assert.Equal(36000m, yearlyDep);
    }

    [Fact]
    public void DecliningBalanceDepreciation_ClampsAtSalvageValue()
    {
        // Current book value 15,000, salvage 10,000, annual rate 50%
        // Monthly theoretical = 15,000 * (0.50 / 12) * 12 = 7,500
        // But max allowed is 15,000 - 10,000 = 5,000
        var dep = AssetCalculationEngine.CalculateDecliningBalance(15000m, 10000m, 50m, 12);
        Assert.Equal(5000m, dep);
    }

    [Fact]
    public void UnitsOfProductionDepreciation_CalculatesAccurately()
    {
        // Machinery cost 500,000, salvage 50,000, expected 10,000 hours -> 45 per hour
        // Used 200 hours in month -> 45 * 200 = 9,000
        var dep = AssetCalculationEngine.CalculateUnitsOfProduction(500000m, 50000m, 10000m, 200m);
        Assert.Equal(9000m, dep);
    }

    [Fact]
    public void MovingWeightedAverageCost_CalculatesSyrianMarketFluctuations()
    {
        // Existing in fuel tank: 10,000 Liters at 10,000 SP = 100,000,000 SP
        // New purchase: 5,000 Liters at 13,000 SP = 65,000,000 SP
        // Total value = 165,000,000 SP / 15,000 Liters = 11,000 SP per Liter
        var avgCost = AssetCalculationEngine.CalculateMovingWeightedAverageCost(10000m, 10000m, 5000m, 13000m);
        Assert.Equal(11000m, avgCost);
    }

    [Fact]
    public void FuelConsumptionRate_AndExcessAlert_WorkCorrectly()
    {
        // Truck traveled 500 KM and consumed 150 Liters -> (150 / 500) * 100 = 30 L / 100KM
        var rateKm = AssetCalculationEngine.CalculateFuelConsumptionRate(150m, 10500m, 10000m, isHours: false);
        Assert.Equal(30m, rateKm);

        // Excavator worked 10 hours and consumed 180 Liters -> 180 / 10 = 18 L / Hour
        var rateHours = AssetCalculationEngine.CalculateFuelConsumptionRate(180m, 2510m, 2500m, isHours: true);
        Assert.Equal(18m, rateHours);

        // Standard rate is 15 L/Hour, actual 18 L/Hour -> Deviation is 20% > 15% tolerance -> Warning
        var isExcessive = AssetCalculationEngine.IsFuelConsumptionExcessive(18m, 15m, 15m);
        Assert.True(isExcessive);

        // Standard rate 15, actual 16 -> Deviation is 6.6% < 15% -> No warning
        var isNormal = AssetCalculationEngine.IsFuelConsumptionExcessive(16m, 15m, 15m);
        Assert.False(isNormal);
    }

    [Fact]
    public void AssetPatternRoles_AndPublishValidation_Succeed()
    {
        var allowed = PatternAccountRoles.AllowedFor(BusinessOperationKind.AssetDepreciation);
        Assert.Contains(PatternAccountRoles.AssetAccount, allowed);
        Assert.Contains(PatternAccountRoles.AccumulatedDepreciation, allowed);
        Assert.Contains(PatternAccountRoles.DepreciationExpense, allowed);
        Assert.Contains(PatternAccountRoles.DisposalGainLoss, allowed);

        var fuelAllowed = PatternAccountRoles.AllowedFor(BusinessOperationKind.AssetFuelDispensing);
        Assert.Contains(PatternAccountRoles.FuelExpense, fuelAllowed);
        Assert.Contains(PatternAccountRoles.Inventory, fuelAllowed);

        var validFuelConfig = new PatternConfiguration
        {
            AccountMappings =
            [
                new PatternAccountMapping { Role = PatternAccountRoles.FuelExpense, AccountId = Guid.NewGuid() },
                new PatternAccountMapping { Role = PatternAccountRoles.Inventory, AccountId = Guid.NewGuid() }
            ]
        };
        // Should succeed without throwing
        PatternAccountRoles.RequireForPublish(BusinessOperationKind.AssetFuelDispensing, validFuelConfig);

        var missingFuelConfig = new PatternConfiguration
        {
            AccountMappings =
            [
                new PatternAccountMapping { Role = PatternAccountRoles.FuelExpense, AccountId = Guid.NewGuid() }
            ]
        };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PatternAccountRoles.RequireForPublish(BusinessOperationKind.AssetFuelDispensing, missingFuelConfig));
        Assert.Equal(PatternAccountRoles.RoleMissing, RuleViolation.CodeOf(ex));
    }
}
