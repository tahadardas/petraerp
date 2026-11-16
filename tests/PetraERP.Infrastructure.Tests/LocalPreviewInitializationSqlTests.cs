using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Parties;
using PetraERP.Application.Patterns;
using PetraERP.Application.Security;
using PetraERP.Domain.Parties;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.GeneralLedger.Templates;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Security;
using PetraERP.Infrastructure.Tests.Security;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.Tests;

public sealed class LocalPreviewInitializationSqlTests
{
    [Theory(Explicit = true)]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "SqlServer")]
    public async Task FirstRunCreatesUsableIndependentWorkspaceAndRestartPreservesExistingMasterData(bool hasHistoricalData)
    {
        var token = TestContext.Current.CancellationToken;
        var installation = Guid.NewGuid();
        var database = await SqlTestDatabase.CreatePreviewAsync(installation).ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var legacy = database.Context();
            await legacy.Database.MigrateAsync(token).ConfigureAwait(true);
            var bootstrap = await new SqlSecurityBootstrapper(legacy, SecuritySqlFixture.Hasher, TimeProvider.System)
                .BootstrapAsync(new BootstrapAdministratorCommand(SecuritySqlFixture.AdminUser, "مدير الاختبار", "Preview admin",
                    SecuritySqlFixture.AdminPassword, new BootstrapOrganization("PREVIEW", "شركة الاختبار", "Preview company", "USD", "دولار أمريكي", "MAIN", "الفرع الرئيسي", "Main branch")), token).ConfigureAwait(true);
            var bootstrapCompany = await legacy.Companies.SingleAsync(c => c.Code == "PREVIEW", token).ConfigureAwait(true);
            Guid? historicalJournalId = null;
            if (hasHistoricalData)
            {
                var branch = await legacy.Branches.SingleAsync(b => b.CompanyId == bootstrapCompany.Id, token).ConfigureAwait(true);
                await new SqlChartOfAccountsService(legacy, TimeProvider.System).ImportAsync(
                    LedgerMasterScope.Create(bootstrapCompany.Id, bootstrap.AdministratorUserId, branch.Id), ChartOfAccountsTemplates.Generic, token).ConfigureAwait(true);
                var cash = await legacy.Accounts.SingleAsync(a => a.CompanyId == bootstrapCompany.Id && a.Code == "1101", token).ConfigureAwait(true);
                var capital = await legacy.Accounts.SingleAsync(a => a.CompanyId == bootstrapCompany.Id && a.Code == "3101", token).ConfigureAwait(true);
                historicalJournalId = (await new SqlJournalEntryService(legacy, TimeProvider.System).CreateDraftAsync(
                    JournalEntryScope.Create(bootstrapCompany.Id, branch.Id, bootstrap.AdministratorUserId), new CreateJournalDraftCommand
                    {
                        AccountingDate = new DateOnly(2026, 10, 25), Description = "قيد تجريبي مركزي سابق يجب الحفاظ عليه",
                        Lines = [LedgerScenario.Line(cash.Id, 10m, 0m, "USD"), LedgerScenario.Line(capital.Id, 0m, 10m, "USD")],
                    }, token).ConfigureAwait(true)).Id;
            }
            using var system = new SystemDbContext(new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(database.ConnectionString,
                sql => sql.UseCompatibilityLevel(database.CompatibilityLevel)).Options);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:PetraERP"] = database.ConnectionString }).Build();
            var options = Options.Create(new FiscalDatabaseOptions { CompatibilityLevel = database.CompatibilityLevel, DatabaseNamePrefix = database.DatabaseName });
            var authority = new SqlAccountingActorAuthority(system);
            var resolver = new SqlFiscalDatabaseResolver(system, configuration, options);
            var factory = new AccountingDbContextFactory(resolver, options, authority, system);
            var provisioning = new SqlCompanyProvisioningService(system, factory, configuration, options, NullLogger<SqlCompanyProvisioningService>.Instance, authority);
            var confirmation = new SqlAdministratorConfirmation(new CentralDataContext(system), SecuritySqlFixture.Hasher, authority, TimeProvider.System);
            var initializer = new SqlLocalPreviewInitializer(system, provisioning, resolver, factory, confirmation);
            try
            {
                var rejected = await Assert.ThrowsAsync<BusinessRuleException>(() => initializer.InitializeAsync(Guid.NewGuid(),
                    SecuritySqlFixture.AdminUser, SecuritySqlFixture.AdminPassword, new DateOnly(2026, 10, 25), token)).ConfigureAwait(true);
                Assert.Equal("preview.database_not_owned", rejected.Code);
                Assert.Empty(await system.FiscalDatabases.ToListAsync(token).ConfigureAwait(true));
                var first = await initializer.InitializeAsync(installation, SecuritySqlFixture.AdminUser, SecuritySqlFixture.AdminPassword,
                    new DateOnly(2026, 10, 25), token).ConfigureAwait(true);
                Assert.Equal("Ready", first.Status);
                Assert.Equal(hasHistoricalData ? "PREVIEW-DEMO" : "PREVIEW", first.CompanyCode);
                Assert.Equal(hasHistoricalData ? "HQ" : "MAIN", first.BranchCode);
                if (hasHistoricalData) Assert.NotEqual(bootstrapCompany.Id, first.CompanyId);
                else Assert.Equal(bootstrapCompany.Id, first.CompanyId);
                Assert.NotEqual(database.DatabaseName, first.AnnualDatabase);
                Guid ownerPartyId;
                Guid[] patterns;
                using (var annual = await factory.CreateDbContextAsync(first.WorkspaceId, token).ConfigureAwait(true))
                {
                    Assert.Equal(17, await annual.Accounts.CountAsync(token).ConfigureAwait(true));
                    Assert.Equal(12, await annual.AccountingPeriods.CountAsync(p => p.State == 0, token).ConfigureAwait(true));
                    Assert.Equal(2, await annual.Parties.CountAsync(token).ConfigureAwait(true));
                    Assert.Single(await annual.Items.ToListAsync(token).ConfigureAwait(true));
                    var warehouse = Assert.Single(await annual.Warehouses.ToListAsync(token).ConfigureAwait(true));
                    Assert.Equal(first.BranchId, warehouse.BranchId);
                    patterns = await annual.DocumentPatterns.Where(p => p.ActiveRevisionId != null).Select(p => p.Id).OrderBy(id => id).ToArrayAsync(token).ConfigureAwait(true);
                    Assert.Equal(4, patterns.Length);
                    Assert.Equal(4, await annual.PatternRevisions.CountAsync(r => r.State == (int)PatternRevisionState.Published, token).ConfigureAwait(true));
                    annual.BindWriteScope(new AccountingWriteScope(first.CompanyId, first.FiscalYearId, bootstrap.AdministratorUserId,
                        AccountingOperation.ManageParties, first.BranchId));
                    var account = await annual.Accounts.SingleAsync(a => a.Code == "1103", token).ConfigureAwait(true);
                    ownerPartyId = (await new SqlPartyService(annual).CreatePartyAsync(first.CompanyId,
                        new CreatePartyCommand("OWNER-CUSTOMER", "عميل أضافه المالك", null, PartyRole.Customer,
                            DefaultCurrencyCode: "USD", ReceivableAccountId: account.Id)
                        { ChangedByUserId = bootstrap.AdministratorUserId, ChangedInBranchId = first.BranchId }, token).ConfigureAwait(true)).Id;
                }
                Guid ownerDraftId;
                using (var annual = await factory.CreateDbContextAsync(first.WorkspaceId, token).ConfigureAwait(true))
                {
                    annual.BindWriteScope(new AccountingWriteScope(first.CompanyId, first.FiscalYearId, bootstrap.AdministratorUserId,
                        AccountingOperation.ManagePatterns, first.BranchId));
                    var service = new SqlPatternService(annual);
                    var scope = PatternScope.Create(first.CompanyId, first.BranchId, bootstrap.AdministratorUserId);
                    var detail = await service.GetPatternAsync(scope, patterns[0], token).ConfigureAwait(true);
                    Assert.NotNull(detail.ActiveRevision);
                    ownerDraftId = (await service.CreateRevisionAsync(scope, detail.Id, new CreatePatternRevisionCommand
                    {
                        ExpectedVersion = detail.Version, Configuration = detail.ActiveRevision.Configuration,
                        Reason = "Owner draft must remain unpublished after restart",
                    }, token).ConfigureAwait(true)).Id;
                }
                var repeated = await initializer.InitializeAsync(installation, SecuritySqlFixture.AdminUser, SecuritySqlFixture.AdminPassword,
                    new DateOnly(2026, 10, 25), token).ConfigureAwait(true);
                Assert.Equal(first, repeated);
                using var check = await factory.CreateDbContextAsync(first.WorkspaceId, token).ConfigureAwait(true);
                Assert.Equal(17, await check.Accounts.CountAsync(token).ConfigureAwait(true));
                Assert.Equal(3, await check.Parties.CountAsync(token).ConfigureAwait(true));
                Assert.True(await check.Parties.AnyAsync(p => p.Id == ownerPartyId && p.Code == "OWNER-CUSTOMER", token).ConfigureAwait(true));
                Assert.Equal(patterns, await check.DocumentPatterns.Select(p => p.Id).OrderBy(id => id).ToArrayAsync(token).ConfigureAwait(true));
                Assert.Equal(5, await check.PatternRevisions.CountAsync(token).ConfigureAwait(true));
                Assert.Equal(4, await check.PatternRevisions.CountAsync(r => r.State == (int)PatternRevisionState.Published, token).ConfigureAwait(true));
                Assert.Equal((int)PatternRevisionState.Draft, (await check.PatternRevisions.SingleAsync(r => r.Id == ownerDraftId, token).ConfigureAwait(true)).State);
                Assert.Empty(await check.JournalEntries.ToListAsync(token).ConfigureAwait(true));
                Assert.Empty(await check.StockMovements.ToListAsync(token).ConfigureAwait(true));
                Assert.Single(await system.FiscalDatabases.ToListAsync(token).ConfigureAwait(true));
                Assert.Single(await system.ProvisioningOperations.ToListAsync(token).ConfigureAwait(true));
                Assert.Equal(hasHistoricalData ? 1 : 0, await legacy.JournalEntries.CountAsync(token).ConfigureAwait(true));
                if (historicalJournalId is { } preserved)
                {
                    var historical = await legacy.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == preserved, token).ConfigureAwait(true);
                    Assert.Equal(bootstrapCompany.Id, historical.CompanyId);
                    Assert.Equal("قيد تجريبي مركزي سابق يجب الحفاظ عليه", historical.Description);
                    Assert.Equal(2, await legacy.JournalLines.CountAsync(l => l.JournalId == preserved, token).ConfigureAwait(true));
                    Assert.False(await system.FiscalDatabases.AnyAsync(y => y.CompanyId == bootstrapCompany.Id, token).ConfigureAwait(true));
                }
            }
            finally
            {
                foreach (var name in await system.FiscalDatabases.Select(y => y.DatabaseName).ToListAsync(CancellationToken.None).ConfigureAwait(true))
                    if (await system.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.databases WHERE name={name}").SingleAsync(CancellationToken.None).ConfigureAwait(true) == 1)
                        await database.RegisterAnnualDatabaseAsync(name).ConfigureAwait(true);
            }
        }
    }
}
