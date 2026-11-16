using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Purchases;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Purchases;
using PetraERP.Domain.Sales;

namespace PetraERP.Infrastructure.Tests;

/// <summary>Purchase discounts (received) and additions: totals, the balanced journal, counter accounts and the guards.</summary>
public sealed partial class SalesPostingSqlTests
{
    [Theory(Explicit = true)]
    [InlineData(PaymentMethod.Credit)]
    [InlineData(PaymentMethod.Cash)]
    [Trait("Category", "SqlServer")]
    public async Task PurchaseDiscountAndAdditionRowsFeedTheTotalsAndPostABalancedJournal(PaymentMethod method)
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var p = await PurchaseSetupAsync(fixture).ConfigureAwait(true);
            var earned = await s.AccountAsync(s.CompanyA, "4141", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            var freight = await s.AccountAsync(s.CompanyA, "5141", AccountTypeCode.Expenses, true).ConfigureAwait(true);
            var internalDebit = await s.AccountAsync(s.CompanyA, "5151", AccountTypeCode.Expenses, true).ConfigureAwait(true);
            var internalCredit = await s.AccountAsync(s.CompanyA, "2151", AccountTypeCode.Liabilities, true).ConfigureAwait(true);
            using var context = database.Context();
            var service = Purchases(context);

            var draft = await service.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, CementPurchase(p, method, 2.5m) with
            {
                Adjustments =
                [
                    new(SalesInvoiceAdjustmentKind.Discount, earned.Id, 4000m, 0m, "حسم مكتسب"),
                    new(SalesInvoiceAdjustmentKind.Addition, freight.Id, 6000m, 0m, "أجور شحن"),
                    new(SalesInvoiceAdjustmentKind.Addition, internalDebit.Id, 500m, CounterAccountId: internalCredit.Id),
                ],
            }, Cancellation).ConfigureAwait(true);

            // Lines alone: 250000 - 1000 + 10000 = 259000. Rows: -4000, +6000; the counter row stays outside the totals.
            Assert.Equal(3, draft.Adjustments.Count);
            Assert.Equal(5000m, draft.DiscountTotal);
            Assert.Equal(6000m, draft.AdditionsTotal);
            Assert.Equal(261000m, draft.GrandTotal);

            var posted = await service.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor, new PostPurchaseInvoiceCommand(ConcurrencyToken.FromBytes(draft.RowVersion), "purchase-adj-1"), Cancellation).ConfigureAwait(true);
            Assert.Equal(PurchaseInvoiceStatus.Posted, posted.Status);
            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
            var settlementAccount = method == PaymentMethod.Cash ? p.Cash : p.Payable;
            Assert.Equal(261000m, lines.Where(l => l.AccountId == settlementAccount).Sum(l => l.TransactionCredit - l.TransactionDebit));
            Assert.Equal(4000m, Assert.Single(lines, l => l.AccountId == earned.Id).TransactionCredit);
            Assert.Equal(6000m, Assert.Single(lines, l => l.AccountId == freight.Id).TransactionDebit);
            Assert.Equal(500m, Assert.Single(lines, l => l.AccountId == internalDebit.Id).TransactionDebit);
            Assert.Equal(500m, Assert.Single(lines, l => l.AccountId == internalCredit.Id).TransactionCredit);

            if (method == PaymentMethod.Credit)
                Assert.Equal(261000m, (await service.GetAsync(s.CompanyA, s.BranchA1, draft.Id, Cancellation).ConfigureAwait(true)).OutstandingAmount);

            await AssertRuleAsync("purchases.return_with_adjustments_unsupported", () => service.ReturnAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                new("purchase-adj-return", "مرتجع", [new(posted.Lines[0].Id, 1m)]), Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PurchaseAdjustmentRowsAreValidatedAndReplacedOnUpdate()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var p = await PurchaseSetupAsync(fixture).ConfigureAwait(true);
            var header = await s.AccountAsync(s.CompanyA, "6001", AccountTypeCode.Expenses, false).ConfigureAwait(true);
            var good = await s.AccountAsync(s.CompanyA, "5141", AccountTypeCode.Expenses, true).ConfigureAwait(true);

            async Task<string> CodeOf(params PurchaseAdjustmentCommand[] rows)
            {
                using var requestContext = database.Context();
                return (await Assert.ThrowsAsync<BusinessRuleException>(() => Purchases(requestContext).CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                    CementPurchase(p, PaymentMethod.Credit, 1m) with { Adjustments = rows }, Cancellation)).ConfigureAwait(true)).Code;
            }

            Assert.Equal("purchases.adjustment_account_invalid", await CodeOf(new PurchaseAdjustmentCommand(SalesInvoiceAdjustmentKind.Addition, header.Id, 5m)).ConfigureAwait(true));
            Assert.Equal("purchases.adjustment_amount_invalid", await CodeOf(new PurchaseAdjustmentCommand(SalesInvoiceAdjustmentKind.Addition, good.Id, 0m)).ConfigureAwait(true));
            Assert.Equal("purchases.adjustment_counter_account_invalid", await CodeOf(new PurchaseAdjustmentCommand(SalesInvoiceAdjustmentKind.Addition, good.Id, 5m, CounterAccountId: good.Id)).ConfigureAwait(true));
            Assert.Equal("purchases.adjustments_exceed_total", await CodeOf(new PurchaseAdjustmentCommand(SalesInvoiceAdjustmentKind.Discount, good.Id, 500000m)).ConfigureAwait(true));

            using var context = database.Context();
            var service = Purchases(context);
            var draft = await service.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, CementPurchase(p, PaymentMethod.Credit, 1m) with
            { Adjustments = [new(SalesInvoiceAdjustmentKind.Addition, good.Id, 10m)] }, Cancellation).ConfigureAwait(true);
            Assert.Equal(10m, draft.AdditionsTotal);
            using var updateContext = database.Context();
            var updated = await Purchases(updateContext).UpdateDraftAsync(s.CompanyA, s.BranchA1, draft.Id,
                CementPurchase(p, PaymentMethod.Credit, 1m) with { ExpectedVersion = ConcurrencyToken.FromBytes(draft.RowVersion), Adjustments = [] }, Cancellation).ConfigureAwait(true);
            Assert.Empty(updated.Adjustments);
            Assert.Equal(0m, updated.AdditionsTotal);
            Assert.Equal(updated.SubTotal - updated.DiscountTotal + updated.TaxTotal, updated.GrandTotal);
        }
    }
}
