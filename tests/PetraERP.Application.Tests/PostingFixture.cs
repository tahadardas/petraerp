using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Organizations;
using PetraERP.Domain.Primitives;

namespace PetraERP.Application.Tests;

internal sealed class PostingFixture
{
    private readonly Company company;
    private readonly Account debit;
    private readonly Account credit;
    private readonly FiscalYear year;

    internal PostingFixture()
    {
        company = Company.Create(EntityCode.Create("PETRA"), BilingualName.Create("شركة البتراء"), CurrencyCode.Create("USD"), []);
        BranchScope = BranchScope.Create(company, Branch.Create(company, EntityCode.Create("DMS"), BilingualName.Create("دمشق")));
        Scope = PostingRequestScope.Create(company.Id, BranchScope.BranchId, Guid.NewGuid());
        var range = DateRange.Create(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        year = FiscalYear.Create(company, EntityCode.Create("FY26"), range,
            [PeriodDefinition.Create(EntityCode.Create("ALL"), range)]);
        debit = Account.Create(company, EntityCode.Create("1101"), BilingualName.Create("الصندوق"),
            AccountType.Assets, true, CostCenterPolicy.Optional, AccountCurrencyPolicy.BaseOnly());
        credit = Account.Create(company, EntityCode.Create("3101"), BilingualName.Create("رأس المال"),
            AccountType.Equity, true, CostCenterPolicy.Optional, AccountCurrencyPolicy.BaseOnly());
        Validation = JournalValidationContext.Create(company, BranchScope, year, [debit, credit], []);
    }

    internal BranchScope BranchScope { get; }
    internal PostingRequestScope Scope { get; }
    internal JournalValidationContext Validation { get; }
    internal DateOnly Date { get; } = new(2026, 10, 15);
    internal AuditReason Reason { get; } = AuditReason.Create("ترحيل قيد معتمد");
    internal static ExpectedJournalVersion Version => ExpectedJournalVersion.Create([0, 0, 0, 0, 0, 0, 0, 1]);

    internal JournalActionContext Action() => JournalActionContext.Create(Scope.ActorUserId, Guid.NewGuid(), Reason.Value,
        new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    internal JournalPostingNumber Number(long sequence) => JournalPostingNumber.Create(BranchScope, year, sequence);

    internal JournalEntry Draft() => JournalEntry.CreateManualDraft(BranchScope, Date, "قيد رأس المال",
        [Line(debit, 100m, 0m), Line(credit, 0m, 100m)], Action());

    internal PostingRequest Request(JournalEntry entry) => PostingRequest.Post(Scope, Guid.NewGuid(), entry.Id, Version, Reason);

    internal JournalEntry Post(JournalEntry draft) => draft.Validate(Validation, Action()).Approve(Action()).Post(Validation, Number(1), Action());

    private JournalLine Line(Account account, decimal debitAmount, decimal creditAmount) => JournalLine.Create(company,
        account.Id, null, JournalLineAmounts.Create(Money.Create(debitAmount, company.BaseCurrency), Money.Create(creditAmount, company.BaseCurrency)),
        ExchangeRate.Create(company.BaseCurrency, company.BaseCurrency, 1m), MidpointRounding.ToEven, "سطر محاسبي");
}
