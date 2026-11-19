using Microsoft.EntityFrameworkCore;

using PetraERP.Application.GeneralLedger.Posting;

using PetraERP.Application.Common;

using PetraERP.Application.GeneralLedger.ChartOfAccounts;

using PetraERP.Application.Inventory;

using PetraERP.Application.Parties;

using PetraERP.Application.Patterns;

using PetraERP.Application.Sales;

using PetraERP.Domain.GeneralLedger;

using PetraERP.Domain.Inventory;

using PetraERP.Domain.Parties;

using PetraERP.Domain.Patterns;

using PetraERP.Domain.Primitives;

using PetraERP.Domain.Sales;

using PetraERP.Infrastructure.Inventory;

using PetraERP.Infrastructure.Parties;

using PetraERP.Infrastructure.Patterns;

using PetraERP.Infrastructure.Persistence;

using PetraERP.Infrastructure.Sales;



namespace PetraERP.Infrastructure.Tests;



public sealed partial class SalesPostingSqlTests

{

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;



    [Theory(Explicit = true)]

    [InlineData(PaymentMethod.Credit)]

    [InlineData(PaymentMethod.Cash)]

    [Trait("Category", "SqlServer")]

    public async Task ForeignCurrencySalePostsReplaysAndReversesOriginalCostThroughLedger(PaymentMethod paymentMethod)

    {

        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);

        await using (database.ConfigureAwait(true))

        {

            var fixture = await CreateAsync(database, paymentMethod).ConfigureAwait(true);

            var scenario = fixture.Scenario;

            using var context = database.Context();

            var service = Service(context);

            var command = new PostSalesInvoiceCommand("sale-request-001", "ترحيل بيع موثق");

            var posted = await service.PostInvoiceAsync(scenario.CompanyA, scenario.BranchA1, fixture.InvoiceId,

                scenario.Actor, command, Cancellation).ConfigureAwait(true);

            Assert.Equal(SalesInvoiceStatus.Posted, posted.Status);

            var journal = await context.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == posted.JournalEntryId, Cancellation).ConfigureAwait(true);

            Assert.Equal((byte)JournalEntryState.Posted, journal.State);

            Assert.NotNull(journal.PostingSequence);

            Assert.Equal(4, await context.JournalAudit.CountAsync(a => a.JournalId == journal.Id, Cancellation).ConfigureAwait(true));

            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == journal.Id).ToListAsync(Cancellation).ConfigureAwait(true);

            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));

            var receivable = Assert.Single(lines, l => l.AccountId == (paymentMethod == PaymentMethod.Cash ? fixture.CashId : fixture.ReceivableId));

            Assert.Equal(210m, receivable.TransactionDebit);

            Assert.Equal("SYP", receivable.TransactionCurrencyCode);

            Assert.Equal("USD", receivable.BaseCurrencyCode);

            Assert.Equal(2.1m, receivable.BaseDebit);

            Assert.Equal(4m, Assert.Single(lines, l => l.AccountId == fixture.CogsId).BaseDebit);

            var delivery = await context.StockMovements.AsNoTracking().SingleAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true);

            Assert.Equal(-2m, delivery.BaseQuantity); Assert.Equal(4m, delivery.TotalCost);



            var replay = await service.PostInvoiceAsync(scenario.CompanyA, scenario.BranchA1, fixture.InvoiceId,

                scenario.Actor, command, Cancellation).ConfigureAwait(true);

            Assert.Equal(posted.JournalEntryId, replay.JournalEntryId);

            await Assert.ThrowsAsync<PostingIdempotencyConflictException>(() => service.PostInvoiceAsync(scenario.CompanyA,

                scenario.BranchA1, fixture.InvoiceId, scenario.Actor, command with { Notes = "طلب مختلف" }, Cancellation)).ConfigureAwait(true);

            Assert.Equal(1, await context.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));



            // A later receipt changes the current average; reversal must return the original authoritative cost.

            await LedgerScenario.AddStockMovementAsync(context, Receipt(scenario, fixture.ItemId, fixture.UnitId, fixture.WarehouseId, 10m, 10m), Cancellation).ConfigureAwait(true);

            var reversed = await service.ReverseInvoiceAsync(scenario.CompanyA, scenario.BranchA1, fixture.InvoiceId,

                scenario.Actor, new ReverseSalesInvoiceCommand("تصحيح بيع خاطئ"), Cancellation).ConfigureAwait(true);

            Assert.Equal(SalesInvoiceStatus.Reversed, reversed.Status);

            var reversalInvoice = await context.SalesInvoices.AsNoTracking().Include(i => i.Lines)

                .SingleAsync(i => i.Id == reversed.ReversedByInvoiceId, Cancellation).ConfigureAwait(true);

            Assert.Equal(fixture.InvoiceId, reversalInvoice.ReversesInvoiceId);

            Assert.All(reversalInvoice.Lines, l => Assert.True(l.Quantity > 0));

            var returned = await context.StockMovements.AsNoTracking().SingleAsync(m => m.DocumentId == reversalInvoice.Id, Cancellation).ConfigureAwait(true);

            Assert.Equal(2m, returned.BaseQuantity); Assert.Equal(delivery.TotalCost, returned.TotalCost);

            var original = await context.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == journal.Id, Cancellation).ConfigureAwait(true);

            Assert.Equal((byte)JournalEntryState.Reversed, original.State);

            Assert.Equal(reversalInvoice.JournalEntryId, original.ReversedByEntryId);

            var reversalLines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == original.ReversedByEntryId).ToListAsync(Cancellation).ConfigureAwait(true);

            Assert.All(reversalLines, l =>

            {

                var source = Assert.Single(lines, originalLine => originalLine.Id == l.ReversesLineId);

                Assert.Equal(source.BaseDebit, l.BaseCredit); Assert.Equal(source.BaseCredit, l.BaseDebit);

            });

            Assert.Equal(2, await context.PostingRequests.CountAsync(p => p.CompletedAtUtc != null, Cancellation).ConfigureAwait(true));

            Assert.Equal(2, await context.SalesPostingRequests.CountAsync(p => p.CompletedAtUtc != null, Cancellation).ConfigureAwait(true));

            var retryReversal = await service.ReverseInvoiceAsync(scenario.CompanyA, scenario.BranchA1, fixture.InvoiceId,

                scenario.Actor, new ReverseSalesInvoiceCommand("تصحيح بيع خاطئ"), Cancellation).ConfigureAwait(true);

            Assert.Equal(reversed.ReversedByInvoiceId, retryReversal.ReversedByInvoiceId);

            Assert.Equal(1, await context.StockMovements.CountAsync(m => m.DocumentId == reversalInvoice.Id, Cancellation).ConfigureAwait(true));

        }

    }



    [Fact(Explicit = true)]

    [Trait("Category", "SqlServer")]

    public async Task DeniedPostingRollsBackStockJournalNumberAndReceipt()

    {

        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);

        await using (database.ConfigureAwait(true))

        {

            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;

            using var context = database.Context();

            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [security].[RolePermissions] WHERE [PermissionCode]=N'Sales.Invoices.Post' AND [RoleId] IN (SELECT [RoleId] FROM [security].[UserRoles] WHERE [UserId]={s.Actor})", Cancellation).ConfigureAwait(true);

            await Assert.ThrowsAsync<PostingAuthorizationException>(() => Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,

                new PostSalesInvoiceCommand("denied"), Cancellation)).ConfigureAwait(true);

            var draft = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);

            Assert.Equal((int)SalesInvoiceStatus.Draft, draft.Status); Assert.Null(draft.InvoiceNumber); Assert.Null(draft.JournalEntryId);

            Assert.Equal(0, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));

            Assert.Equal(0, await context.PostingRequests.CountAsync(Cancellation).ConfigureAwait(true));

            Assert.Equal(0, await context.PatternNumberSequences.CountAsync(Cancellation).ConfigureAwait(true));

            Assert.Equal(0, await context.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));

        }

    }



    [Fact(Explicit = true)]

    [Trait("Category", "SqlServer")]

    public async Task ConcurrentSalesCannotOversellTheSameStock()

    {

        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);

        await using (database.ConfigureAwait(true))

        {

            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;

            Guid secondId;

            using (var context = database.Context())

            {

                await LedgerScenario.AddStockMovementAsync(context, new StockMovementRow

                {

                    Id = Guid.NewGuid(), CompanyId = s.CompanyA, BranchId = s.BranchA1, WarehouseId = fixture.WarehouseId, ItemId = fixture.ItemId, UnitId = fixture.UnitId,

                    MovementKind = (int)StockMovementKind.AdjustmentDecrease, Quantity = 7, BaseQuantity = -7, UnitCost = 2, TotalCost = 14,

                    MovementDateUtc = DateTimeOffset.UtcNow, DocumentType = "Adjustment", DocumentId = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = s.Actor

                }, Cancellation).ConfigureAwait(true);

                var first = await Service(context).GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);

                var second = await Service(context).CreateInvoiceAsync(s.CompanyA, s.BranchA1, s.Actor, new CreateSalesInvoiceCommand(

                    first.PatternRevisionId, first.CustomerId, first.WarehouseId, first.InvoiceDateUtc, CurrencyCode: "USD",

                    Lines: [new(fixture.ItemId, fixture.UnitId, 2, 10)]), Cancellation).ConfigureAwait(true);

                secondId = second.Id;

            }

            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<bool> PostAsync(Guid id)

            {

                using var context = database.Context(); await ready.Task.ConfigureAwait(true);

                try { await Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, id, s.Actor, new PostSalesInvoiceCommand(id.ToString("N")), Cancellation).ConfigureAwait(true); return true; }

                catch (BusinessRuleException ex) when (ex.Code == "inventory.negative_stock_blocked") { return false; }

            }

            var firstTask = PostAsync(fixture.InvoiceId); var secondTask = PostAsync(secondId); ready.SetResult();

            var results = await Task.WhenAll(firstTask, secondTask).ConfigureAwait(true);

            Assert.Single(results, r => r); Assert.Single(results, r => !r);

            using var verify = database.Context();

            Assert.Equal(1m, await verify.StockMovements.SumAsync(m => m.BaseQuantity, Cancellation).ConfigureAwait(true));

            Assert.Equal(1, await verify.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));

            Assert.Equal(1, await verify.PostingRequests.CountAsync(Cancellation).ConfigureAwait(true));

        }

    }



    private static SqlSalesInvoiceService Service(PetraDbContext context) => new(context, new SqlPatternService(context), TimeProvider.System);



    [Fact(Explicit = true)]

    [Trait("Category", "SqlServer")]

    public async Task PostingUsesFrozenRevisionAfterANewRevisionIsPublished()

    {

        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);

        await using (database.ConfigureAwait(true))

        {

            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;

            using var context = database.Context();

            var invoice = await Service(context).GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);

            var revision = await context.PatternRevisions.AsNoTracking().SingleAsync(r => r.Id == invoice.PatternRevisionId, Cancellation).ConfigureAwait(true);

            var service = new SqlPatternService(context); var scope = PatternScope.Create(s.CompanyA, s.BranchA1, s.Actor);

            var pattern = await service.GetPatternAsync(scope, revision.PatternId, Cancellation).ConfigureAwait(true);

            var next = await service.CreateRevisionAsync(scope, pattern.Id, new CreatePatternRevisionCommand

            { ExpectedVersion = pattern.Version, Reason = "نسخة لاحقة", Configuration = pattern.ActiveRevision!.Configuration with { NumberingPolicy = new() { Prefix = "NEW-" } } }, Cancellation).ConfigureAwait(true);

            pattern = await service.GetPatternAsync(scope, pattern.Id, Cancellation).ConfigureAwait(true);

            await service.PublishRevisionAsync(scope, pattern.Id, new PublishPatternRevisionCommand

            { ExpectedVersion = pattern.Version, RevisionId = next.Id, Reason = "نشر النسخة اللاحقة" }, Cancellation).ConfigureAwait(true);

            var posted = await Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, new PostSalesInvoiceCommand("frozen"), Cancellation).ConfigureAwait(true);

            Assert.Equal("SI-0001", posted.InvoiceNumber); Assert.Equal(invoice.PatternRevisionId, posted.PatternRevisionId);

        }

    }



    [Fact(Explicit = true)]

    [Trait("Category", "SqlServer")]

    public async Task TenConcurrentIdenticalRequestsProduceOneInvoiceJournalAndMovement()

    {

        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);

        await using (database.ConfigureAwait(true))

        {

            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;

            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<Guid?> PostAsync()

            {

                using var context = database.Context(); await ready.Task.ConfigureAwait(true);

                return (await Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, new PostSalesInvoiceCommand("same-request"), Cancellation).ConfigureAwait(true)).JournalEntryId;

            }

            var tasks = Enumerable.Range(0, 10).Select(_ => PostAsync()).ToArray(); ready.SetResult();

            var results = await Task.WhenAll(tasks).ConfigureAwait(true);

            Assert.Single(results.Distinct()); Assert.NotNull(results[0]);

            using var verify = database.Context(); Assert.Equal(1, await verify.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));

            Assert.Equal(1, await verify.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));

            Assert.Equal(1, await verify.PostingRequests.CountAsync(Cancellation).ConfigureAwait(true));

        }

    }



    [Fact(Explicit = true)]

    [Trait("Category", "SqlServer")]

    public async Task CashSaleCannotFallBackToTheCustomersReceivableAccount()

    {

        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);

        await using (database.ConfigureAwait(true))

        {

            var fixture = await CreateAsync(database, includeCashMapping: false).ConfigureAwait(true); var s = fixture.Scenario;

            using var context = database.Context();

            var invoice = await context.SalesInvoices.SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);

            invoice.PaymentMethod = (int)PaymentMethod.Cash; await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,

                new PostSalesInvoiceCommand("cash-mapping-missing"), Cancellation)).ConfigureAwait(true);

            Assert.Equal(RuleViolation.PatternAccountUnresolved, RuleViolation.CodeOf(error));

            Assert.Equal(0, await context.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));

            Assert.Equal(0, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));

            var intent = await context.SalesPostingRequests.AsNoTracking().SingleAsync(Cancellation).ConfigureAwait(true);

            Assert.Null(intent.CompletedAtUtc); Assert.Null(intent.OutcomeJournalId);

            await Assert.ThrowsAsync<PostingIdempotencyConflictException>(() => Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1,

                fixture.InvoiceId, s.Actor, new PostSalesInvoiceCommand("cash-mapping-missing", "محتوى مختلف بعد الفشل"), Cancellation)).ConfigureAwait(true);

            invoice = await context.SalesInvoices.SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);

            invoice.PaymentMethod = (int)PaymentMethod.Credit;

            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);

            await Assert.ThrowsAsync<PostingConcurrencyException>(() => Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1,

                fixture.InvoiceId, s.Actor, new PostSalesInvoiceCommand("cash-mapping-missing"), Cancellation)).ConfigureAwait(true);

            var corrected = await Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,

                new PostSalesInvoiceCommand("corrected-credit-intent"), Cancellation).ConfigureAwait(true);

            Assert.Equal(SalesInvoiceStatus.Posted, corrected.Status);

        }

    }



    [Fact(Explicit = true)]

    [Trait("Category", "SqlServer")]

    public async Task LedgerValidationFailureRetainsIntentButRollsBackAllFinancialEffectsAndCanResume()

    {

        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);

        await using (database.ConfigureAwait(true))

        {

            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;

            using var context = database.Context();

            var account = await context.Accounts.SingleAsync(a => a.Id == fixture.CogsId, Cancellation).ConfigureAwait(true);

            account.IsActive = false;

            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);

            var command = new PostSalesInvoiceCommand("validation-failure");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context).PostInvoiceAsync(s.CompanyA,

                s.BranchA1, fixture.InvoiceId, s.Actor, command, Cancellation)).ConfigureAwait(true);

            Assert.Equal(RuleViolation.AccountInvalid, RuleViolation.CodeOf(exception));

            Assert.Equal(0, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));

            Assert.Equal(0, await context.JournalAudit.CountAsync(Cancellation).ConfigureAwait(true));

            Assert.Equal(0, await context.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));

            Assert.Equal(0, await context.PatternNumberSequences.CountAsync(Cancellation).ConfigureAwait(true));

            var intent = await context.SalesPostingRequests.AsNoTracking().SingleAsync(Cancellation).ConfigureAwait(true);

            Assert.Null(intent.CompletedAtUtc);

            var draft = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);

            Assert.Null(draft.InvoiceNumber); Assert.Null(draft.JournalEntryId);

            account = await context.Accounts.SingleAsync(a => a.Id == fixture.CogsId, Cancellation).ConfigureAwait(true);

            account.IsActive = true;

            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);

            var posted = await Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, command, Cancellation).ConfigureAwait(true);

            Assert.Equal(SalesInvoiceStatus.Posted, posted.Status);

            Assert.Equal(1, await context.SalesPostingRequests.CountAsync(r => r.CompletedAtUtc != null, Cancellation).ConfigureAwait(true));

        }

    }



    private sealed record Fixture(LedgerScenario Scenario, Guid InvoiceId, Guid ItemId, Guid UnitId, Guid WarehouseId, Guid ReceivableId, Guid CogsId, Guid CashId);



    [Fact(Explicit = true)]

    [Trait("Category", "SqlServer")]

    public async Task TenDistinctConcurrentInvoicesHaveUniqueNumbersAfterAManualJournal()

    {

        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);

        await using (database.ConfigureAwait(true))

        {

            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;

            var ids = new List<Guid> { fixture.InvoiceId };

            using (var context = database.Context())

            {

                await LedgerScenario.AddStockMovementAsync(context, new StockMovementRow
                {
                    Id = Guid.NewGuid(), CompanyId = s.CompanyA, BranchId = s.BranchA1, WarehouseId = fixture.WarehouseId, ItemId = fixture.ItemId, UnitId = fixture.UnitId,
                    MovementKind = (int)StockMovementKind.AdjustmentIncrease, Quantity = 90, BaseQuantity = 90, UnitCost = 2, TotalCost = 180,
                    MovementDateUtc = DateTimeOffset.UtcNow, DocumentType = "Adjustment", DocumentId = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = s.Actor
                }, Cancellation).ConfigureAwait(true);

                await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],N'Ledger.Journals.Post' FROM [security].[UserRoles] WHERE [UserId]={s.Actor}", Cancellation).ConfigureAwait(true);

                await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 10, 19), "USD", true,

                    (fixture.CashId, 1m, 0m), (fixture.ReceivableId, 0m, 1m)).ConfigureAwait(true);

                await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [security].[RolePermissions] WHERE [PermissionCode]=N'Ledger.Journals.Post' AND [RoleId] IN (SELECT [RoleId] FROM [security].[UserRoles] WHERE [UserId]={s.Actor})", Cancellation).ConfigureAwait(true);

                var first = await Service(context).GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);

                for (var index = 1; index < 10; index++)

                {

                    var invoice = await Service(context).CreateInvoiceAsync(s.CompanyA, s.BranchA1, s.Actor,

                        new CreateSalesInvoiceCommand(first.PatternRevisionId, first.CustomerId, first.WarehouseId, first.InvoiceDateUtc,

                            CurrencyCode: "USD", Lines: [new(fixture.ItemId, fixture.UnitId, 2, 10)]), Cancellation).ConfigureAwait(true);

                    ids.Add(invoice.Id);

                }

            }

            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<SalesInvoiceDetailResult> PostAsync(Guid id)

            {

                using var context = database.Context(); await ready.Task.ConfigureAwait(true);

                return await Service(context).PostInvoiceAsync(s.CompanyA, s.BranchA1, id, s.Actor, new PostSalesInvoiceCommand(id.ToString("N")), Cancellation).ConfigureAwait(true);

            }

            var tasks = ids.Select(PostAsync).ToArray(); ready.SetResult();

            var posted = await Task.WhenAll(tasks).ConfigureAwait(true);

            Assert.Equal(10, posted.Select(i => i.InvoiceNumber).Distinct().Count());

            Assert.All(posted, invoice =>
            {
                Assert.Equal(SalesInvoiceStatus.Posted, invoice.Status);
                Assert.False(string.IsNullOrWhiteSpace(invoice.InvoiceNumber));
                Assert.NotNull(invoice.JournalEntryId);
            });

            using var verify = database.Context();

            var journals = await verify.JournalEntries.AsNoTracking().Where(j => posted.Select(i => i.JournalEntryId).Contains(j.Id)).ToListAsync(Cancellation).ConfigureAwait(true);

            Assert.Equal(10, journals.Count); Assert.Equal(10, journals.Select(j => j.PostingSequence).Distinct().Count());

            Assert.All(journals, j => Assert.True(j.PostingSequence > 1));

            var deliveries = await verify.StockMovements.AsNoTracking().Where(m => m.DocumentId.HasValue && ids.Contains(m.DocumentId.Value))
                .ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(10, deliveries.Count);
            Assert.Equal(-20m, deliveries.Sum(m => m.BaseQuantity));
            Assert.Equal(40m, deliveries.Sum(m => m.TotalCost));
            Assert.All(posted, invoice => Assert.Equal(invoice.InvoiceNumber, Assert.Single(deliveries, m => m.DocumentId == invoice.Id).DocumentNumber));
            Assert.Equal(80m, await verify.StockMovements.SumAsync(m => m.BaseQuantity, Cancellation).ConfigureAwait(true));

        }

    }



    [Fact(Explicit = true)]

    [Trait("Category", "SqlServer")]

    public async Task LineCostCentersArePreservedInRevenueReceivableAndCostPosting()

    {

        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);

        await using (database.ConfigureAwait(true))

        {

            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;

            using var context = database.Context(); var chart = LedgerScenario.Chart(context);

            var first = await chart.CreateCostCenterAsync(s.Master(s.CompanyA), new CostCenterInput { Code = "A", NameArabic = "مركز أ" }, Cancellation).ConfigureAwait(true);

            var second = await chart.CreateCostCenterAsync(s.Master(s.CompanyA), new CostCenterInput { Code = "B", NameArabic = "مركز ب" }, Cancellation).ConfigureAwait(true);

            var service = Service(context);

            var current = await service.GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);

            await service.UpdateInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, new UpdateSalesInvoiceCommand(current.CustomerId, current.WarehouseId, current.InvoiceDateUtc,

                CurrencyCode: "USD", Lines: [new(fixture.ItemId, fixture.UnitId, 1, 10, CostCenterId: first.Id), new(fixture.ItemId, fixture.UnitId, 1, 10, CostCenterId: second.Id)])

                { ExpectedVersion = ConcurrencyToken.FromBytes(current.RowVersion) }, Cancellation).ConfigureAwait(true);

            var posted = await service.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, new PostSalesInvoiceCommand("centers"), Cancellation).ConfigureAwait(true);

            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);

            foreach (var center in new[] { first.Id, second.Id })

            {

                var group = lines.Where(l => l.CostCenterId == center).ToArray(); Assert.Equal(4, group.Length);

                Assert.Equal(10m, Assert.Single(group, l => l.AccountId == fixture.ReceivableId).BaseDebit);

                Assert.Equal(2m, Assert.Single(group, l => l.AccountId == fixture.CogsId).BaseDebit);

                Assert.Equal(group.Sum(l => l.BaseDebit), group.Sum(l => l.BaseCredit));

            }

        }

    }



    /// <summary>Al-Ameen journal detail: with the option on, two invoice lines of one item give two revenue lines in the same single journal; off, they merge.</summary>
    [Theory(Explicit = true)]
    [Trait("Category", "SqlServer")]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task DetailedJournalLinesSplitRevenuePerInvoiceLineWithinOneJournal(bool detailed, int expectedRevenueLines)
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database, detailedJournalLines: detailed).ConfigureAwait(true); var s = fixture.Scenario;
            using var context = database.Context();
            var service = Service(context);
            var current = await service.GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            await service.UpdateInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, new UpdateSalesInvoiceCommand(current.CustomerId, current.WarehouseId, current.InvoiceDateUtc,
                CurrencyCode: "USD", Lines: [new(fixture.ItemId, fixture.UnitId, 1, 10), new(fixture.ItemId, fixture.UnitId, 2, 10)])
                { ExpectedVersion = ConcurrencyToken.FromBytes(current.RowVersion) }, Cancellation).ConfigureAwait(true);
            var posted = await service.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, new PostSalesInvoiceCommand("detail-" + detailed), Cancellation).ConfigureAwait(true);
            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(expectedRevenueLines, lines.Count(l => l.BaseCredit > 0 && l.Description == "إيراد البيع"));
            Assert.Equal(30m, lines.Where(l => l.AccountId == fixture.ReceivableId).Sum(l => l.BaseDebit));
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
        }
    }

    /// <summary>Al-Ameen sales shape: the line discount is its own credit on the receivable; the customer's net movement is unchanged.</summary>
    [Theory(Explicit = true)]
    [Trait("Category", "SqlServer")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeparateDiscountMovementDebitsTheGrossAndCreditsTheDiscountOnTheReceivable(bool separate)
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database, separateDiscountMovement: separate).ConfigureAwait(true); var s = fixture.Scenario;
            using var context = database.Context();
            var service = Service(context);
            var current = await service.GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            await service.UpdateInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, new UpdateSalesInvoiceCommand(current.CustomerId, current.WarehouseId, current.InvoiceDateUtc,
                CurrencyCode: "USD", Lines: [new(fixture.ItemId, fixture.UnitId, 1, 10, DiscountAmount: 2m)])
                { ExpectedVersion = ConcurrencyToken.FromBytes(current.RowVersion) }, Cancellation).ConfigureAwait(true);
            var posted = await service.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, new PostSalesInvoiceCommand("disc-" + separate), Cancellation).ConfigureAwait(true);
            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            var receivable = lines.Where(l => l.AccountId == fixture.ReceivableId).ToArray();
            Assert.Equal(separate ? 2 : 1, receivable.Length);
            Assert.Equal(8m, receivable.Sum(l => l.BaseDebit - l.BaseCredit));
            Assert.Equal(separate ? 10m : 8m, receivable.Sum(l => l.BaseDebit));
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
        }
    }

    /// <summary>Marks the cash and customer roles as not enforced, which is what lets a document name its own account.</summary>
    private static List<PatternAccountMapping> Overridable(bool allow, List<PatternAccountMapping> mappings) =>
        allow ? mappings.Select(m => m.Role is "Cash" or "Customer" ? m with { AllowOverride = true } : m).ToList() : mappings;

    private static async Task<Fixture> CreateAsync(SqlTestDatabase database, PaymentMethod paymentMethod = PaymentMethod.Credit, bool includeCashMapping = true, bool allowAccountOverride = false, bool detailedJournalLines = false, bool separateDiscountMovement = false)

    {

        var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);

        using var context = database.Context();

        // A sales operator need not possess manual GL posting permission.

        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],N'Sales.Invoices.Post' FROM [security].[UserRoles] WHERE [UserId]={scenario.Actor}", Cancellation).ConfigureAwait(true);

        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],N'Sales.Invoices.Reverse' FROM [security].[UserRoles] WHERE [UserId]={scenario.Actor}", Cancellation).ConfigureAwait(true);

        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [security].[RolePermissions] WHERE [PermissionCode] IN (N'Ledger.Journals.Post',N'Ledger.Journals.Reverse') AND [RoleId] IN (SELECT [RoleId] FROM [security].[UserRoles] WHERE [UserId]={scenario.Actor})", Cancellation).ConfigureAwait(true);

        var ar = await scenario.AccountAsync(scenario.CompanyA, "1131", AccountTypeCode.Assets, true).ConfigureAwait(true);

        var cash = await scenario.AccountAsync(scenario.CompanyA, "1011", AccountTypeCode.Assets, true).ConfigureAwait(true);

        var revenue = await scenario.AccountAsync(scenario.CompanyA, "4111", AccountTypeCode.Revenue, true).ConfigureAwait(true);

        var cogs = await scenario.AccountAsync(scenario.CompanyA, "5111", AccountTypeCode.Expenses, true).ConfigureAwait(true);

        var inventoryAccount = await scenario.AccountAsync(scenario.CompanyA, "1211", AccountTypeCode.Assets, true).ConfigureAwait(true);

        var discount = await scenario.AccountAsync(scenario.CompanyA, "5121", AccountTypeCode.Expenses, true).ConfigureAwait(true);

        var tax = await scenario.AccountAsync(scenario.CompanyA, "2111", AccountTypeCode.Liabilities, true).ConfigureAwait(true);

        var inventory = new SqlInventoryService(context);

        var unit = await inventory.CreateUnitAsync(scenario.CompanyA, new CreateUnitCommand("PCS", "قطعة", null, "pc", 0), Cancellation).ConfigureAwait(true);

        var warehouse = await inventory.CreateWarehouseAsync(scenario.CompanyA, new CreateWarehouseCommand(scenario.BranchA1, "WH", "مستودع البيع", null, null), Cancellation).ConfigureAwait(true);

        var item = await inventory.CreateItemAsync(scenario.CompanyA, new CreateItemCommand("I1", "مادة اختبار", null, null, unit.Id, warehouse.Id, CostMethod.WeightedAverage, null, null, null), Cancellation).ConfigureAwait(true);

        await LedgerScenario.AddStockMovementAsync(context, Receipt(scenario, item.Id, unit.Id, warehouse.Id, 10m, 2m), Cancellation).ConfigureAwait(true);

        var customer = await new SqlPartyService(context).CreatePartyAsync(scenario.CompanyA, new CreatePartyCommand("C1", "عميل الاختبار", null, PartyRole.Customer, ReceivableAccountId: ar.Id), Cancellation).ConfigureAwait(true);

        var patterns = new SqlPatternService(context);

        var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);

        var pattern = await patterns.CreatePatternAsync(scope, new CreateDocumentPatternCommand

        {

            Code = "SI",

            NameArabic = "بيع",

            ShortCode = "SI",

            OperationKind = BusinessOperationKind.SalesInvoice,

            BranchIds = [scenario.BranchA1],

            InitialConfiguration = new PatternConfiguration

            {

                AccountMappings = Overridable(allowAccountOverride, includeCashMapping

                    ? [new(){Role="Customer",AccountId=ar.Id},new(){Role="Sales",AccountId=revenue.Id},

                        new(){Role="CostOfGoods",AccountId=cogs.Id},new(){Role="Inventory",AccountId=inventoryAccount.Id},

                        new(){Role="Discount",AccountId=discount.Id},new(){Role="Cash",AccountId=cash.Id},new(){Role="Tax",AccountId=tax.Id}]

                    : [new(){Role="Customer",AccountId=ar.Id},new(){Role="Sales",AccountId=revenue.Id},

                        new(){Role="CostOfGoods",AccountId=cogs.Id},new(){Role="Inventory",AccountId=inventoryAccount.Id},

                        new(){Role="Discount",AccountId=discount.Id},new(){Role="Tax",AccountId=tax.Id}]),

                NumberingPolicy = new() { Prefix = "SI-", MinimumDigits = 4 },

                EntryPolicy = new() { DetailedJournalLines = detailedJournalLines, SeparateDiscountMovement = separateDiscountMovement }

            }

        }, Cancellation).ConfigureAwait(true);

        var published = await patterns.PublishRevisionAsync(scope, pattern.Id, new PublishPatternRevisionCommand

        { ExpectedVersion = pattern.Version, RevisionId = pattern.ActiveRevision!.Id, Reason = "نشر" }, Cancellation).ConfigureAwait(true);

        var invoice = await Service(context).CreateInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,

            new CreateSalesInvoiceCommand(published.ActiveRevision!.Id, customer.Id, warehouse.Id, new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero),

                PaymentMethod: paymentMethod, CurrencyCode: "SYP", ExchangeRate: 0.01m, Lines: [new(item.Id, unit.Id, 2m, 100m, DiscountAmount: 20m, TaxAmount: 18m, PumpCost: 5m, TransportCost: 7m)]), Cancellation).ConfigureAwait(true);

        return new(scenario, invoice.Id, item.Id, unit.Id, warehouse.Id, ar.Id, cogs.Id, cash.Id);

    }



    private static StockMovementRow Receipt(LedgerScenario s, Guid item, Guid unit, Guid warehouse, decimal quantity, decimal cost) => new()

    {

        Id = Guid.NewGuid(),

        CompanyId = s.CompanyA,

        BranchId = s.BranchA1,

        ItemId = item,

        UnitId = unit,

        WarehouseId = warehouse,

        MovementKind = (int)StockMovementKind.OpeningBalance,

        Quantity = quantity,

        BaseQuantity = quantity,

        UnitCost = cost,

        TotalCost = quantity * cost,

        MovementDateUtc = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero),

        DocumentType = "OpeningBalance",

        DocumentId = Guid.NewGuid(),

        CreatedAtUtc = DateTimeOffset.UtcNow,

        CreatedByUserId = s.Actor

    };

}

