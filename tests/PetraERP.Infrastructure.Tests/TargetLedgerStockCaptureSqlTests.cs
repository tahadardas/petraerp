using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Data;
using PetraERP.Application.AlAmeenMigration.Reconciliation;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.Security;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.AlAmeenMigration.Reconciliation;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Tests;

public sealed class TargetLedgerStockCaptureSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CapturesScopedLedgerAndStockWithCutoffFromTheAnnualDatabase()
    {
        var token = TestContext.Current.CancellationToken;
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var setup = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(setup.Succeeded, setup.Error);
            using var read = await fixture.Factory.CreateDbContextAsync(setup.WorkspaceId, token).ConfigureAwait(true);
            var branch = await read.Branches.Select(b => b.Id).SingleAsync(token).ConfigureAwait(true);
            var warehouse = await read.Warehouses.Select(w => w.Id).SingleAsync(token).ConfigureAwait(true);
            var assetsRoot = await read.Accounts.Where(a => a.Code == "1").Select(a => a.Id).SingleAsync(token).ConfigureAwait(true);
            var revenueRoot = await read.Accounts.Where(a => a.Code == "4").Select(a => a.Id).SingleAsync(token).ConfigureAwait(true);
            var cashId = Guid.NewGuid();
            var revenueId = Guid.NewGuid();
            var unit = new UnitOfMeasureRow { Id = Guid.NewGuid(), CompanyId = setup.CompanyId, Code = "CUT", NameArabic = "قطعة", Symbol = "pc" };
            var item = new ItemRow { Id = Guid.NewGuid(), CompanyId = setup.CompanyId, Code = "CUT", NameArabic = "مادة اختبار", NormalizedSearchText = "مادة اختبار", BaseUnitId = unit.Id, DefaultWarehouseId = warehouse };
            using (var master = await fixture.Factory.CreateDbContextAsync(setup.WorkspaceId, token).ConfigureAwait(true))
            {
                master.BindWriteScope(new(setup.CompanyId, setup.FiscalYearId, fixture.Actor, PermissionCatalog.InventoryItemsManage, branch));
                master.UnitsOfMeasure.Add(unit);
                master.Items.Add(item);
                await master.SaveChangesAsync(token).ConfigureAwait(true);
            }
            using (var accounts = await fixture.Factory.CreateDbContextAsync(setup.WorkspaceId, token).ConfigureAwait(true))
            {
                accounts.BindProvisioningScope(new AccountingWriteScope(setup.CompanyId, setup.FiscalYearId, fixture.Actor, PermissionCatalog.DatabaseManage));
                accounts.Accounts.AddRange(
                    new AccountRow { Id = cashId, CompanyId = setup.CompanyId, Code = "1101", NameArabic = "الصندوق", ParentId = assetsRoot, AccountType = 1, PostingAllowed = true, CostCenterPolicy = 1, CurrencyMode = 1, IsActive = true, FxMonetaryEligibility = 1 },
                    new AccountRow { Id = revenueId, CompanyId = setup.CompanyId, Code = "4101", NameArabic = "الإيراد", ParentId = revenueRoot, AccountType = 4, PostingAllowed = true, CostCenterPolicy = 1, CurrencyMode = 1, IsActive = true, FxMonetaryEligibility = 2 });
                await accounts.SaveChangesAsync(token).ConfigureAwait(true);
            }
            async Task PostAsync(DateOnly date, decimal amount) => await FiscalYearRolloverSqlTests.PostOperationalJournalAsync(
                fixture, setup.WorkspaceId, setup.CompanyId, setup.FiscalYearId, branch, date, "قيد اختبار المطابقة",
                [
                    new JournalDraftLineInput { AccountId = cashId, TransactionDebit = amount, TransactionCredit = 0m, TransactionCurrencyCode = "USD", RateSourceCurrencyCode = "USD", RateTargetCurrencyCode = "USD", RateValue = 1m, Description = "مدين" },
                    new JournalDraftLineInput { AccountId = revenueId, TransactionDebit = 0m, TransactionCredit = amount, TransactionCurrencyCode = "USD", RateSourceCurrencyCode = "USD", RateTargetCurrencyCode = "USD", RateValue = 1m, Description = "دائن" },
                ]).ConfigureAwait(true);
            await PostAsync(new(2026, 4, 30), 100m).ConfigureAwait(true);
            await PostAsync(new(2026, 5, 1), 40m).ConfigureAwait(true);
            using (var writer = await fixture.Factory.CreateDbContextAsync(setup.WorkspaceId, token).ConfigureAwait(true))
            {
                writer.BindWriteScope(new(setup.CompanyId, setup.FiscalYearId, fixture.Actor, PermissionCatalog.InventoryAdjustmentsManage, branch));
                StockMovementRow Movement(decimal quantity, decimal unitCost, DateTimeOffset date) => new()
                {
                    Id = Guid.NewGuid(), CompanyId = setup.CompanyId, BranchId = branch, WarehouseId = warehouse, ItemId = item.Id, UnitId = unit.Id,
                    MovementKind = (int)(quantity > 0 ? StockMovementKind.AdjustmentIncrease : StockMovementKind.AdjustmentDecrease),
                    Quantity = Math.Abs(quantity), BaseQuantity = quantity, UnitCost = unitCost, TotalCost = Math.Abs(quantity) * unitCost,
                    MovementDateUtc = date, DocumentType = "CutoffTest", DocumentId = Guid.NewGuid(), CreatedByUserId = fixture.Actor,
                };
                writer.StockMovements.AddRange(
                    Movement(10m, 2m, new(2026, 4, 10, 0, 0, 0, TimeSpan.Zero)),
                    Movement(10m, 4m, new(2026, 4, 30, 23, 59, 59, TimeSpan.Zero)));
                await writer.SaveChangesAsync(token).ConfigureAwait(true);
                writer.StockMovements.Add(Movement(-5m, 3m, new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)));
                await writer.SaveChangesAsync(token).ConfigureAwait(true);
            }

            var capture = new TargetLedgerStockCapture(fixture.Factory);
            var april = await capture.ReadAsync(setup.CompanyId, setup.FiscalYearId, branch, new(2026, 4, 30), token).ConfigureAwait(true);
            Assert.Equal(1, april.Ledger.JournalCount);
            Assert.Equal(100m, april.Ledger.Balances.Single(b => b.Key.AccountId == cashId).Debit);
            Assert.Equal(100m, april.Ledger.Balances.Single(b => b.Key.AccountId == revenueId).Credit);
            Assert.Equal(2, april.Stock.MovementCount);
            var aprilStock = Assert.Single(april.Stock.Balances);
            Assert.Equal((branch, warehouse, item.Id, unit.Id), (aprilStock.Key.BranchId, aprilStock.Key.WarehouseId, aprilStock.Key.ItemId, aprilStock.Key.UnitId));
            Assert.Equal(20m, aprilStock.Quantity);
            Assert.Equal(60m, aprilStock.BaseValue);
            var expected = new ReconciliationBalances(
                [new(new(branch, cashId, "USD"), 100m, 0m, 100m, 0m), new(new(branch, revenueId, "USD"), 0m, 100m, 0m, 100m)],
                [], [], [new(new(branch, item.Id, warehouse, unit.Id), 20m, 60m)]);
            var captured = new ReconciliationBalances(april.Ledger.Balances, [], [], april.Stock.Balances);
            var comparison = ReconciliationBalanceComparer.Compare(expected, captured, AccountComparisonMode.FullHistory);
            Assert.Empty(comparison.Accounts);
            Assert.Empty(comparison.Stock);
            var altered = expected with { Stock = [new(new(branch, item.Id, warehouse, unit.Id), 20m, 61m)] };
            Assert.Equal("BaseValue", Assert.Single(ReconciliationBalanceComparer.Compare(altered, captured, AccountComparisonMode.FullHistory).Stock).Measure);
            var may = await capture.ReadAsync(setup.CompanyId, setup.FiscalYearId, branch, new(2026, 5, 1), token).ConfigureAwait(true);
            Assert.Equal(2, may.Ledger.JournalCount);
            Assert.Equal(140m, may.Ledger.Balances.Single(b => b.Key.AccountId == cashId).BaseDebit);
            Assert.Equal(140m, may.Ledger.Balances.Single(b => b.Key.AccountId == revenueId).BaseCredit);
            Assert.Equal(3, may.Stock.MovementCount);
            Assert.Equal(15m, Assert.Single(may.Stock.Balances).Quantity);
            Assert.Equal(45m, Assert.Single(may.Stock.Balances).BaseValue);
            await Assert.ThrowsAsync<BusinessRuleException>(() => capture.ReadAsync(setup.CompanyId, setup.FiscalYearId, Guid.NewGuid(), new(2026, 4, 30), token)).ConfigureAwait(true);

            var journalId = await read.JournalEntries.Where(j => j.AccountingDate == new DateOnly(2026, 4, 30))
                .Select(j => j.Id).SingleAsync(token).ConfigureAwait(true);
            using (var snapshot = await fixture.Factory.CreateDbContextAsync(setup.WorkspaceId, token).ConfigureAwait(true))
            using (var readerTransaction = await snapshot.Database.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(true))
            using (var contender = await fixture.Factory.CreateDbContextAsync(setup.WorkspaceId, token).ConfigureAwait(true))
            {
                await TargetLedgerBalanceReader.ReadWithinTransactionAsync(snapshot, setup.CompanyId, setup.FiscalYearId, branch, new(2026, 4, 30), token).ConfigureAwait(true);
                await TargetStockBalanceReader.ReadWithinTransactionAsync(snapshot, setup.CompanyId, setup.FiscalYearId, branch, new(2026, 4, 30), token).ConfigureAwait(true);
                // What the serializable capture has to guarantee is that nobody can WRITE inside the range it read. Contending with a
                // UPDLOCK *read* cannot prove that: update locks are compatible with the shared locks this read holds — that is exactly
                // what UPDLOCK is for. The writes the captured range must block are asserted instead.
                var utcNow = DateTimeOffset.UtcNow;
                var blockedWrite = await Assert.ThrowsAsync<SqlException>(() => contender.Database.ExecuteSqlInterpolatedAsync(
                    $"SET LOCK_TIMEOUT 1000; UPDATE [ledger].[JournalEntries] SET [Description]=N'كتابة أثناء الالتقاط' WHERE [Id]={journalId}", token)).ConfigureAwait(true);
                Assert.Equal(1222, blockedWrite.Number);
                var blockedPhantom = await Assert.ThrowsAsync<SqlException>(() => contender.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    SET LOCK_TIMEOUT 1000;
                    INSERT INTO [ledger].[JournalEntries]([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State],[CreatedAtUtc],[UpdatedAtUtc])
                    VALUES({Guid.NewGuid()},{setup.CompanyId},{branch},DATEFROMPARTS(2026,4,20),N'قيد داخل النطاق المقروء',1,{utcNow},{utcNow})
                    """, token)).ConfigureAwait(true);
                Assert.Equal(1222, blockedPhantom.Number);
                await readerTransaction.CommitAsync(token).ConfigureAwait(true);
                await contender.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE [ledger].[JournalEntries] SET [Description]=N'كتابة بعد اكتمال الالتقاط' WHERE [Id]={journalId}", token).ConfigureAwait(true);
            }
            using var weaker = await read.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => TargetLedgerBalanceReader.ReadWithinTransactionAsync(read, setup.CompanyId, setup.FiscalYearId, branch, new(2026, 4, 30), token)).ConfigureAwait(true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => TargetStockBalanceReader.ReadWithinTransactionAsync(read, setup.CompanyId, setup.FiscalYearId, branch, new(2026, 4, 30), token)).ConfigureAwait(true);
        }
    }
}
