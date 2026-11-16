using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Sales;
using PetraERP.Application.Treasury;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Treasury;
using PetraERP.Domain.Sales;

namespace PetraERP.Infrastructure.Tests;

/// <summary>Document-level discounts and additions: totals, the balanced journal, and the guards around returns and bad accounts.</summary>
public sealed partial class SalesPostingSqlTests
{
    [Theory(Explicit = true)]
    [InlineData(PaymentMethod.Credit)]
    [InlineData(PaymentMethod.Cash)]
    [Trait("Category", "SqlServer")]
    public async Task DiscountAndAdditionRowsFeedTheTotalsAndPostABalancedJournalThenReverseCleanly(PaymentMethod paymentMethod)
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database, paymentMethod).ConfigureAwait(true); var s = fixture.Scenario;
            var discountAccount = await s.AccountAsync(s.CompanyA, "5131", AccountTypeCode.Expenses, true).ConfigureAwait(true);
            var additionAccount = await s.AccountAsync(s.CompanyA, "4131", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            using var context = database.Context();
            var service = Service(context);

            var draft = await UpdateWithAdjustmentsAsync(service, s, fixture, paymentMethod,
                [new(SalesInvoiceAdjustmentKind.Discount, discountAccount.Id, 10m, 5m, "حسم نهاية الشهر"), new(SalesInvoiceAdjustmentKind.Addition, additionAccount.Id, 25m, 0m, "رسوم تغليف")]).ConfigureAwait(true);

            // Lines alone: 200 - 20 + 18 + 12 = 210. Rows: -10 and +25 → discounts 30, additions 37, grand 225.
            Assert.Equal(2, draft.Adjustments.Count);
            Assert.Equal(30m, draft.DiscountTotal);
            Assert.Equal(37m, draft.AdditionsTotal);
            Assert.Equal(225m, draft.GrandTotal);

            var posted = await service.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new PostSalesInvoiceCommand("adjustment-post-001", "ترحيل بحسم وإضافة"), Cancellation).ConfigureAwait(true);
            Assert.Equal(SalesInvoiceStatus.Posted, posted.Status);
            Assert.Equal(225m, posted.GrandTotal);

            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
            var counter = paymentMethod == PaymentMethod.Cash ? fixture.CashId : fixture.ReceivableId;
            Assert.Equal(225m, lines.Where(l => l.AccountId == counter).Sum(l => l.TransactionDebit - l.TransactionCredit));
            Assert.Equal(10m, Assert.Single(lines, l => l.AccountId == discountAccount.Id).TransactionDebit);
            Assert.Equal(25m, Assert.Single(lines, l => l.AccountId == additionAccount.Id).TransactionCredit);

            // A document with header rows is never partially returned (the rows are not apportioned yet); the guard fails closed.
            var returnError = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ReturnInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new CreateSalesReturnCommand("adjustment-return-001", "مرتجع", [new SalesReturnLineCommand(posted.Lines[0].Id, 1m)]), Cancellation)).ConfigureAwait(true);
            Assert.Equal("sales.return_with_adjustments_unsupported", returnError.Code);

            var reversed = await service.ReverseInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, new ReverseSalesInvoiceCommand("إلغاء"), Cancellation).ConfigureAwait(true);
            Assert.Equal(SalesInvoiceStatus.Reversed, reversed.Status);
            var all = await context.JournalLines.AsNoTracking().Where(l => l.AccountId == discountAccount.Id || l.AccountId == additionAccount.Id).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(0m, all.Where(l => l.AccountId == discountAccount.Id).Sum(l => l.BaseDebit - l.BaseCredit));
            Assert.Equal(0m, all.Where(l => l.AccountId == additionAccount.Id).Sum(l => l.BaseCredit - l.BaseDebit));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task TheOpenItemOfAnInvoiceWithAdjustmentsIsItsGrandTotalAndAFirstPaymentSettlesPartOfIt()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var setup = await VoucherSetupAsync(fixture).ConfigureAwait(true);
            var discountAccount = await s.AccountAsync(s.CompanyA, "5131", AccountTypeCode.Expenses, true).ConfigureAwait(true);
            var additionAccount = await s.AccountAsync(s.CompanyA, "4131", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            using var context = database.Context();
            var service = Service(context);
            await UpdateWithAdjustmentsAsync(service, s, fixture, PaymentMethod.Credit,
                [new(SalesInvoiceAdjustmentKind.Discount, discountAccount.Id, 10m), new(SalesInvoiceAdjustmentKind.Addition, additionAccount.Id, 25m)]).ConfigureAwait(true);
            await service.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, new PostSalesInvoiceCommand("adjustment-open-item"), Cancellation).ConfigureAwait(true);
            var treasury = Treasury(context);

            // The customer owes the grand total (225), not the lines alone (210).
            Assert.Equal(225m, Assert.Single(await treasury.GetOutstandingInvoicesAsync(s.CompanyA, s.BranchA1, setup.Customer, "SYP", Cancellation).ConfigureAwait(true)).Outstanding);

            // A first payment is an ordinary receipt allocated to the invoice: 100 settles part of it, the rest stays open.
            var draft = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, Receipt(setup, 100m, (fixture.InvoiceId, 100m)), Cancellation).ConfigureAwait(true);
            var posted = await treasury.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor, new PostVoucherCommand(ConcurrencyToken.FromBytes(draft.RowVersion), "first-payment-1"), Cancellation).ConfigureAwait(true);
            Assert.Equal(VoucherStatus.Posted, posted.Status);
            Assert.Equal(125m, Assert.Single(await treasury.GetOutstandingInvoicesAsync(s.CompanyA, s.BranchA1, setup.Customer, "SYP", Cancellation).ConfigureAwait(true)).Outstanding);

            // It can never settle more than what is owed.
            await AssertRuleAsync(VoucherRules.InvoiceOverAllocated, () => treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                Receipt(setup, 200m, (fixture.InvoiceId, 126m)), Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ACounterAccountRowIsBookedBetweenTwoAccountsOutsideTheCustomerAndTheTotals()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var discountAccount = await s.AccountAsync(s.CompanyA, "5131", AccountTypeCode.Expenses, true).ConfigureAwait(true);
            var counterAccount = await s.AccountAsync(s.CompanyA, "5141", AccountTypeCode.Expenses, true).ConfigureAwait(true);
            using var context = database.Context();
            var service = Service(context);

            var draft = await UpdateWithAdjustmentsAsync(service, s, fixture, PaymentMethod.Credit,
                [new(SalesInvoiceAdjustmentKind.Discount, discountAccount.Id, 15m, CounterAccountId: counterAccount.Id)]).ConfigureAwait(true);
            Assert.Equal(20m, draft.DiscountTotal);   // the line discount only
            Assert.Equal(210m, draft.GrandTotal);

            using var other = database.Context();
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => UpdateWithAdjustmentsAsync(Service(other), s, fixture, PaymentMethod.Credit,
                [new(SalesInvoiceAdjustmentKind.Discount, discountAccount.Id, 5m, CounterAccountId: discountAccount.Id)])).ConfigureAwait(true);
            Assert.Equal("sales.adjustment_counter_account_invalid", error.Code);

            var posted = await service.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, new PostSalesInvoiceCommand("counter-post-001"), Cancellation).ConfigureAwait(true);
            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
            Assert.Equal(210m, lines.Where(l => l.AccountId == fixture.ReceivableId).Sum(l => l.TransactionDebit - l.TransactionCredit));
            Assert.Equal(15m, lines.Where(l => l.AccountId == counterAccount.Id).Sum(l => l.TransactionCredit));
            Assert.Equal(15m, lines.Where(l => l.AccountId == discountAccount.Id).Sum(l => l.TransactionDebit));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AdjustmentRowsAreValidatedAgainstTheChartAndTheInvoiceTotal()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var header = await s.AccountAsync(s.CompanyA, "6000", AccountTypeCode.Expenses, false).ConfigureAwait(true);
            var good = await s.AccountAsync(s.CompanyA, "5131", AccountTypeCode.Expenses, true).ConfigureAwait(true);
            // A fresh context per request, as in production: a rejected update must not poison the next one.
            async Task<string> CodeOf(params CreateSalesInvoiceAdjustmentCommand[] rows)
            {
                using var requestContext = database.Context();
                return (await Assert.ThrowsAsync<BusinessRuleException>(() => UpdateWithAdjustmentsAsync(Service(requestContext), s, fixture, PaymentMethod.Credit, rows)).ConfigureAwait(true)).Code;
            }

            Assert.Equal("sales.adjustment_account_invalid", await CodeOf(new CreateSalesInvoiceAdjustmentCommand(SalesInvoiceAdjustmentKind.Discount, header.Id, 5m)).ConfigureAwait(true));
            Assert.Equal("sales.adjustment_account_invalid", await CodeOf(new CreateSalesInvoiceAdjustmentCommand(SalesInvoiceAdjustmentKind.Discount, Guid.NewGuid(), 5m)).ConfigureAwait(true));
            Assert.Equal("sales.adjustment_amount_invalid", await CodeOf(new CreateSalesInvoiceAdjustmentCommand(SalesInvoiceAdjustmentKind.Discount, good.Id, 0m)).ConfigureAwait(true));
            Assert.Equal("sales.adjustments_exceed_total", await CodeOf(new CreateSalesInvoiceAdjustmentCommand(SalesInvoiceAdjustmentKind.Discount, good.Id, 500m)).ConfigureAwait(true));

            // The rows replace on every update: sending none clears them and restores the line-only total.
            using var clearContext = database.Context();
            var cleared = await UpdateWithAdjustmentsAsync(Service(clearContext), s, fixture, PaymentMethod.Credit, []).ConfigureAwait(true);
            Assert.Empty(cleared.Adjustments);
            Assert.Equal(210m, cleared.GrandTotal);
        }
    }

    private static async Task<SalesInvoiceDetailResult> UpdateWithAdjustmentsAsync(PetraERP.Infrastructure.Sales.SqlSalesInvoiceService service, PetraERP.Infrastructure.Tests.LedgerScenario s,
        Fixture fixture, PaymentMethod paymentMethod, IReadOnlyList<CreateSalesInvoiceAdjustmentCommand> adjustments)
    {
        var existing = await service.GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
        return await service.UpdateInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, new UpdateSalesInvoiceCommand(existing.CustomerId, existing.WarehouseId,
            existing.InvoiceDateUtc, paymentMethod, "SYP", 0.01m, null, null,
            [new(fixture.ItemId, fixture.UnitId, 2m, 100m, DiscountAmount: 20m, TaxAmount: 18m, PumpCost: 5m, TransportCost: 7m)])
        {
            ExpectedVersion = ConcurrencyToken.FromBytes(existing.RowVersion),
            Adjustments = adjustments,
        }, Cancellation).ConfigureAwait(true);
    }
}
