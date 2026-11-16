using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Productivity;

internal sealed class WorkspacePreferenceRow : ITrackedStorageRow
{
    public Guid UserId { get; set; }
    public required string Document { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
internal sealed class WorkspacePreferenceConfiguration : IEntityTypeConfiguration<WorkspacePreferenceRow>
{
    public void Configure(EntityTypeBuilder<WorkspacePreferenceRow> builder)
    {
        builder.ToTable("WorkspacePreferences", "workspace", table =>
        {
            table.HasCheckConstraint("CK_WorkspacePreferences_Document", "ISJSON([Document]) = 1 AND DATALENGTH([Document]) <= 65536");
            StorageConventions.UtcCheck(table, "CK_WorkspacePreferences_Utc");
        });
        builder.HasKey(row => row.UserId);
        builder.Property(row => row.UserId).ValueGeneratedNever();
        builder.Property(row => row.Document).HasColumnType("nvarchar(max)").IsRequired();
        builder.HasOne<UserRow>().WithOne().HasForeignKey<WorkspacePreferenceRow>(row => row.UserId).OnDelete(DeleteBehavior.NoAction);
        StorageConventions.Tracking(builder);
    }
}
