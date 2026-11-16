using PetraERP.Application.Sales;
using PetraERP.Application.Treasury;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.DocumentProcessing;

namespace PetraERP.Infrastructure.Tests;

/// <summary>The canonical request hash has no tolerance: only representation noise (trailing zeros, padding) is normalized.</summary>
public sealed class DocumentRequestHashTests
{
    private static readonly Guid Pattern = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Customer = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Warehouse = Guid.Parse("00000000-0000-0000-0000-0000000000a3");
    private static readonly Guid Item = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid Unit = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly DateTimeOffset Day = new(2026, 1, 20, 12, 0, 0, TimeSpan.Zero);

    private static CreateSalesInvoiceLineCommand Line(decimal quantity = 1m, decimal price = 10m, decimal? discount = null, decimal? taxPct = null, Guid? costCenter = null) =>
        new(Item, Unit, quantity, price, 0m, discount ?? 0m, taxPct ?? 0m, 0m, 0m, 0m, costCenter);

    private static CreateSalesInvoiceCommand Sale(decimal rate = 1m, DateTimeOffset? date = null, params CreateSalesInvoiceLineCommand[] lines) =>
        new(Pattern, Customer, Warehouse, date ?? Day, PaymentMethod.Credit, "JOD", rate, null, null, lines.Length == 0 ? [Line()] : lines);

    private static string H(CreateSalesInvoiceCommand command) => DocumentRequestHash.Of("SalesInvoice", command);

    [Fact]
    public void IdenticalRequestsHashIdenticallyAndServerAssignedMembersAreIgnored()
    {
        var a = Sale();
        Assert.Equal(H(a), H(a with { DocumentId = Guid.NewGuid() }));
        Assert.Equal(64, H(a).Length);
    }

    [Fact]
    public void TrailingZerosAreNotMaterialButEverySignificantDigitIs()
    {
        Assert.Equal(H(Sale(lines: Line(price: 10m))), H(Sale(lines: Line(price: 10.00m))));
        Assert.NotEqual(H(Sale(lines: Line(price: 10.00m))), H(Sale(lines: Line(price: 10.01m))));          // one cent
        Assert.NotEqual(H(Sale(rate: 1m)), H(Sale(rate: 1.00000001m)));                                       // eighth FX digit
        Assert.NotEqual(H(Sale(lines: Line(quantity: 1m))), H(Sale(lines: Line(quantity: 1.00000001m))));
    }

    [Fact]
    public void DiscountTaxCostCenterAndDateEachChangeTheHash()
    {
        var baseline = H(Sale());
        Assert.NotEqual(baseline, H(Sale(lines: Line(discount: 0.01m))));
        Assert.NotEqual(baseline, H(Sale(lines: Line(taxPct: 0.0001m))));
        Assert.NotEqual(baseline, H(Sale(lines: Line(costCenter: Guid.NewGuid()))));
        Assert.NotEqual(baseline, H(Sale(date: Day.AddDays(1))));
        Assert.NotEqual(baseline, H(Sale(date: Day.AddMinutes(1))));
    }

    [Fact]
    public void DuplicatedSameItemLinesAreNotMergedOrReorderedIntoTheSameRequest()
    {
        var split = H(Sale(lines: [Line(1m), Line(2m)]));
        Assert.NotEqual(split, H(Sale(lines: Line(3m))));                       // merged
        Assert.NotEqual(split, H(Sale(lines: [Line(2m), Line(1m)])));           // reordered
        Assert.NotEqual(split, H(Sale(lines: [Line(1m), Line(2m), Line(0m)])));
    }

    [Fact]
    public void InstantsCompareAsUtcTicksSoEquivalentOffsetsAreTheSameRequest()
    {
        Assert.Equal(H(Sale(date: Day)), H(Sale(date: Day.ToOffset(TimeSpan.FromHours(3)))));
    }

    [Fact]
    public void VoucherAllocationDestinationAndAmountAreMaterial()
    {
        var target = Guid.NewGuid();
        var other = Guid.NewGuid();
        SaveVoucherCommand Voucher(Guid destination, decimal amount) => new(Domain.Treasury.VoucherKind.Receipt, Pattern, Customer, Domain.Treasury.SettlementMethod.Cash, Day,
            "JOD", 1m, 100m, null, [new VoucherAllocationCommand(destination, amount)], []);
        var baseline = DocumentRequestHash.Of("Voucher", Voucher(target, 100m));
        Assert.Equal(baseline, DocumentRequestHash.Of("Voucher", Voucher(target, 100.0m) with { ActorUserId = Guid.NewGuid() }));
        Assert.NotEqual(baseline, DocumentRequestHash.Of("Voucher", Voucher(other, 100m)));
        Assert.NotEqual(baseline, DocumentRequestHash.Of("Voucher", Voucher(target, 99.99m)));
    }
}
