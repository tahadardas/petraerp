using PetraERP.Domain.Sales;

namespace PetraERP.Domain.Tests;

public sealed class SalesInvoiceDomainTests
{
    [Fact]
    public void SalesInvoiceLineCalculatesTotalsAccuratelyWithDiscountsAndTaxes()
    {
        // Line: 10 m³ concrete @ 45.0000 JOD/m³
        // Base: 450.0000 JOD
        // Discount: 10% = 45.0000 JOD
        // Net: 405.0000 JOD
        // Tax: 16% = 64.8000 JOD
        // PumpCost: 50.0000 JOD
        // TransportCost: 25.0000 JOD
        // LineTotal = 405.0000 + 64.8000 + 50.0000 + 25.0000 = 544.8000 JOD
        var lineId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var unitId = Guid.NewGuid();

        var line = new SalesInvoiceLine(
            id: lineId,
            salesInvoiceId: invoiceId,
            itemId: itemId,
            unitId: unitId,
            quantity: 10.0000m,
            unitPrice: 45.0000m,
            discountPercentage: 10.0000m,
            taxPercentage: 16.0000m,
            pumpCost: 50.0000m,
            transportCost: 25.0000m,
            concreteGrade: "C30");

        Assert.Equal(45.0000m, line.DiscountAmount);
        Assert.Equal(64.8000m, line.TaxAmount);
        Assert.Equal(50.0000m, line.PumpCost);
        Assert.Equal(25.0000m, line.TransportCost);
        Assert.Equal(544.8000m, line.LineTotal);
        Assert.Equal("C30", line.ConcreteGrade);
    }

    [Fact]
    public void SalesInvoiceAggregatesTotalsAcrossMultipleLines()
    {
        var invoice = new SalesInvoice(
            id: Guid.NewGuid(),
            companyId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            patternRevisionId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            warehouseId: Guid.NewGuid(),
            invoiceDateUtc: DateTimeOffset.UtcNow,
            paymentMethod: PaymentMethod.Credit,
            currencyCode: "JOD",
            exchangeRate: 1.0m);

        var line1 = new SalesInvoiceLine(
            id: Guid.NewGuid(),
            salesInvoiceId: invoice.Id,
            itemId: Guid.NewGuid(),
            unitId: Guid.NewGuid(),
            quantity: 20m,
            unitPrice: 50m,
            discountPercentage: 5m,
            taxPercentage: 16m);
        // Base: 1000, Disc: 50, Net: 950, Tax: 152, Total: 1102

        var line2 = new SalesInvoiceLine(
            id: Guid.NewGuid(),
            salesInvoiceId: invoice.Id,
            itemId: Guid.NewGuid(),
            unitId: Guid.NewGuid(),
            quantity: 5m,
            unitPrice: 100m,
            pumpCost: 30m,
            transportCost: 20m);
        // Base: 500, Disc: 0, Net: 500, Tax: 0, Additions: 50, Total: 550

        invoice.AddLine(line1);
        invoice.AddLine(line2);

        Assert.Equal(1500m, invoice.SubTotal);
        Assert.Equal(50m, invoice.DiscountTotal);
        Assert.Equal(152m, invoice.TaxTotal);
        Assert.Equal(50m, invoice.AdditionsTotal);
        Assert.Equal(1652m, invoice.GrandTotal);
    }

    [Fact]
    public void SalesInvoiceFollowsStrictStateTransitions()
    {
        var invoice = new SalesInvoice(
            id: Guid.NewGuid(),
            companyId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            patternRevisionId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            warehouseId: Guid.NewGuid(),
            invoiceDateUtc: DateTimeOffset.UtcNow);

        var line = new SalesInvoiceLine(
            id: Guid.NewGuid(),
            salesInvoiceId: invoice.Id,
            itemId: Guid.NewGuid(),
            unitId: Guid.NewGuid(),
            quantity: 1m,
            unitPrice: 10m);
        invoice.AddLine(line);

        Assert.Equal(SalesInvoiceStatus.Draft, invoice.Status);

        var approverId = Guid.NewGuid();
        invoice.Approve(approverId);
        Assert.Equal(SalesInvoiceStatus.Approved, invoice.Status);
        Assert.Equal(approverId, invoice.ApprovedByUserId);
        Assert.NotNull(invoice.ApprovedAtUtc);

        var posterId = Guid.NewGuid();
        var journalId = Guid.NewGuid();
        invoice.Post("INV-2026-0001", journalId, posterId);
        Assert.Equal(SalesInvoiceStatus.Posted, invoice.Status);
        Assert.Equal("INV-2026-0001", invoice.InvoiceNumber);
        Assert.Equal(journalId, invoice.JournalEntryId);
        Assert.Equal(posterId, invoice.PostedByUserId);
        Assert.NotNull(invoice.PostedAtUtc);

        // Cannot modify posted invoice
        Assert.Throws<InvalidOperationException>(() => invoice.AddLine(new SalesInvoiceLine(
            Guid.NewGuid(), invoice.Id, Guid.NewGuid(), Guid.NewGuid(), 2m, 20m)));

        // Reversal
        var reversalId = Guid.NewGuid();
        invoice.MarkReversed(reversalId);
        Assert.Equal(SalesInvoiceStatus.Reversed, invoice.Status);
        Assert.Equal(reversalId, invoice.ReversedByInvoiceId);
    }

    [Fact]
    public void EmptyInvoiceCannotBeApprovedOrPosted()
    {
        var invoice = new SalesInvoice(
            id: Guid.NewGuid(),
            companyId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            patternRevisionId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            warehouseId: Guid.NewGuid(),
            invoiceDateUtc: DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => invoice.Approve(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => invoice.Post("INV-001", Guid.NewGuid(), Guid.NewGuid()));
    }
}
