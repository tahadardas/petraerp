using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Sales;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.Sales;

namespace PetraERP.Infrastructure.Tests;

public sealed partial class SalesPostingSqlTests
{
    [Theory(Explicit = true)]
    [InlineData(PaymentMethod.Credit)]
    [InlineData(PaymentMethod.Cash)]
    [Trait("Category", "SqlServer")]
    public async Task PartialReturnsCreditCashOrReceivableAtOriginalPriceAndCostAndNeverOverReturn(PaymentMethod paymentMethod)
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database, paymentMethod).ConfigureAwait(true); var s = fixture.Scenario;
            using var context = database.Context();
            var service = Service(context);
            var posted = await service.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new PostSalesInvoiceCommand("return-sale"), Cancellation).ConfigureAwait(true);
            var sourceLine = Assert.Single(posted.Lines).Id;
            var settlement = paymentMethod == PaymentMethod.Cash ? fixture.CashId : fixture.ReceivableId;
            // A later receipt changes the current average; returns must still use the original delivery cost.
            await LedgerScenario.AddStockMovementAsync(context, Receipt(s, fixture.ItemId, fixture.UnitId, fixture.WarehouseId, 10m, 10m), Cancellation).ConfigureAwait(true);

            var request = new CreateSalesReturnCommand("return-1", "إرجاع جزئي تالف", [new(sourceLine, 0.5m)]);
            var first = await service.ReturnInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, request, Cancellation).ConfigureAwait(true);
            Assert.Equal(fixture.InvoiceId, first.ReturnOfInvoiceId);
            Assert.Equal(SalesInvoiceStatus.Posted, first.Status);
            Assert.Equal(paymentMethod, first.PaymentMethod);
            Assert.Equal(52.5m, first.GrandTotal); Assert.Equal(50m, first.SubTotal); Assert.Equal(5m, first.DiscountTotal);
            Assert.Equal(4.5m, first.TaxTotal); Assert.Equal(3m, first.AdditionsTotal);
            Assert.Equal(sourceLine, Assert.Single(first.Lines).SourceLineId);

            var journal = await context.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == first.JournalEntryId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Posted, journal.State);
            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == journal.Id).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
            var credited = Assert.Single(lines, l => l.AccountId == settlement);
            Assert.Equal(52.5m, credited.TransactionCredit); Assert.Equal(0.525m, credited.BaseCredit); Assert.Equal("SYP", credited.TransactionCurrencyCode);
            Assert.DoesNotContain(lines, l => l.AccountId == (paymentMethod == PaymentMethod.Cash ? fixture.ReceivableId : fixture.CashId));
            Assert.Equal(1m, Assert.Single(lines, l => l.AccountId == fixture.CogsId).BaseCredit);
            var movement = await context.StockMovements.AsNoTracking().SingleAsync(m => m.DocumentId == first.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal((int)StockMovementKind.SalesReturn, movement.MovementKind); Assert.Equal(SqlSalesInvoiceService.ReturnDocumentType, movement.DocumentType);
            Assert.Equal(0.5m, movement.BaseQuantity); Assert.Equal(1m, movement.TotalCost); Assert.Equal(2m, movement.UnitCost);
            Assert.Equal(first.Lines[0].Id, movement.DocumentLineId);

            // Same key and lines replays; a changed payload under the key is rejected without a second effect.
            var replay = await service.ReturnInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, request, Cancellation).ConfigureAwait(true);
            Assert.Equal(first.Id, replay.Id);
            await Assert.ThrowsAsync<PostingIdempotencyConflictException>(() => service.ReturnInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                request with { Lines = [new(sourceLine, 0.25m)] }, Cancellation)).ConfigureAwait(true);
            Assert.Equal(1, await context.SalesInvoices.CountAsync(i => i.ReturnOfInvoiceId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));

            await AssertRuleAsync("sales.over_return", () => service.ReturnInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new("return-over", "إرجاع زائد", [new(sourceLine, 1.5001m)]), Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("sales.invoice_has_returns", () => service.ReverseInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new ReverseSalesInvoiceCommand("عكس كامل بعد مرتجع"), Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("sales.return_source_invalid", () => service.ReturnInvoiceAsync(s.CompanyA, s.BranchA1, first.Id, s.Actor,
                new("return-of-return", "إرجاع مرتجع", [new(first.Lines[0].Id, 0.1m)]), Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("sales.invoice_not_posted", () => service.ReverseInvoiceAsync(s.CompanyA, s.BranchA1, first.Id, s.Actor,
                new ReverseSalesInvoiceCommand("عكس مرتجع"), Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("sales.return_line_invalid", () => service.ReturnInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new("return-foreign-line", "سطر غريب", [new(Guid.NewGuid(), 0.1m)]), Cancellation)).ConfigureAwait(true);
            var foreign = await Assert.ThrowsAnyAsync<Exception>(() => service.ReturnInvoiceAsync(s.CompanyA, s.BranchA2, fixture.InvoiceId, s.Actor,
                new("return-foreign-branch", "فرع آخر", [new(sourceLine, 0.1m)]), Cancellation)).ConfigureAwait(true);
            Assert.True(foreign is ResourceNotFoundException or PostingAuthorizationException, foreign.GetType().Name);

            // The last return takes the exact remainder: all returns equal the original amounts, quantity and cost.
            var last = await service.ReturnInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new("return-2", "إرجاع الباقي", [new(sourceLine, 1.5m)]), Cancellation).ConfigureAwait(true);
            Assert.Equal(posted.GrandTotal, first.GrandTotal + last.GrandTotal);
            Assert.Equal(posted.TaxTotal, first.TaxTotal + last.TaxTotal);
            var returnMovements = await context.StockMovements.AsNoTracking().Where(m => m.DocumentType == SqlSalesInvoiceService.ReturnDocumentType
                && (m.DocumentId == first.Id || m.DocumentId == last.Id)).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(2m, returnMovements.Sum(m => m.BaseQuantity)); Assert.Equal(4m, returnMovements.Sum(m => m.TotalCost));
            var original = await service.GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal(2m, Assert.Single(original.Lines).ReturnedQuantity);
            Assert.Equal(SalesInvoiceStatus.Posted, original.Status);
            await AssertRuleAsync("sales.over_return", () => service.ReturnInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new("return-3", "لا شيء متبقٍ", [new(sourceLine, 0.0001m)]), Cancellation)).ConfigureAwait(true);

            // The settlement account and revenue net back to zero across sale and returns.
            var all = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId
                || l.JournalId == first.JournalEntryId || l.JournalId == last.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            foreach (var account in all.Select(l => l.AccountId).Distinct())
                Assert.Equal(all.Where(l => l.AccountId == account).Sum(l => l.TransactionDebit), all.Where(l => l.AccountId == account).Sum(l => l.TransactionCredit));
            var returns = await service.GetInvoicesAsync(s.CompanyA, s.BranchA1, new SalesInvoiceSearchQuery { ReturnOfInvoiceId = fixture.InvoiceId }, Cancellation).ConfigureAwait(true);
            Assert.Equal(2, returns.TotalCount); Assert.All(returns.Items, r => Assert.Equal(fixture.InvoiceId, r.ReturnOfInvoiceId));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ConcurrentReturnsCannotTogetherExceedTheSoldQuantity()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            Guid sourceLine;
            using (var context = database.Context())
            {
                var posted = await Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                    new PostSalesInvoiceCommand("concurrent-return-sale"), Cancellation).ConfigureAwait(true);
                sourceLine = Assert.Single(posted.Lines).Id;
            }

            async Task<string> ReturnAsync(string key)
            {
                using var context = database.Context();
                try
                {
                    await Service(context).ReturnInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                        new(key, "إرجاع متزامن", [new(sourceLine, 1.5m)]), Cancellation).ConfigureAwait(false);
                    return "ok";
                }
                catch (BusinessRuleException exception)
                {
                    return exception.Code;
                }
            }

            var results = await Task.WhenAll(ReturnAsync("c1"), ReturnAsync("c2"), ReturnAsync("c3")).ConfigureAwait(true);
            Assert.Single(results, r => r == "ok");
            Assert.All(results.Where(r => r != "ok"), r => Assert.Equal("sales.over_return", r));
            using (var verify = database.Context())
            {
                Assert.Equal(1.5m, await verify.SalesInvoiceLines.Where(l => l.SourceLineId == sourceLine).SumAsync(l => l.Quantity, Cancellation).ConfigureAwait(true));
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AFullyReversedInvoiceCannotAlsoBeReturned()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            using var context = database.Context();
            var service = Service(context);
            var sale = await service.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new PostSalesInvoiceCommand("reversed-sale"), Cancellation).ConfigureAwait(true);
            await service.ReverseInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new ReverseSalesInvoiceCommand("عكس كامل"), Cancellation).ConfigureAwait(true);
            await AssertRuleAsync("sales.invoice_reversed", () => service.ReturnInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId,
                s.Actor, new("after-reversal", "إرجاع بعد العكس", [new(sale.Lines[0].Id, 1m)]), Cancellation)).ConfigureAwait(true);
            Assert.Equal(0, await context.SalesInvoices.CountAsync(i => i.ReturnOfInvoiceId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));
        }
    }

    private static async Task AssertRuleAsync(string code, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(action).ConfigureAwait(true);
        Assert.Equal(code, exception.Code);
    }
}
