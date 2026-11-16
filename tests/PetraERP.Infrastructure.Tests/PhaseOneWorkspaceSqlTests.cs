using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory;
using PetraERP.Application.Security;
using PetraERP.Application.Workspaces;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Registry;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Security;
using PetraERP.Infrastructure.Workspaces;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Tests;

public sealed class PhaseOneWorkspaceSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task DryRunCountsAssignedJournalsOnceAndNeverReportsThemAsMigrated()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var originalYear = await context.FiscalYears.SingleAsync(y => y.CompanyId == scenario.CompanyA, Cancellation).ConfigureAwait(true);
            var period = await context.AccountingPeriods.Where(p => p.FiscalYearId == originalYear.Id).OrderBy(p => p.RangeStart).FirstAsync(Cancellation).ConfigureAwait(true);
            // Reproduce ambiguous historical ranges without disabling any SQL constraints.
            var overlappingYear = new FiscalYearRow
            {
                Id = Guid.NewGuid(), CompanyId = scenario.CompanyA, Code = "OVERLAPPING-LEGACY",
                RangeStart = originalYear.RangeStart, RangeEnd = originalYear.RangeEnd,
            };
            context.FiscalYears.Add(overlappingYear);
            context.JournalEntries.Add(new JournalEntryRow
            {
                Id = Guid.NewGuid(), CompanyId = scenario.CompanyA, BranchId = scenario.BranchA1,
                AccountingDate = new(2026, 1, 15), Description = "قيد تاريخي ذو سنة صريحة", State = 4,
                PostingFiscalYearId = originalYear.Id, PostingPeriodId = period.Id, PostingSequence = 1,
            });
            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            var plans = await new SqlLegacyToIndependentDbMigrator(context).PlanMigrationAsync(Cancellation).ConfigureAwait(true);
            var companyPlans = plans.Where(p => p.CompanyId == scenario.CompanyA).ToArray();
            Assert.Equal(1, companyPlans.Sum(p => p.PlannedCounts["JournalEntries"]));
            Assert.Equal(1, Assert.Single(companyPlans, p => p.FiscalYearId == originalYear.Id).PlannedCounts["JournalEntries"]);
            Assert.Equal(0, Assert.Single(companyPlans, p => p.FiscalYearId == overlappingYear.Id).PlannedCounts["JournalEntries"]);
            Assert.All(plans, p =>
            {
                Assert.True(p.IsDryRun);
                Assert.False(p.IsExecutionApproved);
                Assert.Equal(0, p.JournalEntriesMigrated);
                Assert.Equal(0, p.AccountsMigrated);
                Assert.Contains("Warehouses and StockMovements", p.PendingEntities);
            });
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task WarehouseForeignKeyRequiresABranchOfTheSameCompanyAndPeriodsPermitASingleDay()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            var seed = await OrganizationSqlSeed.CreateAsync(context).ConfigureAwait(true);
            var branch = await context.Branches.SingleAsync(b => b.CompanyId == seed.FirstCompanyId, Cancellation).ConfigureAwait(true);
            var foreignBranch = await context.Branches.SingleAsync(b => b.CompanyId == seed.SecondCompanyId, Cancellation).ConfigureAwait(true);
            var warehouse = new WarehouseRow
            {
                Id = Guid.NewGuid(), CompanyId = seed.FirstCompanyId, BranchId = foreignBranch.Id,
                Code = "WRONG", NameArabic = "مستودع", IsActive = true,
            };
            context.Warehouses.Add(warehouse);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Cancellation)).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            warehouse.BranchId = branch.Id;
            context.Warehouses.Add(warehouse);
            var yearId = Guid.NewGuid();
            var date = new DateOnly(2026, 12, 31);
            context.FiscalYears.Add(new FiscalYearRow { Id = yearId, CompanyId = seed.FirstCompanyId, Code = "DAY", RangeStart = date, RangeEnd = date });
            context.AccountingPeriods.AddRange(FiscalYearProvisioningRules.Periods(seed.FirstCompanyId, yearId, date, date));
            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(branch.Id, (await context.Warehouses.SingleAsync(Cancellation).ConfigureAwait(true)).BranchId);
            Assert.Equal(0, (await context.AccountingPeriods.SingleAsync(Cancellation).ConfigureAwait(true)).State);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ResolverRejectsUnregisteredCrossCompanyUnreadyYearsAndUnapprovedSchemas()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var legacy = database.Context();
            var seed = await OrganizationSqlSeed.CreateAsync(legacy).ConfigureAwait(true);
            using var system = SystemContext(database);
            var resolver = Resolver(system, database);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => resolver.ResolveByCompanyAndYearAsync(seed.FirstCompanyId, Guid.NewGuid(), Cancellation)).ConfigureAwait(true);
            var year = Registry(seed.FirstCompanyId, 2026, "PetraERP_Test_A2026");
            year.IsDefault = true;
            system.FiscalDatabases.AddRange(year, Registry(seed.FirstCompanyId, 2027, "PetraERP_Test_A2027"), Registry(seed.SecondCompanyId, 2026, "PetraERP_Test_B2026"));
            await system.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            var unapproved = await Assert.ThrowsAsync<BusinessRuleException>(() => resolver.ResolveByCompanyAndYearAsync(seed.FirstCompanyId, year.FiscalYearId, Cancellation)).ConfigureAwait(true);
            Assert.Equal("workspace.database_unavailable", unapproved.Code);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => resolver.ResolveByCompanyAndYearAsync(seed.SecondCompanyId, year.FiscalYearId, Cancellation)).ConfigureAwait(true);
            foreach (var state in new[] { "Pending", "Provisioning", "Upgrading", "Failed" })
            {
                year.State = state;
                await system.SaveChangesAsync(Cancellation).ConfigureAwait(true);
                await Assert.ThrowsAsync<BusinessRuleException>(() => resolver.ResolveAsync(year.Id, Cancellation)).ConfigureAwait(true);
            }
            year.State = "Active";
            year.SchemaVersion = 0;
            await system.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => resolver.ResolveAsync(year.Id, Cancellation)).ConfigureAwait(true);
            year.SchemaVersion = 1;
            var company = await system.Companies.SingleAsync(c => c.Id == year.CompanyId, Cancellation).ConfigureAwait(true);
            company.IsActive = false;
            await system.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => resolver.ResolveAsync(year.Id, Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task UnauthorizedProvisioningCannotReserveCompanyOrCreateDatabase()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using (var context = database.Context()) await context.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            using var system = SystemContext(database);
            var configuration = Configuration(database);
            var options = Options.Create(new FiscalDatabaseOptions { CompatibilityLevel = database.CompatibilityLevel, DatabaseNamePrefix = database.DatabaseName });
            var authority = new SqlAccountingActorAuthority(system);
            var factory = new AccountingDbContextFactory(Resolver(system, database), options, authority);
            var service = new SqlCompanyProvisioningService(system, factory, configuration, options, NullLogger<SqlCompanyProvisioningService>.Instance, authority);
            var input = new ProvisionCompanyInput("NEWCO", "شركة جديدة", null, "USD", 2026, new(2026, 1, 31), new(2026, 6, 30));
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ProvisionCompanyAsync(input, Guid.NewGuid(), Cancellation)).ConfigureAwait(true);
            Assert.Equal("accounting.actor_denied", error.Code);
            Assert.Empty(await system.Companies.ToListAsync(Cancellation).ConfigureAwait(true));
            Assert.Empty(await system.ProvisioningOperations.ToListAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task UpgradeGrantsDatabaseManagementOnlyToAdministratorAndEnforcesOneDefault()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            await context.GetService<IMigrator>().MigrateAsync("20261021120000_CommercialPapersAndDatabaseRegistry", Cancellation).ConfigureAwait(true);
            var adminId = Guid.NewGuid();
            var clerkId = Guid.NewGuid();
            context.Roles.AddRange(new RoleRow { Id = adminId, Code = "ADMINISTRATOR", NameArabic = "مدير النظام" },
                new RoleRow { Id = clerkId, Code = "CLERK", NameArabic = "موظف" });
            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            await context.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            var grants = await context.RolePermissions.Where(r => r.PermissionCode == PermissionCatalog.DatabaseManage).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(adminId, Assert.Single(grants).RoleId);
            using var system = SystemContext(database);
            var companyId = Guid.NewGuid();
            var first = Registry(companyId, 2026, "PetraERP_Test_Default2026");
            first.IsDefault = true;
            system.FiscalDatabases.Add(first);
            await system.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            var second = Registry(companyId, 2027, "PetraERP_Test_Default2027");
            second.IsDefault = true;
            system.FiscalDatabases.Add(second);
            await Assert.ThrowsAsync<DbUpdateException>(() => system.SaveChangesAsync(Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task LegacyInventoryGuardUsesRealSqlLocksWacAndAtomicNegativeStockRejection()
    {
        // Legacy acceptance only. Independent annual-schema acceptance has its own fixture.
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var legacy = database.Context();
            var inventory = new SqlInventoryService(legacy);
            var unit = await inventory.CreateUnitAsync(scenario.CompanyA, new("PCS", "قطعة", null, "pc", 0), Cancellation).ConfigureAwait(true);
            var warehouse = await inventory.CreateWarehouseAsync(scenario.CompanyA, new(scenario.BranchA1, "WAC", "مستودع", null, null), Cancellation).ConfigureAwait(true);
            var item = await inventory.CreateItemAsync(scenario.CompanyA, new("WAC", "مادة", null, null, unit.Id, warehouse.Id, CostMethod.WeightedAverage, null, null, null), Cancellation).ConfigureAwait(true);
            StockMovementRow Movement(decimal quantity, decimal cost) => new()
            {
                Id = Guid.NewGuid(), CompanyId = scenario.CompanyA, BranchId = scenario.BranchA1,
                WarehouseId = warehouse.Id, ItemId = item.Id, UnitId = unit.Id,
                MovementKind = (int)(quantity > 0 ? StockMovementKind.AdjustmentIncrease : StockMovementKind.AdjustmentDecrease),
                Quantity = Math.Abs(quantity), BaseQuantity = quantity, UnitCost = cost, TotalCost = Math.Abs(quantity) * cost,
                MovementDateUtc = DateTimeOffset.UtcNow, DocumentType = "PhaseOne", DocumentId = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = scenario.Actor,
            };
            using (var accounting = database.Context())
            {
                var transaction = await accounting.Database.BeginTransactionAsync(IsolationLevel.Serializable, Cancellation).ConfigureAwait(true);
                await using (transaction.ConfigureAwait(true))
                {
                    accounting.StockMovements.AddRange(Movement(10, 2), Movement(10, 4));
                    await accounting.SaveChangesAsync(Cancellation).ConfigureAwait(true);
                    await transaction.CommitAsync(Cancellation).ConfigureAwait(true);
                }
            }
            using (var accounting = database.Context())
            {
                var transaction = await accounting.Database.BeginTransactionAsync(IsolationLevel.Serializable, Cancellation).ConfigureAwait(true);
                await using (transaction.ConfigureAwait(true))
                {
                    accounting.StockMovements.AddRange(Movement(-5, 3), Movement(-16, 3));
                    var error = await Assert.ThrowsAsync<BusinessRuleException>(() => accounting.SaveChangesAsync(Cancellation)).ConfigureAwait(true);
                    Assert.Equal("inventory.negative_stock_blocked", error.Code);
                    await transaction.RollbackAsync(Cancellation).ConfigureAwait(true);
                }
            }
            Assert.Equal(2, await legacy.StockMovements.CountAsync(Cancellation).ConfigureAwait(true));
            using (var accounting = database.Context())
            {
                var transaction = await accounting.Database.BeginTransactionAsync(IsolationLevel.Serializable, Cancellation).ConfigureAwait(true);
                await using (transaction.ConfigureAwait(true))
                {
                    var issue = Movement(-5, 3);
                    accounting.StockMovements.Add(issue);
                    await accounting.SaveChangesAsync(Cancellation).ConfigureAwait(true);
                    Assert.Equal(20, issue.QuantityBefore);
                    Assert.Equal(15, issue.QuantityAfter);
                    Assert.Equal(60, issue.ValueBefore);
                    Assert.Equal(45, issue.ValueAfter);
                    Assert.True(issue.RecordedSequence > 0);
                    await transaction.CommitAsync(Cancellation).ConfigureAwait(true);
                }
            }
        }
    }

    private static SystemDbContext SystemContext(SqlTestDatabase database) => new(new DbContextOptionsBuilder<SystemDbContext>()
        .UseSqlServer(database.ConnectionString, sql => sql.UseCompatibilityLevel(database.CompatibilityLevel)).Options);
    private static IConfiguration Configuration(SqlTestDatabase database) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["ConnectionStrings:PetraERP"] = database.ConnectionString }).Build();
    private static SqlFiscalDatabaseResolver Resolver(SystemDbContext system, SqlTestDatabase database) => new(system, Configuration(database), Options.Create(new FiscalDatabaseOptions()));
    private static FiscalDatabaseRegistryRow Registry(Guid company, int year, string name) => new()
    {
        Id = Guid.NewGuid(), CompanyId = company, FiscalYearId = Guid.NewGuid(), YearNumber = year, YearCode = $"FY{year}",
        RangeStart = new(year, 1, 1), RangeEnd = new(year, 12, 31), DatabaseName = name, SafeDatabaseKey = name, State = "Active", SchemaVersion = 1,
    };
}
