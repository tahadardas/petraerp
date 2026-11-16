using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.Inventory;
using Xunit;

namespace PetraERP.Infrastructure.Tests;

public sealed class InventorySqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ItemCreationAndCompanyUniqueness()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlInventoryService(context);

            // Create base unit
            var unitA = await service.CreateUnitAsync(scenario.CompanyA, new CreateUnitCommand(
                Code: "PCS",
                NameArabic: "قطعة",
                NameEnglish: "Piece",
                Symbol: "pc",
                DecimalPlaces: 0), Cancellation).ConfigureAwait(true);

            var unitB = await service.CreateUnitAsync(scenario.CompanyB, new CreateUnitCommand(
                Code: "PCS",
                NameArabic: "قطعة",
                NameEnglish: "Piece",
                Symbol: "pc",
                DecimalPlaces: 0), Cancellation).ConfigureAwait(true);

            var cmdA = new CreateItemCommand(
                Code: "ITEM-001",
                NameArabic: "مصفاة زيت 116",
                NameEnglish: "Oil Filter 116",
                GroupId: null,
                BaseUnitId: unitA.Id,
                DefaultWarehouseId: null,
                CostMethod: CostMethod.WeightedAverage,
                MinStock: 5m,
                MaxStock: 50m,
                ReorderPoint: 10m);

            var itemA = await service.CreateItemAsync(scenario.CompanyA, cmdA, Cancellation).ConfigureAwait(true);
            Assert.Equal("ITEM-001", itemA.Code);
            Assert.Equal("مصفاة زيت 116", itemA.NameArabic);
            Assert.Equal("Oil Filter 116", itemA.NameEnglish);
            Assert.True(itemA.IsActive);
            Assert.Equal(CostMethod.WeightedAverage, itemA.CostMethod);
            Assert.Equal(0m, itemA.CurrentStock);

            // Duplicate code in same company fails
            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                service.CreateItemAsync(scenario.CompanyA, cmdA, Cancellation)).ConfigureAwait(true);

            // Same code in different company succeeds
            var cmdB = cmdA with { BaseUnitId = unitB.Id };
            var itemB = await service.CreateItemAsync(scenario.CompanyB, cmdB, Cancellation).ConfigureAwait(true);
            Assert.Equal(scenario.CompanyB, itemB.CompanyId);
            Assert.Equal("ITEM-001", itemB.Code);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task TokenizedArabicItemLookup()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlInventoryService(context);

            var unit = await service.CreateUnitAsync(scenario.CompanyA, new CreateUnitCommand("PCS", "قطعة", null, "pc", 0), Cancellation).ConfigureAwait(true);

            await service.CreateItemAsync(scenario.CompanyA, new CreateItemCommand(
                Code: "FLT-116",
                NameArabic: "مصفاة زيت ديزل 116",
                NameEnglish: "Diesel Oil Filter 116",
                GroupId: null,
                BaseUnitId: unit.Id,
                DefaultWarehouseId: null,
                CostMethod: CostMethod.WeightedAverage,
                MinStock: null,
                MaxStock: null,
                ReorderPoint: null), Cancellation).ConfigureAwait(true);

            // Match with prefixes and different token order
            var match1 = await service.LookupItemsAsync(scenario.CompanyA, "مص 116", 20, Cancellation).ConfigureAwait(true);
            Assert.Single(match1);
            Assert.Equal("FLT-116", match1[0].Code);

            var match2 = await service.LookupItemsAsync(scenario.CompanyA, "116 ديزل", 20, Cancellation).ConfigureAwait(true);
            Assert.Single(match2);
            Assert.Equal("FLT-116", match2[0].Code);

            // Non-matching query returns empty
            var nonMatch = await service.LookupItemsAsync(scenario.CompanyA, "بنزين", 20, Cancellation).ConfigureAwait(true);
            Assert.Empty(nonMatch);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task StockTransferAndNegativeStockBlock()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlInventoryService(context);

            var unit = await service.CreateUnitAsync(scenario.CompanyA, new CreateUnitCommand("KG", "كيلوغرام", null, "kg", 2), Cancellation).ConfigureAwait(true);

            var wh1 = await service.CreateWarehouseAsync(scenario.CompanyA, new CreateWarehouseCommand(
                BranchId: scenario.BranchA1,
                Code: "WH-MAIN",
                NameArabic: "المستودع الرئيسي",
                NameEnglish: "Main Warehouse",
                Location: "دمشق"), Cancellation).ConfigureAwait(true);

            var wh2 = await service.CreateWarehouseAsync(scenario.CompanyA, new CreateWarehouseCommand(
                BranchId: scenario.BranchA1,
                Code: "WH-SEC",
                NameArabic: "المستودع الثانوي",
                NameEnglish: "Secondary Warehouse",
                Location: "دمشق"), Cancellation).ConfigureAwait(true);

            var item = await service.CreateItemAsync(scenario.CompanyA, new CreateItemCommand(
                Code: "MAT-01",
                NameArabic: "مادة خام",
                NameEnglish: null,
                GroupId: null,
                BaseUnitId: unit.Id,
                DefaultWarehouseId: wh1.Id,
                CostMethod: CostMethod.WeightedAverage,
                MinStock: null,
                MaxStock: null,
                ReorderPoint: null), Cancellation).ConfigureAwait(true);

            // Add initial stock: 100 KG in WH-MAIN
            var movementId = Guid.NewGuid();
            await LedgerScenario.AddStockMovementAsync(context, new StockMovementRow
            {
                Id = movementId,
                CompanyId = scenario.CompanyA,
                BranchId = scenario.BranchA1,
                WarehouseId = wh1.Id,
                ItemId = item.Id,
                UnitId = unit.Id,
                MovementKind = (int)StockMovementKind.OpeningBalance,
                Quantity = 100m,
                BaseQuantity = 100m,
                UnitCost = 10m,
                TotalCost = 1000m,
                MovementDateUtc = DateTimeOffset.UtcNow,
                DocumentType = "OpeningBalance",
                DocumentId = movementId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CreatedByUserId = scenario.Actor
            }, Cancellation).ConfigureAwait(true);

            // Verify stock: WH-MAIN has 100, WH-SEC has 0
            var stockSummary = await service.GetItemStockAsync(scenario.CompanyA, item.Id, null, Cancellation).ConfigureAwait(true);
            Assert.Equal(100m, stockSummary.TotalStock);

            // PT-089: Same warehouse transfer rejected
            var sameWhCmd = new CreateStockTransferCommand(
                TransferNumber: "TR-000",
                SourceWarehouseId: wh1.Id,
                TargetBranchId: scenario.BranchA1,
                TargetWarehouseId: wh1.Id,
                TransferDateUtc: DateTimeOffset.UtcNow,
                Notes: null,
                Lines: [new CreateStockTransferLineCommand(item.Id, unit.Id, 10m, 10m)]);

            var sameWhEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.CreateStockTransferAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor, sameWhCmd, Cancellation)).ConfigureAwait(true);
            Assert.Equal("inventory.transfer_same_warehouse", sameWhEx.Code);

            // PT-088: Over-transfer (150 KG > 100 KG) blocked by negative stock check
            var overTransferCmd = new CreateStockTransferCommand(
                TransferNumber: "TR-001",
                SourceWarehouseId: wh1.Id,
                TargetBranchId: scenario.BranchA1,
                TargetWarehouseId: wh2.Id,
                TransferDateUtc: DateTimeOffset.UtcNow,
                Notes: null,
                Lines: [new CreateStockTransferLineCommand(item.Id, unit.Id, 150m, 10m)]);

            var overEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.CreateStockTransferAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor, overTransferCmd, Cancellation)).ConfigureAwait(true);
            Assert.Equal("inventory.negative_stock_blocked", overEx.Code);

            // Valid transfer of 40 KG from WH-MAIN to WH-SEC succeeds atomically (PT-089)
            var validTransferCmd = new CreateStockTransferCommand(
                TransferNumber: "TR-002",
                SourceWarehouseId: wh1.Id,
                TargetBranchId: scenario.BranchA1,
                TargetWarehouseId: wh2.Id,
                TransferDateUtc: DateTimeOffset.UtcNow,
                Notes: "نقل جزئي",
                Lines: [new CreateStockTransferLineCommand(item.Id, unit.Id, 40m, 10m)]);

            var transferResult = await service.CreateStockTransferAsync(
                scenario.CompanyA, scenario.BranchA1, scenario.Actor, validTransferCmd, Cancellation).ConfigureAwait(true);

            Assert.Equal(StockTransferStatus.Completed, transferResult.Status);
            Assert.Single(transferResult.Lines);

            // Verify stock balances after transfer: WH-MAIN has 60, WH-SEC has 40, Total is still 100
            var stock1 = await service.GetItemStockAsync(scenario.CompanyA, item.Id, wh1.Id, Cancellation).ConfigureAwait(true);
            var stock2 = await service.GetItemStockAsync(scenario.CompanyA, item.Id, wh2.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(60m, stock1.TotalStock);
            Assert.Equal(40m, stock2.TotalStock);
            var pair = await context.StockMovements.AsNoTracking().Where(row => row.DocumentId == transferResult.Id).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(2, pair.Count); Assert.Equal(0m, pair.Sum(row => row.BaseQuantity));
            Assert.All(pair, row => { Assert.Equal(40m, row.Quantity); Assert.Equal(400m, row.TotalCost); });
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task StockAdjustmentLifecycleAndReason()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlInventoryService(context);

            var unit = await service.CreateUnitAsync(scenario.CompanyA, new CreateUnitCommand("PCS", "قطعة", null, "pc", 0), Cancellation).ConfigureAwait(true);
            var wh = await service.CreateWarehouseAsync(scenario.CompanyA, new CreateWarehouseCommand(scenario.BranchA1, "WH-ADJ", "مستودع الجرد", null, null), Cancellation).ConfigureAwait(true);
            var item = await service.CreateItemAsync(scenario.CompanyA, new CreateItemCommand("ADJ-01", "صنف جرد", null, null, unit.Id, wh.Id, CostMethod.WeightedAverage, null, null, null), Cancellation).ConfigureAwait(true);

            // Add opening stock: 50 PCS
            var movId = Guid.NewGuid();
            await LedgerScenario.AddStockMovementAsync(context, new StockMovementRow
            {
                Id = movId,
                CompanyId = scenario.CompanyA,
                BranchId = scenario.BranchA1,
                WarehouseId = wh.Id,
                ItemId = item.Id,
                UnitId = unit.Id,
                MovementKind = (int)StockMovementKind.OpeningBalance,
                Quantity = 50m,
                BaseQuantity = 50m,
                UnitCost = 20m,
                TotalCost = 1000m,
                MovementDateUtc = DateTimeOffset.UtcNow,
                DocumentType = "OpeningBalance",
                DocumentId = movId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CreatedByUserId = scenario.Actor
            }, Cancellation).ConfigureAwait(true);

            // PT-090: Missing reason throws
            var noReasonCmd = new CreateStockAdjustmentCommand(
                AdjustmentNumber: "ADJ-2026-001",
                WarehouseId: wh.Id,
                AdjustmentDateUtc: DateTimeOffset.UtcNow,
                Reason: "  ",
                Notes: null,
                Lines: [new CreateStockAdjustmentLineCommand(item.Id, unit.Id, 45m, 20m)]);

            var noReasonEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.CreateStockAdjustmentAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor, noReasonCmd, Cancellation)).ConfigureAwait(true);
            Assert.Equal("inventory.adjustment_reason_required", noReasonEx.Code);

            // Valid adjustment with reason creates Draft
            var validCmd = noReasonCmd with { Reason = "عجز جرد مستودعي - تلف" };
            var draft = await service.CreateStockAdjustmentAsync(
                scenario.CompanyA, scenario.BranchA1, scenario.Actor, validCmd, Cancellation).ConfigureAwait(true);

            Assert.Equal(StockAdjustmentStatus.Draft, draft.Status);
            Assert.Equal(50m, draft.Lines[0].SystemQuantity);
            Assert.Equal(45m, draft.Lines[0].CountedQuantity);
            Assert.Equal(-5m, draft.Lines[0].DifferenceQuantity);

            // Before approval: stock is still 50
            var preStock = await service.GetItemStockAsync(scenario.CompanyA, item.Id, wh.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(50m, preStock.TotalStock);

            // Approve adjustment: posts -5 AdjustmentDecrease movement
            var approved = await service.ApproveStockAdjustmentAsync(
                scenario.CompanyA, scenario.BranchA1, draft.Id, scenario.Actor, Cancellation).ConfigureAwait(true);

            Assert.Equal(StockAdjustmentStatus.Approved, approved.Status);

            // After approval: stock is now 45 via authoritative movement
            var postStock = await service.GetItemStockAsync(scenario.CompanyA, item.Id, wh.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(45m, postStock.TotalStock);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task InternalIssueRequiresCostCenter()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlInventoryService(context);

            var unit = await service.CreateUnitAsync(scenario.CompanyA, new CreateUnitCommand("LTR", "ليتر", null, "L", 2), Cancellation).ConfigureAwait(true);
            var wh = await service.CreateWarehouseAsync(scenario.CompanyA, new CreateWarehouseCommand(scenario.BranchA1, "WH-DIESEL", "مستودع المحروقات", null, null), Cancellation).ConfigureAwait(true);
            var diesel = await service.CreateItemAsync(scenario.CompanyA, new CreateItemCommand("DSL-01", "ديزل آليات", null, null, unit.Id, wh.Id, CostMethod.WeightedAverage, null, null, null), Cancellation).ConfigureAwait(true);

            // Insert a cost center in CompanyA
            var costCenterId = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[CostCenters]([Id],[CompanyId],[Code],[NameArabic],[IsActive],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({costCenterId},{scenario.CompanyA},N'CC-TRUCK',N'شاحنة مرسيدس 501',1,{DateTimeOffset.UtcNow},{DateTimeOffset.UtcNow})",
                Cancellation).ConfigureAwait(true);

            // Add stock: 500 Liters
            var movId = Guid.NewGuid();
            await LedgerScenario.AddStockMovementAsync(context, new StockMovementRow
            {
                Id = movId,
                CompanyId = scenario.CompanyA,
                BranchId = scenario.BranchA1,
                WarehouseId = wh.Id,
                ItemId = diesel.Id,
                UnitId = unit.Id,
                MovementKind = (int)StockMovementKind.PurchaseReceipt,
                Quantity = 500m,
                BaseQuantity = 500m,
                UnitCost = 1.25m,
                TotalCost = 625m,
                MovementDateUtc = DateTimeOffset.UtcNow,
                DocumentType = "PurchaseReceipt",
                DocumentId = movId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CreatedByUserId = scenario.Actor
            }, Cancellation).ConfigureAwait(true);

            // PT-091: Empty CostCenterId throws
            var emptyCcCmd = new PostInternalIssueCommand(
                WarehouseId: wh.Id,
                ItemId: diesel.Id,
                UnitId: unit.Id,
                Quantity: 50m,
                UnitCost: 1.25m,
                CostCenterId: Guid.Empty,
                DocumentNumber: "ISS-001",
                Notes: null);

            var emptyCcEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.PostInternalIssueAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor, emptyCcCmd, Cancellation)).ConfigureAwait(true);
            Assert.Equal("inventory.cost_center_required", emptyCcEx.Code);

            // PT-088: Over-issue (600 L > 500 L) throws negative stock blocked
            var overIssueCmd = emptyCcCmd with { CostCenterId = costCenterId, Quantity = 600m };
            var overEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.PostInternalIssueAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor, overIssueCmd, Cancellation)).ConfigureAwait(true);
            Assert.Equal("inventory.negative_stock_blocked", overEx.Code);

            // Valid internal issue with CostCenterId succeeds (PT-091)
            var validCmd = emptyCcCmd with { CostCenterId = costCenterId, Quantity = 80m };
            var issueResult = await service.PostInternalIssueAsync(
                scenario.CompanyA, scenario.BranchA1, scenario.Actor, validCmd, Cancellation).ConfigureAwait(true);

            Assert.Equal(StockMovementKind.InternalIssue, issueResult.MovementKind);
            Assert.Equal(80m, issueResult.Quantity);
            Assert.Equal(-80m, issueResult.BaseQuantity);
            Assert.Equal(100m, issueResult.TotalCost);
            Assert.Equal(costCenterId, issueResult.CostCenterId);
            Assert.Equal("CC-TRUCK", issueResult.CostCenterCode);

            // Stock is now 420 Liters
            var stock = await service.GetItemStockAsync(scenario.CompanyA, diesel.Id, wh.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(420m, stock.TotalStock);
        }
    }
}
