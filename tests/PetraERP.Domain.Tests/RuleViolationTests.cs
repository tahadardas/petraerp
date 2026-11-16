using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Tests;

/// <summary>Stable rule codes travel with the unchanged exception types so outer layers never parse messages.</summary>
public sealed class RuleViolationTests
{
    private static readonly CurrencyCode Usd = CurrencyCode.Create("USD");
    private static readonly CurrencyCode Eur = CurrencyCode.Create("EUR");

    private static Account Control(PetraERP.Domain.Organizations.Company company, string code = "11", AccountType type = AccountType.Assets) =>
        Account.Create(company, EntityCode.Create(code), BilingualName.Create("أصول متداولة"), type, false, CostCenterPolicy.Optional, AccountCurrencyPolicy.BaseOnly());

    private static Account Posting(PetraERP.Domain.Organizations.Company company, Account? parent = null, CostCenterPolicy cost = CostCenterPolicy.Optional) =>
        Account.Create(company, EntityCode.Create("1101"), BilingualName.Create("الصندوق"), AccountType.Assets, true, cost, AccountCurrencyPolicy.BaseOnly(), parent);

    private static string? Code(Action action) => RuleViolation.CodeOf(Assert.ThrowsAny<Exception>(action));

    [Fact]
    public void PostingRuleViolationsCarryTheirCodes()
    {
        var company = OrganizationTests.Company();
        var center = CostCenter.Create(company, EntityCode.Create("C1"), BilingualName.Create("مركز"));
        Assert.Equal(RuleViolation.CostCenterRequired, Code(() => Posting(company, cost: CostCenterPolicy.Required).ValidatePostingRules(company, Usd, null)));
        Assert.Equal(RuleViolation.CostCenterNotAllowed, Code(() => Posting(company, cost: CostCenterPolicy.NotAllowed).ValidatePostingRules(company, Usd, center)));
        Assert.Equal(RuleViolation.AccountInvalid, Code(() => Control(company).ValidatePostingRules(company, Usd, null)));
        Assert.Equal(RuleViolation.CostCenterInvalid, Code(() => Posting(company).ValidatePostingRules(company, Usd, center.WithActivation(false))));
        Assert.Equal(RuleViolation.CurrencyInvalid, Code(() => Posting(company).ValidatePostingRules(company, Eur, null)));
        Assert.IsType<InvalidOperationException>(Assert.ThrowsAny<Exception>(() => Control(company).ValidatePostingRules(company, Usd, null)));
    }

    [Fact]
    public void ClosedPeriodAndInvalidTransitionCarryTheirCodes()
    {
        var company = OrganizationTests.Company();
        var year = FiscalYear.Create(company, EntityCode.Create("FY2026"), DateRange.Create(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            [PeriodDefinition.Create(EntityCode.Create("P01"), DateRange.Create(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)))]);
        var context = PeriodTransitionContext.Create(Guid.NewGuid(), Guid.NewGuid(), "إقفال", DateTimeOffset.UtcNow);
        var closed = year.ClosePeriod(company, year.Periods[0].Id, context).UpdatedYear;
        var branch = PetraERP.Domain.Organizations.Branch.Create(company, EntityCode.Create("HQ"), BilingualName.Create("المقر"));
        var scope = PetraERP.Domain.Organizations.BranchScope.Create(company, branch);
        Assert.Equal(RuleViolation.PeriodClosed, Code(() => closed.GetOpenPeriodForPosting(scope, new DateOnly(2026, 1, 10))));
        Assert.Equal(RuleViolation.PeriodTransitionInvalid, Code(() => closed.SoftClosePeriod(company, year.Periods[0].Id, context)));
        Assert.Equal(AccountingPeriodState.Open, closed.ReopenPeriod(company, year.Periods[0].Id, context).UpdatedYear.Periods[0].State);
    }

    [Fact]
    public void AccountHierarchyRejectsCyclesInvalidParentsAndTypeMismatch()
    {
        var company = OrganizationTests.Company();
        var root = Control(company);
        var child = Account.Create(company, EntityCode.Create("111"), BilingualName.Create("نقدية"), AccountType.Assets, false, CostCenterPolicy.Optional,
            AccountCurrencyPolicy.BaseOnly(), root);
        var none = new AccountUsage(false, false, false);
        var withChildren = new AccountUsage(false, true, true);

        // Moving the root under its own child is a cycle.
        Assert.Equal(RuleViolation.AccountHierarchyCycle, Code(() => root.Revise(company, root.Code, root.Name, root.Type, false, root.CostCenterPolicy,
            root.CurrencyPolicy, child, [root.Id], withChildren)));
        Assert.Equal(RuleViolation.AccountHierarchyCycle, Code(() => root.Revise(company, root.Code, root.Name, root.Type, false, root.CostCenterPolicy,
            root.CurrencyPolicy, root, [], withChildren)));
        var postingParent = Posting(company);
        Assert.Equal(RuleViolation.AccountParentInvalid, Code(() => child.Revise(company, child.Code, child.Name, child.Type, false, child.CostCenterPolicy,
            child.CurrencyPolicy, postingParent, [], none)));
        var liabilities = Control(company, "21", AccountType.Liabilities);
        Assert.Equal(RuleViolation.AccountParentInvalid, Code(() => child.Revise(company, child.Code, child.Name, child.Type, false, child.CostCenterPolicy,
            child.CurrencyPolicy, liabilities, [], none)));
        Assert.Equal(RuleViolation.AccountParentInvalid, Code(() => Account.Create(company, EntityCode.Create("x"), BilingualName.Create("x"),
            AccountType.Assets, true, CostCenterPolicy.Optional, AccountCurrencyPolicy.BaseOnly(), postingParent)));
        Assert.Equal(RuleViolation.AccountHasChildren, Code(() => root.Revise(company, root.Code, root.Name, root.Type, true, root.CostCenterPolicy,
            root.CurrencyPolicy, null, [], withChildren)));
        Assert.Equal(RuleViolation.AccountHasChildren, Code(() => root.Revise(company, root.Code, root.Name, AccountType.Liabilities, false,
            root.CostCenterPolicy, root.CurrencyPolicy, null, [], withChildren)));

        var renamed = child.Revise(company, child.Code, BilingualName.Create("النقدية والبنوك", "Cash and banks"), child.Type, false,
            child.CostCenterPolicy, child.CurrencyPolicy, root, [], none);
        Assert.Equal("النقدية والبنوك", renamed.Name.Arabic);
        Assert.Equal(root.Id, renamed.ParentId);
    }

    [Fact]
    public void AccountsWithHistoryKeepTheirAccountingMeaning()
    {
        var company = OrganizationTests.Company();
        var account = Posting(company);
        var used = new AccountUsage(true, false, false);
        Assert.Equal(RuleViolation.AccountInUse, Code(() => account.Revise(company, EntityCode.Create("1102"), account.Name, account.Type, true,
            account.CostCenterPolicy, account.CurrencyPolicy, null, [], used)));
        Assert.Equal(RuleViolation.AccountInUse, Code(() => account.Revise(company, account.Code, account.Name, AccountType.Expenses, true,
            account.CostCenterPolicy, account.CurrencyPolicy, null, [], used)));
        Assert.Equal(RuleViolation.AccountInUse, Code(() => account.Revise(company, account.Code, account.Name, account.Type, false,
            account.CostCenterPolicy, account.CurrencyPolicy, null, [], used)));
        Assert.Equal(RuleViolation.AccountInUse, Code(() => account.Revise(company, account.Code, account.Name, account.Type, true,
            account.CostCenterPolicy, AccountCurrencyPolicy.AnyEnabled(), null, [], used)));
        Assert.Equal("الصندوق الرئيسي", account.Revise(company, account.Code, BilingualName.Create("الصندوق الرئيسي"), account.Type, true,
            CostCenterPolicy.Required, account.CurrencyPolicy, null, [], used).Name.Arabic);
        Assert.Equal(RuleViolation.CurrencyInvalid, Code(() => account.Revise(company, account.Code, account.Name, account.Type, true,
            account.CostCenterPolicy, AccountCurrencyPolicy.Fixed(Eur), null, [], new AccountUsage(false, false, false))));
    }

    [Fact]
    public void ActivationRespectsChildrenAndParents()
    {
        var company = OrganizationTests.Company();
        var root = Control(company);
        Assert.Equal(RuleViolation.AccountHasChildren, Code(() => root.WithActivation(false, null, new AccountUsage(false, true, true))));
        Assert.False(root.WithActivation(false, null, new AccountUsage(true, true, false)).IsActive);
        var inactiveRoot = root.WithActivation(false, null, new AccountUsage(false, false, false));
        var child = Account.Reconstitute(Guid.NewGuid(), company.Id, EntityCode.Create("111"), BilingualName.Create("نقدية"), AccountType.Assets,
            true, CostCenterPolicy.Optional, AccountCurrencyPolicy.BaseOnly(), root.Id, false);
        Assert.Equal(RuleViolation.AccountParentInvalid, Code(() => child.WithActivation(true, inactiveRoot, new AccountUsage(false, false, false))));
        Assert.True(child.WithActivation(true, root, new AccountUsage(false, false, false)).IsActive);
    }

    [Fact]
    public void CostCenterCodeIsFixedOnceUsed()
    {
        var company = OrganizationTests.Company();
        var center = CostCenter.Create(company, EntityCode.Create("C1"), BilingualName.Create("مركز دمشق"));
        Assert.Equal(RuleViolation.CostCenterInUse, Code(() => center.Revise(EntityCode.Create("C2"), center.Name, hasJournalLines: true)));
        Assert.Equal("C2", center.Revise(EntityCode.Create("C2"), center.Name, hasJournalLines: false).Code.Value);
        Assert.Equal("مركز حلب", center.Revise(center.Code, BilingualName.Create("مركز حلب"), true).Name.Arabic);
        Assert.False(center.WithActivation(false).IsActive);
    }
}
