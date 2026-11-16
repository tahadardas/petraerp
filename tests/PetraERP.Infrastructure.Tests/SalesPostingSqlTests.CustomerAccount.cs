using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Sales;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.Parties;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// The invoice-level account: accepted only where the pattern permits a document override, carried by the journal, met again by
/// the return, and without effect on the customer's balance.
/// </summary>
public sealed partial class SalesPostingSqlTests
{
    private static async Task<SalesInvoiceDetailResult> UpdateAccountAsync(PetraERP.Infrastructure.Sales.SqlSalesInvoiceService service, LedgerScenario s,
        Fixture fixture, PaymentMethod paymentMethod, Guid? customerAccountId)
    {
        var existing = await service.GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
        return await service.UpdateInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, new UpdateSalesInvoiceCommand(existing.CustomerId, existing.WarehouseId,
            existing.InvoiceDateUtc, paymentMethod, "SYP", 0.01m, null, null,
            [new(fixture.ItemId, fixture.UnitId, 2m, 100m, DiscountAmount: 20m, TaxAmount: 18m, PumpCost: 5m, TransportCost: 7m)])
        {
            ExpectedVersion = ConcurrencyToken.FromBytes(existing.RowVersion),
            CustomerAccountId = customerAccountId,
        }, Cancellation).ConfigureAwait(true);
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ACashInvoicePostsToItsOwnCashAccountWhenThePatternPermitsAndItsReturnGoesBackThroughTheSameAccount()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database, PaymentMethod.Cash, allowAccountOverride: true).ConfigureAwait(true); var s = fixture.Scenario;
            var branchCash = await s.AccountAsync(s.CompanyA, "1012", AccountTypeCode.Assets, true).ConfigureAwait(true);
            using var context = database.Context();
            var service = Service(context);

            // Naming the pattern's own cash account is not an override: nothing is stored.
            var unchanged = await UpdateAccountAsync(service, s, fixture, PaymentMethod.Cash, fixture.CashId).ConfigureAwait(true);
            Assert.Null(unchanged.CustomerAccountId);

            var draft = await UpdateAccountAsync(service, s, fixture, PaymentMethod.Cash, branchCash.Id).ConfigureAwait(true);
            Assert.Equal(branchCash.Id, draft.CustomerAccountId);

            var posted = await service.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new PostSalesInvoiceCommand("account-override-post", "ترحيل إلى صندوق الفرع"), Cancellation).ConfigureAwait(true);
            Assert.Equal(SalesInvoiceStatus.Posted, posted.Status);
            Assert.Equal(branchCash.Id, posted.CustomerAccountId);

            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
            Assert.Equal(posted.GrandTotal, lines.Where(l => l.AccountId == branchCash.Id).Sum(l => l.TransactionDebit - l.TransactionCredit));
            Assert.DoesNotContain(lines, l => l.AccountId == fixture.CashId); // the pattern's cash account is untouched

            var returned = await service.ReturnInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new CreateSalesReturnCommand("account-override-return", "مرتجع قطعة", [new SalesReturnLineCommand(posted.Lines[0].Id, 1m)]), Cancellation).ConfigureAwait(true);
            Assert.Equal(branchCash.Id, returned.CustomerAccountId);
            var returnLines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == returned.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(returnLines.Sum(l => l.BaseDebit), returnLines.Sum(l => l.BaseCredit));
            Assert.True(returnLines.Where(l => l.AccountId == branchCash.Id).Sum(l => l.TransactionCredit) > 0m); // the refund leaves the same cash box
            Assert.DoesNotContain(returnLines, l => l.AccountId == fixture.CashId);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AnInvoiceAccountIsRefusedWhenThePatternEnforcesItsAccountOrTheAccountCannotBePostedTo()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database, PaymentMethod.Cash).ConfigureAwait(true); var s = fixture.Scenario; // mappings are enforced by default
            var branchCash = await s.AccountAsync(s.CompanyA, "1012", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var heading = await s.AccountAsync(s.CompanyA, "1019", AccountTypeCode.Assets, false).ConfigureAwait(true);
            using var context = database.Context();
            var service = Service(context);

            var enforced = await Assert.ThrowsAsync<BusinessRuleException>(() => UpdateAccountAsync(service, s, fixture, PaymentMethod.Cash, branchCash.Id)).ConfigureAwait(true);
            Assert.Equal("sales.customer_account_override_not_allowed", enforced.Code);
            var notPosting = await Assert.ThrowsAsync<BusinessRuleException>(() => UpdateAccountAsync(service, s, fixture, PaymentMethod.Cash, heading.Id)).ConfigureAwait(true);
            Assert.Equal("sales.customer_account_not_postable", notPosting.Code);
            var unknown = await Assert.ThrowsAsync<BusinessRuleException>(() => UpdateAccountAsync(service, s, fixture, PaymentMethod.Cash, Guid.NewGuid())).ConfigureAwait(true);
            Assert.Equal("sales.customer_account_invalid", unknown.Code);

            // Nothing was stored by the refused attempts, and the invoice still posts to the pattern's cash account.
            context.ChangeTracker.Clear();
            var stored = await service.GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Null(stored.CustomerAccountId);
            var posted = await service.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new PostSalesInvoiceCommand("account-enforced-post", "ترحيل"), Cancellation).ConfigureAwait(true);
            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(posted.GrandTotal, lines.Where(l => l.AccountId == fixture.CashId).Sum(l => l.TransactionDebit - l.TransactionCredit));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ACreditInvoiceOnItsOwnReceivableAccountStillCountsInTheCustomersBalance()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database, PaymentMethod.Credit, allowAccountOverride: true).ConfigureAwait(true); var s = fixture.Scenario;
            var projectReceivable = await s.AccountAsync(s.CompanyA, "1132", AccountTypeCode.Assets, true).ConfigureAwait(true);
            using var context = database.Context();
            var service = Service(context);

            var draft = await UpdateAccountAsync(service, s, fixture, PaymentMethod.Credit, projectReceivable.Id).ConfigureAwait(true);
            Assert.Equal(projectReceivable.Id, draft.CustomerAccountId);
            var posted = await service.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new PostSalesInvoiceCommand("account-credit-post", "ترحيل على حساب المشروع"), Cancellation).ConfigureAwait(true);

            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(posted.GrandTotal, lines.Where(l => l.AccountId == projectReceivable.Id).Sum(l => l.TransactionDebit - l.TransactionCredit));
            Assert.DoesNotContain(lines, l => l.AccountId == fixture.ReceivableId);

            // The balance follows the document, not the account: the customer owes the invoice whichever account carries it.
            var controlBase = lines.Where(l => l.AccountId == projectReceivable.Id).Sum(l => l.BaseDebit - l.BaseCredit);
            var exposure = await new SqlPartyService(context).GetPartyCreditExposureAsync(s.CompanyA, posted.CustomerId, Cancellation).ConfigureAwait(true);
            Assert.Equal(controlBase, exposure.CurrentBalance);
            Assert.True(exposure.CurrentBalance > 0m);
        }
    }
}
