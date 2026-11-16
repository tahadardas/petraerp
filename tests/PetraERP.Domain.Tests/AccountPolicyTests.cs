using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Organizations;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Tests;

public sealed class AccountPolicyTests
{
    private static readonly CurrencyCode Usd = CurrencyCode.Create("USD");
    private static readonly CurrencyCode Syp = CurrencyCode.Create("SYP");

    private static Account Create(Company company, bool posting = true, CostCenterPolicy cost = CostCenterPolicy.Optional,
        AccountCurrencyPolicy? currency = null, Account? parent = null, bool active = true, AccountType type = AccountType.Assets) =>
        Account.Create(company, EntityCode.Create("1101"), BilingualName.Create("الصندوق", "Cash"), type,
            posting, cost, currency ?? AccountCurrencyPolicy.BaseOnly(), parent, active);

    [Fact]
    public void RequiredCostCenterCannotBeOmittedOrBorrowedFromAnotherCompany()
    {
        var company = OrganizationTests.Company();
        var account = Create(company, cost: CostCenterPolicy.Required);
        Assert.Throws<InvalidOperationException>(() => account.ValidatePostingRules(company, Usd, null));
        var foreign = CostCenter.Create(OrganizationTests.Company("OTHER"), EntityCode.Create("C"), BilingualName.Create("مركز"));
        Assert.Throws<ArgumentException>(() => account.ValidatePostingRules(company, Usd, foreign));
        var center = CostCenter.Create(company, EntityCode.Create("C"), BilingualName.Create("مركز دمشق"));
        account.ValidatePostingRules(company, Usd, center);
    }

    [Fact]
    public void OptionalAndForbiddenCostCentersHaveDifferentRules()
    {
        var company = OrganizationTests.Company();
        var center = CostCenter.Create(company, EntityCode.Create("C"), BilingualName.Create("مركز"));
        Create(company).ValidatePostingRules(company, Usd, null);
        Create(company).ValidatePostingRules(company, Usd, center);
        var forbidden = Create(company, cost: CostCenterPolicy.NotAllowed);
        forbidden.ValidatePostingRules(company, Usd, null);
        Assert.Throws<InvalidOperationException>(() => forbidden.ValidatePostingRules(company, Usd, center));
    }

    [Fact]
    public void InactiveAndControlAccountsRejectPostingRulesValidation()
    {
        var company = OrganizationTests.Company();
        Assert.Throws<InvalidOperationException>(() => Create(company, posting: false).ValidatePostingRules(company, Usd, null));
        Assert.Throws<InvalidOperationException>(() => Create(company, active: false).ValidatePostingRules(company, Usd, null));
        var center = CostCenter.Create(company, EntityCode.Create("C"), BilingualName.Create("مركز"), false);
        Assert.Throws<InvalidOperationException>(() => Create(company).ValidatePostingRules(company, Usd, center));
    }

    [Fact]
    public void CurrencyPoliciesUseTheCompanyConfiguration()
    {
        var company = OrganizationTests.Company();
        Assert.Throws<InvalidOperationException>(() => Create(company).ValidatePostingRules(company, Syp, null));
        Create(company, currency: AccountCurrencyPolicy.AnyEnabled()).ValidatePostingRules(company, Syp, null);
        var fixedAccount = Create(company, currency: AccountCurrencyPolicy.Fixed(Syp));
        fixedAccount.ValidatePostingRules(company, Syp, null);
        Assert.Throws<InvalidOperationException>(() => fixedAccount.ValidatePostingRules(company, Usd, null));
        Assert.Throws<InvalidOperationException>(() => Create(company, currency: AccountCurrencyPolicy.AnyEnabled())
            .ValidatePostingRules(company, CurrencyCode.Create("EUR"), null));
        Assert.Throws<InvalidOperationException>(() => Create(company, currency: AccountCurrencyPolicy.Fixed(CurrencyCode.Create("EUR"))));
    }

    [Fact]
    public void AccountHierarchyCannotCrossCompanyOrTypeOrUsePostingParent()
    {
        var company = OrganizationTests.Company();
        var control = Create(company, posting: false);
        Assert.Equal(control.Id, Create(company, parent: control).ParentId);
        Assert.Throws<ArgumentException>(() => Create(OrganizationTests.Company("OTHER"), parent: control));
        Assert.Throws<ArgumentException>(() => Create(company, parent: control, type: AccountType.Expenses));
        Assert.Throws<ArgumentException>(() => Create(company, parent: Create(company)));
        Assert.Throws<ArgumentException>(() => Create(company, parent: Create(company, posting: false, active: false)));
    }

    [Fact]
    public void UnknownPolicyEnumsAndForeignCompanyFailClosed()
    {
        var company = OrganizationTests.Company();
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(company, cost: (CostCenterPolicy)0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(company, type: (AccountType)0));
        Assert.Throws<ArgumentException>(() => Create(company).ValidatePostingRules(OrganizationTests.Company("OTHER"), Usd, null));
    }

    [Fact]
    public void OnlyAnUnusedLeafIsDeletable()
    {
        Account.EnsureDeletable(new AccountUsage(false, false, false));
        var children = Assert.Throws<InvalidOperationException>(() => Account.EnsureDeletable(new AccountUsage(false, true, false)));
        Assert.Equal(RuleViolation.AccountHasChildren, RuleViolation.CodeOf(children));
        var used = Assert.Throws<InvalidOperationException>(() => Account.EnsureDeletable(new AccountUsage(true, false, false)));
        Assert.Equal(RuleViolation.AccountInUse, RuleViolation.CodeOf(used));
        Assert.Throws<ArgumentNullException>(() => Account.EnsureDeletable(null!));
    }
}
