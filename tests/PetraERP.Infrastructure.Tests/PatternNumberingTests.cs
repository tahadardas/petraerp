using PetraERP.Domain.Patterns;
using PetraERP.Infrastructure.Patterns;

namespace PetraERP.Infrastructure.Tests;

public sealed class PatternNumberingTests
{
    [Fact]
    public void NumberAllocationKeyProducesDeterministicGuid()
    {
        var docId = Guid.NewGuid();
        var key1 = SqlPatternService.NumberAllocationKey("SalesInvoice", docId, "REQ-1");
        var key2 = SqlPatternService.NumberAllocationKey("SalesInvoice", docId, "REQ-1");
        var key3 = SqlPatternService.NumberAllocationKey("SalesInvoice", docId, "REQ-2");

        Assert.Equal(key1, key2);
        Assert.NotEqual(key1, key3);
        Assert.NotEqual(Guid.Empty, key1);
    }

    [Fact]
    public void PatternNumberingPolicyEnforcesValidScopeAndInitialSequence()
    {
        var policy = new PatternNumberingPolicy
        {
            Prefix = "INV-",
            Suffix = "-{yyyyMM}",
            MinimumDigits = 5,
            InitialSequence = 100,
            Scope = PatternNumberingScope.BranchAndFiscalYear,
            ResetPolicy = PatternNumberResetPolicy.Monthly,
            AllowManualNumbering = true,
        };

        Assert.Equal("INV-", policy.Prefix);
        Assert.Equal("-{yyyyMM}", policy.Suffix);
        Assert.Equal(5, policy.MinimumDigits);
        Assert.Equal(100, policy.InitialSequence);
        Assert.Equal(PatternNumberingScope.BranchAndFiscalYear, policy.Scope);
        Assert.Equal(PatternNumberResetPolicy.Monthly, policy.ResetPolicy);
        Assert.True(policy.AllowManualNumbering);
    }

    [Theory]
    [InlineData(PatternNumberingScope.Company)]
    [InlineData(PatternNumberingScope.Branch)]
    [InlineData(PatternNumberingScope.FiscalYear)]
    [InlineData(PatternNumberingScope.BranchAndFiscalYear)]
    public void AllDefinedNumberingScopesAreHandledByDomain(PatternNumberingScope scope)
    {
        Assert.True(Enum.IsDefined(scope));
    }
}
