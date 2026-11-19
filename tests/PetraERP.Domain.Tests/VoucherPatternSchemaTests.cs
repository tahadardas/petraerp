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
    public void TheCutoffIsStoredAndFreezesOnlyDocumentsOnOrBeforeIt()
    {
        var normalized = PatternAccountRoles.Normalize(BusinessOperationKind.CashReceipt, new PatternConfiguration
        { Voucher = new PatternVoucherPolicy { DenyMutationsOnOrBefore = new DateOnly(2026, 10, 31) } });
        Assert.True(VoucherPatternRules.IsFrozen(normalized, new DateOnly(2026, 10, 30)));
        Assert.True(VoucherPatternRules.IsFrozen(normalized, new DateOnly(2026, 10, 31)));
        Assert.False(VoucherPatternRules.IsFrozen(normalized, new DateOnly(2026, 11, 1)));
        Assert.False(VoucherPatternRules.IsFrozen(new PatternConfiguration(), new DateOnly(2000, 1, 1)));
        Assert.Equal(VoucherPatternRules.CutoffInvalid, CodeOf(() => PatternAccountRoles.Normalize(BusinessOperationKind.CashReceipt,
            new PatternConfiguration { Voucher = new PatternVoucherPolicy { DenyMutationsOnOrBefore = new DateOnly(1999, 12, 31) } })));
    }

    [Fact]
    public void LookupRestrictionSelectsASubtreeAndRejectsEmptyOrTooManyIds()
    {
        Guid root = Guid.NewGuid(), child = Guid.NewGuid(), grand = Guid.NewGuid(), other = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [root] = null, [child] = root, [grand] = child, [other] = null };
        var eligible = VoucherPatternRules.EligibleAccounts(new PatternLookupPolicy { EligibleAccountRootIds = [child] }, parents);
        Assert.Equal(new[] { child, grand }.Order(), eligible.Order());
        Assert.Empty(VoucherPatternRules.EligibleAccounts(new PatternLookupPolicy { EligibleAccountRootIds = [Guid.NewGuid()] }, parents));
        // A cycle in bad data cannot loop forever.
        var loop = new Dictionary<Guid, Guid?> { [root] = child, [child] = root };
        Assert.Empty(VoucherPatternRules.EligibleAccounts(new PatternLookupPolicy { EligibleAccountRootIds = [other] }, loop));

        Assert.Equal(VoucherPatternRules.LookupInvalid, CodeOf(() => VoucherPatternRules.NormalizeLookup(new PatternLookupPolicy { EligibleAccountRootIds = [Guid.Empty] })));
        Assert.Equal(VoucherPatternRules.LookupInvalid, CodeOf(() => VoucherPatternRules.NormalizeLookup(new PatternLookupPolicy
        { EligibleCostCenterIds = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList() })));
        Assert.Single(VoucherPatternRules.NormalizeLookup(new PatternLookupPolicy { EligibleAccountRootIds = [root, root] }).EligibleAccountRootIds);
    }

    [Fact]
    public void RequiredVisibleFieldsMustBeFilledAndHiddenFieldsMustStayEmpty()
    {
        var config = new PatternConfiguration
        {
            LineFields =
            [
                new() { FieldName = "CostCenter", IsVisible = true, IsRequired = true },
                new() { FieldName = "Description", IsVisible = false },
                new() { FieldName = "Amount", IsVisible = false }, // not a field the rules check: ignored
            ],
        };
        Assert.Null(VoucherPatternRules.CheckLineFields(config, hasCostCenter: true, hasDescription: false));
        Assert.Equal((VoucherPatternRules.LineFieldRequired, "CostCenter"), VoucherPatternRules.CheckLineFields(config, false, false));
        Assert.Equal((VoucherPatternRules.LineFieldHidden, "Description"), VoucherPatternRules.CheckLineFields(config, true, true));
        Assert.Null(VoucherPatternRules.CheckLineFields(new PatternConfiguration(), false, false));
    }

    [Theory]
    [InlineData(false, null, 1, false)]  // approval not required at all
    [InlineData(true, null, 1, true)]    // no threshold: every document
    [InlineData(true, "50", 49.9999, false)]
    [InlineData(true, "50", 50, true)]   // the threshold itself needs approval
    [InlineData(true, "50", 5000, true)]
    public void ApprovalIsRequiredOnlyWhenThePatternAsksAndTheAmountReachesTheThreshold(bool requires, string? threshold, double amount, bool expected)
    {
        var config = new PatternConfiguration
        {
            ApprovalPolicy = new PatternApprovalPolicy { RequiresApproval = requires, ThresholdAmount = threshold is null ? null : decimal.Parse(threshold, System.Globalization.CultureInfo.InvariantCulture) },
        };
        Assert.Equal(expected, VoucherPatternRules.ApprovalRequired(config, (decimal)amount));
        if (!requires) Assert.False(VoucherPatternRules.ApprovalRequired(config, 1_000_000m));
    }

    [Fact]
    public void ManualOnlyPostingIsStoredAndAnUndefinedModeIsRefused()
    {
        var normalized = PatternAccountRoles.Normalize(BusinessOperationKind.CashPayment,
            new PatternConfiguration { Voucher = new PatternVoucherPolicy { PostingMode = VoucherPostingMode.ManualOnly } });
        Assert.Equal(VoucherPostingMode.ManualOnly, normalized.Voucher!.PostingMode);
        Assert.Equal(VoucherPostingMode.InheritFromCompany, VoucherPatternRules.Effective(new PatternConfiguration()).PostingMode);
        Assert.Equal(VoucherPatternRules.FormUnsupported, CodeOf(() => PatternAccountRoles.Normalize(BusinessOperationKind.CashPayment,
            new PatternConfiguration { Voucher = new PatternVoucherPolicy { PostingMode = (VoucherPostingMode)9 } })));
    }

    [Fact]
    public void OptionsTheServerDoesNotEnforceYetCannotBeStored()
    {
        foreach (var policy in new[]
        {
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
