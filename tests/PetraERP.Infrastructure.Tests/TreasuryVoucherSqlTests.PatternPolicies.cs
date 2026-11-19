using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Patterns;
using PetraERP.Application.Sales;
using PetraERP.Application.Treasury;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Patterns;

namespace PetraERP.Infrastructure.Tests;

/// <summary>AMN-VPAT-GATE-002: cut-off, lookup restriction, line-field rules and the effective-pattern reads, against a real database.</summary>
public sealed partial class SalesPostingSqlTests
{
    private static async Task<Guid> PublishedPaymentPatternAsync(SqlPatternService patterns, PatternScope scope, Guid branch, string code,
        VoucherSetup setup, Guid cash, PatternVoucherPolicy? voucher, List<PatternFieldRule>? fields = null, PatternApprovalPolicy? approval = null)
    {
        var created = await patterns.CreatePatternAsync(scope, new CreateDocumentPatternCommand
        {
            Code = code, NameArabic = code, ShortCode = code, OperationKind = BusinessOperationKind.CashPayment, BranchIds = [branch],
            InitialConfiguration = new PatternConfiguration
            {
                AccountMappings =
                [
                    new() { Role = "Cash", AccountId = cash }, new() { Role = "Bank", AccountId = setup.Bank }, new() { Role = "Payable", AccountId = setup.Payable },
                ],
                NumberingPolicy = new() { Prefix = code + "-", MinimumDigits = 4 },
                Voucher = voucher, LineFields = fields ?? [], ApprovalPolicy = approval ?? new(),
            },
        }, Cancellation).ConfigureAwait(true);
        var published = await patterns.PublishRevisionAsync(scope, created.Id, new PublishPatternRevisionCommand
        { ExpectedVersion = created.Version, RevisionId = created.ActiveRevision!.Id, Reason = "نشر" }, Cancellation).ConfigureAwait(true);
        return published.ActiveRevision!.Id;
    }

    private static SaveVoucherCommand PaymentOn(VoucherSetup setup, Guid revision, DateTimeOffset date, params VoucherLineCommand[] lines) =>
        new(VoucherKind.Payment, revision, setup.Supplier, SettlementMethod.Bank, date, "USD", 1m, lines.Sum(l => l.Amount), "دفع", [], lines);

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task VoucherCutoffFreezesCreateEditDeletePostAndReverseAndNothingNewer()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var setup = await VoucherSetupAsync(fixture).ConfigureAwait(true);
            using var context = database.Context();
            var patterns = new SqlPatternService(context);
            var scope = PatternScope.Create(s.CompanyA, s.BranchA1, s.Actor);
            var treasury = Treasury(context);
            var oct19 = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
            var oct20 = oct19.AddDays(1);
            VoucherLineCommand[] one = [new(null, setup.Expense, 10m, Description: "مصروف")];

            // Cut-off 2026-10-18: a voucher on the 19th is newer and runs normally.
            var open = await PublishedPaymentPatternAsync(patterns, scope, s.BranchA1, "PO", setup, fixture.CashId,
                new PatternVoucherPolicy { DenyMutationsOnOrBefore = new DateOnly(2026, 10, 18) }).ConfigureAwait(true);
            // Cut-off 2026-10-19: the 19th itself is frozen, the 20th is not.
            var frozen = await PublishedPaymentPatternAsync(patterns, scope, s.BranchA1, "PF", setup, fixture.CashId,
                new PatternVoucherPolicy { DenyMutationsOnOrBefore = new DateOnly(2026, 10, 19) }).ConfigureAwait(true);

            await AssertRuleAsync("treasury.voucher_date_cutoff", () => treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                PaymentOn(setup, frozen, oct19, one), Cancellation)).ConfigureAwait(true);
            var newer = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, PaymentOn(setup, frozen, oct20, one), Cancellation).ConfigureAwait(true);
            Assert.Equal(VoucherStatus.Draft, newer.Status);

            // Editing cannot move a voucher into the frozen period, and an older stored document cannot be edited.
            var draft = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, PaymentOn(setup, open, oct19, one), Cancellation).ConfigureAwait(true);
            await AssertRuleAsync("treasury.voucher_date_cutoff", () => treasury.UpdateDraftAsync(s.CompanyA, s.BranchA1, draft.Id,
                PaymentOn(setup, frozen, oct19, one) with { ExpectedVersion = PetraERP.Application.Common.ConcurrencyToken.FromBytes(draft.RowVersion) },
                Cancellation)).ConfigureAwait(true);

            // A later revision of the same pattern freezes what is already stored: the stored revision's own rule decides delete/post/reverse.
            var toReverse = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, PaymentOn(setup, open, oct19, one), Cancellation).ConfigureAwait(true);
            var posted = await treasury.PostAsync(s.CompanyA, s.BranchA1, toReverse.Id, s.Actor,
                new(PetraERP.Application.Common.ConcurrencyToken.FromBytes(toReverse.RowVersion), "cut-post-1"), Cancellation).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            Assert.Equal(VoucherStatus.Posted, posted.Status);
            var toDelete = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, PaymentOn(setup, open, oct19, one), Cancellation).ConfigureAwait(true);
            var toPost = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, PaymentOn(setup, open, oct19, one), Cancellation).ConfigureAwait(true);

            // Freeze through the 31st by rewriting the stored revision (the service never edits a published revision).
            var freezeJson = System.Text.Json.JsonSerializer.Serialize(new PatternConfiguration
            {
                SchemaVersion = 2,
                AccountMappings =
                [
                    new() { Role = "Cash", AccountId = fixture.CashId }, new() { Role = "Bank", AccountId = setup.Bank }, new() { Role = "Payable", AccountId = setup.Payable },
                ],
                NumberingPolicy = new() { Prefix = "PO-", MinimumDigits = 4 },
                Voucher = new PatternVoucherPolicy { DenyMutationsOnOrBefore = new DateOnly(2026, 10, 31) },
            });
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE [patterns].[PatternRevisions] SET [ConfigurationJson] = {freezeJson} WHERE [Id] = {open}", Cancellation).ConfigureAwait(true);
            context.ChangeTracker.Clear();

            await AssertRuleAsync("treasury.voucher_date_cutoff", () => treasury.DeleteDraftAsync(s.CompanyA, s.BranchA1, toDelete.Id,
                PetraERP.Application.Common.ConcurrencyToken.FromBytes(toDelete.RowVersion), Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("treasury.voucher_date_cutoff", () => treasury.PostAsync(s.CompanyA, s.BranchA1, toPost.Id, s.Actor,
                new(PetraERP.Application.Common.ConcurrencyToken.FromBytes(toPost.RowVersion), "cut-post-2"), Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("treasury.voucher_date_cutoff", () => treasury.ReverseAsync(s.CompanyA, s.BranchA1, posted.Id, s.Actor,
                new(PetraERP.Application.Common.ConcurrencyToken.FromBytes(posted.RowVersion), "تصحيح"), Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task VoucherApprovalThresholdAndManualOnlyPostingFollowThePatternAndNeverExceedTheCompanyPolicy()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var setup = await VoucherSetupAsync(fixture).ConfigureAwait(true);
            using var context = database.Context();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],{"Treasury.Vouchers.Approve"} FROM [security].[UserRoles] WHERE [UserId]={s.Actor}", Cancellation).ConfigureAwait(true);
            var patterns = new SqlPatternService(context);
            var scope = PatternScope.Create(s.CompanyA, s.BranchA1, s.Actor);
            var treasury = Treasury(context);
            var date = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);

            // Approval is required from 50 upward: 40 posts straight away, 100 needs an approval first.
            var gated = await PublishedPaymentPatternAsync(patterns, scope, s.BranchA1, "PG", setup, fixture.CashId, null,
                approval: new PatternApprovalPolicy { RequiresApproval = true, ThresholdAmount = 50m }).ConfigureAwait(true);
            var small = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, PaymentOn(setup, gated, date, new VoucherLineCommand(null, setup.Expense, 40m)), Cancellation).ConfigureAwait(true);
            Assert.Equal(VoucherStatus.Posted, (await treasury.PostAsync(s.CompanyA, s.BranchA1, small.Id, s.Actor,
                new(PetraERP.Application.Common.ConcurrencyToken.FromBytes(small.RowVersion), "thr-small"), Cancellation).ConfigureAwait(true)).Status);

            var big = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, PaymentOn(setup, gated, date, new VoucherLineCommand(null, setup.Expense, 100m)), Cancellation).ConfigureAwait(true);
            await AssertRuleAsync("treasury.approval_required", () => treasury.PostAsync(s.CompanyA, s.BranchA1, big.Id, s.Actor,
                new(PetraERP.Application.Common.ConcurrencyToken.FromBytes(big.RowVersion), "thr-big"), Cancellation)).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            var approved = await treasury.ApproveAsync(s.CompanyA, s.BranchA1, big.Id, s.Actor,
                new(PetraERP.Application.Common.ConcurrencyToken.FromBytes(big.RowVersion), "اعتماد المبلغ الكبير"), Cancellation).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            Assert.Equal(VoucherStatus.Posted, (await treasury.PostAsync(s.CompanyA, s.BranchA1, big.Id, s.Actor,
                new(PetraERP.Application.Common.ConcurrencyToken.FromBytes(approved.RowVersion), "thr-big-2"), Cancellation).ConfigureAwait(true)).Status);

            // One-step save-and-process: the company posts automatically; a ManualOnly pattern still holds its vouchers.
            var options = new PetraERP.Infrastructure.CompanyOptions.SqlCompanyOptionsService(context);
            var year = await context.FiscalYears.AsNoTracking().Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).FirstAsync(Cancellation).ConfigureAwait(true);
            var current = await options.GetAsync(s.CompanyA, Cancellation).ConfigureAwait(true);
            await options.SaveAsync(s.CompanyA, year, s.Actor, current.Policy with { DocumentPostingMode = PetraERP.Application.CompanyOptions.DocumentPostingMode.ApproveAndPost },
                current.Version, "ترحيل آلي للاختبار", Cancellation).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            var auto = await PublishedPaymentPatternAsync(patterns, scope, s.BranchA1, "PA", setup, fixture.CashId, null).ConfigureAwait(true);
            var manual = await PublishedPaymentPatternAsync(patterns, scope, s.BranchA1, "PM", setup, fixture.CashId,
                new PatternVoucherPolicy { PostingMode = VoucherPostingMode.ManualOnly }).ConfigureAwait(true);
            var purchases = new PetraERP.Infrastructure.Purchases.SqlPurchaseInvoiceService(context, patterns, TimeProvider.System);
            var processing = new PetraERP.Infrastructure.DocumentProcessing.SqlDocumentSaveAndProcessService(
                Service(context), purchases, options, context, null, TimeProvider.System, treasury);
            var permissions = new PetraERP.Application.DocumentProcessing.DocumentProcessingPermissions(true, true);

            var autoResult = await processing.CreateVoucherAsync(s.CompanyA, s.BranchA1, s.Actor, permissions,
                PaymentOn(setup, auto, date, new VoucherLineCommand(null, setup.Expense, 10m)), false, new("sp-auto"), Cancellation).ConfigureAwait(true);
            Assert.Equal(PetraERP.Application.DocumentProcessing.DocumentProcessingOutcome.Posted, autoResult.Outcome);
            var manualResult = await processing.CreateVoucherAsync(s.CompanyA, s.BranchA1, s.Actor, permissions,
                PaymentOn(setup, manual, date, new VoucherLineCommand(null, setup.Expense, 10m)), false, new("sp-manual"), Cancellation).ConfigureAwait(true);
            Assert.NotEqual(PetraERP.Application.DocumentProcessing.DocumentProcessingOutcome.Posted, manualResult.Outcome);
            Assert.False(manualResult.PostingApplied);
            Assert.Null(manualResult.JournalEntryId);

            // Manual posting of the held voucher is still available through the post action.
            context.ChangeTracker.Clear();
            var held = await treasury.GetAsync(s.CompanyA, s.BranchA1, manualResult.DocumentId, Cancellation).ConfigureAwait(true);
            Assert.Equal(VoucherStatus.Draft, held.Status);
            Assert.Equal(VoucherStatus.Posted, (await treasury.PostAsync(s.CompanyA, s.BranchA1, held.Id, s.Actor,
                new(PetraERP.Application.Common.ConcurrencyToken.FromBytes(held.RowVersion), "sp-manual-post"), Cancellation).ConfigureAwait(true)).Status);
        }
    }

    /// <summary>VAT-10 (reduced): one party, one account — sale, receipt and payment of the same Both party all land on that one account.</summary>
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ASaleReceiptAndPaymentOfOneBothPartyAllUseItsSingleLedgerAccount()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var setup = await VoucherSetupAsync(fixture).ConfigureAwait(true);
            using var context = database.Context();
            // The fixture's customer becomes customer-and-supplier with the one account it already has.
            await context.Parties.Where(p => p.Id == setup.Customer)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.Role, (int)PetraERP.Domain.Parties.PartyRole.Both).SetProperty(p => p.PayableAccountId, fixture.ReceivableId), Cancellation).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            var party = await context.Parties.AsNoTracking().SingleAsync(p => p.Id == setup.Customer, Cancellation).ConfigureAwait(true);
            Assert.Equal(party.ReceivableAccountId, party.PayableAccountId);

            var sale = await Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new PostSalesInvoiceCommand("both-sale"), Cancellation).ConfigureAwait(true);
            var treasury = Treasury(context);
            var receiptDraft = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, Receipt(setup, 100m, (fixture.InvoiceId, 100m)), Cancellation).ConfigureAwait(true);
            var receipt = await treasury.PostAsync(s.CompanyA, s.BranchA1, receiptDraft.Id, s.Actor,
                new(PetraERP.Application.Common.ConcurrencyToken.FromBytes(receiptDraft.RowVersion), "both-receipt"), Cancellation).ConfigureAwait(true);
            var paymentDraft = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, new SaveVoucherCommand(VoucherKind.Payment, setup.Payment, setup.Customer,
                SettlementMethod.Bank, new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), "USD", 1m, 40m, "دفع للطرف نفسه", [], [new(setup.Customer, null, 40m)])
                { ActorUserId = s.Actor }, Cancellation).ConfigureAwait(true);
            var payment = await treasury.PostAsync(s.CompanyA, s.BranchA1, paymentDraft.Id, s.Actor,
                new(PetraERP.Application.Common.ConcurrencyToken.FromBytes(paymentDraft.RowVersion), "both-payment"), Cancellation).ConfigureAwait(true);

            foreach (var journalId in new[] { sale.JournalEntryId, receipt.JournalEntryId, payment.JournalEntryId })
            {
                var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == journalId).ToListAsync(Cancellation).ConfigureAwait(true);
                Assert.Contains(lines, l => l.AccountId == fixture.ReceivableId); // the party's single account
                Assert.DoesNotContain(lines, l => l.AccountId == setup.Payable);   // the supplier-only account is never used for this party
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task VoucherLookupRestrictionAndLineFieldRulesAreEnforcedByTheServerAndTheEffectiveReadsFollowThem()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var setup = await VoucherSetupAsync(fixture).ConfigureAwait(true);
            using var context = database.Context(); var chart = LedgerScenario.Chart(context);
            var patterns = new SqlPatternService(context);
            var scope = PatternScope.Create(s.CompanyA, s.BranchA1, s.Actor);
            var treasury = Treasury(context);
            var date = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
            var allowedCenter = await chart.CreateCostCenterAsync(s.Master(s.CompanyA), new CostCenterInput { Code = "CA", NameArabic = "مسموح" }, Cancellation).ConfigureAwait(true);
            var otherCenter = await chart.CreateCostCenterAsync(s.Master(s.CompanyA), new CostCenterInput { Code = "CB", NameArabic = "غير مسموح" }, Cancellation).ConfigureAwait(true);

            // Only the expense account (and anything under it) and one cost center; the cost center is required, the description hidden.
            var revision = await PublishedPaymentPatternAsync(patterns, scope, s.BranchA1, "PL", setup, fixture.CashId,
                new PatternVoucherPolicy
                {
                    Lookup = new PatternLookupPolicy { EligibleAccountRootIds = [setup.Expense], EligibleCostCenterIds = [allowedCenter.Id] },
                },
                [new() { FieldName = "CostCenter", IsVisible = true, IsRequired = true }, new() { FieldName = "Description", IsVisible = false }]).ConfigureAwait(true);

            var good = new VoucherLineCommand(null, setup.Expense, 10m, allowedCenter.Id);
            var created = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, PaymentOn(setup, revision, date, good), Cancellation).ConfigureAwait(true);
            Assert.Equal(VoucherStatus.Draft, created.Status);

            // A forged account id is refused exactly like one the lookup would never offer.
            await AssertRuleAsync("treasury.account_not_eligible", () => treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                PaymentOn(setup, revision, date, good with { AccountId = setup.Bank }), Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("treasury.cost_center_not_eligible", () => treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                PaymentOn(setup, revision, date, good with { CostCenterId = otherCenter.Id }), Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("treasury.line_field_required", () => treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                PaymentOn(setup, revision, date, good with { CostCenterId = null }), Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("treasury.line_field_hidden", () => treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                PaymentOn(setup, revision, date, good with { Description = "بيان محظور" }), Cancellation)).ConfigureAwait(true);

            // The lookup offers only what the pattern allows, and the effective read resolves the block.
            var pattern = (await patterns.ListPatternsAsync(scope, BusinessOperationKind.CashPayment, true, PageRequest.Create(1, 50), Cancellation).ConfigureAwait(true))
                .Items.Single(p => p.Code == "PL");
            var offered = await patterns.SearchEligibleAccountsAsync(scope, pattern.Id, null, 50, Cancellation).ConfigureAwait(true);
            Assert.Equal(setup.Expense, Assert.Single(offered).Id);
            Assert.Empty(await patterns.SearchEligibleAccountsAsync(scope, pattern.Id, "1021", 50, Cancellation).ConfigureAwait(true)); // the bank account is not offered
            var effective = await patterns.GetEffectivePatternAsync(scope, pattern.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(revision, effective.RevisionId);
            Assert.Equal(VoucherFormKind.GenericVoucher, effective.Configuration.Voucher!.FormKind);
            Assert.Equal([setup.Expense], effective.Configuration.Voucher.Lookup.EligibleAccountRootIds);

            // Another company's caller gets the same answer as for an unknown pattern.
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => patterns.GetEffectivePatternAsync(
                PatternScope.Create(s.CompanyB, s.BranchB1, s.Actor), pattern.Id, Cancellation)).ConfigureAwait(true);

            // Publishing refuses a lookup that names an account of another company or an unknown cost center.
            await Assert.ThrowsAsync<BusinessRuleException>(() => PublishedPaymentPatternAsync(patterns, scope, s.BranchA1, "PX", setup, fixture.CashId,
                new PatternVoucherPolicy { Lookup = new PatternLookupPolicy { EligibleCostCenterIds = [Guid.NewGuid()] } })).ConfigureAwait(true);
        }
    }
}
