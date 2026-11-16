using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Organizations;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Tests;

public sealed class OrganizationTests
{
    private static readonly CurrencyCode Usd = CurrencyCode.Create("USD");

    internal static Company Company(string code = "البتراء", bool active = true) =>
        Organizations.Company.Create(EntityCode.Create(code), BilingualName.Create("شركة البتراء", "Petra"),
            Usd, [CurrencyCode.Create("SYP")], active);

    [Fact]
    public void ArabicNamesAndCodesRemainValidUnicode()
    {
        var company = Company();
        var branch = Branch.Create(company, EntityCode.Create("فرع-١"), BilingualName.Create("دمشق – المَزّة"));
        var scope = BranchScope.Create(company, branch);
        Assert.Equal("شركة البتراء", company.Name.Arabic);
        Assert.Equal("دمشق – المَزّة", branch.Name.Arabic);
        Assert.Equal(company.Id, scope.CompanyId);
        Assert.Equal(branch.Id, scope.BranchId);
        Assert.NotEqual(Guid.Empty, company.Id);
        Assert.Equal(7, company.Id.Version);
    }

    [Fact]
    public void BranchScopeRejectsAnotherCompany()
    {
        var company = Company();
        var other = Company("other");
        var branch = Branch.Create(other, EntityCode.Create("B1"), BilingualName.Create("فرع"));
        Assert.Throws<ArgumentException>(() => BranchScope.Create(company, branch));
    }

    [Fact]
    public void InactiveCompanyOrBranchCannotProducePostingScope()
    {
        var inactive = Company(active: false);
        var active = Company();
        var branch = Branch.Create(active, EntityCode.Create("B1"), BilingualName.Create("فرع"), false);
        Assert.Throws<InvalidOperationException>(() => BranchScope.Create(active, branch));
        var inactiveBranch = Branch.Create(inactive, EntityCode.Create("B2"), BilingualName.Create("فرع"));
        Assert.Throws<InvalidOperationException>(() => BranchScope.Create(inactive, inactiveBranch));
    }

    [Fact]
    public void CurrencyConfigurationIsExplicitAndCopied()
    {
        var additional = new List<CurrencyCode> { CurrencyCode.Create("SYP") };
        var company = Organizations.Company.Create(EntityCode.Create("P"), BilingualName.Create("شركة"), Usd, additional);
        additional.Clear();
        Assert.True(company.SupportsCurrency(Usd));
        Assert.True(company.SupportsCurrency(CurrencyCode.Create("SYP")));
        Assert.False(company.SupportsCurrency(CurrencyCode.Create("EUR")));
        Assert.Throws<ArgumentException>(() => Organizations.Company.Create(EntityCode.Create("P"), BilingualName.Create("شركة"), Usd, [Usd]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("name\nline")]
    public void NamesRejectMissingControlOrMalformedUnicode(string value)
    {
        Assert.Throws<ArgumentException>(() => BilingualName.Create(value));
    }

    [Fact]
    public void InvalidRuntimeSurrogatesAreRejectedWhileValidPairsArePreserved()
    {
        var invalid = new string((char)0xD800, 1);
        Assert.Throws<ArgumentException>(() => BilingualName.Create(invalid));
        Assert.Equal("شركة 😀", BilingualName.Create("شركة 😀").Arabic);
    }
}
