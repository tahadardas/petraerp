using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.Inventory;

namespace PetraERP.Infrastructure.Tests;

public sealed partial class IndependentAccountingSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AnnualInventoryUsesLocalActorFkLocksWacAndAtomicRollback()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            using var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            var branch = await annual.Branches.Select(b => b.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var warehouse = await annual.Warehouses.Select(w => w.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var unit = new UnitOfMeasureRow { Id = Guid.NewGuid(), CompanyId = result.CompanyId, Code = "PC", NameArabic = "قطعة", Symbol = "pc" };
            var item = new ItemRow { Id = Guid.NewGuid(), CompanyId = result.CompanyId, Code = "ITEM", NameArabic = "مادة", NormalizedSearchText = "مادة", BaseUnitId = unit.Id, DefaultWarehouseId = warehouse };
            // Master data (units/items) and stock adjustments are distinct authorized operations; each binds its own context scope.
            using (var master = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                master.BindWriteScope(new(result.CompanyId, result.FiscalYearId, fixture.Actor, PermissionCatalog.InventoryItemsManage, branch));
                master.UnitsOfMeasure.Add(unit);
                master.Items.Add(item);
                await master.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            }
            annual.BindWriteScope(new(result.CompanyId, result.FiscalYearId, fixture.Actor, PermissionCatalog.InventoryAdjustmentsManage, branch));
            StockMovementRow Movement(decimal quantity, decimal cost) => new()
            {
                Id = Guid.NewGuid(), CompanyId = result.CompanyId, BranchId = branch, WarehouseId = warehouse, ItemId = item.Id, UnitId = unit.Id,
                MovementKind = (int)(quantity > 0 ? StockMovementKind.AdjustmentIncrease : StockMovementKind.AdjustmentDecrease),
                Quantity = Math.Abs(quantity), BaseQuantity = quantity, UnitCost = cost, TotalCost = Math.Abs(quantity) * cost,
                MovementDateUtc = DateTimeOffset.UtcNow, DocumentType = "AnnualAcceptance", DocumentId = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = fixture.Actor,
            };
            annual.StockMovements.AddRange(Movement(10, 2), Movement(10, 4));
            await annual.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            var issue = Movement(-5, 3);
            annual.StockMovements.Add(issue);
            await annual.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(20, issue.QuantityBefore);
            Assert.Equal(15, issue.QuantityAfter);
            Assert.Equal(60, issue.ValueBefore);
            Assert.Equal(45, issue.ValueAfter);
            Assert.True(issue.RecordedSequence > 0);
            annual.StockMovements.AddRange(Movement(-5, 3), Movement(-11, 3));
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => annual.SaveChangesAsync(Cancellation)).ConfigureAwait(true);
            Assert.Equal("inventory.negative_stock_blocked", error.Code);
            Assert.Equal(3, await annual.StockMovements.CountAsync(Cancellation).ConfigureAwait(true));
            // The negative-stock rejection poisoned that unit of work; a fresh context confirms a posted movement is immutable.
            using var audit = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            audit.BindWriteScope(new(result.CompanyId, result.FiscalYearId, fixture.Actor, PermissionCatalog.InventoryAdjustmentsManage, branch));
            var stored = await audit.StockMovements.SingleAsync(m => m.Id == issue.Id, Cancellation).ConfigureAwait(true);
            stored.Notes = "تعديل مرفوض";
            Assert.Equal("inventory.immutable", (await Assert.ThrowsAsync<BusinessRuleException>(() => audit.SaveChangesAsync(Cancellation)).ConfigureAwait(true)).Code);
            Assert.Equal(1, await audit.ActorReferences.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PendingPasswordAndUnavailableAuthorityFailClosedWithoutFinancialWrites()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            using var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            annual.BindWriteScope(new(result.CompanyId, result.FiscalYearId, fixture.Actor, PermissionCatalog.AccountsManage));
            var actor = await fixture.System.Users.SingleAsync(u => u.Id == fixture.Actor, Cancellation).ConfigureAwait(true);
            actor.MustChangePassword = true;
            await fixture.System.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            annual.AccountAudit.Add(Audit(result.CompanyId, fixture.Actor));
            Assert.Equal("accounting.actor_denied", (await Assert.ThrowsAsync<BusinessRuleException>(() => annual.SaveChangesAsync(Cancellation)).ConfigureAwait(true)).Code);
            actor.MustChangePassword = false;
            await fixture.System.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            // The actor_denied failure poisons its unit of work; a fresh context exercises the unavailable-authority fail-closed path.
            using var retry = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            retry.BindWriteScope(new(result.CompanyId, result.FiscalYearId, fixture.Actor, PermissionCatalog.AccountsManage));
            fixture.System.Database.SetConnectionString(new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(fixture.Database.ConnectionString)
                { InitialCatalog = fixture.Database.DatabaseName + "_UNAVAILABLE", ConnectTimeout = 1, ConnectRetryCount = 0 }.ConnectionString);
            retry.AccountAudit.Add(Audit(result.CompanyId, fixture.Actor));
            var unavailable = await Assert.ThrowsAnyAsync<Exception>(() => retry.SaveChangesAsync(Cancellation)).ConfigureAwait(true);
            Assert.True(unavailable is Microsoft.Data.SqlClient.SqlException || unavailable.InnerException is Microsoft.Data.SqlClient.SqlException,
                $"expected a SQL failure, got {unavailable.GetType().Name}: {unavailable.Message}");
            fixture.System.Database.SetConnectionString(fixture.Database.ConnectionString);
            Assert.Equal(0, await annual.AccountAudit.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }
}
