using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<SessionRow>
{
    public void Configure(EntityTypeBuilder<SessionRow> builder)
    {
        builder.ToTable("Sessions", SecurityStorage.Schema, table =>
        {
            table.HasCheckConstraint("CK_Sessions_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_Sessions_ClientKind", "[ClientKind] IN (1, 2, 3, 4)");
            table.HasCheckConstraint("CK_Sessions_SecurityStamp", SecurityStorage.NonEmptyGuid("[UserSecurityStamp]"));
            table.HasCheckConstraint("CK_Sessions_Lifetime",
                "[ExpiresAtUtc] > [CreatedAtUtc] AND [LastRefreshedAtUtc] >= [CreatedAtUtc]");
            table.HasCheckConstraint("CK_Sessions_Revocation",
                "([RevokedAtUtc] IS NULL AND [RevocationReason] IS NULL) OR ([RevokedAtUtc] IS NOT NULL AND [RevocationReason] BETWEEN 1 AND 8)");
            table.HasCheckConstraint("CK_Sessions_Utc",
                $"{SecurityStorage.Utc("[CreatedAtUtc]")} AND {SecurityStorage.Utc("[LastRefreshedAtUtc]")} AND {SecurityStorage.Utc("[ExpiresAtUtc]")} AND ({SecurityStorage.NullableUtc("[RevokedAtUtc]")})");
        });
        builder.HasKey(static row => row.Id);
        builder.Property(static row => row.Id).ValueGeneratedNever();
        builder.Property(static row => row.ClientKind).HasColumnType("tinyint");
        builder.Property(static row => row.DeviceName).HasMaxLength(100);
        builder.Property(static row => row.CreatedAtUtc).HasPrecision(7);
        builder.Property(static row => row.LastRefreshedAtUtc).HasPrecision(7);
        builder.Property(static row => row.ExpiresAtUtc).HasPrecision(7);
        builder.Property(static row => row.RevokedAtUtc).HasPrecision(7);
        builder.Property(static row => row.RevocationReason).HasColumnType("tinyint");
        builder.HasIndex(static row => new { row.UserId, row.RevokedAtUtc });
        builder.HasOne<UserRow>().WithMany().HasForeignKey(static row => row.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
