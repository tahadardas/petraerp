using PetraERP.Domain.Parties;
using PetraERP.Domain.Primitives;
using Xunit;

namespace PetraERP.Domain.Tests;

public sealed class PartyDomainTests
{
    [Fact]
    public void ArabicNormalizerNormalizesDiacriticsTatweelAndAlefForms()
    {
        const string input = "مَصَارِيفُ الجِبَالَةِـ١١٦";
        var normalized = ArabicTextNormalizer.Normalize(input);

        // Tashkeel removed, tatweel removed, Alef normalized, Taa Marbuta to Haa, Eastern digits to Western
        Assert.Equal("مصاريف الجباله116", normalized);
    }

    [Theory]
    [InlineData("مص 116", true)]
    [InlineData("جب 116", true)]
    [InlineData("116 مص", true)]
    [InlineData("مص ١١٦", true)]
    [InlineData("مصاريف جبالة 116", true)]
    [InlineData("مصاريف", true)]
    [InlineData("١١٦", true)]
    [InlineData("116", true)]
    [InlineData("مص 117", false)]
    [InlineData("سوق 116", false)]
    public void ArabicTokenPrefixMatcherMatchesDeterministicQueries(string query, bool shouldMatch)
    {
        const string target = "مصاريف جبالة 116";
        var result = ArabicTextNormalizer.MatchesTokenPrefixes(query, target);
        Assert.Equal(shouldMatch, result);
    }

    [Fact]
    public void ArabicNormalizerHandlesEasternAndPersianDigits()
    {
        Assert.Equal("0123456789", ArabicTextNormalizer.Normalize("٠١٢٣٤٥٦٧٨٩"));
        Assert.Equal("0123456789", ArabicTextNormalizer.Normalize("۰۱۲۳۴۵۶۷۸۹"));
    }

    [Theory]
    [InlineData("امل", "شركة الأمل للتجارة", true)]
    [InlineData("أمل", "شركة الامل للتجارة", true)]
    [InlineData("جب", "مصاريف الجبالة", true)]
    [InlineData("مل", "شركة الأمل للتجارة", false)]
    public void ArabicSearchMatchesPrefixAfterDefiniteArticle(string query, string target, bool expected)
    {
        Assert.Equal(expected, ArabicTextNormalizer.MatchesTokenPrefixes(query, target));
    }

    [Fact]
    public void CustomerCannotHavePayableAccount()
    {
        var companyId = Guid.NewGuid();
        var code = EntityCode.Create("CUST-001");
        var name = BilingualName.Create("عميل تجاري", "Commercial Customer");
        var receivableAcc = Guid.NewGuid();
        var payableAcc = Guid.NewGuid();

        var ex = Assert.Throws<InvalidOperationException>(() => new Party(
            Guid.NewGuid(),
            companyId,
            code,
            name,
            PartyRole.Customer,
            receivableAccountId: receivableAcc,
            payableAccountId: payableAcc));

        Assert.Equal(RuleViolation.PartyRoleInvalid, RuleViolation.CodeOf(ex));
    }

    [Fact]
    public void SupplierCannotHaveReceivableAccount()
    {
        var companyId = Guid.NewGuid();
        var code = EntityCode.Create("SUPP-001");
        var name = BilingualName.Create("مورد تجاري", "Commercial Supplier");
        var receivableAcc = Guid.NewGuid();
        var payableAcc = Guid.NewGuid();

        var ex = Assert.Throws<InvalidOperationException>(() => new Party(
            Guid.NewGuid(),
            companyId,
            code,
            name,
            PartyRole.Supplier,
            receivableAccountId: receivableAcc,
            payableAccountId: payableAcc));

        Assert.Equal(RuleViolation.PartyRoleInvalid, RuleViolation.CodeOf(ex));
    }

    [Fact]
    public void BothRoleAllowsBothAccounts()
    {
        var companyId = Guid.NewGuid();
        var code = EntityCode.Create("BOTH-001");
        var name = BilingualName.Create("زبون ومورد معا", "Both Customer and Supplier");
        var receivableAcc = Guid.NewGuid();
        var payableAcc = Guid.NewGuid();

        var party = new Party(
            Guid.NewGuid(),
            companyId,
            code,
            name,
            PartyRole.Both,
            receivableAccountId: receivableAcc,
            payableAccountId: payableAcc);

        Assert.Equal(receivableAcc, party.ReceivableAccountId);
        Assert.Equal(payableAcc, party.PayableAccountId);
        Assert.True(party.IsActive);
    }

    [Fact]
    public void NegativeCreditLimitOrPaymentTermsThrowsOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PartyCreditPolicy.Create(-100m, 30, CreditPolicyKind.BlockOnExceed));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PartyCreditPolicy.Create(50000m, -5, CreditPolicyKind.AllowWithWarning));
    }

    [Fact]
    public void DeactivateAndReactivateTogglesActiveState()
    {
        var party = new Party(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EntityCode.Create("P-01"),
            BilingualName.Create("شركة الأمل"),
            PartyRole.Customer);

        Assert.True(party.IsActive);

        party.Deactivate();
        Assert.False(party.IsActive);

        party.Reactivate();
        Assert.True(party.IsActive);
    }

    [Fact]
    public void BuildSearchTargetIncludesAllSearchableTokens()
    {
        var party = new Party(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EntityCode.Create("CUST-116"),
            BilingualName.Create("مصاريف جبالة", "Jeblah Expenses"),
            PartyRole.Both,
            taxNumber: "TX-9988");

        var target = party.BuildSearchTarget();
        Assert.Contains("CUST-116", target);
        Assert.Contains("مصاريف جبالة", target);
        Assert.Contains("Jeblah Expenses", target);
        Assert.Contains("TX-9988", target);

        // Matching test
        Assert.True(ArabicTextNormalizer.MatchesTokenPrefixes("مص 116", target));
        Assert.True(ArabicTextNormalizer.MatchesTokenPrefixes("جب tx", target));
    }
}
