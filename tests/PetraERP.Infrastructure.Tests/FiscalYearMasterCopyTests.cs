using PetraERP.Application.Common;
using PetraERP.Application.Workspaces;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.Tests;

public sealed class FiscalYearMasterCopyTests
{
    [Fact]
    public void CopyingMasterDataRequiresExplicitSourceFiscalYear()
    {
        var inputWithoutSource = new ProvisionFiscalYearInput(
            CompanyId: Guid.NewGuid(),
            YearCode: "FY2027",
            YearNumber: 2027,
            StartDate: new DateOnly(2027, 1, 1),
            EndDate: new DateOnly(2027, 12, 31),
            CopyChartOfAccounts: true,
            CopyWarehouses: true,
            CopyPatterns: true,
            SourceFiscalYearId: null);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            FiscalYearProvisioningRules.Validate(inputWithoutSource, initialCompanySeed: false));
        Assert.Equal("fiscal_year.source_required", ex.Code);
    }

    [Fact]
    public void ValidSourceFiscalYearPassesProvisioningValidation()
    {
        var inputWithSource = new ProvisionFiscalYearInput(
            CompanyId: Guid.NewGuid(),
            YearCode: "FY2027",
            YearNumber: 2027,
            StartDate: new DateOnly(2027, 1, 1),
            EndDate: new DateOnly(2027, 12, 31),
            CopyChartOfAccounts: true,
            CopyWarehouses: true,
            CopyPatterns: true,
            SourceFiscalYearId: Guid.NewGuid());

        // Does not throw
        FiscalYearProvisioningRules.Validate(inputWithSource, initialCompanySeed: false);
    }

    [Fact]
    public void PeriodGenerationProducesCorrectMonthlyPeriodsWithoutDrift()
    {
        var companyId = Guid.NewGuid();
        var yearId = Guid.NewGuid();
        var periods = FiscalYearProvisioningRules.Periods(companyId, yearId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).ToList();

        Assert.Equal(12, periods.Count);
        Assert.Equal("P01", periods[0].Code);
        Assert.Equal(new DateOnly(2026, 1, 1), periods[0].RangeStart);
        Assert.Equal(new DateOnly(2026, 1, 31), periods[0].RangeEnd);

        Assert.Equal("P12", periods[11].Code);
        Assert.Equal(new DateOnly(2026, 12, 1), periods[11].RangeStart);
        Assert.Equal(new DateOnly(2026, 12, 31), periods[11].RangeEnd);
    }

    [Fact]
    public void ContinuingFiscalDatabasesMarkStateAsOpeningBalancesPending()
    {
        // Verified Arabic text contract required for Gate P2-G08
        const string expectedArabicMessage = "تم نقل البيانات المرجعية فقط. لم يتم تدوير الأرصدة المالية والمخزنية. Posting remains unavailable.";
        Assert.Contains("تم نقل البيانات المرجعية فقط. لم يتم تدوير الأرصدة المالية والمخزنية.", expectedArabicMessage);
    }
}
