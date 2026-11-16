using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

public sealed partial class IndependentAccountingSqlTests
{
    [Theory(Explicit = true)]
    [Trait("Category", "SqlServer")]
    [InlineData("ALTER TABLE ledger.AccountAudit ADD UnexpectedAmount decimal(19,3) NULL")]
    [InlineData("ALTER TABLE inventory.StockTransferLines DROP CONSTRAINT CK_StockTransferLines_UnitCost; ALTER TABLE inventory.StockTransferLines ALTER COLUMN UnitCost decimal(19,3) NOT NULL; ALTER TABLE inventory.StockTransferLines WITH CHECK ADD CONSTRAINT CK_StockTransferLines_UnitCost CHECK ([UnitCost]>=0)")]
    [InlineData("ALTER TABLE inventory.StockTransferLines DROP CONSTRAINT CK_StockTransferLines_UnitCost; ALTER TABLE inventory.StockTransferLines ALTER COLUMN UnitCost decimal(19,4) NULL; ALTER TABLE inventory.StockTransferLines WITH CHECK ADD CONSTRAINT CK_StockTransferLines_UnitCost CHECK ([UnitCost]>=0)")]
    [InlineData("DROP INDEX IX_AccountAudit_CompanyAccount ON ledger.AccountAudit; CREATE INDEX IX_AccountAudit_CompanyAccount ON ledger.AccountAudit(CompanyId)")]
    [InlineData("DROP INDEX UQ_Parties_CashCustomer ON parties.Parties; CREATE UNIQUE INDEX UQ_Parties_CashCustomer ON parties.Parties(CompanyId) WHERE IsCashCustomer=0")]
    [InlineData("ALTER SEQUENCE inventory.MovementOrder INCREMENT BY 2")]
    [InlineData("DECLARE @constraint sysname=(SELECT name FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID(N'system.Currencies') AND parent_column_id=COLUMNPROPERTY(OBJECT_ID(N'system.Currencies'),N'CreatedAtUtc','ColumnId')); DECLARE @sql nvarchar(400)=N'ALTER TABLE system.Currencies DROP CONSTRAINT '+QUOTENAME(@constraint); EXEC(@sql); ALTER TABLE system.Currencies ADD DEFAULT ('2000-01-01T00:00:00+00:00') FOR CreatedAtUtc")]
    [InlineData("ALTER TABLE inventory.StockTransferLines DROP CONSTRAINT FK_StockTransferLines_Items_ItemId; ALTER TABLE inventory.StockTransferLines WITH CHECK ADD CONSTRAINT FK_StockTransferLines_Items_ItemId FOREIGN KEY (ItemId) REFERENCES inventory.UnitsOfMeasure(Id)")]
    [InlineData("ALTER TRIGGER audit.TR_ActorReferences_Immutable ON audit.ActorReferences AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; RETURN; END")]
    public async Task ResolverRejectsSemanticSchemaDriftDespiteUnchangedApprovedHistory(string tamperingSql)
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            using var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            await annual.Database.ExecuteSqlRawAsync(tamperingSql, Cancellation).ConfigureAwait(true);
            Assert.Equal(AccountingSchema.ApprovedMigrations, await annual.Database.GetAppliedMigrationsAsync(Cancellation).ConfigureAwait(true));
            var denied = await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Resolver.ResolveAsync(result.WorkspaceId, Cancellation)).ConfigureAwait(true);
            Assert.Equal("workspace.schema_mismatch", denied.Code);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task TriggerFingerprintIgnoresCommentsWhitespaceKeywordCaseAndIdentifierBrackets()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            using var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            await annual.Database.ExecuteSqlRawAsync("""
                alter trigger audit.TR_ActorReferences_Immutable
                on audit.ActorReferences after update, delete
                as begin -- Formatting is not an authorization or schema change.
                    set nocount on;
                    THROW 51020, 'Accounting audit and posted movement history is immutable.', 1;
                end;
                """, Cancellation).ConfigureAwait(true);
            var resolved = await fixture.Resolver.ResolveAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            Assert.Equal(result.CompanyId, resolved.CompanyId);
        }
    }

    [Theory(Explicit = true)]
    [Trait("Category", "SqlServer")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolverRejectsBaseCurrencyMismatchAndInventedAnnualBranch(bool currencyMismatch)
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            using var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            if (currencyMismatch)
                await annual.Database.ExecuteSqlRawAsync("""
                    INSERT system.Currencies(Code,NameArabic,NameEnglish,IsActive) VALUES(N'EUR',N'يورو',N'Euro',1);
                    UPDATE system.Companies SET BaseCurrencyCode=N'EUR';
                    """, Cancellation).ConfigureAwait(true);
            else
                await annual.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT system.Branches(Id,CompanyId,Code,NameArabic,IsActive)
                    VALUES(NEWID(),{result.CompanyId},N'FORGED',N'فرع غير مسجل',1)
                    """, Cancellation).ConfigureAwait(true);
            var denied = await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Resolver.ResolveAsync(result.WorkspaceId, Cancellation)).ConfigureAwait(true);
            Assert.Equal("workspace.scope_mismatch", denied.Code);
            Assert.Equal("USD", (await fixture.System.Companies.SingleAsync(c => c.Id == result.CompanyId, Cancellation).ConfigureAwait(true)).BaseCurrencyCode);
        }
    }

    /// <summary>
    /// The readiness cache is a bounded trade-off, not a blind spot: within its window a verified workspace is trusted, and drift
    /// introduced meanwhile is rejected as soon as the window ends. With the cache off (the default of this fixture) every call re-verifies.
    /// </summary>
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ReadinessCacheTrustsAVerifiedWorkspaceOnlyUntilItsWindowEnds()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            var configuration = Microsoft.Extensions.Configuration.MemoryConfigurationBuilderExtensions.AddInMemoryCollection(new Microsoft.Extensions.Configuration.ConfigurationBuilder(), new Dictionary<string, string?>
                { ["ConnectionStrings:PetraERP"] = fixture.Database.ConnectionString }).Build();
            var cached = new PetraERP.Infrastructure.Workspaces.SqlFiscalDatabaseResolver(fixture.System, configuration, Microsoft.Extensions.Options.Options.Create(
                new PetraERP.Infrastructure.Persistence.FiscalDatabaseOptions
                { CompatibilityLevel = fixture.Database.CompatibilityLevel, DatabaseNamePrefix = fixture.Database.DatabaseName, ReadinessCacheSeconds = 2 }));
            await cached.ResolveAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            using (var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true))
                await annual.Database.ExecuteSqlRawAsync("ALTER TABLE ledger.AccountAudit ADD CacheWindowDrift int NULL", Cancellation).ConfigureAwait(true);
            await cached.ResolveAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true); // inside the window: still trusted
            await Task.Delay(TimeSpan.FromSeconds(2.5), Cancellation).ConfigureAwait(true);
            var denied = await Assert.ThrowsAsync<BusinessRuleException>(() => cached.ResolveAsync(result.WorkspaceId, Cancellation)).ConfigureAwait(true);
            Assert.Equal("workspace.schema_mismatch", denied.Code);
            await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Resolver.ResolveAsync(result.WorkspaceId, Cancellation)).ConfigureAwait(true); // cache off: immediate
        }
    }

    /// <summary>In-process schema work (provisioning, migration, rollover) calls InvalidateReadiness so the next request re-verifies at once.</summary>
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ExplicitInvalidationEndsTheReadinessWindowImmediately()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            var configuration = Microsoft.Extensions.Configuration.MemoryConfigurationBuilderExtensions.AddInMemoryCollection(new Microsoft.Extensions.Configuration.ConfigurationBuilder(), new Dictionary<string, string?>
                { ["ConnectionStrings:PetraERP"] = fixture.Database.ConnectionString }).Build();
            var cached = new PetraERP.Infrastructure.Workspaces.SqlFiscalDatabaseResolver(fixture.System, configuration, Microsoft.Extensions.Options.Options.Create(
                new PetraERP.Infrastructure.Persistence.FiscalDatabaseOptions
                { CompatibilityLevel = fixture.Database.CompatibilityLevel, DatabaseNamePrefix = fixture.Database.DatabaseName, ReadinessCacheSeconds = 60 }));
            await cached.ResolveAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            using (var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true))
                await annual.Database.ExecuteSqlRawAsync("ALTER TABLE ledger.AccountAudit ADD InvalidationDrift int NULL", Cancellation).ConfigureAwait(true);
            await cached.ResolveAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true); // a minute-long window still trusts it
            PetraERP.Infrastructure.Workspaces.SqlFiscalDatabaseResolver.InvalidateReadiness();
            var denied = await Assert.ThrowsAsync<BusinessRuleException>(() => cached.ResolveAsync(result.WorkspaceId, Cancellation)).ConfigureAwait(true);
            Assert.Equal("workspace.schema_mismatch", denied.Code);
        }
    }
}
