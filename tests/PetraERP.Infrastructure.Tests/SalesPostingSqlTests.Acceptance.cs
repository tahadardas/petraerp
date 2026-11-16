using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.CompanyOptions;
using PetraERP.Application.DocumentProcessing;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Inventory;
using PetraERP.Application.Sales;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.CompanyOptions;
using PetraERP.Infrastructure.DocumentProcessing;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// P09–P30 of the posting acceptance matrix. Each test runs against a disposable SQL Server database and asserts
/// persisted financial state only; nothing here is a mock of the posting engine.
/// </summary>
public sealed partial class SalesPostingSqlTests
{
    private static SqlBulkDocumentPostingService Bulk(PetraDbContext context) =>
        new(context, new SqlAccessEvaluator(context), TimeProvider.System);

    private static BulkPostingFilter Filter(params BulkPostingDocumentType[] types) => new() { DocumentTypes = types };

    private static StartBulkPostingCommand BulkCommand(string key, params BulkPostingDocumentType[] types) => new()
    {
        IdempotencyKey = key,
        Filter = Filter(types),
        Confirmed = true,
        Reason = "اختبار قبول الترحيل الجماعي",
    };

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P09TwoUsersPostingTheSameInvoiceCreateOneFinancialEffect()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            Guid secondUser;
            using (var setup = database.Context())
                secondUser = await CreateSecondUserAsync(setup, s.CompanyA, s.BranchA1, s.Actor).ConfigureAwait(true);

            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<bool> PostAsync(Guid actor)
            {
                using var context = database.Context();
                await ready.Task.ConfigureAwait(true);
                try
                {
                    await Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, actor,
                        new PostSalesInvoiceCommand($"p09-{actor:N}"), Cancellation).ConfigureAwait(true);
                    return true;
                }
                catch (Exception exception) when (exception is BusinessRuleException or PostingConcurrencyException
                    or PostingIdempotencyConflictException or PostingAuthorizationException)
                {
                    return false;
                }
            }

            var firstTask = PostAsync(s.Actor);
            var secondTask = PostAsync(secondUser);
            ready.SetResult();
            var results = await Task.WhenAll(firstTask, secondTask).ConfigureAwait(true);

            Assert.Single(results, static posted => posted);
            using var verify = database.Context();
            Assert.Equal(1, await verify.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await verify.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));
            var invoice = await verify.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)SalesInvoiceStatus.Posted, invoice.Status);
            Assert.NotNull(invoice.JournalEntryId);
            var journal = await verify.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == invoice.JournalEntryId, Cancellation).ConfigureAwait(true);
            var lines = await verify.JournalLines.AsNoTracking().Where(l => l.JournalId == journal.Id).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P10PostingModeIsIsolatedBetweenCompaniesAndDefaultsToManual()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            var options = new SqlCompanyOptionsService(context);
            var fiscalYear = await context.FiscalYears.AsNoTracking()
                .Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var initial = await options.GetAsync(s.CompanyA, Cancellation).ConfigureAwait(true);
            Assert.Equal(DocumentPostingMode.ApproveOnly, initial.Policy.DocumentPostingMode);

            await options.SaveAsync(s.CompanyA, fiscalYear, s.Actor,
                initial.Policy with { DocumentPostingMode = DocumentPostingMode.ApproveAndPost },
                initial.Version, "تفعيل الترحيل التلقائي للشركة أ", Cancellation).ConfigureAwait(true);

            var companyA = await options.GetAsync(s.CompanyA, Cancellation).ConfigureAwait(true);
            var companyB = await options.GetAsync(s.CompanyB, Cancellation).ConfigureAwait(true);
            Assert.Equal(DocumentPostingMode.ApproveAndPost, companyA.Policy.DocumentPostingMode);
            Assert.Equal(DocumentPostingMode.ApproveOnly, companyB.Policy.DocumentPostingMode);
            Assert.True(companyB.Policy.AllowSaveAsDraft);
            Assert.NotEqual(companyA.Version, companyB.Version);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P11PolicyUpdateRejectsStaleVersionsAndAuditsEveryAcceptedChange()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            var options = new SqlCompanyOptionsService(context);
            var fiscalYear = await context.FiscalYears.AsNoTracking()
                .Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var initial = await options.GetAsync(s.CompanyA, Cancellation).ConfigureAwait(true);

            var accepted = await options.SaveAsync(s.CompanyA, fiscalYear, s.Actor,
                initial.Policy with { DocumentPostingMode = DocumentPostingMode.ApproveAndPost },
                initial.Version, "تغيير مقبول", Cancellation).ConfigureAwait(true);
            Assert.NotEqual(initial.Version, accepted.Version);

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => options.SaveAsync(s.CompanyA, fiscalYear, s.Actor,
                initial.Policy with { DocumentPostingMode = DocumentPostingMode.ApproveOnly },
                initial.Version, "تغيير بنسخة قديمة", Cancellation)).ConfigureAwait(true);

            Assert.Equal(DocumentPostingMode.ApproveAndPost,
                (await options.GetAsync(s.CompanyA, Cancellation).ConfigureAwait(true)).Policy.DocumentPostingMode);
            var audits = await context.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM [audit].[CompanyPolicyAudit] WHERE [CompanyId]={s.CompanyA}")
                .SingleAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(1, audits);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P12BulkScansSelectedTypesAndPostsDraftsAndValidatedJournals()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            await GrantAsync(context, s.Actor, "Sales.Invoices.Approve").ConfigureAwait(true);
            await GrantAsync(context, s.Actor, "Ledger.Journals.Post").ConfigureAwait(true);
            var journalService = new SqlJournalEntryService(context, TimeProvider.System);
            var journalScope = JournalEntryScope.Create(s.CompanyA, s.BranchA1, s.Actor);
            var draft = await journalService.CreateDraftAsync(journalScope, new CreateJournalDraftCommand
            {
                AccountingDate = new DateOnly(2026, 10, 19),
                Description = "قيد اختبار الترحيل الجماعي",
                Lines = [LedgerScenario.Line(fixture.CashId, 5m, 0m, "USD"), LedgerScenario.Line(fixture.ReceivableId, 0m, 5m, "USD")],
            }, Cancellation).ConfigureAwait(true);
            var validated = await journalService.ValidateDraftAsync(journalScope, draft.Id,
                new JournalDraftActionCommand { ExpectedVersion = LedgerScenario.Token(draft.VersionBase64), Reason = "تحقق" },
                Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Validated, validated.State);

            var service = Bulk(context);
            var filter = Filter(BulkPostingDocumentType.SalesInvoice, BulkPostingDocumentType.Journal);
            var preview = await service.PreviewAsync(s.CompanyA, s.BranchA1, s.Actor, filter, Cancellation).ConfigureAwait(true);
            Assert.Equal(2, preview.TotalExamined);
            var journalItem = Assert.Single(preview.Items, i => i.DocumentType == BulkPostingDocumentType.Journal);
            Assert.True(journalItem.Postable, $"{journalItem.Classification}: {journalItem.Reason}");
            var result = await service.StartAsync(s.CompanyA, s.BranchA1, s.Actor,
                BulkCommand("p12-types", BulkPostingDocumentType.SalesInvoice, BulkPostingDocumentType.Journal),
                Cancellation).ConfigureAwait(true);

            Assert.Equal(BulkPostingBatchStatus.Completed, result.Status);
            var rows = (await Bulk(context).GetResultsAsync(s.CompanyA, s.BranchA1, result.BatchId, null, null, 1, 50, Cancellation)
                .ConfigureAwait(true)).Items;
            Assert.True(result.Posted == 2,
                string.Join(" | ", rows.Select(r => $"{r.DocumentType}:{r.Status}:{r.NewStatus}:{r.Reason}")));
            Assert.Equal(0, result.Skipped);
            Assert.Equal(0, result.Failed);
            context.ChangeTracker.Clear();
            var invoice = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)SalesInvoiceStatus.Posted, invoice.Status);
            var postedJournal = await context.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == draft.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Posted, postedJournal.State);
            Assert.Equal(2, await context.JournalEntries.CountAsync(j => j.State == (byte)JournalEntryState.Posted, Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P16BulkPostsAnAlreadyApprovedInvoiceWithoutReapproving()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            var current = await Service(context).GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            var approved = await Service(context).ApproveInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                ConcurrencyToken.FromBytes(current.RowVersion), Cancellation).ConfigureAwait(true);
            Assert.Equal(SalesInvoiceStatus.Approved, approved.Status);
            var approvedAt = (await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true)).ApprovedAtUtc;

            var service = Bulk(context);
            var preview = await service.PreviewAsync(s.CompanyA, s.BranchA1, s.Actor, Filter(BulkPostingDocumentType.SalesInvoice), Cancellation).ConfigureAwait(true);
            var item = Assert.Single(preview.Items);
            Assert.Equal(BulkPostingClassification.ReadyForPosting, item.Classification);
            Assert.True(item.Postable);

            var result = await service.StartAsync(s.CompanyA, s.BranchA1, s.Actor,
                BulkCommand("p16-approved", BulkPostingDocumentType.SalesInvoice), Cancellation).ConfigureAwait(true);
            Assert.Equal(1, result.Posted);
            context.ChangeTracker.Clear();
            var invoice = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)SalesInvoiceStatus.Posted, invoice.Status);
            Assert.Equal(approvedAt, invoice.ApprovedAtUtc);
            Assert.Equal(1, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P17OneFailingDocumentDoesNotDuplicateTheSuccessfulPosting()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            Guid excessiveId;
            using (var setup = database.Context())
            {
                await GrantAsync(setup, s.Actor, "Sales.Invoices.Approve").ConfigureAwait(true);
                excessiveId = (await CreateInvoiceAsync(setup, fixture, 500m).ConfigureAwait(true)).Id;
            }

            BulkPostingBatchResult result;
            using (var context = database.Context())
                result = await Bulk(context).StartAsync(s.CompanyA, s.BranchA1, s.Actor,
                    BulkCommand("p17-mixed", BulkPostingDocumentType.SalesInvoice), Cancellation).ConfigureAwait(true);

            Assert.Equal(BulkPostingBatchStatus.CompletedWithFailures, result.Status);
            Assert.Equal(2, result.TotalExamined);
            Assert.Equal(1, result.Posted);
            Assert.Equal(1, result.Failed);
            using var verify = database.Context();
            Assert.Equal(1, await verify.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await verify.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));
            Assert.Equal(0, await verify.StockMovements.CountAsync(m => m.DocumentId == excessiveId, Cancellation).ConfigureAwait(true));
            var failed = await verify.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == excessiveId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)SalesInvoiceStatus.Approved, failed.Status);
            Assert.Null(failed.JournalEntryId);
            var page = await Bulk(verify).GetResultsAsync(s.CompanyA, s.BranchA1, result.BatchId, null, null, 1, 50, Cancellation).ConfigureAwait(true);
            var failedRow = Assert.Single(page.Items, i => i.DocumentId == excessiveId);
            Assert.Equal(BulkPostingItemStatus.Failed, failedRow.Status);
            Assert.Equal("inventory.negative_stock_blocked", failedRow.FailureCode);
            Assert.Contains("المخزون", failedRow.Reason, StringComparison.Ordinal);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P18TwoSimultaneousBatchesNeverDoublePost()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using (var setup = database.Context())
                await GrantAsync(setup, s.Actor, "Sales.Invoices.Approve").ConfigureAwait(true);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<BulkPostingBatchResult> StartAsync(string key)
            {
                using var context = database.Context();
                await ready.Task.ConfigureAwait(true);
                return await Bulk(context).StartAsync(s.CompanyA, s.BranchA1, s.Actor,
                    BulkCommand(key, BulkPostingDocumentType.SalesInvoice), Cancellation).ConfigureAwait(true);
            }

            var firstTask = StartAsync("p18-a");
            var secondTask = StartAsync("p18-b");
            ready.SetResult();
            var batches = await Task.WhenAll(firstTask, secondTask).ConfigureAwait(true);

            Assert.Equal(1, batches.Sum(b => b.Posted));
            Assert.Equal(1, batches.Sum(b => b.Skipped));
            Assert.All(batches, b => Assert.Equal(0, b.Failed));
            using var verify = database.Context();
            Assert.Equal(1, await verify.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await verify.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));
            var invoice = await verify.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)SalesInvoiceStatus.Posted, invoice.Status);
            Assert.NotNull(invoice.JournalEntryId);
            var posted = Assert.Single(batches, b => b.Posted == 1);
            var row = Assert.Single((await Bulk(verify).GetResultsAsync(s.CompanyA, s.BranchA1, posted.BatchId, null, null, 1, 50,
                Cancellation).ConfigureAwait(true)).Items);
            Assert.Equal(BulkPostingItemStatus.Posted, row.Status);
            Assert.Equal(BulkPostingClassification.CanBeApprovedAutomatically, row.Classification);
            Assert.NotNull(row.PostingNumber);
            var yielded = Assert.Single(batches, b => b.Skipped == 1);
            var skippedRow = Assert.Single((await Bulk(verify).GetResultsAsync(s.CompanyA, s.BranchA1, yielded.BatchId, null, null, 1, 50,
                Cancellation).ConfigureAwait(true)).Items);
            Assert.Equal(BulkPostingItemStatus.Skipped, skippedRow.Status);
            Assert.Equal(BulkPostingClassification.ConcurrencyConflict, skippedRow.Classification);
            Assert.Contains("دفعة أخرى", skippedRow.Reason, StringComparison.Ordinal);
            Assert.Equal(1, await verify.JournalEntries.Select(j => j.PostingSequence).Distinct().CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P19BulkPostingKeepsInvoiceNumbersAndPostingSequencesUnique()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using (var setup = database.Context())
            {
                await GrantAsync(setup, s.Actor, "Sales.Invoices.Approve").ConfigureAwait(true);
                for (var index = 0; index < 4; index++)
                    await CreateInvoiceAsync(setup, fixture, 1m).ConfigureAwait(true);
            }

            BulkPostingBatchResult result;
            using (var context = database.Context())
                result = await Bulk(context).StartAsync(s.CompanyA, s.BranchA1, s.Actor,
                    BulkCommand("p19-numbers", BulkPostingDocumentType.SalesInvoice), Cancellation).ConfigureAwait(true);

            Assert.Equal(BulkPostingBatchStatus.Completed, result.Status);
            Assert.Equal(5, result.Posted);
            using var verify = database.Context();
            var invoices = await verify.SalesInvoices.AsNoTracking().Where(i => i.CompanyId == s.CompanyA && i.Status == (byte)SalesInvoiceStatus.Posted)
                .ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(5, invoices.Count);
            Assert.Equal(5, invoices.Select(i => i.InvoiceNumber).Distinct(StringComparer.Ordinal).Count());
            var journals = await verify.JournalEntries.AsNoTracking().Where(j => j.State == (byte)JournalEntryState.Posted)
                .ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(5, journals.Count);
            Assert.Equal(5, journals.Select(j => j.PostingSequence).Distinct().Count());
            Assert.All(journals, j => Assert.NotNull(j.PostingSequence));
            var deliveries = await verify.StockMovements.AsNoTracking()
                .Where(m => m.DocumentType == "SalesInvoice" && m.DocumentId != null).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(6m, deliveries.Sum(m => -m.BaseQuantity));
            Assert.Equal(5, deliveries.Select(m => m.DocumentId).Distinct().Count());
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P20FiftyLineInvoiceUsesTheAuthoritativeWeightedAverageCost()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            await LedgerScenario.AddStockMovementAsync(context, new StockMovementRow
            {
                Id = Guid.NewGuid(), CompanyId = s.CompanyA, BranchId = s.BranchA1, WarehouseId = fixture.WarehouseId,
                ItemId = fixture.ItemId, UnitId = fixture.UnitId, MovementKind = (int)StockMovementKind.AdjustmentIncrease,
                Quantity = 100m, BaseQuantity = 100m, UnitCost = 3m, TotalCost = 300m,
                MovementDateUtc = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero), DocumentType = "Adjustment",
                DocumentId = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = s.Actor,
            }, Cancellation).ConfigureAwait(true);
            var first = await Service(context).GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            var fifty = await Service(context).CreateInvoiceAsync(s.CompanyA, s.BranchA1, s.Actor, new CreateSalesInvoiceCommand(
                first.PatternRevisionId, first.CustomerId, first.WarehouseId, new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero),
                CurrencyCode: "USD",
                Lines: Enumerable.Range(0, 50).Select(_ => new CreateSalesInvoiceLineCommand(fixture.ItemId, fixture.UnitId, 1m, 10m)).ToArray()),
                Cancellation).ConfigureAwait(true);

            var posted = await Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, fifty.Id, s.Actor,
                new PostSalesInvoiceCommand("p20-fifty"), Cancellation).ConfigureAwait(true);
            Assert.Equal(SalesInvoiceStatus.Posted, posted.Status);

            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId)
                .ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
            // (10 × 2) + (100 × 3) = 320 over 110 units; 110 is not the delivered quantity, so WAC is 2.9091 per unit.
            Assert.Equal(145.4550m, lines.Where(l => l.AccountId == fixture.CogsId).Sum(l => l.BaseDebit));
            var delivered = await context.StockMovements.AsNoTracking()
                .Where(m => m.DocumentId == fifty.Id && m.DocumentType == "SalesInvoice").ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(50, delivered.Count);
            Assert.Equal(-50m, delivered.Sum(m => m.BaseQuantity));
            Assert.Equal(145.4550m, delivered.Sum(m => m.TotalCost));
            var remaining = await context.StockMovements.AsNoTracking()
                .Where(m => m.WarehouseId == fixture.WarehouseId && m.ItemId == fixture.ItemId)
                .ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(60m, remaining.Sum(m => m.BaseQuantity));
            Assert.Equal(174.5450m, remaining.Where(m => m.BaseQuantity > 0).Sum(m => m.TotalCost)
                - remaining.Where(m => m.BaseQuantity < 0).Sum(m => m.TotalCost));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P21ReversalRestoresStockAtOriginalCostAndBalancesTheJournal()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            var service = Service(context);
            var posted = await service.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new PostSalesInvoiceCommand("p21-original"), Cancellation).ConfigureAwait(true);
            var delivery = await context.StockMovements.AsNoTracking().SingleAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true);

            await LedgerScenario.AddStockMovementAsync(context, Receipt(s, fixture.ItemId, fixture.UnitId, fixture.WarehouseId, 10m, 10m), Cancellation).ConfigureAwait(true);
            var reversed = await service.ReverseInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new ReverseSalesInvoiceCommand("عكس لأغراض القبول"), Cancellation).ConfigureAwait(true);

            Assert.Equal(SalesInvoiceStatus.Reversed, reversed.Status);
            var returned = await context.StockMovements.AsNoTracking().SingleAsync(m => m.DocumentId == reversed.ReversedByInvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal(-delivery.BaseQuantity, returned.BaseQuantity);
            Assert.Equal(delivery.TotalCost, returned.TotalCost);
            var original = await context.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == posted.JournalEntryId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Reversed, original.State);
            var reversalLines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == original.ReversedByEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            var originalLines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == original.Id).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.All(reversalLines, reversal =>
            {
                var source = Assert.Single(originalLines, line => line.Id == reversal.ReversesLineId);
                Assert.Equal(source.BaseDebit, reversal.BaseCredit);
                Assert.Equal(source.BaseCredit, reversal.BaseDebit);
            });
            var net = await context.JournalLines.AsNoTracking()
                .Where(l => l.JournalId == original.Id || l.JournalId == original.ReversedByEntryId)
                .SumAsync(l => l.BaseDebit - l.BaseCredit, Cancellation).ConfigureAwait(true);
            Assert.Equal(0m, net);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P22ClosedAndMissingPeriodsRejectPostingBeforeAnyEffect()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            var closed = await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [ledger].[AccountingPeriods] SET [State]=2 WHERE [CompanyId]={s.CompanyA} AND [RangeStart]<=N'2026-10-19' AND [RangeEnd]>=N'2026-10-19'",
                Cancellation).ConfigureAwait(true);
            Assert.Equal(1, closed);

            var exception = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(context).PostInvoiceAsync(
                s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, new PostSalesInvoiceCommand("p22-closed"), Cancellation))
                .ConfigureAwait(true);
            Assert.Equal("sales.period_closed", exception.Code);
            Assert.Equal(0, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(0, await context.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));
            var invoice = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)SalesInvoiceStatus.Draft, invoice.Status);
            Assert.Null(invoice.InvoiceNumber);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P23MissingCurrencyOrExchangeRateIsRejectedAndNeverPosted()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            var first = await Service(context).GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => Service(context).CreateInvoiceAsync(s.CompanyA, s.BranchA1, s.Actor,
                new CreateSalesInvoiceCommand(first.PatternRevisionId, first.CustomerId, first.WarehouseId,
                    new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), CurrencyCode: "EUR", ExchangeRate: 1m,
                    Lines: [new(fixture.ItemId, fixture.UnitId, 1m, 100m)]), Cancellation)).ConfigureAwait(true);
            Assert.Equal(1, await context.SalesInvoices.CountAsync(Cancellation).ConfigureAwait(true));

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [system].[CompanyAdditionalCurrencies] WHERE [CompanyId]={s.CompanyA} AND [CurrencyCode]=N'SYP'",
                Cancellation).ConfigureAwait(true);
            var service = Bulk(context);
            var preview = await service.PreviewAsync(s.CompanyA, s.BranchA1, s.Actor, Filter(BulkPostingDocumentType.SalesInvoice), Cancellation).ConfigureAwait(true);
            var item = Assert.Single(preview.Items);
            Assert.Equal(BulkPostingClassification.InvalidCurrencyOrExchangeRate, item.Classification);
            Assert.False(item.Postable);

            var result = await service.StartAsync(s.CompanyA, s.BranchA1, s.Actor,
                BulkCommand("p23-currency", BulkPostingDocumentType.SalesInvoice), Cancellation).ConfigureAwait(true);
            Assert.Equal(0, result.Posted);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(0, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(0, await context.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P24InterruptedBatchResumesAfterTheCauseIsFixedWithoutDuplicating()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            Guid excessiveId;
            using (var setup = database.Context())
            {
                await GrantAsync(setup, s.Actor, "Sales.Invoices.Approve").ConfigureAwait(true);
                excessiveId = (await CreateInvoiceAsync(setup, fixture, 500m).ConfigureAwait(true)).Id;
            }

            BulkPostingBatchResult interrupted;
            using (var context = database.Context())
                interrupted = await Bulk(context).StartAsync(s.CompanyA, s.BranchA1, s.Actor,
                    BulkCommand("p24-resume", BulkPostingDocumentType.SalesInvoice), Cancellation).ConfigureAwait(true);
            Assert.Equal(BulkPostingBatchStatus.CompletedWithFailures, interrupted.Status);
            Assert.Equal(1, interrupted.Posted);
            Assert.Equal(1, interrupted.Failed);

            using (var context = database.Context())
            {
                await LedgerScenario.AddStockMovementAsync(context, new StockMovementRow
                {
                    Id = Guid.NewGuid(), CompanyId = s.CompanyA, BranchId = s.BranchA1, WarehouseId = fixture.WarehouseId,
                    ItemId = fixture.ItemId, UnitId = fixture.UnitId, MovementKind = (int)StockMovementKind.AdjustmentIncrease,
                    Quantity = 600m, BaseQuantity = 600m, UnitCost = 2m, TotalCost = 1200m,
                    MovementDateUtc = new DateTimeOffset(2026, 10, 19, 1, 0, 0, TimeSpan.Zero), DocumentType = "Adjustment",
                    DocumentId = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = s.Actor,
                }, Cancellation).ConfigureAwait(true);
            }

            BulkPostingBatchResult resumed;
            using (var context = database.Context())
                resumed = await Bulk(context).ResumeAsync(s.CompanyA, s.BranchA1, s.Actor, interrupted.BatchId, true, Cancellation).ConfigureAwait(true);

            Assert.Equal(BulkPostingBatchStatus.Completed, resumed.Status);
            Assert.Equal(2, resumed.Posted);
            Assert.Equal(0, resumed.Failed);
            using var verify = database.Context();
            Assert.Equal(2, await verify.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await verify.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await verify.StockMovements.CountAsync(m => m.DocumentId == excessiveId, Cancellation).ConfigureAwait(true));
            var invoices = await verify.SalesInvoices.AsNoTracking()
                .Where(i => i.Id == fixture.InvoiceId || i.Id == excessiveId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.All(invoices, invoice => Assert.Equal((byte)SalesInvoiceStatus.Posted, invoice.Status));
            var page = await Bulk(verify).GetResultsAsync(s.CompanyA, s.BranchA1, interrupted.BatchId, null, null, 1, 50, Cancellation).ConfigureAwait(true);
            Assert.Equal(2, page.Items.Count);
            Assert.All(page.Items, row => Assert.Equal(BulkPostingItemStatus.Posted, row.Status));
            Assert.Equal(2, Assert.Single(page.Items, row => row.DocumentId == excessiveId).AttemptCount);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P25PostedInvoiceJournalAndStockMovementAreLinkedBothWays()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            var posted = await Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new PostSalesInvoiceCommand("p25-link"), Cancellation).ConfigureAwait(true);

            var invoice = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal(posted.JournalEntryId, invoice.JournalEntryId);
            Assert.False(string.IsNullOrWhiteSpace(invoice.InvoiceNumber));
            var journal = await context.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == invoice.JournalEntryId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Posted, journal.State);
            Assert.NotNull(journal.PostingSequence);
            Assert.Contains(invoice.InvoiceNumber!, journal.Description, StringComparison.Ordinal);
            var movement = await context.StockMovements.AsNoTracking()
                .SingleAsync(m => m.DocumentId == invoice.Id && m.DocumentType == "SalesInvoice", Cancellation).ConfigureAwait(true);
            Assert.Equal(invoice.InvoiceNumber, movement.DocumentNumber);
            Assert.Equal((int)StockMovementKind.SalesDelivery, movement.MovementKind);
            Assert.Equal(journal.BranchId, movement.BranchId);
            var postingRequest = await context.PostingRequests.AsNoTracking()
                .SingleAsync(r => r.JournalId == journal.Id && r.CompletedAtUtc != null, Cancellation).ConfigureAwait(true);
            Assert.Equal(journal.Id, postingRequest.OutcomeJournalId);
            var salesIntent = await context.SalesPostingRequests.AsNoTracking()
                .SingleAsync(r => r.InvoiceId == fixture.InvoiceId && r.CompletedAtUtc != null, Cancellation).ConfigureAwait(true);
            Assert.Equal(journal.Id, salesIntent.OutcomeJournalId);
            Assert.Equal(1, await context.JournalEntries.CountAsync(j => j.CompanyId == s.CompanyA, Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P28AUserWithoutBranchScopeCannotPostIntoThatBranch()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            Guid branchOnlyUser;
            using (var setup = database.Context())
            {
                branchOnlyUser = await CreateSecondUserAsync(setup, s.CompanyA, s.BranchA2, s.Actor).ConfigureAwait(true);
                await setup.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],N'Ledger.Journals.Post' FROM [security].[UserRoles] WHERE [UserId]={s.Actor}",
                    Cancellation).ConfigureAwait(true);
            }

            using (var context = database.Context())
            {
                await Assert.ThrowsAsync<PostingAuthorizationException>(() => Service(context).PostInvoiceAsync(
                    s.CompanyA, s.BranchA1, fixture.InvoiceId, branchOnlyUser, new PostSalesInvoiceCommand("p28-a1"), Cancellation))
                    .ConfigureAwait(true);
            }

            using (var verify = database.Context())
            {
                Assert.Equal(0, await verify.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(0, await verify.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));
                var invoice = await verify.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
                Assert.Equal((byte)SalesInvoiceStatus.Draft, invoice.Status);
            }

            // The same user may post in the branch they are scoped to, so the denial is branch-specific, not a blanket lockout.
            using (var context = database.Context())
            {
                var journalService = new SqlJournalEntryService(context, TimeProvider.System);
                var scope = JournalEntryScope.Create(s.CompanyA, s.BranchA2, branchOnlyUser);
                var draft = await journalService.CreateDraftAsync(scope, new CreateJournalDraftCommand
                {
                    AccountingDate = new DateOnly(2026, 10, 19),
                    Description = "قيد فرع أ2",
                    Lines = [LedgerScenario.Line(fixture.CashId, 3m, 0m, "USD"), LedgerScenario.Line(fixture.ReceivableId, 0m, 3m, "USD")],
                }, Cancellation).ConfigureAwait(true);
                var validated = await journalService.ValidateDraftAsync(scope, draft.Id,
                    new JournalDraftActionCommand { ExpectedVersion = LedgerScenario.Token(draft.VersionBase64), Reason = "تحقق" },
                    Cancellation).ConfigureAwait(true);
                var approved = await journalService.ApproveDraftAsync(scope, draft.Id,
                    new JournalDraftActionCommand { ExpectedVersion = LedgerScenario.Token(validated.VersionBase64), Reason = "اعتماد" },
                    Cancellation).ConfigureAwait(true);
                await new SqlPostingService(context).PostAsync(PostingRequest.Post(PostingRequestScope.Create(s.CompanyA, s.BranchA2, branchOnlyUser),
                    Guid.NewGuid(), draft.Id, ExpectedJournalVersion.FromBase64(approved.VersionBase64), AuditReason.Create("ترحيل فرع أ2")),
                    Cancellation).ConfigureAwait(true);
                var posted = await journalService.GetJournalAsync(scope, draft.Id, Cancellation).ConfigureAwait(true);
                Assert.Equal((byte)JournalEntryState.Posted, posted.State);
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P29InventoryDocumentsUseTheirOwnPostingWorkflowAndAreNeverCalledApproved()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            Guid documentId;
            using (var context = database.Context())
            {
                var inventoryAccount = await context.Accounts.AsNoTracking()
                    .SingleAsync(a => a.CompanyId == s.CompanyA && a.Code == "1211", Cancellation).ConfigureAwait(true);
                var offsetAccount = await context.Accounts.AsNoTracking()
                    .SingleAsync(a => a.CompanyId == s.CompanyA && a.Code == "5121", Cancellation).ConfigureAwait(true);
                var documents = new SqlInventoryDocumentService(context, TimeProvider.System);
                var draft = await documents.SaveAsync(new InventoryDocumentScope(s.CompanyA, s.BranchA1, s.Actor), null,
                    new SaveInventoryDocumentCommand(InventoryDocumentKind.Count, fixture.WarehouseId, null,
                        new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), "جرد قبول", null,
                        inventoryAccount.Id, offsetAccount.Id,
                        [new InventoryDocumentLineInput(fixture.ItemId, fixture.UnitId, 15m)], null)
                    { CreationKey = "p29-count" }, Cancellation).ConfigureAwait(true);
                Assert.Equal(InventoryDocumentState.Draft, draft.State);
                documentId = draft.Id;
            }

            BulkPostingBatchResult result;
            using (var context = database.Context())
            {
                var service = Bulk(context);
                var preview = await service.PreviewAsync(s.CompanyA, s.BranchA1, s.Actor, Filter(BulkPostingDocumentType.InventoryDocument), Cancellation).ConfigureAwait(true);
                var item = Assert.Single(preview.Items);
                Assert.Equal(BulkPostingClassification.ReadyForPosting, item.Classification);
                Assert.True(item.Postable);
                result = await service.StartAsync(s.CompanyA, s.BranchA1, s.Actor,
                    BulkCommand("p29-inventory", BulkPostingDocumentType.InventoryDocument), Cancellation).ConfigureAwait(true);
            }

            Assert.Equal(1, result.Posted);
            using var verify = database.Context();
            var document = await verify.InventoryDocuments.AsNoTracking().SingleAsync(d => d.Id == documentId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)InventoryDocumentState.Posted, document.State);
            Assert.NotNull(document.JournalId);
            Assert.False(string.IsNullOrWhiteSpace(document.Number));
            var movement = await verify.StockMovements.AsNoTracking()
                .SingleAsync(m => m.DocumentId == documentId && m.DocumentType == "InventoryDocumentIn", Cancellation).ConfigureAwait(true);
            Assert.Equal(5m, movement.BaseQuantity);
            Assert.Equal((int)StockMovementKind.AdjustmentIncrease, movement.MovementKind);
            var journal = await verify.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == document.JournalId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Posted, journal.State);
            var lines = await verify.JournalLines.AsNoTracking().Where(l => l.JournalId == journal.Id).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
            var page = await Bulk(verify).GetResultsAsync(s.CompanyA, s.BranchA1, result.BatchId, null, null, 1, 50, Cancellation).ConfigureAwait(true);
            var row = Assert.Single(page.Items);
            Assert.Equal(BulkPostingItemStatus.Posted, row.Status);
            Assert.Equal(nameof(InventoryDocumentState.Posted), row.NewStatus);
            Assert.DoesNotContain("Approved", row.NewStatus, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P30TrialBalanceReconcilesToThePostedSalesJournal()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            var posted = await Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new PostSalesInvoiceCommand("p30-trial"), Cancellation).ConfigureAwait(true);
            var journalLines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId)
                .ToListAsync(Cancellation).ConfigureAwait(true);

            var reports = new SqlLedgerQueryService(context);
            var trial = await reports.GetTrialBalanceAsync(LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false),
                new TrialBalanceQuery(null, new DateOnly(2026, 12, 31), null, null, false, null), Cancellation).ConfigureAwait(true);

            Assert.True(trial.IsBalanced);
            Assert.Equal(trial.TotalPeriodDebit, trial.TotalPeriodCredit);
            Assert.Equal(journalLines.Sum(l => l.BaseDebit), trial.TotalPeriodDebit);
            Assert.Equal(journalLines.Sum(l => l.BaseCredit), trial.TotalPeriodCredit);
            Assert.Equal(journalLines.Where(l => l.AccountId == fixture.ReceivableId).Sum(l => l.BaseDebit),
                Assert.Single(trial.Rows, row => row.Code == "1131").PeriodDebit);
            Assert.Equal(journalLines.Where(l => l.AccountId == fixture.CogsId).Sum(l => l.BaseDebit),
                Assert.Single(trial.Rows, row => row.Code == "5111").PeriodDebit);
            // Every account touched by the journal must reconcile to its trial-balance row, including the discount
            // and inventory accounts the earlier fixed subset omitted.
            var touchedIds = journalLines.Select(l => l.AccountId).Distinct().ToArray();
            var touched = await context.Accounts.AsNoTracking()
                .Where(a => touchedIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Code })
                .ToListAsync(Cancellation).ConfigureAwait(true);
            foreach (var account in touched)
            {
                var row = Assert.Single(trial.Rows, r => r.Code == account.Code);
                Assert.Equal(journalLines.Where(l => l.AccountId == account.Id).Sum(l => l.BaseDebit), row.PeriodDebit);
                Assert.Equal(journalLines.Where(l => l.AccountId == account.Id).Sum(l => l.BaseCredit), row.PeriodCredit);
            }

            var accountLedger = await reports.GetAccountLedgerAsync(LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false),
                new AccountLedgerQuery(fixture.ReceivableId, null, null, PageRequest.Create(1, 50)), Cancellation).ConfigureAwait(true);
            Assert.Equal(journalLines.Where(l => l.AccountId == fixture.ReceivableId).Sum(l => l.BaseDebit),
                accountLedger.PeriodDebit);
            Assert.Equal(0m, accountLedger.OpeningBalance);
            Assert.Equal(accountLedger.PeriodDebit - accountLedger.PeriodCredit, accountLedger.ClosingBalance);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P03SaveAsDraftStaysDraftInManualModeToo()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            var result = await Processing(context).CreateSalesInvoiceAsync(s.CompanyA, s.BranchA1, s.Actor, FullGrants,
                NewCommand(fixture), saveAsDraft: true, posting: new DocumentPostingIntent($"p03-manual-{Guid.NewGuid():N}"),
                Cancellation).ConfigureAwait(true);

            Assert.Equal(DocumentProcessingOutcome.SavedDraft, result.Outcome);
            Assert.Equal("ApproveOnly", result.EffectivePostingMode);
            Assert.False(result.ApprovalApplied);
            Assert.False(result.PostingApplied);
            var row = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == result.DocumentId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)SalesInvoiceStatus.Draft, row.Status);
            Assert.Equal(0, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P04InvalidDocumentIsRejectedBeforeAutomaticApprovalOrPosting()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            await SetPostingModeAsync(context, s.CompanyA, s.Actor, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);
            var invalid = NewCommand(fixture) with { Lines = [new(Guid.NewGuid(), fixture.UnitId, 2m, 100m)] };

            var failure = await Assert.ThrowsAsync<BusinessRuleException>(() => Processing(context).CreateSalesInvoiceAsync(
                s.CompanyA, s.BranchA1, s.Actor, FullGrants, invalid, saveAsDraft: false,
                posting: new DocumentPostingIntent($"p04-invalid-{Guid.NewGuid():N}"), Cancellation)).ConfigureAwait(true);

            Assert.Equal("sales.item_invalid", failure.Code);
            Assert.Equal(0, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await context.SalesInvoices.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await context.StockMovements.CountAsync(Cancellation).ConfigureAwait(true));
            var untouched = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)SalesInvoiceStatus.Draft, untouched.Status);
        }
    }

    private static async Task GrantAsync(PetraDbContext context, Guid actorId, string permission)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],{permission} FROM [security].[UserRoles] WHERE [UserId]={actorId}",
            Cancellation).ConfigureAwait(true);
    }

    private static async Task<SalesInvoiceDetailResult> CreateInvoiceAsync(PetraDbContext context, Fixture fixture, decimal quantity)
    {
        var s = fixture.Scenario;
        var first = await Service(context).GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
        return await Service(context).CreateInvoiceAsync(s.CompanyA, s.BranchA1, s.Actor, new CreateSalesInvoiceCommand(
            first.PatternRevisionId, first.CustomerId, first.WarehouseId, new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero),
            CurrencyCode: "SYP", ExchangeRate: 0.01m,
            Lines: [new(fixture.ItemId, fixture.UnitId, quantity, 100m)]), Cancellation).ConfigureAwait(true);
    }

    private static async Task<Guid> CreateSecondUserAsync(PetraDbContext context, Guid companyId, Guid branchId, Guid actorId)
    {
        var userId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var salt = new byte[32]; salt[0] = 1;
        var hash = new byte[64]; hash[0] = 1;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [security].[Users]([Id],[UserName],[NormalizedUserName],[DisplayNameArabic],[DisplayNameEnglish],[PasswordHash],[PasswordSalt],
                [PasswordIterations],[PasswordAlgorithm],[PasswordChangedAtUtc],[MustChangePassword],[IsActive],[AccessFailedCount],[SecurityStamp],[CreatedAtUtc],[UpdatedAtUtc])
               VALUES({userId},N'second_user',N'SECOND_USER',N'محاسب ثانٍ',N'Second accountant',{hash},{salt},100000,1,{now},0,1,0,{Guid.NewGuid()},{now},{now})",
            Cancellation).ConfigureAwait(true);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[UserRoles]([UserId],[RoleId]) SELECT {userId},[RoleId] FROM [security].[UserRoles] WHERE [UserId]={actorId}",
            Cancellation).ConfigureAwait(true);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[UserCompanyScopes]([UserId],[CompanyId]) VALUES({userId},{companyId})", Cancellation).ConfigureAwait(true);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[UserBranchScopes]([UserId],[CompanyId],[BranchId]) VALUES({userId},{companyId},{branchId})",
            Cancellation).ConfigureAwait(true);
        return userId;
    }
}
