using PetraERP.Modules.Abstractions;
using Xunit;

namespace PetraERP.Modules.Tests;

public class WellKnownModulesTests
{
    [Fact]
    public void PayrollDependsOnEmployeesAndTheOthersAreIndependent()
    {
        var dependency = Assert.Single(WellKnownModules.RequiredDependenciesOf(WellKnownModules.Payroll));
        Assert.Equal(WellKnownModules.Employees, dependency.ModuleId);
        Assert.False(dependency.Optional);
        Assert.Empty(WellKnownModules.RequiredDependenciesOf(WellKnownModules.FixedAssets));
        Assert.Empty(WellKnownModules.RequiredDependenciesOf(WellKnownModules.Employees));
    }

    [Fact]
    public void ReferencesAndPermissionsAreNamespacedByModule()
    {
        Assert.Equal("petra.payroll:run-2026-11", WellKnownModules.PostingReference(WellKnownModules.Payroll, "run-2026-11"));
        Assert.Equal("Module.petra.fixed-assets.Depreciate", WellKnownModules.Permission(WellKnownModules.FixedAssets, "Depreciate"));
        Assert.Throws<ArgumentException>(() => WellKnownModules.PostingReference(" ", "x"));
        Assert.Throws<ArgumentException>(() => WellKnownModules.Permission(WellKnownModules.Payroll, ""));
    }
}
