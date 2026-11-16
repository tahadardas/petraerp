using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Inventory;
using PetraERP.Application.Parties;
using PetraERP.Application.Patterns;
using PetraERP.Application.Purchases;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Parties;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Purchases;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Purchases;

namespace PetraERP.Infrastructure.Tests;

public sealed partial class SalesPostingSqlTests
{
    private static SqlPurchaseInvoiceService Purchases(PetraDbContext context) => new(context, new SqlPatternService(context), TimeProvider.System);

    private sealed record PurchaseSetup(Guid Revision, Guid Supplier, Guid Cement, Guid Kg, Guid Ton, Guid Warehouse, Guid Payable, Guid Inventory, Guid Tax, Guid Cash);

    /// <summary>Cement stocked in kg and bought in tons (item-specific 1 ton = 1000 kg).</summary>
    private static async Task<PurchaseSetup> PurchaseSetupAsync(Fixture fixture)
    {
        var s = fixture.Scenario;
        using var context = s.Database.Context();
        foreach (var permission in new[] { "Purchases.Invoices.View", "Purchases.Invoices.Manage", "Purchases.Invoices.Post", "Purchases.Invoices.Return" })
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],{permission} FROM [security].[UserRoles] WHERE [UserId]={s.Actor}", Cancellation).ConfigureAwait(true);
        var payable = await s.AccountAsync(s.CompanyA, "2212", AccountTypeCode.Liabilities, true).ConfigureAwait(true);
        var inventory = await context.Accounts.Where(a => a.CompanyId == s.CompanyA && a.Code == "1211").Select(a => a.Id).SingleAsync(Cancellation).ConfigureAwait(true);
        var tax = await context.Accounts.Where(a => a.CompanyId == s.CompanyA && a.Code == "2111").Select(a => a.Id).SingleAsync(Cancellation).ConfigureAwait(true);
        var stock = new SqlInventoryService(context);
        var kg = await stock.CreateUnitAsync(s.CompanyA, new CreateUnitCommand("KG", "كيلوغرام", null, "kg", 0), Cancellation).ConfigureAwait(true);
        var ton = await stock.CreateUnitAsync(s.CompanyA, new CreateUnitCommand("TON", "طن", null, "t", 0), Cancellation).ConfigureAwait(true);
        var cement = await stock.CreateItemCommandAsync(s, kg.Id, fixture.WarehouseId).ConfigureAwait(true);
        await stock.CreateUnitConversionAsync(s.CompanyA, new CreateUnitConversionCommand(cement, ton.Id, kg.Id, 1000m), Cancellation).ConfigureAwait(true);
        var supplier = await new SqlPartyService(context).CreatePartyAsync(s.CompanyA, new CreatePartyCommand("SUP-CEM", "مورد الإسمنت", null, PartyRole.Supplier,
            PayableAccountId: payable.Id), Cancellation).ConfigureAwait(true);
        var patterns = new SqlPatternService(context);
        var scope = PatternScope.Create(s.CompanyA, s.BranchA1, s.Actor);
        var created = await patterns.CreatePatternAsync(scope, new CreateDocumentPatternCommand
        {
            Code = "PI", NameArabic = "مشتريات إسمنت", ShortCode = "PI", OperationKind = BusinessOperationKind.PurchaseInvoice, BranchIds = [s.BranchA1],
            InitialConfiguration = new PatternConfiguration
            {
                AccountMappings = [new() { Role = "Inventory", AccountId = inventory }, new() { Role = "Tax", AccountId = tax },
                    new() { Role = "Payable", AccountId = payable.Id }, new() { Role = "Cash", AccountId = fixture.CashId }],
                NumberingPolicy = new() { Prefix = "PI-", MinimumDigits = 4 }
            }
        }, Cancellation).ConfigureAwait(true);
        var published = await patterns.PublishRevisionAsync(scope, created.Id, new PublishPatternRevisionCommand
        { ExpectedVersion = created.Version, RevisionId = created.ActiveRevision!.Id, Reason = "نشر" }, Cancellation).ConfigureAwait(true);
        return new(published.ActiveRevision!.Id, supplier.Id, cement, kg.Id, ton.Id, fixture.WarehouseId, payable.Id, inventory, tax, fixture.CashId);
    }

    private static SavePurchaseInvoiceCommand CementPurchase(PurchaseSetup p, PaymentMethod method, decimal tons) =>
        new(p.Revision, p.Supplier, p.Warehouse, new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), method, "SYP", 0.01m, "SUP-778", "إسمنت بورتلاندي",
            [new(p.Cement, p.Ton, tons, 100000m, 1000m, 10000m)]);

    [Theory(Explicit = true)]
    [InlineData(PaymentMethod.Credit)]
    [InlineData(PaymentMethod.Cash)]
    [Trait("Category", "SqlServer")]
    public async Task CementPurchaseReceivesKgAtCostCreditsSupplierAndReturnsExactlyToZero(PaymentMethod method)
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var p = await PurchaseSetupAsync(fixture).ConfigureAwait(true);
            using var context = database.Context();
            var service = Purchases(context);
            await AssertRuleAsync("inventory.unit_conversion_invalid", () => service.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                CementPurchase(p, method, 1m) with { Lines = [new(p.Cement, fixture.UnitId, 1m, 1m)] }, Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("purchases.line_invalid", () => service.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                CementPurchase(p, method, 1m) with { Lines = [new(p.Cement, p.Ton, 1m, 10m, 11m)] }, Cancellation)).ConfigureAwait(true);

            var draft = await service.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, CementPurchase(p, method, 2.5m), Cancellation).ConfigureAwait(true);
            Assert.Equal(250000m, draft.SubTotal); Assert.Equal(259000m, draft.GrandTotal);
            var command = new PostPurchaseInvoiceCommand(ConcurrencyToken.FromBytes(draft.RowVersion), "purchase-1");
            var posted = await service.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor, command, Cancellation).ConfigureAwait(true);
            Assert.Equal(PurchaseInvoiceStatus.Posted, posted.Status); Assert.Equal("PI-0001", posted.InvoiceNumber);
            var receipt = await context.StockMovements.AsNoTracking().SingleAsync(m => m.DocumentId == draft.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal((int)StockMovementKind.PurchaseReceipt, receipt.MovementKind);
            Assert.Equal(2500m, receipt.BaseQuantity); Assert.Equal(2490m, receipt.TotalCost); Assert.Equal(posted.Lines[0].Id, receipt.DocumentLineId);
            var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
            Assert.Equal(2490m, Assert.Single(lines, l => l.AccountId == p.Inventory).BaseDebit);
            Assert.Equal(10000m, Assert.Single(lines, l => l.AccountId == p.Tax).TransactionDebit);
            var settlement = Assert.Single(lines, l => l.AccountId == (method == PaymentMethod.Cash ? p.Cash : p.Payable));
            Assert.Equal(259000m, settlement.TransactionCredit); Assert.Equal("SYP", settlement.TransactionCurrencyCode); Assert.Equal(2590m, settlement.BaseCredit);
            var journal = await context.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == posted.JournalEntryId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Posted, journal.State);

            Assert.Equal(posted.JournalEntryId, (await service.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor, command, Cancellation).ConfigureAwait(true)).JournalEntryId);
            await AssertRuleAsync("purchases.invoice_not_draft", () => service.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                command with { IdempotencyKey = "purchase-other" }, Cancellation)).ConfigureAwait(true);

            var source = posted.Lines[0].Id;
            var request = new CreatePurchaseReturnCommand("purchase-return-1", "إسمنت متضرر", [new(source, 1m)]);
            var first = await service.ReturnAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor, request, Cancellation).ConfigureAwait(true);
            Assert.Equal(draft.Id, first.ReturnOfInvoiceId); Assert.Equal(103600m, first.GrandTotal); Assert.Equal(4000m, first.TaxTotal);
            Assert.Equal(first.Id, (await service.ReturnAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor, request, Cancellation).ConfigureAwait(true)).Id);
            await Assert.ThrowsAsync<ResourceConflictException>(() => service.ReturnAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                request with { Lines = [new(source, 0.5m)] }, Cancellation)).ConfigureAwait(true);
            var returned = await context.StockMovements.AsNoTracking().SingleAsync(m => m.DocumentId == first.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(-1000m, returned.BaseQuantity); Assert.Equal(996m, returned.TotalCost);
            var returnLines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == first.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(returnLines.Sum(l => l.BaseDebit), returnLines.Sum(l => l.BaseCredit));
            Assert.Equal(103600m, Assert.Single(returnLines, l => l.AccountId == (method == PaymentMethod.Cash ? p.Cash : p.Payable)).TransactionDebit);
            Assert.Equal(996m, Assert.Single(returnLines, l => l.AccountId == p.Inventory).BaseCredit);

            await AssertRuleAsync("purchases.over_return", () => service.ReturnAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                new("purchase-return-over", "زيادة", [new(source, 1.5001m)]), Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("purchases.return_source_invalid", () => service.ReturnAsync(s.CompanyA, s.BranchA1, first.Id, s.Actor,
                new("purchase-return-of-return", "خطأ", [new(first.Lines[0].Id, 0.1m)]), Cancellation)).ConfigureAwait(true);
            var last = await service.ReturnAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor, new("purchase-return-2", "الباقي", [new(source, 1.5m)]), Cancellation).ConfigureAwait(true);
            Assert.Equal(posted.GrandTotal, first.GrandTotal + last.GrandTotal);
            var all = await context.StockMovements.AsNoTracking().Where(m => m.ItemId == p.Cement).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(0m, all.Sum(m => m.BaseQuantity));
            Assert.Equal(0m, all.Sum(m => m.BaseQuantity < 0 ? -m.TotalCost : m.TotalCost));
            Assert.Equal(2.5m, Assert.Single((await service.GetAsync(s.CompanyA, s.BranchA1, draft.Id, Cancellation).ConfigureAwait(true)).Lines).ReturnedQuantity);
            var listed = await service.ListAsync(s.CompanyA, s.BranchA1, new PurchaseSearchQuery(ReturnOfInvoiceId: draft.Id), Cancellation).ConfigureAwait(true);
            Assert.Equal(2, listed.TotalCount);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetAsync(s.CompanyA, s.BranchA2, draft.Id, Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PurchaseReturnCannotMakeStockNegativeAndDraftsAreVersioned()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var p = await PurchaseSetupAsync(fixture).ConfigureAwait(true);
            using var context = database.Context();
            var service = Purchases(context);
            var draft = await service.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, CementPurchase(p, PaymentMethod.Credit, 1m), Cancellation).ConfigureAwait(true);
            var edited = await service.UpdateDraftAsync(s.CompanyA, s.BranchA1, draft.Id, CementPurchase(p, PaymentMethod.Credit, 2m) with
            { ExpectedVersion = ConcurrencyToken.FromBytes(draft.RowVersion) }, Cancellation).ConfigureAwait(true);
            Assert.Equal(2m, edited.Lines[0].Quantity);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.UpdateDraftAsync(s.CompanyA, s.BranchA1, draft.Id,
                CementPurchase(p, PaymentMethod.Credit, 3m) with { ExpectedVersion = ConcurrencyToken.FromBytes(draft.RowVersion) }, Cancellation)).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            var posted = await service.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                new(ConcurrencyToken.FromBytes(edited.RowVersion), "purchase-neg"), Cancellation).ConfigureAwait(true);

            // 1800 of the 2000 kg leave the warehouse; returning 1 ton (1000 kg) would go negative.
            await LedgerScenario.AddStockMovementAsync(context, new StockMovementRow
            {
                Id = Guid.NewGuid(), CompanyId = s.CompanyA, BranchId = s.BranchA1, WarehouseId = p.Warehouse, ItemId = p.Cement, UnitId = p.Kg,
                MovementKind = (int)StockMovementKind.AdjustmentDecrease, Quantity = 1800m, BaseQuantity = -1800m, UnitCost = 0.996m, TotalCost = 1792.8m,
                MovementDateUtc = DateTimeOffset.UtcNow, DocumentType = "Adjustment", DocumentId = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = s.Actor
            }, Cancellation).ConfigureAwait(true);
            await AssertRuleAsync("inventory.negative_stock_blocked", () => service.ReturnAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                new("purchase-neg-return", "إرجاع", [new(posted.Lines[0].Id, 1m)]), Cancellation)).ConfigureAwait(true);
            Assert.False(await context.PurchaseInvoices.AnyAsync(i => i.ReturnOfInvoiceId == draft.Id, Cancellation).ConfigureAwait(true));

            var deletable = await service.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, CementPurchase(p, PaymentMethod.Credit, 1m), Cancellation).ConfigureAwait(true);
            await service.DeleteDraftAsync(s.CompanyA, s.BranchA1, deletable.Id, ConcurrencyToken.FromBytes(deletable.RowVersion), Cancellation).ConfigureAwait(true);
            await AssertRuleAsync("purchases.invoice_not_draft", () => service.DeleteDraftAsync(s.CompanyA, s.BranchA1, draft.Id,
                ConcurrencyToken.FromBytes(posted.RowVersion), Cancellation)).ConfigureAwait(true);
        }
    }
}

internal static class PurchaseTestItems
{
    internal static async Task<Guid> CreateItemCommandAsync(this SqlInventoryService stock, LedgerScenario s, Guid baseUnit, Guid warehouse) =>
        (await stock.CreateItemAsync(s.CompanyA, new CreateItemCommand("CEM-42.5", "إسمنت 42.5", null, null, baseUnit, warehouse, CostMethod.WeightedAverage,
            null, null, null), TestContext.Current.CancellationToken).ConfigureAwait(false)).Id;
}
