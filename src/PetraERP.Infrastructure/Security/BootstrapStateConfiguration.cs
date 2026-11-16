using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetraERP.Infrastructure.Security;

internal sealed class BootstrapStateConfiguration : IEntityTypeConfiguration<BootstrapStateRow>
{
    public void Configure(EntityTypeBuilder<BootstrapStateRow> builder)
    {
        builder.ToTable("BootstrapState", SecurityStorage.Schema, table =>
        {
            table.HasCheckConstraint("CK_BootstrapState_Singleton", "[Id] = 1");
            table.HasCheckConstraint("CK_BootstrapState_Utc", SecurityStorage.Utc("[CompletedAtUtc]"));
        });
        builder.HasKey(static row => row.Id);
        builder.Property(static row => row.Id).HasColumnType("tinyint").ValueGeneratedNever();
        builder.Property(static row => row.CompletedAtUtc).HasPrecision(7);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(static row => row.AdministratorUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
