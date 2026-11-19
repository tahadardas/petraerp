using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;
using Xunit;

namespace PetraERP.Domain.Tests;

public class VoucherPatternSchemaTests
{
    private static string CodeOf(Action action)
    {
        var exception = Assert.ThrowsAny<InvalidOperationException>(action);
        return RuleViolation.CodeOf(exception)!;
    }

    [Fact]
    public void SchemaOneUpgradesToTwoKeepingEveryValueAndLeavingTheVoucherBlockAbsent()
    {
        var v1 = new PatternConfiguration
        {
            SchemaVersion = 1, DefaultCurrencyCode = "USD",
            NumberingPolicy = new PatternNumberingPolicy { Prefix = "RV-", MinimumDigits = 5 },
            EntryPolicy = new PatternEntryPolicy { LockDocumentDate = true },
        };
        var upgraded = PatternConfigurationUpgrader.ToCurrent(v1);
        Assert.Equal(PatternConfiguration.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.Equal("USD", upgraded.DefaultCurrencyCode);
        Assert.Equal("RV-", upgraded.NumberingPolicy.Prefix);
        Assert.True(upgraded.EntryPolicy.LockDocumentDate);
        Assert.Null(upgraded.Voucher);
        Assert.Same(upgraded, PatternConfigurationUpgrader.ToCurrent(upgraded)); // idempotent
        Assert.Equal(1, v1.SchemaVersion); // the input is never mutated
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void UnknownSchemaVersionsAreRejectedWithAStableCode(int version)
    {
        var code = CodeOf(() => PatternConfigurationUpgrader.ToCurrent(new PatternConfiguration { SchemaVersion = version }));
        Assert.Equal(VoucherPatternRules.SchemaUnsupported, code);
    }

    [Fact]
    public void ARevisionWithoutTheBlockRunsWithTheDefaults()
    {
        var effective = VoucherPatternRules.Effective(new PatternConfiguration());
        Assert.Equal(VoucherFormKind.GenericVoucher, effective.FormKind);
        Assert.Equal(VoucherCaptionMode.DebitCredit, effective.CaptionMode);
        Assert.Null(effective.DenyMutationsOnOrBefore);
        Assert.False(effective.PerLineJournal);
        Assert.False(effective.AutoPost);
    }

    [Theory]
    [InlineData(BusinessOperationKind.CashReceipt)]
    [InlineData(BusinessOperationKind.CashPayment)]
    [InlineData(BusinessOperationKind.CashJournal)]
    [InlineData(BusinessOperationKind.ManualJournal)]
    public void GenericVoucherWithFromToCaptionsIsAcceptedForVoucherOperations(BusinessOperationKind kind)
    {
        var normalized = PatternAccountRoles.Normalize(kind, new PatternConfiguration
        {
            Voucher = new PatternVoucherPolicy { FormKind = VoucherFormKind.GenericVoucher, CaptionMode = VoucherCaptionMode.FromTo },
        });
        Assert.Equal(VoucherCaptionMode.FromTo, normalized.Voucher!.CaptionMode);
        Assert.Equal(PatternConfiguration.CurrentSchemaVersion, normalized.SchemaVersion);
    }

    [Theory]
    [InlineData(BusinessOperationKind.SalesInvoice)]
    [InlineData(BusinessOperationKind.PurchaseInvoice)]
    [InlineData(BusinessOperationKind.Assembly)]
    public void NonVoucherOperationsRejectTheBlock(BusinessOperationKind kind)
    {
        var code = CodeOf(() => PatternAccountRoles.Normalize(kind, new PatternConfiguration { Voucher = new PatternVoucherPolicy() }));
        Assert.Equal(VoucherPatternRules.BlockNotAllowed, code);
    }

    [Theory]
    [InlineData(VoucherFormKind.MultiMethodReceipt)]
    [InlineData(VoucherFormKind.MultiMethodPayment)]
    [InlineData((VoucherFormKind)99)]
    public void FormsThatDoNotRunYetAreRejected(VoucherFormKind form)
    {
        var code = CodeOf(() => PatternAccountRoles.Normalize(BusinessOperationKind.CashReceipt,
            new PatternConfiguration { Voucher = new PatternVoucherPolicy { FormKind = form } }));
        Assert.Equal(VoucherPatternRules.FormUnsupported, code);
    }

    [Fact]
    public void OptionsTheServerDoesNotEnforceYetCannotBeStored()
    {
        foreach (var policy in new[]
        {
            new PatternVoucherPolicy { DenyMutationsOnOrBefore = new DateOnly(2026, 10, 31) },
            new PatternVoucherPolicy { PerLineJournal = true },
            new PatternVoucherPolicy { AutoPost = true },
        })
        {
            var code = CodeOf(() => PatternAccountRoles.Normalize(BusinessOperationKind.CashPayment, new PatternConfiguration { Voucher = policy }));
            Assert.Equal(VoucherPatternRules.OptionNotEnforced, code);
        }
    }

    [Fact]
    public void TheDocumentDateLockAndTheCutoffAreDistinctSettings()
    {
        var onlyLock = PatternAccountRoles.Normalize(BusinessOperationKind.CashReceipt,
            new PatternConfiguration { EntryPolicy = new PatternEntryPolicy { LockDocumentDate = true } });
        Assert.True(onlyLock.EntryPolicy.LockDocumentDate);
        Assert.Null(onlyLock.Voucher?.DenyMutationsOnOrBefore);
    }
}
