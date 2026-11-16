using Microsoft.EntityFrameworkCore;
using PetraERP.Application.CompanyOptions;
using PetraERP.Application.DocumentProcessing;
using PetraERP.Application.Sales;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.CompanyOptions;
using PetraERP.Infrastructure.DocumentProcessing;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Purchases;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// P01–P08 (owner-numbered): save → approve → post through the real sales posting engine on a disposable SQL Server
/// database. Every document row, journal and stock movement asserted here is real persisted state.
/// </summary>
public sealed partial class SalesPostingSqlTests
{
    private static readonly DocumentProcessingPermissions FullGrants = new(CanApprove: true, CanPost: true);

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P01ManualModeSaveApprovesButDoesNotPost()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            var processing = Processing(context);

            var result = await processing.CreateSalesInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                FullGrants, NewCommand(fixture), saveAsDraft: false, posting: new DocumentPostingIntent($"p01-{Guid.NewGuid():N}"),
                Cancellation).ConfigureAwait(true);

            Assert.Equal(DocumentProcessingOutcome.Approved, result.Outcome);
            Assert.Equal("ApproveOnly", result.EffectivePostingMode);
            Assert.True(result.ApprovalApplied);
            Assert.False(result.PostingApplied);
            Assert.Null(result.JournalEntryId);
            var row = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == result.DocumentId, Cancellation).ConfigureAwait(true);
            Assert.Equal(Domain.Sales.SalesInvoiceStatus.Approved, (Domain.Sales.SalesInvoiceStatus)row.Status);
            Assert.Equal(0, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P02AutomaticModeSavePostsJournalAndStockThroughTheExistingEngine()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            await SetPostingModeAsync(context, scenario.CompanyA, scenario.Actor, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);
            var processing = Processing(context);

            var result = await processing.CreateSalesInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                FullGrants, NewCommand(fixture), saveAsDraft: false, posting: new DocumentPostingIntent($"p02-{Guid.NewGuid():N}"),
                Cancellation).ConfigureAwait(true);

            Assert.Equal(DocumentProcessingOutcome.Posted, result.Outcome);
            Assert.True(result.ApprovalApplied);
            Assert.True(result.PostingApplied);
            Assert.NotNull(result.JournalEntryId);
            Assert.False(string.IsNullOrWhiteSpace(result.DocumentNumber));
            var journal = await context.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == result.JournalEntryId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Posted, journal.State);
            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == journal.Id).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
            Assert.Single(await context.StockMovements.AsNoTracking().Where(m => m.DocumentId == result.DocumentId).ToListAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P03ExplicitSaveAsDraftNeverApprovesOrPostsInAutomaticMode()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            await SetPostingModeAsync(context, scenario.CompanyA, scenario.Actor, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);
            var processing = Processing(context);

            var result = await processing.CreateSalesInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                FullGrants, NewCommand(fixture), saveAsDraft: true, posting: new DocumentPostingIntent($"p03-{Guid.NewGuid():N}"),
                Cancellation).ConfigureAwait(true);

            Assert.Equal(DocumentProcessingOutcome.SavedDraft, result.Outcome);
            Assert.False(result.ApprovalApplied);
            Assert.False(result.PostingApplied);
            var row = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == result.DocumentId, Cancellation).ConfigureAwait(true);
            Assert.Equal(Domain.Sales.SalesInvoiceStatus.Draft, (Domain.Sales.SalesInvoiceStatus)row.Status);
            Assert.Equal(0, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P05MissingApprovalGrantLeavesTheRealSavedDraftAndExplainsWhy()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            var processing = Processing(context);

            var result = await processing.CreateSalesInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                new DocumentProcessingPermissions(CanApprove: false, CanPost: false), NewCommand(fixture), saveAsDraft: false,
                posting: new DocumentPostingIntent($"p04-{Guid.NewGuid():N}"), Cancellation).ConfigureAwait(true);

            Assert.Equal(DocumentProcessingOutcome.ApprovalPermissionMissing, result.Outcome);
            Assert.False(result.ApprovalApplied);
            Assert.False(result.PostingApplied);
            Assert.Contains("لم يُعتمد", result.Message);
            var row = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == result.DocumentId, Cancellation).ConfigureAwait(true);
            Assert.Equal(Domain.Sales.SalesInvoiceStatus.Draft, (Domain.Sales.SalesInvoiceStatus)row.Status);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P06AutomaticModeWithoutPostingGrantStopsAfterApproval()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            await SetPostingModeAsync(context, scenario.CompanyA, scenario.Actor, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);
            var processing = Processing(context);

            var result = await processing.CreateSalesInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                new DocumentProcessingPermissions(CanApprove: true, CanPost: false), NewCommand(fixture), saveAsDraft: false,
                posting: new DocumentPostingIntent($"p05-{Guid.NewGuid():N}"), Cancellation).ConfigureAwait(true);

            Assert.Equal(DocumentProcessingOutcome.PostingPermissionMissing, result.Outcome);
            Assert.True(result.ApprovalApplied);
            Assert.False(result.PostingApplied);
            Assert.Null(result.JournalEntryId);
            var row = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == result.DocumentId, Cancellation).ConfigureAwait(true);
            Assert.Equal(Domain.Sales.SalesInvoiceStatus.Approved, (Domain.Sales.SalesInvoiceStatus)row.Status);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P07PostingFailureKeepsTheApprovedStateAndReportsIt()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            await SetPostingModeAsync(context, scenario.CompanyA, scenario.Actor, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);
            var processing = Processing(context);
            // Sell more than the 10 units in stock: posting must be refused, approval must not be rolled back.
            var command = NewCommand(fixture, quantity: 500m);

            var result = await processing.CreateSalesInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                FullGrants, command, saveAsDraft: false, posting: new DocumentPostingIntent($"p06-{Guid.NewGuid():N}"),
                Cancellation).ConfigureAwait(true);

            Assert.Equal(DocumentProcessingOutcome.PostingFailed, result.Outcome);
            Assert.True(result.ApprovalApplied);
            Assert.False(result.PostingApplied);
            Assert.NotNull(result.FailureCode);
            Assert.Contains("ولم يُرحَّل", result.Message);
            Assert.Null(result.JournalEntryId);
            var row = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == result.DocumentId, Cancellation).ConfigureAwait(true);
            Assert.Equal(Domain.Sales.SalesInvoiceStatus.Approved, (Domain.Sales.SalesInvoiceStatus)row.Status);
            Assert.Equal(0, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task P08ReprocessingAPostedDocumentCreatesNoSecondJournal()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            await SetPostingModeAsync(context, scenario.CompanyA, scenario.Actor, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);
            var processing = Processing(context);
            var key = $"p07-{Guid.NewGuid():N}";

            var first = await processing.CreateSalesInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                FullGrants, NewCommand(fixture), saveAsDraft: false, posting: new DocumentPostingIntent(key), Cancellation).ConfigureAwait(true);
            var second = await processing.UpdateSalesInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor, FullGrants,
                first.DocumentId, NewUpdateCommand(fixture), saveAsDraft: false, posting: new DocumentPostingIntent(key), Cancellation)
                .ConfigureAwait(true);

            Assert.Equal(DocumentProcessingOutcome.Posted, first.Outcome);
            Assert.Equal(DocumentProcessingOutcome.AlreadyPosted, second.Outcome);
            Assert.Equal(first.JournalEntryId, second.JournalEntryId);
            Assert.Equal(1, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await context.StockMovements.CountAsync(m => m.DocumentId == first.DocumentId, Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PolicyChangeDoesNotRewriteEarlierDocuments()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            var processing = Processing(context);

            var manual = await processing.CreateSalesInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                FullGrants, NewCommand(fixture), saveAsDraft: false, posting: new DocumentPostingIntent($"p08a-{Guid.NewGuid():N}"),
                Cancellation).ConfigureAwait(true);
            Assert.Equal(DocumentProcessingOutcome.Approved, manual.Outcome);

            await SetPostingModeAsync(context, scenario.CompanyA, scenario.Actor, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);

            var afterSwitch = await context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == manual.DocumentId, Cancellation).ConfigureAwait(true);
            Assert.Equal(Domain.Sales.SalesInvoiceStatus.Approved, (Domain.Sales.SalesInvoiceStatus)afterSwitch.Status);
            Assert.Equal(0, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));

            var automatic = await processing.CreateSalesInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                FullGrants, NewCommand(fixture), saveAsDraft: false, posting: new DocumentPostingIntent($"p08b-{Guid.NewGuid():N}"),
                Cancellation).ConfigureAwait(true);
            Assert.Equal(DocumentProcessingOutcome.Posted, automatic.Outcome);
            Assert.Equal(1, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    private static SqlDocumentSaveAndProcessService Processing(PetraDbContext context) =>
        new(Service(context), new SqlPurchaseInvoiceService(context, new SqlPatternService(context), TimeProvider.System),
            new SqlCompanyOptionsService(context));

    private static async Task SetPostingModeAsync(PetraDbContext context, Guid companyId, Guid actor, DocumentPostingMode mode)
    {
        var options = new SqlCompanyOptionsService(context);
        var fiscalYearId = await context.FiscalYears.AsNoTracking().Where(f => f.CompanyId == companyId)
            .Select(f => f.Id).SingleAsync(Cancellation).ConfigureAwait(true);
        var snapshot = await options.GetAsync(companyId, Cancellation).ConfigureAwait(true);
        await options.SaveAsync(companyId, fiscalYearId, actor, snapshot.Policy with { DocumentPostingMode = mode },
            snapshot.Version, "اختبار سياسة الحفظ والترحيل", Cancellation).ConfigureAwait(true);
    }

    private static CreateSalesInvoiceCommand NewCommand(Fixture fixture, decimal quantity = 2m)
    {
        var scenario = fixture.Scenario;
        using var context = scenario.Database.Context();
        var invoice = Service(context).GetInvoiceAsync(scenario.CompanyA, scenario.BranchA1, fixture.InvoiceId, Cancellation)
            .GetAwaiter().GetResult();
        return new CreateSalesInvoiceCommand(invoice.PatternRevisionId, invoice.CustomerId, invoice.WarehouseId,
            new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), CurrencyCode: "SYP", ExchangeRate: 0.01m,
            Lines: [new(fixture.ItemId, fixture.UnitId, quantity, 100m)]);
    }

    private static UpdateSalesInvoiceCommand NewUpdateCommand(Fixture fixture)
    {
        var scenario = fixture.Scenario;
        using var context = scenario.Database.Context();
        var invoice = Service(context).GetInvoiceAsync(scenario.CompanyA, scenario.BranchA1, fixture.InvoiceId, Cancellation)
            .GetAwaiter().GetResult();
        return new UpdateSalesInvoiceCommand(invoice.CustomerId, invoice.WarehouseId,
            new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), CurrencyCode: "SYP", ExchangeRate: 0.01m,
            Lines: [new(fixture.ItemId, fixture.UnitId, 2m, 100m)])
        {
            ExpectedVersion = Application.Common.ConcurrencyToken.FromBytes(invoice.RowVersion),
        };
    }
}
