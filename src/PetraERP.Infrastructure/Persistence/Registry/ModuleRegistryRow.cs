using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Modules.Host;

namespace PetraERP.Infrastructure.Persistence.Registry;

/// <summary>
/// Durable registration of every module Petra knows about. Module state must survive an API restart, or a half-finished
/// install would silently reset itself and an operator could enable something that never verified.
/// </summary>
public sealed class ModuleRegistryRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public string ModuleId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int ContractVersion { get; set; }
    public int State { get; set; }
    public string PackageFingerprint { get; set; } = string.Empty;
    public string DataDirectory { get; set; } = string.Empty;
    public string? LastError { get; set; }
    public string DeclaredPermissions { get; set; } = string.Empty;
    public string Dependencies { get; set; } = string.Empty;
    public string LicenseRequirement { get; set; } = "None";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class ModuleRegistryConfiguration : IEntityTypeConfiguration<ModuleRegistryRow>
{
    public void Configure(EntityTypeBuilder<ModuleRegistryRow> builder)
    {
        builder.ToTable("ModuleRegistry", "system", table =>
        {
            table.HasCheckConstraint("CK_ModuleRegistry_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_ModuleRegistry_State",
                $"[State] BETWEEN {(int)ModuleState.Discovered} AND {(int)ModuleState.RollbackRequired}");
            table.HasCheckConstraint("CK_ModuleRegistry_Contract", "[ContractVersion] > 0");
            table.HasCheckConstraint("CK_ModuleRegistry_ModuleId", "[ModuleId] <> ''");
            StorageConventions.UtcCheck(table, "CK_ModuleRegistry_Utc");
        });

        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.ModuleId).HasMaxLength(64).IsRequired();
        builder.Property(row => row.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(row => row.Publisher).HasMaxLength(128).IsRequired();
        builder.Property(row => row.Version).HasMaxLength(32).IsRequired();
        builder.Property(row => row.PackageFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(row => row.DataDirectory).HasMaxLength(512).IsRequired();
        builder.Property(row => row.LastError).HasMaxLength(1024);
        builder.Property(row => row.DeclaredPermissions).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.Dependencies).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.LicenseRequirement).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => row.ModuleId).IsUnique().HasDatabaseName("UX_ModuleRegistry_ModuleId");
        StorageConventions.Tracking(builder);
    }
}
