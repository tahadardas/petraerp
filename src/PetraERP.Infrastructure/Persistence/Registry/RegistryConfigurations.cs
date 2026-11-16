using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetraERP.Infrastructure.Persistence.Registry;

internal sealed class CompanyDatabaseRegistryConfiguration : IEntityTypeConfiguration<CompanyDatabaseRegistryRow>
{
    public void Configure(EntityTypeBuilder<CompanyDatabaseRegistryRow> builder)
    {
        builder.ToTable("CompanyDatabases", "system");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.DatabaseKey).HasMaxLength(64).IsRequired();
        builder.Property(row => row.DatabaseName).HasMaxLength(128).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(row => row.CompanyId).IsUnique();
        builder.HasIndex(row => row.DatabaseKey).IsUnique();
        StorageConventions.Tracking(builder);
    }
}

internal sealed class FiscalDatabaseRegistryConfiguration : IEntityTypeConfiguration<FiscalDatabaseRegistryRow>
{
    public void Configure(EntityTypeBuilder<FiscalDatabaseRegistryRow> builder)
    {
        builder.ToTable("FiscalDatabases", "system", table =>
            table.HasCheckConstraint("CK_FiscalDatabases_Range", "([RangeStart] IS NULL AND [RangeEnd] IS NULL) OR ([RangeStart] IS NOT NULL AND [RangeEnd] IS NOT NULL AND [RangeStart] <= [RangeEnd])"));
        builder.HasKey(row => row.Id);
        builder.Property(row => row.YearCode).HasMaxLength(64);
        builder.HasIndex(row => row.CompanyId).IsUnique().HasFilter("[IsDefault] = 1")
            .HasDatabaseName("UX_FiscalDatabases_CompanyDefault");
        builder.HasIndex(row => new { row.CompanyId, row.YearCode }).IsUnique().HasFilter("[YearCode] IS NOT NULL");
        builder.Property(row => row.ProvisioningFingerprint).HasMaxLength(64);
        builder.Property(row => row.SourceFiscalYearId);
        builder.Property(row => row.SafeDatabaseKey).HasMaxLength(64).IsRequired();
        builder.Property(row => row.DatabaseName).HasMaxLength(128).IsRequired();
        builder.Property(row => row.State).HasMaxLength(32).IsRequired();
        builder.HasIndex(row => new { row.CompanyId, row.FiscalYearId }).IsUnique();
        builder.HasIndex(row => new { row.CompanyId, row.YearNumber }).IsUnique();
        builder.HasIndex(row => row.SafeDatabaseKey).IsUnique();
        StorageConventions.Tracking(builder);
    }
}

internal sealed class ProvisioningOperationConfiguration : IEntityTypeConfiguration<ProvisioningOperationRow>
{
    public void Configure(EntityTypeBuilder<ProvisioningOperationRow> builder)
    {
        builder.ToTable("ProvisioningOperations", "system");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.OperationType).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.TargetDatabaseName).HasMaxLength(128).IsRequired();
        builder.Property(row => row.CurrentStep).HasMaxLength(128);
        builder.Property(row => row.ErrorDetails).HasMaxLength(4000);
        StorageConventions.Tracking(builder);
    }
}

internal sealed class DatabaseVersionRegistryConfiguration : IEntityTypeConfiguration<DatabaseVersionRegistryRow>
{
    public void Configure(EntityTypeBuilder<DatabaseVersionRegistryRow> builder)
    {
        builder.ToTable("DatabaseVersions", "system");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.DatabaseName).HasMaxLength(128).IsRequired();
        builder.Property(row => row.MigrationId).HasMaxLength(150).IsRequired();
        builder.Property(row => row.ProductVersion).HasMaxLength(32).IsRequired();
        builder.HasIndex(row => new { row.DatabaseName, row.MigrationId }).IsUnique();
    }
}

internal sealed class BackupCatalogConfiguration : IEntityTypeConfiguration<BackupCatalogRow>
{
    public void Configure(EntityTypeBuilder<BackupCatalogRow> builder)
    {
        builder.ToTable("BackupCatalog", "system");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.DatabaseName).HasMaxLength(128).IsRequired();
        builder.Property(row => row.BackupType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.BackupFilePath).HasMaxLength(512).IsRequired();
        builder.Property(row => row.ChecksumSha256).HasMaxLength(64).IsRequired();
        StorageConventions.Tracking(builder);
    }
}

internal sealed class GlobalEntityMappingConfiguration : IEntityTypeConfiguration<GlobalEntityMappingRow>
{
    public void Configure(EntityTypeBuilder<GlobalEntityMappingRow> builder)
    {
        builder.ToTable("GlobalEntityMappings", "system");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.EntityType).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => new { row.CompanyId, row.EntityType, row.LocalEntityId, row.FiscalYearId }).IsUnique();
        builder.HasIndex(row => new { row.CompanyId, row.EntityType, row.GlobalEntityId });
    }
}

internal sealed class IntercompanyOperationRegistryConfiguration : IEntityTypeConfiguration<IntercompanyOperationRegistryRow>
{
    public void Configure(EntityTypeBuilder<IntercompanyOperationRegistryRow> builder)
    {
        builder.ToTable("IntercompanyOperations", "system");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.OperationType).HasMaxLength(64).IsRequired();
        builder.Property(row => row.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Amount).HasPrecision(18, 4);
        StorageConventions.Tracking(builder);
    }

internal sealed class MigrationRunConfiguration : IEntityTypeConfiguration<MigrationRunRow>
{
    public void Configure(EntityTypeBuilder<MigrationRunRow> builder)
    {
        builder.ToTable("MigrationRuns", "system", table =>
        {
            table.HasCheckConstraint("CK_MigrationRuns_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_MigrationRuns_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_MigrationRuns_Mode", "[Mode] IN (1, 2)");
            table.HasCheckConstraint("CK_MigrationRuns_State", "[State] BETWEEN 1 AND 11");
            table.HasCheckConstraint("CK_MigrationRuns_Issues", "[IdentityIssueCount] >= 0");
            StorageConventions.UtcCheck(table, "CK_MigrationRuns_Utc");
        });

        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.SourceFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(row => row.SourceDatabaseName).HasMaxLength(128).IsRequired();
        builder.Property(row => row.TargetDatabaseName).HasMaxLength(128);
        builder.Property(row => row.ReconciliationJson).HasMaxLength(4000);
        builder.HasIndex(row => row.CompanyId).HasDatabaseName("IX_MigrationRuns_Company");
        StorageConventions.Tracking(builder);
    }
}
}
