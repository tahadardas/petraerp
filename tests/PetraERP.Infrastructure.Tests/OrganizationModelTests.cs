using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

public sealed class OrganizationModelTests
{
    private static readonly string[] BranchIdentity = ["CompanyId", "Id"];
    private static readonly string[] BranchCode = ["CompanyId", "Code"];
    private static PetraDbContext Context() => new(new DbContextOptionsBuilder<PetraDbContext>().UseSqlServer().Options);

    [Fact]
    public void OrganizationStorageHasSixExplicitTablesAndNoCascadeDeletes()
    {
        using var context = new PetraDbContext(new DbContextOptionsBuilder<PetraDbContext>()
            .UseSqlServer().Options);
        var entities = context.Model.GetEntityTypes().Where(static entity => entity.GetSchema() == "system").ToArray();
        var registry = typeof(PetraERP.Infrastructure.Persistence.Registry.CompanyDatabaseRegistryRow).Namespace;
        Assert.Equal(8, entities.Count(entity => entity.ClrType.Namespace != registry));

        // The central registry is an explicit, named set: adding a table is a reviewed decision, so the names are
        // asserted rather than only counted. A new table that happens to keep the count at nine must still be listed.
        var registryTables = entities
            .Where(entity => entity.ClrType.Namespace == registry)
            .Select(static entity => entity.GetTableName())
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(10, registryTables.Length);
        Assert.Equal(
            [
                "BackupCatalog", "CompanyDatabases", "DatabaseVersions", "FiscalDatabases",
                "GlobalEntityMappings", "InterCompanyLinks", "IntercompanyOperations", "MigrationRuns",
                "ModuleRegistry", "ProvisioningOperations",
            ],
            registryTables);
        Assert.All(entities.SelectMany(static entity => entity.GetForeignKeys()),
            foreignKey => Assert.True(foreignKey.DeleteBehavior is DeleteBehavior.NoAction or DeleteBehavior.Restrict));
    }

    [Fact]
    public void StorageUsesUnicodeBoundedCodesAndGeneratedConcurrencyVersions()
    {
        using var context = Context();
        foreach (var type in context.Model.GetEntityTypes().Where(static t => typeof(ITrackedStorageRow).IsAssignableFrom(t.ClrType)))
        {
            var version = type.FindProperty("RowVersion")!;
            Assert.True(version.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
            Assert.Equal("rowversion", version.GetColumnType());
            Assert.Equal(7, type.FindProperty("CreatedAtUtc")!.GetPrecision());
            Assert.Equal(7, type.FindProperty("UpdatedAtUtc")!.GetPrecision());
            if (type.FindProperty("NameArabic") is { } name)
            {
                Assert.False(name.IsNullable);
                Assert.Equal(200, name.GetMaxLength());
                Assert.Equal(typeof(string), name.ClrType);
            }
        }

        var company = context.Model.FindEntityType(typeof(CompanyRow))!;
        Assert.Equal(64, company.FindProperty("Code")!.GetMaxLength());
        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CompanyRow))!;
        Assert.Equal("Latin1_General_100_BIN2", design.FindProperty("Code")!.GetCollation());
        Assert.Equal(ValueGenerated.Never, company.FindProperty("Id")!.ValueGenerated);
    }

    [Fact]
    public void BranchIdentityAndCodesHaveCompanyScopedUniqueness()
    {
        using var context = Context();
        var branch = context.Model.FindEntityType(typeof(BranchRow))!;
        Assert.Contains(branch.GetKeys(), key => key.Properties.Select(static property => property.Name).SequenceEqual(BranchIdentity));
        Assert.Contains(branch.GetIndexes(), index => index.IsUnique && index.Properties.Select(static property => property.Name)
            .SequenceEqual(BranchCode));
    }

    [Fact]
    public void MigrationHasBothBaseCurrencyGuardsAndSafeIdempotentTriggerBatches()
    {
        using var context = Context();
        var migrator = context.GetService<IMigrator>();
        var up = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("TR_CompanyAdditionalCurrencies_BaseCurrency", up, StringComparison.Ordinal);
        Assert.Contains("TR_Companies_BaseCurrency", up, StringComparison.Ordinal);
        Assert.Contains("EXEC(N'CREATE TRIGGER", up, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, HOLDLOCK", up, StringComparison.Ordinal);
        var down = migrator.GenerateScript("20261015154528_OrganizationFoundation", Migration.InitialDatabase);
        Assert.Contains("DROP TRIGGER IF EXISTS", down, StringComparison.Ordinal);
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
