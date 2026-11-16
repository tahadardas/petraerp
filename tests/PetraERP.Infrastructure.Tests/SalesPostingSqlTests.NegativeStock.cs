using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.CompanyOptions;
using PetraERP.Application.Security;
using PetraERP.Application.Sales;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.CompanyOptions;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

public sealed partial class SalesPostingSqlTests
{
    private const string NegativeReason = "أمر شحن طارئ معتمد من مدير المخزون";

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AuthorizedNegativeIssuePostsBelowZeroWithApprovalReasonAndReferenceCost()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            await SetNegativePolicyAsync(context, scenario, NegativeIssuePolicy.AuthorizedException).ConfigureAwait(true);
            await ReduceStockAsync(context, scenario, fixture, 9m).ConfigureAwait(true);

            var posted = await Service(context).PostInvoiceAsync(scenario.CompanyA, scenario.BranchA1, fixture.InvoiceId,
                scenario.Actor, new PostSalesInvoiceCommand("negative-authorized") { NegativeStockReason = NegativeReason },
                Cancellation).ConfigureAwait(true);

            Assert.Equal(SalesInvoiceStatus.Posted, posted.Status);
            var movement = await context.StockMovements.AsNoTracking()
                .SingleAsync(m => m.DocumentId == fixture.InvoiceId && m.CompanyId == scenario.CompanyA, Cancellation).ConfigureAwait(true);
            Assert.Equal(-2m, movement.BaseQuantity);
            Assert.Equal(4m, movement.TotalCost);
            Assert.Equal(1m, movement.QuantityBefore);
            Assert.Equal(-1m, movement.QuantityAfter);
            Assert.Equal(2m, movement.ValueBefore);
            Assert.Equal(-2m, movement.ValueAfter);
            Assert.Contains(NegativeReason, movement.Notes, StringComparison.Ordinal);

            var journalId = Assert.IsType<Guid>(posted.JournalEntryId);
            var journal = await context.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == journalId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Posted, journal.State);
            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == journalId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
            Assert.Equal(4m, Assert.Single(lines, l => l.AccountId == fixture.CogsId).BaseDebit);

            // A receipt at the same reference cost is allowed to close the negative balance without a new exception.
            await LedgerScenario.AddStockMovementAsync(context, StockMovement(scenario, fixture, 1m, 2m,
                StockMovementKind.PurchaseReceipt, "PurchaseInvoice", Guid.NewGuid()), Cancellation).ConfigureAwait(true);
            var balance = await context.StockMovements.AsNoTracking()
                .Where(m => m.CompanyId == scenario.CompanyA && m.WarehouseId == fixture.WarehouseId && m.ItemId == fixture.ItemId)
                .GroupBy(m => m.ItemId).Select(g => new { Quantity = g.Sum(m => m.BaseQuantity), Value = g.Sum(m => m.BaseQuantity < 0 ? -m.TotalCost : m.TotalCost) })
                .SingleAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(0m, balance.Quantity);
            Assert.Equal(0m, balance.Value);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task NegativeIssueWithoutApprovalGrantIsDeniedWithoutSideEffects()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            await SetNegativePolicyAsync(context, scenario, NegativeIssuePolicy.AuthorizedException).ConfigureAwait(true);
            await RevokeNegativePermissionAsync(context, scenario).ConfigureAwait(true);
            await ReduceStockAsync(context, scenario, fixture, 9m).ConfigureAwait(true);

            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(context).PostInvoiceAsync(
                scenario.CompanyA, scenario.BranchA1, fixture.InvoiceId, scenario.Actor,
                new PostSalesInvoiceCommand("negative-no-grant") { NegativeStockReason = NegativeReason }, Cancellation)).ConfigureAwait(true);

            Assert.Equal("inventory.negative_override_denied", error.Code);
            await AssertNoPostingSideEffectsAsync(context, fixture, scenario).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task NegativeIssueWithoutReasonPostsWithTheDefaultPolicyReason()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            await SetNegativePolicyAsync(context, scenario, NegativeIssuePolicy.AuthorizedException).ConfigureAwait(true);
            await ReduceStockAsync(context, scenario, fixture, 9m).ConfigureAwait(true);

            var posted = await Service(context).PostInvoiceAsync(
                scenario.CompanyA, scenario.BranchA1, fixture.InvoiceId, scenario.Actor,
                new PostSalesInvoiceCommand("negative-no-reason"), Cancellation).ConfigureAwait(true);

            Assert.Equal(SalesInvoiceStatus.Posted, posted.Status);
            var movement = await context.StockMovements.AsNoTracking()
                .SingleAsync(m => m.DocumentId == fixture.InvoiceId && m.CompanyId == scenario.CompanyA, Cancellation).ConfigureAwait(true);
            Assert.True(movement.QuantityAfter < 0m);
            Assert.Contains("سياسة الشركة", movement.Notes, StringComparison.Ordinal);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task BlockPolicyStillRejectsNegativeIssueEvenWithReasonAndGrant()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            await SetNegativePolicyAsync(context, scenario, NegativeIssuePolicy.Block).ConfigureAwait(true);
            await ReduceStockAsync(context, scenario, fixture, 9m).ConfigureAwait(true);

            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(context).PostInvoiceAsync(
                scenario.CompanyA, scenario.BranchA1, fixture.InvoiceId, scenario.Actor,
                new PostSalesInvoiceCommand("negative-blocked") { NegativeStockReason = NegativeReason }, Cancellation)).ConfigureAwait(true);

            Assert.Equal("inventory.negative_stock_blocked", error.Code);
            await AssertNoPostingSideEffectsAsync(context, fixture, scenario).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task NegativeIssueWithoutAnyReceiptPostsWithZeroReferenceCost()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var scenario = fixture.Scenario;
            using var context = database.Context();
            await SetNegativePolicyAsync(context, scenario, NegativeIssuePolicy.AuthorizedException).ConfigureAwait(true);
            await context.StockMovements.Where(m => m.CompanyId == scenario.CompanyA && m.ItemId == fixture.ItemId)
                .ExecuteDeleteAsync(Cancellation).ConfigureAwait(true);

            var posted = await Service(context).PostInvoiceAsync(
                scenario.CompanyA, scenario.BranchA1, fixture.InvoiceId, scenario.Actor,
                new PostSalesInvoiceCommand("negative-no-cost") { NegativeStockReason = NegativeReason }, Cancellation).ConfigureAwait(true);

            Assert.Equal(SalesInvoiceStatus.Posted, posted.Status);
            var movement = await context.StockMovements.AsNoTracking()
                .SingleAsync(m => m.DocumentId == fixture.InvoiceId && m.CompanyId == scenario.CompanyA, Cancellation).ConfigureAwait(true);
            Assert.True(movement.BaseQuantity < 0m);
            Assert.Equal(0m, movement.TotalCost);
            Assert.True(movement.QuantityAfter < 0m);
        }
    }

    private static async Task SetNegativePolicyAsync(PetraDbContext context, LedgerScenario scenario, NegativeIssuePolicy policy)
    {
        var service = new SqlCompanyOptionsService(context);
        var snapshot = await service.GetAsync(scenario.CompanyA, Cancellation).ConfigureAwait(true);
        var fiscalYear = await context.FiscalYears.AsNoTracking()
            .SingleAsync(y => y.CompanyId == scenario.CompanyA && y.Code == "FY2026", Cancellation).ConfigureAwait(true);
        var response = await service.SaveAsync(scenario.CompanyA, fiscalYear.Id, scenario.Actor,
            snapshot.Policy with { NegativeIssuePolicy = policy }, snapshot.Version,
            "اختبار سياسة الإخراج بالسالب", Cancellation).ConfigureAwait(true);
        Assert.Equal(policy == NegativeIssuePolicy.AuthorizedException, response.NegativePostingAvailable);
        var saved = await service.GetAsync(scenario.CompanyA, Cancellation).ConfigureAwait(true);
        Assert.Equal(policy == NegativeIssuePolicy.AuthorizedException, saved.NegativePostingAvailable);
    }

    private static async Task RevokeNegativePermissionAsync(PetraDbContext context, LedgerScenario scenario)
        => await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [security].[RolePermissions] WHERE [PermissionCode]={PermissionCatalog.InventoryNegativeApprove} AND [RoleId] IN (SELECT [RoleId] FROM [security].[UserRoles] WHERE [UserId]={scenario.Actor})",
            Cancellation).ConfigureAwait(true);

    private static async Task ReduceStockAsync(PetraDbContext context, LedgerScenario scenario,
        Fixture fixture, decimal quantity)
        => await LedgerScenario.AddStockMovementAsync(context,
            StockMovement(scenario, fixture, quantity, 2m, StockMovementKind.AdjustmentDecrease, "Adjustment", Guid.NewGuid()), Cancellation)
            .ConfigureAwait(true);

    private static StockMovementRow StockMovement(LedgerScenario scenario, Fixture fixture, decimal quantity,
        decimal unitCost, StockMovementKind kind, string documentType, Guid documentId) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = scenario.CompanyA,
        BranchId = scenario.BranchA1,
        ItemId = fixture.ItemId,
        UnitId = fixture.UnitId,
        WarehouseId = fixture.WarehouseId,
        MovementKind = (int)kind,
        Quantity = quantity,
        BaseQuantity = kind == StockMovementKind.AdjustmentDecrease ? -quantity : quantity,
        UnitCost = unitCost,
        TotalCost = quantity * unitCost,
        MovementDateUtc = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
        DocumentType = documentType,
        DocumentId = documentId,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        CreatedByUserId = scenario.Actor,
    };

    private static async Task AssertNoPostingSideEffectsAsync(PetraDbContext context, Fixture fixture, LedgerScenario scenario)
    {
        var invoice = await context.SalesInvoices.AsNoTracking()
            .SingleAsync(i => i.Id == fixture.InvoiceId, Cancellation).ConfigureAwait(true);
        Assert.Equal((byte)SalesInvoiceStatus.Draft, invoice.Status);
        Assert.False(await context.StockMovements.AsNoTracking()
            .AnyAsync(m => m.DocumentId == fixture.InvoiceId, Cancellation).ConfigureAwait(true));
        Assert.False(await context.JournalEntries.AsNoTracking()
            .AnyAsync(j => j.CompanyId == scenario.CompanyA, Cancellation).ConfigureAwait(true));
    }
}
