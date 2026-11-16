using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Workspaces;
using PetraERP.Domain.AccountingCalendars;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.Tests;

public sealed class PhaseOneProvisioningTests
{
    [Theory]
    [InlineData(2026, 1, 1, 2026, 1, 1, 1)]
    [InlineData(2026, 1, 1, 2026, 6, 15, 6)]
    [InlineData(2026, 1, 1, 2027, 3, 31, 15)]
    [InlineData(2024, 1, 31, 2024, 3, 31, 3)]
    [InlineData(2026, 1, 31, 2026, 3, 30, 2)]
    [InlineData(9999, 12, 1, 9999, 12, 31, 1)]
    public void PeriodsCoverTheExactRangeWithoutGapsAndAreOpen(int sy, int sm, int sd, int ey, int em, int ed, int count)
    {
        var start = new DateOnly(sy, sm, sd);
        var end = new DateOnly(ey, em, ed);
        var company = Guid.NewGuid();
        var year = Guid.NewGuid();
        var periods = FiscalYearProvisioningRules.Periods(company, year, start, end).ToArray();
        Assert.Equal(count, periods.Length);
        Assert.Equal(start, periods[0].RangeStart);
        Assert.Equal(end, periods[^1].RangeEnd);
        Assert.Equal(end.DayNumber - start.DayNumber + 1, periods.Sum(p => p.RangeEnd.DayNumber - p.RangeStart.DayNumber + 1));
        for (var index = 0; index < periods.Length; index++)
        {
            var period = periods[index];
            Assert.Equal((byte)AccountingPeriodState.Open, period.State);
            Assert.Equal(company, period.CompanyId);
            Assert.Equal(year, period.FiscalYearId);
            Assert.Equal($"P{index + 1:D2}", period.Code);
            Assert.InRange(period.RangeStart, start, end);
            Assert.InRange(period.RangeEnd, period.RangeStart, end);
            if (index > 0) Assert.Equal(periods[index - 1].RangeEnd.AddDays(1), period.RangeStart);
        }
    }

    [Fact]
    public void InvalidRangeNumberAndUnsupportedCopyAreRejected()
    {
        var valid = new ProvisionFiscalYearInput(Guid.NewGuid(), 2026, "FY2026", new(2026, 1, 1), new(2026, 12, 31), false, false, false);
        FiscalYearProvisioningRules.Validate(valid, false);
        Assert.Equal("fiscal_year.range_invalid", Assert.Throws<BusinessRuleException>(() =>
            FiscalYearProvisioningRules.Validate(valid with { EndDate = new(2025, 12, 31) }, false)).Code);
        Assert.Equal("fiscal_year.number_invalid", Assert.Throws<BusinessRuleException>(() =>
            FiscalYearProvisioningRules.Validate(valid with { YearNumber = 2027 }, false)).Code);
        foreach (var input in new[] { valid with { CopyChartOfAccounts = true }, valid with { CopyWarehouses = true }, valid with { CopyPatterns = true } })
            Assert.Equal("fiscal_year.source_required", Assert.Throws<BusinessRuleException>(() => FiscalYearProvisioningRules.Validate(input, false)).Code);
    }

    [Fact]
    public async Task LegacyExecutionIsRejectedBeforeAnyDatabaseConnection()
    {
        using var context = new PetraDbContext(new DbContextOptionsBuilder<PetraDbContext>().UseSqlServer().Options);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => new SqlLegacyToIndependentDbMigrator(context)
            .MigrateCompanyYearAsync(Guid.NewGuid(), Guid.NewGuid(), false, TestContext.Current.CancellationToken)).ConfigureAwait(true);
        Assert.Equal("legacy.migration_not_approved", error.Code);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccountingContextRejectsMovementMutationBeforeDatabaseAccess(bool delete)
    {
        using var context = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer().Options);
        var movement = new StockMovementRow { Id = Guid.NewGuid() };
        context.Attach(movement);
        context.Entry(movement).State = delete ? EntityState.Deleted : EntityState.Modified;
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
        Assert.Equal("inventory.immutable", error.Code);
    }
}
