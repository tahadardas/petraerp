using PetraERP.Domain.Primitives;
using PetraERP.Domain.Treasury;

namespace PetraERP.Domain.Tests;

public sealed class VoucherRulesTests
{
    [Fact]
    public void PartialAllocationLeavesAnExplicitAdvance()
    {
        var advance = VoucherRules.Allocate(100m, [new(Guid.NewGuid(), 60m, 60m), new(Guid.NewGuid(), 25.5m, 210m)]);
        Assert.Equal(14.5m, advance);
        Assert.Equal(100m, VoucherRules.Allocate(100m, []));
    }

    [Theory]
    [InlineData(100, 101, 500, VoucherRules.OverAllocation)]
    [InlineData(100, 50, 49.9999, VoucherRules.InvoiceOverAllocated)]
    [InlineData(100, 0, 500, VoucherRules.AmountInvalid)]
    [InlineData(100, 0.00001, 500, VoucherRules.AmountInvalid)]
    [InlineData(0, 1, 500, VoucherRules.AmountInvalid)]
    public void InvalidAllocationsAreRejected(double amount, double allocated, double outstanding, string code)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            VoucherRules.Allocate((decimal)amount, [new(Guid.NewGuid(), (decimal)allocated, (decimal)outstanding)]));
        Assert.Equal(code, RuleViolation.CodeOf(exception));
    }

    [Fact]
    public void AnInvoiceCannotBeAllocatedTwiceInOneVoucher()
    {
        var invoice = Guid.NewGuid();
        var exception = Assert.Throws<InvalidOperationException>(() => VoucherRules.Allocate(100m, [new(invoice, 10m, 50m), new(invoice, 10m, 50m)]));
        Assert.Equal(VoucherRules.AllocationDuplicate, RuleViolation.CodeOf(exception));
    }

    [Fact]
    public void PaymentLinesMustEqualTheVoucherAmount()
    {
        VoucherRules.RequireBalancedLines(100m, [60m, 40m]);
        foreach (var lines in new[] { new[] { 60m, 39.9999m }, Array.Empty<decimal>(), new[] { 100m, 0m } })
        {
            var exception = Assert.Throws<InvalidOperationException>(() => VoucherRules.RequireBalancedLines(100m, lines));
            Assert.NotNull(RuleViolation.CodeOf(exception));
        }
    }
}
