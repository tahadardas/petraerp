using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Tests;

public sealed class JournalValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void IncompleteOrUnbalancedDraftCanBeSavedButCannotBeValidated(int count)
    {
        var fixture = new JournalFixture();
        var lines = new[] { fixture.Line(fixture.DebitAccount, 1m, 0m), fixture.Line(fixture.CreditAccount, 0m, 0.9999m) };
        var draft = fixture.Draft(lines.Take(count));
        Assert.Equal(count, draft.Lines.Count);
        Assert.Throws<ArgumentException>(() => draft.Validate(fixture.Validation(), fixture.Action()));
        Assert.Equal(JournalEntryState.Draft, draft.State);
    }

    [Fact]
    public void LineCurrenciesAndHistoricalRateProduceOneExactBaseBalance()
    {
        var fixture = new JournalFixture();
        var syp = CurrencyCode.Create("SYP");
        var debit = fixture.Line(fixture.DebitAccount, 200m, 0m, syp, 0.5m);
        var draft = fixture.Draft([debit, fixture.Line(fixture.CreditAccount, 0m, 100m)]);
        var validated = draft.Validate(fixture.Validation(), fixture.Action());
        Assert.Equal(JournalEntryState.Validated, validated.State);
        Assert.Equal(200m, debit.TransactionAmounts.Debit.Amount);
        Assert.Equal(syp, debit.TransactionAmounts.Debit.Currency);
        Assert.Equal(0.5m, debit.Rate.Value);
        Assert.Equal(100m, debit.BaseAmounts.Debit.Amount);
        Assert.Equal(fixture.Company.BaseCurrency, debit.BaseAmounts.Debit.Currency);
        Assert.Equal(MidpointRounding.ToEven, debit.Rounding);
    }

    [Fact]
    public void RoundingNeverSilentlyAddsAToleranceOrBalancingLine()
    {
        var fixture = new JournalFixture();
        var syp = CurrencyCode.Create("SYP");
        Assert.Throws<ArgumentException>(() => fixture.Line(fixture.DebitAccount, 0.0001m, 0m, syp, 0.5m));
        var debit = fixture.Line(fixture.DebitAccount, 0.0003m, 0m, syp, 0.5m);
        Assert.Equal(0.0002m, debit.BaseAmounts.Debit.Amount);
        var draft = fixture.Draft([debit, fixture.Line(fixture.CreditAccount, 0m, 0.0001m)]);
        Assert.Throws<ArgumentException>(() => draft.Validate(fixture.Validation(), fixture.Action()));
        Assert.Equal(2, draft.Lines.Count);
    }

    [Fact]
    public void MissingAccountAndMissingCenterRejectValidationAndPosting()
    {
        var fixture = new JournalFixture();
        var missingAccount = fixture.Validation(accounts: [fixture.DebitAccount]);
        Assert.Throws<InvalidOperationException>(() => fixture.Draft().Validate(missingAccount, fixture.Action()));
        var approved = fixture.AtState(JournalEntryState.Approved);
        Assert.Throws<InvalidOperationException>(() => approved.Post(missingAccount,
            JournalPostingNumber.Create(fixture.Scope, fixture.Year, 1), fixture.Action()));
        var missingCenter = fixture.Draft([fixture.Line(fixture.DebitAccount, 100m, 0m, centerId: Guid.NewGuid()),
            fixture.Line(fixture.CreditAccount, 0m, 100m)]);
        Assert.Throws<InvalidOperationException>(() => missingCenter.Validate(fixture.Validation(), fixture.Action()));
    }

    [Theory]
    [InlineData(CostCenterPolicy.Required, true, true)]
    [InlineData(CostCenterPolicy.Required, false, false)]
    [InlineData(CostCenterPolicy.NotAllowed, true, false)]
    [InlineData(CostCenterPolicy.NotAllowed, false, true)]
    [InlineData(CostCenterPolicy.Optional, true, true)]
    [InlineData(CostCenterPolicy.Optional, false, true)]
    public void ResolvedAccountCostCenterPoliciesAreAppliedToJournalLines(CostCenterPolicy policy, bool supplied, bool allowed)
    {
        var fixture = new JournalFixture();
        var account = Account.Create(fixture.Company, EntityCode.Create("EXP"), BilingualName.Create("مصروف"),
            AccountType.Expenses, true, policy, AccountCurrencyPolicy.BaseOnly());
        var center = CostCenter.Create(fixture.Company, EntityCode.Create("C1"), BilingualName.Create("مركز دمشق"));
        var draft = fixture.Draft([fixture.Line(account, 100m, 0m, centerId: supplied ? center.Id : null),
            fixture.Line(fixture.CreditAccount, 0m, 100m)]);
        var validation = fixture.Validation(accounts: [account, fixture.CreditAccount], centers: [center]);
        if (allowed)
        {
            Assert.Equal(JournalEntryState.Validated, draft.Validate(validation, fixture.Action()).State);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => draft.Validate(validation, fixture.Action()));
        }
    }

    [Fact]
    public void DisabledCurrenciesAndInactiveOrControlAccountsAreRejected()
    {
        var fixture = new JournalFixture();
        var eur = CurrencyCode.Create("EUR");
        var disabledCurrency = fixture.Draft([fixture.Line(fixture.DebitAccount, 100m, 0m, eur),
            fixture.Line(fixture.CreditAccount, 0m, 100m)]);
        Assert.Throws<InvalidOperationException>(() => disabledCurrency.Validate(fixture.Validation(), fixture.Action()));
        foreach (var (posting, active) in new[] { (false, true), (true, false) })
        {
            var account = Account.Create(fixture.Company, EntityCode.Create("A"), BilingualName.Create("حساب"),
                AccountType.Assets, posting, CostCenterPolicy.Optional, AccountCurrencyPolicy.BaseOnly(), isActive: active);
            var draft = fixture.Draft([fixture.Line(account, 100m, 0m), fixture.Line(fixture.CreditAccount, 0m, 100m)]);
            Assert.Throws<InvalidOperationException>(() => draft.Validate(
                fixture.Validation(accounts: [account, fixture.CreditAccount]), fixture.Action()));
        }

        var center = CostCenter.Create(fixture.Company, EntityCode.Create("OFF"), BilingualName.Create("مغلق"), false);
        var inactiveCenter = fixture.Draft([fixture.Line(fixture.DebitAccount, 100m, 0m, centerId: center.Id),
            fixture.Line(fixture.CreditAccount, 0m, 100m)]);
        Assert.Throws<InvalidOperationException>(() => inactiveCenter.Validate(fixture.Validation(centers: [center]), fixture.Action()));
    }

    [Fact]
    public void InvalidLineIdentityRateDirectionAndOverflowFailBeforeDraftCreation()
    {
        var fixture = new JournalFixture();
        var usd = fixture.Company.BaseCurrency;
        var syp = CurrencyCode.Create("SYP");
        var amounts = JournalLineAmounts.Create(Money.Create(1m, usd), Money.Create(0m, usd));
        var rate = ExchangeRate.Create(usd, usd, 1m);
        Assert.Throws<ArgumentException>(() => JournalLine.Create(fixture.Company, Guid.Empty, null, amounts, rate, MidpointRounding.ToEven, "سطر"));
        Assert.Throws<ArgumentException>(() => JournalLine.Create(fixture.Company, fixture.DebitAccount.Id, Guid.Empty, amounts, rate, MidpointRounding.ToEven, "سطر"));
        Assert.Throws<ArgumentException>(() => JournalLine.Create(fixture.Company, fixture.DebitAccount.Id, null, amounts,
            ExchangeRate.Create(usd, syp, 2m), MidpointRounding.ToEven, "سطر"));
        Assert.Throws<ArgumentException>(() => JournalLine.Create(fixture.Company, fixture.DebitAccount.Id, null, amounts,
            ExchangeRate.Create(syp, usd, 2m), MidpointRounding.ToEven, "سطر"));
        Assert.Throws<OverflowException>(() => fixture.Line(fixture.DebitAccount, FinancialPrecision.MaximumMoney, 0m, syp, 2m));
        Assert.Throws<ArgumentOutOfRangeException>(() => JournalLine.Create(fixture.Company, fixture.DebitAccount.Id, null,
            amounts, rate, (MidpointRounding)99, "سطر"));
    }
}
