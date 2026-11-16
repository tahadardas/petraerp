using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Parties;
using PetraERP.Application.Patterns;
using PetraERP.Application.Sales;
using PetraERP.Application.Treasury;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Parties;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Treasury;

namespace PetraERP.Infrastructure.Tests;

public sealed partial class SalesPostingSqlTests
{
    private static SqlTreasuryVoucherService Treasury(PetraDbContext context) => new(context, new SqlPatternService(context), TimeProvider.System);

    private sealed record VoucherSetup(Guid Receipt, Guid Payment, Guid Customer, Guid Supplier, Guid Bank, Guid Payable, Guid Expense)
    {
        /// <summary>A receipt pattern that fixes the voucher date to the day it is added.</summary>
        public Guid DateLockedReceipt { get; init; }
    }

    private static async Task<VoucherSetup> VoucherSetupAsync(Fixture fixture)
    {
        var s = fixture.Scenario;
        using var context = s.Database.Context();
        foreach (var permission in new[] { "Treasury.Vouchers.View", "Treasury.Vouchers.Manage", "Treasury.Vouchers.Post", "Treasury.Vouchers.Reverse", "Treasury.Vouchers.Allocate" })
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],{permission} FROM [security].[UserRoles] WHERE [UserId]={s.Actor}", Cancellation).ConfigureAwait(true);
        var bank = await s.AccountAsync(s.CompanyA, "1021", AccountTypeCode.Assets, true).ConfigureAwait(true);
        var payable = await s.AccountAsync(s.CompanyA, "2211", AccountTypeCode.Liabilities, true).ConfigureAwait(true);
        var expense = await s.AccountAsync(s.CompanyA, "5211", AccountTypeCode.Expenses, true).ConfigureAwait(true);
        var invoice = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
        var supplier = await new SqlPartyService(context).CreatePartyAsync(s.CompanyA, new CreatePartyCommand("S1", "مورد الاختبار", null, PartyRole.Supplier,
            PayableAccountId: payable.Id), Cancellation).ConfigureAwait(true);
        var patterns = new SqlPatternService(context);
        var scope = PatternScope.Create(s.CompanyA, s.BranchA1, s.Actor);
        async Task<Guid> PatternAsync(string code, BusinessOperationKind kind, Dictionary<string, Guid> roles, bool lockDate = false)
        {
            var created = await patterns.CreatePatternAsync(scope, new CreateDocumentPatternCommand
            {
                Code = code, NameArabic = code, ShortCode = code, OperationKind = kind, BranchIds = [s.BranchA1],
                InitialConfiguration = new PatternConfiguration
                {
                    AccountMappings = roles.Select(r => new PatternAccountMapping { Role = r.Key, AccountId = r.Value }).ToList(),
                    NumberingPolicy = new() { Prefix = code + "-", MinimumDigits = 4 },
                    EntryPolicy = new() { LockDocumentDate = lockDate }
                }
            }, Cancellation).ConfigureAwait(true);
            var published = await patterns.PublishRevisionAsync(scope, created.Id, new PublishPatternRevisionCommand
            { ExpectedVersion = created.Version, RevisionId = created.ActiveRevision!.Id, Reason = "نشر" }, Cancellation).ConfigureAwait(true);
            return published.ActiveRevision!.Id;
        }
        var receipt = await PatternAsync("RV", BusinessOperationKind.CashReceipt,
            new() { ["Cash"] = fixture.CashId, ["Bank"] = bank.Id, ["Receivable"] = fixture.ReceivableId }).ConfigureAwait(true);
        var payment = await PatternAsync("PV", BusinessOperationKind.CashPayment,
            new() { ["Cash"] = fixture.CashId, ["Bank"] = bank.Id, ["Payable"] = payable.Id }).ConfigureAwait(true);
        var lockedReceipt = await PatternAsync("RL", BusinessOperationKind.CashReceipt,
            new() { ["Cash"] = fixture.CashId, ["Bank"] = bank.Id, ["Receivable"] = fixture.ReceivableId }, lockDate: true).ConfigureAwait(true);
        return new(receipt, payment, invoice.CustomerId, supplier.Id, bank.Id, payable.Id, expense.Id) { DateLockedReceipt = lockedReceipt };
    }

    private static SaveVoucherCommand Receipt(VoucherSetup v, decimal amount, params (Guid Invoice, decimal Amount)[] allocations) =>
        new(VoucherKind.Receipt, v.Receipt, v.Customer, SettlementMethod.Cash, new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero),
            "SYP", 0.01m, amount, "قبض من الزبون", allocations.Select(a => new VoucherAllocationCommand(a.Invoice, a.Amount)).ToList(), []);

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ReceiptAllocatesPartiallyPostsAtomicallyReplaysAndNeverDoubleAllocates()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var setup = await VoucherSetupAsync(fixture).ConfigureAwait(true);
            using var context = database.Context();
            await Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, new PostSalesInvoiceCommand("receipt-sale"), Cancellation).ConfigureAwait(true);
            var treasury = Treasury(context);

            var draft = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, Receipt(setup, 300m, (fixture.InvoiceId, 150m)), Cancellation).ConfigureAwait(true);
            Assert.Equal(150m, draft.AllocatedAmount); Assert.Equal(150m, draft.AdvanceAmount); Assert.Null(draft.VoucherNumber);
            var command = new PostVoucherCommand(ConcurrencyToken.FromBytes(draft.RowVersion), "receipt-1");
            var posted = await treasury.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor, command, Cancellation).ConfigureAwait(true);
            Assert.Equal(VoucherStatus.Posted, posted.Status); Assert.Equal("RV-0001", posted.VoucherNumber);
            var journal = await context.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == posted.JournalEntryId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Posted, journal.State);
            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == journal.Id).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(300m, Assert.Single(lines, l => l.AccountId == fixture.CashId).TransactionDebit);
            Assert.Equal(300m, lines.Where(l => l.AccountId == fixture.ReceivableId).Sum(l => l.TransactionCredit));
            Assert.Equal(3m, lines.Sum(l => l.BaseDebit)); Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));

            Assert.Equal(posted.JournalEntryId, (await treasury.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor, command, Cancellation).ConfigureAwait(true)).JournalEntryId);
            await AssertRuleAsync("treasury.voucher_not_draft", () => treasury.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                command with { IdempotencyKey = "receipt-other" }, Cancellation)).ConfigureAwait(true);
            Assert.Equal(60m, Assert.Single(await treasury.GetOutstandingInvoicesAsync(s.CompanyA, s.BranchA1, setup.Customer, "SYP", Cancellation).ConfigureAwait(true)).Outstanding);
            await AssertRuleAsync(VoucherRules.InvoiceOverAllocated, () => treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                Receipt(setup, 100m, (fixture.InvoiceId, 61m)), Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("treasury.rate_invalid", () => treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                Receipt(setup, 100m) with { CurrencyCode = "USD", ExchangeRate = 2m }, Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("treasury.pattern_invalid", () => treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                Receipt(setup, 100m) with { PatternRevisionId = setup.Payment }, Cancellation)).ConfigureAwait(true);

            // Two drafts may each claim the remaining 60; posting both at once lets exactly one succeed.
            var first = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, Receipt(setup, 60m, (fixture.InvoiceId, 60m)), Cancellation).ConfigureAwait(true);
            var second = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, Receipt(setup, 60m, (fixture.InvoiceId, 60m)), Cancellation).ConfigureAwait(true);
            async Task<string> PostAsync(VoucherDetailResult voucher)
            {
                using var other = database.Context();
                try
                {
                    await Treasury(other).PostAsync(s.CompanyA, s.BranchA1, voucher.Id, s.Actor,
                        new(ConcurrencyToken.FromBytes(voucher.RowVersion), $"race-{voucher.Id:N}"), Cancellation).ConfigureAwait(false);
                    return "ok";
                }
                catch (BusinessRuleException exception) { return exception.Code; }
            }
            var results = await Task.WhenAll(PostAsync(first), PostAsync(second)).ConfigureAwait(true);
            Assert.Single(results, r => r == "ok");
            Assert.Single(results, r => r == VoucherRules.InvoiceOverAllocated);
            Assert.Empty(await treasury.GetOutstandingInvoicesAsync(s.CompanyA, s.BranchA1, setup.Customer, "SYP", Cancellation).ConfigureAwait(true));

            // Reversal frees the allocation and posts a linked journal; stale versions never act.
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => treasury.ReverseAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                new(ConcurrencyToken.FromBytes(draft.RowVersion), "خطأ"), Cancellation)).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            var current = await treasury.GetAsync(s.CompanyA, s.BranchA1, draft.Id, Cancellation).ConfigureAwait(true);
            var reversed = await treasury.ReverseAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                new(ConcurrencyToken.FromBytes(current.RowVersion), "سند مكرر"), Cancellation).ConfigureAwait(true);
            Assert.Equal(VoucherStatus.Reversed, reversed.Status); Assert.NotNull(reversed.ReversalJournalEntryId);
            // The voucher keeps who created, posted and reversed it, and when (shown in the voucher window).
            context.ChangeTracker.Clear();
            var audited = (await treasury.GetAsync(s.CompanyA, s.BranchA1, draft.Id, Cancellation).ConfigureAwait(true)).Audit;
            Assert.NotNull(audited);
            Assert.NotEqual(default, audited.CreatedAtUtc);
            Assert.NotNull(audited.PostedAtUtc);
            Assert.NotNull(audited.ReversedAtUtc);
            Assert.True(audited.ReversedAtUtc >= audited.PostedAtUtc);
            var original = await context.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == posted.JournalEntryId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Reversed, original.State);
            Assert.Equal(150m, Assert.Single(await treasury.GetOutstandingInvoicesAsync(s.CompanyA, s.BranchA1, setup.Customer, "SYP", Cancellation).ConfigureAwait(true)).Outstanding);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => treasury.GetAsync(s.CompanyA, s.BranchA2, draft.Id, Cancellation)).ConfigureAwait(true);
            var byInvoice = await treasury.ListAsync(s.CompanyA, s.BranchA1, new VoucherSearchQuery(SalesInvoiceId: fixture.InvoiceId), Cancellation).ConfigureAwait(true);
            Assert.Equal(3, byInvoice.TotalCount);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PaymentDebitsSupplierAndExpenseCreditsBankAndValidatesLines()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var setup = await VoucherSetupAsync(fixture).ConfigureAwait(true);
            using var context = database.Context();
            var treasury = Treasury(context);
            var command = new SaveVoucherCommand(VoucherKind.Payment, setup.Payment, setup.Supplier, SettlementMethod.Bank,
                new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), "USD", 1m, 100m, "دفع للمورد",
                [], [new(setup.Supplier, null, 70m, Description: "دفعة مورد"), new(null, setup.Expense, 30m, Description: "مصاريف")]) { ActorUserId = s.Actor };
            await AssertRuleAsync(VoucherRules.LinesUnbalanced, () => treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                command with { Amount = 101m }, Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("treasury.supplier_invalid", () => treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                command with { Lines = [new(setup.Customer, null, 100m)] }, Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("treasury.payment_allocations", () => treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                command with { Allocations = [new(fixture.InvoiceId, 1m)] }, Cancellation)).ConfigureAwait(true);

            var draft = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, command, Cancellation).ConfigureAwait(true);
            var edited = await treasury.UpdateDraftAsync(s.CompanyA, s.BranchA1, draft.Id, command with
            { Notes = "دفع معدل", ExpectedVersion = ConcurrencyToken.FromBytes(draft.RowVersion) }, Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => treasury.UpdateDraftAsync(s.CompanyA, s.BranchA1, draft.Id, command with
            { ExpectedVersion = ConcurrencyToken.FromBytes(draft.RowVersion) }, Cancellation)).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            var posted = await treasury.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                new(ConcurrencyToken.FromBytes(edited.RowVersion), "payment-1"), Cancellation).ConfigureAwait(true);
            Assert.Equal("PV-0001", posted.VoucherNumber); Assert.Equal("دفع معدل", posted.Notes);
            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(100m, Assert.Single(lines, l => l.AccountId == setup.Bank).TransactionCredit);
            Assert.Equal(70m, Assert.Single(lines, l => l.AccountId == setup.Payable).TransactionDebit);
            Assert.Equal(30m, Assert.Single(lines, l => l.AccountId == setup.Expense).TransactionDebit);
            await AssertRuleAsync("treasury.voucher_not_draft", () => treasury.DeleteDraftAsync(s.CompanyA, s.BranchA1, draft.Id,
                ConcurrencyToken.FromBytes(posted.RowVersion), Cancellation)).ConfigureAwait(true);

            var deletable = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, command, Cancellation).ConfigureAwait(true);
            await treasury.DeleteDraftAsync(s.CompanyA, s.BranchA1, deletable.Id, ConcurrencyToken.FromBytes(deletable.RowVersion), Cancellation).ConfigureAwait(true);
            Assert.False(await context.Vouchers.AnyAsync(v => v.Id == deletable.Id, Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AVoucherKeepsItsSourceDocumentAndAPatternCanFixTheVoucherDate()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var setup = await VoucherSetupAsync(fixture).ConfigureAwait(true);
            using var context = database.Context();
            var treasury = Treasury(context);

            // The number and date printed on the paper document are stored with the voucher and survive an update.
            var draft = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                Receipt(setup, 50m) with { SourceNumber = "  ق-1234  ", SourceDate = new DateOnly(2026, 10, 17) }, Cancellation).ConfigureAwait(true);
            Assert.Equal("ق-1234", draft.SourceNumber);
            Assert.Equal(new DateOnly(2026, 10, 17), draft.SourceDate);
            context.ChangeTracker.Clear();
            var updated = await treasury.UpdateDraftAsync(s.CompanyA, s.BranchA1, draft.Id,
                Receipt(setup, 60m) with { SourceNumber = "ق-1235", SourceDate = null, ExpectedVersion = ConcurrencyToken.FromBytes(draft.RowVersion) }, Cancellation).ConfigureAwait(true);
            Assert.Equal("ق-1235", updated.SourceNumber);
            Assert.Null(updated.SourceDate);

            // A pattern that fixes the date accepts only the day the voucher is added (one day of tolerance for the time zone).
            var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
            var locked = Receipt(setup, 40m) with { PatternRevisionId = setup.DateLockedReceipt };
            await AssertRuleAsync("treasury.document_date_locked", () => treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                locked with { VoucherDateUtc = today.AddDays(-30) }, Cancellation)).ConfigureAwait(true);
            var ok = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, locked with { VoucherDateUtc = today }, Cancellation).ConfigureAwait(true);
            Assert.Equal(today, ok.VoucherDateUtc);
            context.ChangeTracker.Clear();
            await AssertRuleAsync("treasury.document_date_locked", () => treasury.UpdateDraftAsync(s.CompanyA, s.BranchA1, ok.Id,
                locked with { VoucherDateUtc = today.AddDays(-30), ExpectedVersion = ConcurrencyToken.FromBytes(ok.RowVersion) }, Cancellation)).ConfigureAwait(true);
        }
    }
}
