using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.Tests;

/// <summary>The first payment of a credit invoice is an ordinary receipt voucher allocated to the posted invoice, keyed by the invoice.</summary>
public sealed class FirstPaymentRegistrarTests
{
    private static FirstPaymentRequest Request(Guid invoice) => new(Guid.NewGuid(), Guid.NewGuid(), invoice, "SI-0001", Guid.NewGuid(), 150m, SettlementMethod.Bank,
        "SYP", "1.00000000", new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero));

    private static AccountingFixture ReceiptPatterns()
    {
        var accounting = new AccountingFixture();
        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = Guid.NewGuid(), Code = "RV", NameArabic = "قبض", OperationKind = BusinessOperationKind.CashReceipt, IsActive = true, IsRunnable = true,
            Version = "AAAAAAAAB9E=", ActiveRevision = new PatternRevisionResponse { Id = Guid.NewGuid(), RevisionNumber = 1 },
        });
        return accounting;
    }

    [Fact]
    public async Task RecordsAReceiptAllocatedToTheInvoiceWithAnInvoiceDerivedKey()
    {
        var vouchers = new TreasuryFixture { ProcessingOutcome = "Posted" };
        var registrar = new FirstPaymentRegistrar(vouchers, ReceiptPatterns(), new LocalizationService("ar"));
        var invoice = Guid.NewGuid();

        var result = await registrar.RegisterAsync(Request(invoice), TestContext.Current.CancellationToken);

        Assert.True(result.Registered);
        var sent = Assert.Single(vouchers.SaveAndProcessRequests);
        Assert.Equal($"first-payment:{invoice:N}", sent.IdempotencyKey);
        Assert.Equal(VoucherKind.Receipt, sent.Document!.Kind);
        Assert.Equal(SettlementMethod.Bank, sent.Document.SettlementMethod);
        Assert.Equal("150.0000", sent.Document.Amount);
        var allocation = Assert.Single(sent.Document.Allocations!);
        Assert.Equal(invoice, allocation.SalesInvoiceId);
        Assert.Equal("150.0000", allocation.Amount);
    }

    [Fact]
    public async Task AVoucherThatIsNotPostedIsReportedAsNotYetApplied()
    {
        var vouchers = new TreasuryFixture { ProcessingOutcome = "SavedAsDraft" };
        var result = await new FirstPaymentRegistrar(vouchers, ReceiptPatterns(), new LocalizationService("ar")).RegisterAsync(Request(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.False(result.Registered);
        Assert.NotEmpty(result.Message);
        Assert.Single(vouchers.SaveAndProcessRequests);
    }

    [Fact]
    public async Task WithoutAnActiveReceiptPatternNothingIsSentAndTheOperatorIsTold()
    {
        var vouchers = new TreasuryFixture();
        var result = await new FirstPaymentRegistrar(vouchers, new AccountingFixture(), new LocalizationService("ar")).RegisterAsync(Request(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.False(result.Registered);
        Assert.Empty(vouchers.SaveAndProcessRequests);
        Assert.Contains("نمط", result.Message, StringComparison.Ordinal);
    }
}
