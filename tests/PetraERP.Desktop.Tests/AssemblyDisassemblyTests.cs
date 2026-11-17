using PetraERP.Contracts.Inventory;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Inventory;

namespace PetraERP.Desktop.Tests;

public sealed class AssemblyDisassemblyTests
{
    [Fact]
    public void BOMQuantityAndCostScalingCalculatesAccurately()
    {
        var line1 = new AssemblyGridLine
        {
            StandardQuantity = 2.5m,
            UnitCost = 40.00m,
        };
        var line2 = new AssemblyGridLine
        {
            StandardQuantity = 1.0m,
            UnitCost = 150.00m,
        };

        decimal assembledQuantity = 4m;

        line1.TotalQuantity = line1.StandardQuantity * assembledQuantity;
        line1.Recalculate();

        line2.TotalQuantity = line2.StandardQuantity * assembledQuantity;
        line2.Recalculate();

        Assert.Equal(10.0m, line1.TotalQuantity);
        Assert.Equal(400.00m, line1.TotalCost);

        Assert.Equal(4.0m, line2.TotalQuantity);
        Assert.Equal(600.00m, line2.TotalCost);

        var totalMaterial = line1.TotalCost + line2.TotalCost;
        Assert.Equal(1000.00m, totalMaterial);
    }

    [Fact]
    public void OperatingExpensesAbsorptionIntoUnitCost()
    {
        decimal totalMaterial = 1000.00m;
        decimal operatingExpenses = 200.00m; // Labor, electricity, packing
        decimal assembledQuantity = 4m;

        decimal grandTotal = totalMaterial + operatingExpenses;
        decimal unitProducedCost = decimal.Round(grandTotal / assembledQuantity, 4);

        Assert.Equal(1200.00m, grandTotal);
        Assert.Equal(300.0000m, unitProducedCost);

        // Tafqeet integration
        var words = ArabicTafqeet.ToWords(grandTotal, "ليرة سورية", "قرش");
        Assert.Contains("ألف ومئتان", words);
    }

    [Fact]
    public void DisassemblyCostAllocationProportionalCalculation()
    {
        var lineA = new AssemblyGridLine { TotalCost = 300m };
        var lineB = new AssemblyGridLine { TotalCost = 700m };
        var lines = new List<AssemblyGridLine> { lineA, lineB };

        var totalCost = lines.Sum(l => l.TotalCost);
        Assert.Equal(1000m, totalCost);

        foreach (var line in lines)
        {
            line.CostPercentage = decimal.Round((line.TotalCost / totalCost) * 100m, 2);
        }

        Assert.Equal(30.00m, lineA.CostPercentage);
        Assert.Equal(70.00m, lineB.CostPercentage);
        Assert.Equal(100.00m, lines.Sum(l => l.CostPercentage));
    }
}
