using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.DocumentProcessing;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Inventory;
using PetraERP.Application.Parties;
using PetraERP.Application.Patterns;
using PetraERP.Application.Sales;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Parties;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.DocumentProcessing;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Sales;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests;

public sealed class BulkDocumentPostingSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PreviewClassifiesDraftAndConfirmedBatchPostsFinancialAndStockEffects()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            var service = Bulk(context);
            var filter = SalesOnly();

            var preview = await service.PreviewAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor, filter,
                Cancellation).ConfigureAwait(true);
            var previewItem = Assert.Single(preview.Items);
            Assert.Equal(BulkPostingDocumentType.SalesInvoice, previewItem.DocumentType);
            Assert.Equal(BulkPostingClassification.CanBeApprovedAutomatically, previewItem.Classification);
            Assert.True(previewItem.Postable);
            Assert.Equal(1, preview.NeedsApproval);
            Assert.Equal(0, preview.ReadyToPost);
            Assert.Equal(0, preview.Ineligible);

            var result = await service.StartAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                Command("bulk-first", filter), Cancellation).ConfigureAwait(true);
            Assert.Equal(BulkPostingBatchStatus.Completed, result.Status);
            Assert.Equal(1, result.TotalExamined);
            Assert.Equal(1, result.Eligible);
            Assert.Equal(1, result.Posted);
            Assert.Equal(0, result.AlreadyPosted);
            Assert.Equal(0, result.Skipped);
            Assert.Equal(0, result.Failed);
            Assert.Equal(0, result.Unknown);
            Assert.NotNull(result.CompletedAtUtc);
            Assert.Contains("رُحّل 1", result.Message, StringComparison.Ordinal);

            context.ChangeTracker.Clear();
            var invoice = await context.SalesInvoices.AsNoTracking()
                .SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)SalesInvoiceStatus.Posted, invoice.Status);
            Assert.NotNull(invoice.JournalEntryId);

            var journal = await context.JournalEntries.AsNoTracking()
                .SingleAsync(j => j.Id == invoice.JournalEntryId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Posted, journal.State);
            Assert.NotNull(journal.PostingSequence);
            var lines = await context.JournalLines.AsNoTracking()
                .Where(l => l.JournalId == journal.Id).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
            Assert.Equal(1, await context.JournalEntries.CountAsync(j => j.CompanyId == scenario.CompanyA, Cancellation)
                .ConfigureAwait(true));
            Assert.Equal(1, await context.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation)
                .ConfigureAwait(true));

            var page = await service.GetResultsAsync(scenario.CompanyA, scenario.BranchA1, result.BatchId, null, null,
                1, 50, Cancellation).ConfigureAwait(true);
            var row = Assert.Single(page.Items);
            Assert.Equal(BulkPostingItemStatus.Posted, row.Status);
            Assert.Equal(fixture.InvoiceId, row.DocumentId);
            Assert.Equal(journal.Id, row.JournalEntryId);
            Assert.NotNull(row.PostingNumber);
            Assert.Equal(1, row.AttemptCount);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ReplayWithSameKeyDoesNotDuplicateAndFinalBatchSkipsAlreadyPosted()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            var service = Bulk(context);
            var filter = SalesOnly();
            var command = Command("bulk-replay", filter);

            var first = await service.StartAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor, command,
                Cancellation).ConfigureAwait(true);
            Assert.Equal(BulkPostingBatchStatus.Completed, first.Status);
            Assert.Equal(1, first.Posted);

            var replay = await service.StartAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor, command,
                Cancellation).ConfigureAwait(true);
            Assert.Equal(first.BatchId, replay.BatchId);
            Assert.Equal(1, replay.Posted);
            Assert.Equal(1, await context.JournalEntries.CountAsync(j => j.CompanyId == scenario.CompanyA, Cancellation)
                .ConfigureAwait(true));
            Assert.Equal(1, await context.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation)
                .ConfigureAwait(true));

            var replayPage = await service.GetResultsAsync(scenario.CompanyA, scenario.BranchA1, first.BatchId, null, null,
                1, 50, Cancellation).ConfigureAwait(true);
            Assert.Equal(1, Assert.Single(replayPage.Items).AttemptCount);

            var second = await service.StartAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                Command("bulk-after-posted", filter), Cancellation).ConfigureAwait(true);
            Assert.Equal(BulkPostingBatchStatus.Completed, second.Status);
            Assert.Equal(1, second.AlreadyPosted);
            Assert.Equal(0, second.Posted);
            Assert.Equal(1, second.Skipped);
            Assert.Equal(1, await context.JournalEntries.CountAsync(j => j.CompanyId == scenario.CompanyA, Cancellation)
                .ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MissingApprovalPermissionIsClassifiedAndNeverPosted()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [security].[RolePermissions] WHERE [PermissionCode] IN (N'Sales.Invoices.Approve',N'Sales.Invoices.Post') AND [RoleId] IN (SELECT [RoleId] FROM [security].[UserRoles] WHERE [UserId]={scenario.Actor})",
                Cancellation).ConfigureAwait(true);
            var service = Bulk(context);
            var filter = SalesOnly();

            var preview = await service.PreviewAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor, filter,
                Cancellation).ConfigureAwait(true);
            var item = Assert.Single(preview.Items);
            Assert.Equal(BulkPostingClassification.MissingPermission, item.Classification);
            Assert.False(item.Postable);

            var result = await service.StartAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                Command("bulk-denied", filter), Cancellation).ConfigureAwait(true);
            Assert.Equal(BulkPostingBatchStatus.Completed, result.Status);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(0, result.Posted);
            Assert.Equal(0, result.Failed);

            context.ChangeTracker.Clear();
            var invoice = await context.SalesInvoices.AsNoTracking()
                .SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)SalesInvoiceStatus.Draft, invoice.Status);
            Assert.Null(invoice.JournalEntryId);
            Assert.Equal(0, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(0, await context.StockMovements.CountAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation)
                .ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ClosedPeriodBlocksClassificationAndPosting()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            var closed = await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [ledger].[AccountingPeriods] SET [State]=2 WHERE [CompanyId]={scenario.CompanyA} AND [RangeStart]<=N'2026-10-19' AND [RangeEnd]>=N'2026-10-19'",
                Cancellation).ConfigureAwait(true);
            Assert.Equal(1, closed);
            var service = Bulk(context);
            var filter = SalesOnly();

            var preview = await service.PreviewAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor, filter,
                Cancellation).ConfigureAwait(true);
            var item = Assert.Single(preview.Items);
            Assert.Equal(BulkPostingClassification.ClosedAccountingPeriod, item.Classification);
            Assert.False(item.Postable);

            var result = await service.StartAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                Command("bulk-closed", filter), Cancellation).ConfigureAwait(true);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(0, result.Posted);

            context.ChangeTracker.Clear();
            var invoice = await context.SalesInvoices.AsNoTracking()
                .SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)SalesInvoiceStatus.Draft, invoice.Status);
            Assert.Equal(0, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ConfirmationKeyConflictAndCrossCompanyScopeAreRejected()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            var service = Bulk(context);
            var filter = SalesOnly();

            var unconfirmed = await Assert.ThrowsAsync<BusinessRuleException>(() => service.StartAsync(
                scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                new StartBulkPostingCommand { IdempotencyKey = "bulk-confirm", Filter = filter, Confirmed = false },
                Cancellation)).ConfigureAwait(true);
            Assert.Equal("posting.batch_confirmation_required", unconfirmed.Code);

            var crossCompany = await service.StartAsync(scenario.CompanyB, scenario.BranchB1, scenario.Actor,
                new StartBulkPostingCommand
                {
                    IdempotencyKey = "bulk-cross-company",
                    Filter = filter,
                    Documents = [new BulkPostingDocumentReference(BulkPostingDocumentType.SalesInvoice, fixture.InvoiceId)],
                    Confirmed = true,
                }, Cancellation).ConfigureAwait(true);
            Assert.Equal(BulkPostingBatchStatus.Completed, crossCompany.Status);
            Assert.Equal(1, crossCompany.Skipped);
            Assert.Equal(0, crossCompany.Posted);

            var empty = await Assert.ThrowsAsync<BusinessRuleException>(() => service.StartAsync(
                scenario.CompanyB, scenario.BranchB1, scenario.Actor, Command("bulk-other-company", new BulkPostingFilter()),
                Cancellation)).ConfigureAwait(true);
            Assert.Equal("posting.no_documents", empty.Code);

            context.ChangeTracker.Clear();
            var untouched = await context.SalesInvoices.AsNoTracking()
                .SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)SalesInvoiceStatus.Draft, untouched.Status);
            Assert.Equal(0, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));

            var posted = await service.StartAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                Command("bulk-scope", filter), Cancellation).ConfigureAwait(true);
            Assert.Equal(1, posted.Posted);
            var conflict = await Assert.ThrowsAsync<BusinessRuleException>(() => service.StartAsync(
                scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                Command("bulk-scope", new BulkPostingFilter
                {
                    DocumentTypes = [BulkPostingDocumentType.SalesInvoice],
                    Search = "غير مطابق",
                }), Cancellation)).ConfigureAwait(true);
            Assert.Equal("posting.batch_key_conflict", conflict.Code);

            context.ChangeTracker.Clear();
            var invoice = await context.SalesInvoices.AsNoTracking()
                .SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)SalesInvoiceStatus.Posted, invoice.Status);
        }
    }

    private static SqlBulkDocumentPostingService Bulk(PetraDbContext context) =>
        new(context, new SqlAccessEvaluator(context), TimeProvider.System);

    private static BulkPostingFilter SalesOnly() => new() { DocumentTypes = [BulkPostingDocumentType.SalesInvoice] };

    private static StartBulkPostingCommand Command(string key, BulkPostingFilter filter) => new()
    {
        IdempotencyKey = key,
        Filter = filter,
        Confirmed = true,
        Reason = "اختبار الترحيل الجماعي",
    };

    private sealed record Fixture(LedgerScenario Scenario, Guid InvoiceId);

    private static async Task<Fixture> CreateAsync(SqlTestDatabase database)
    {
        var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
        using var context = database.Context();
        foreach (var permission in new[] { "Sales.Invoices.Approve", "Sales.Invoices.Post", "Posting.Batches.Manage" })
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],{permission} FROM [security].[UserRoles] WHERE [UserId]={scenario.Actor}",
                Cancellation).ConfigureAwait(true);
        }

        var ar = await scenario.AccountAsync(scenario.CompanyA, "1131", AccountTypeCode.Assets, true).ConfigureAwait(true);
        var cash = await scenario.AccountAsync(scenario.CompanyA, "1011", AccountTypeCode.Assets, true).ConfigureAwait(true);
        var revenue = await scenario.AccountAsync(scenario.CompanyA, "4111", AccountTypeCode.Revenue, true).ConfigureAwait(true);
        var cogs = await scenario.AccountAsync(scenario.CompanyA, "5111", AccountTypeCode.Expenses, true).ConfigureAwait(true);
        var inventoryAccount = await scenario.AccountAsync(scenario.CompanyA, "1211", AccountTypeCode.Assets, true).ConfigureAwait(true);
        var discount = await scenario.AccountAsync(scenario.CompanyA, "5121", AccountTypeCode.Expenses, true).ConfigureAwait(true);
        var tax = await scenario.AccountAsync(scenario.CompanyA, "2111", AccountTypeCode.Liabilities, true).ConfigureAwait(true);

        var inventory = new SqlInventoryService(context);
        var unit = await inventory.CreateUnitAsync(scenario.CompanyA,
            new CreateUnitCommand("PCS", "قطعة", null, "pc", 0), Cancellation).ConfigureAwait(true);
        var warehouse = await inventory.CreateWarehouseAsync(scenario.CompanyA,
            new CreateWarehouseCommand(scenario.BranchA1, "WH", "مستودع البيع", null, null), Cancellation).ConfigureAwait(true);
        var item = await inventory.CreateItemAsync(scenario.CompanyA,
            new CreateItemCommand("I1", "مادة اختبار", null, null, unit.Id, warehouse.Id, CostMethod.WeightedAverage,
                null, null, null), Cancellation).ConfigureAwait(true);
        await LedgerScenario.AddStockMovementAsync(context,
            Receipt(scenario, item.Id, unit.Id, warehouse.Id, 10m, 2m), Cancellation).ConfigureAwait(true);

        var customer = await new SqlPartyService(context).CreatePartyAsync(scenario.CompanyA,
            new CreatePartyCommand("C1", "عميل الاختبار", null, PartyRole.Customer, ReceivableAccountId: ar.Id),
            Cancellation).ConfigureAwait(true);

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
                AccountMappings =
                [
                    new() { Role = "Customer", AccountId = ar.Id },
                    new() { Role = "Sales", AccountId = revenue.Id },
                    new() { Role = "CostOfGoods", AccountId = cogs.Id },
                    new() { Role = "Inventory", AccountId = inventoryAccount.Id },
                    new() { Role = "Discount", AccountId = discount.Id },
                    new() { Role = "Cash", AccountId = cash.Id },
                    new() { Role = "Tax", AccountId = tax.Id },
                ],
                NumberingPolicy = new() { Prefix = "SI-", MinimumDigits = 4 },
            },
        }, Cancellation).ConfigureAwait(true);
        var published = await patterns.PublishRevisionAsync(scope, pattern.Id, new PublishPatternRevisionCommand
        {
            ExpectedVersion = pattern.Version,
            RevisionId = pattern.ActiveRevision!.Id,
            Reason = "نشر",
        }, Cancellation).ConfigureAwait(true);

        var sales = new SqlSalesInvoiceService(context, new SqlPatternService(context), TimeProvider.System);
        var invoice = await sales.CreateInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
            new CreateSalesInvoiceCommand(published.ActiveRevision!.Id, customer.Id, warehouse.Id,
                new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), PaymentMethod: PaymentMethod.Credit,
                CurrencyCode: "SYP", ExchangeRate: 0.01m,
                Lines: [new(item.Id, unit.Id, 2m, 100m, DiscountAmount: 20m, TaxAmount: 18m)]),
            Cancellation).ConfigureAwait(true);
        return new(scenario, invoice.Id);
    }

    private static StockMovementRow Receipt(LedgerScenario scenario, Guid item, Guid unit, Guid warehouse,
        decimal quantity, decimal cost) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = scenario.CompanyA,
        BranchId = scenario.BranchA1,
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
        CreatedByUserId = scenario.Actor,
    };
}
