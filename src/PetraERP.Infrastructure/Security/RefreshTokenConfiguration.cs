using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshTokenRow>
{
    public void Configure(EntityTypeBuilder<RefreshTokenRow> builder)
    {
        builder.ToTable("RefreshTokens", SecurityStorage.Schema, table =>
        {
            table.HasCheckConstraint("CK_RefreshTokens_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_RefreshTokens_TokenHash", "DATALENGTH([TokenHash]) = 32");
            table.HasCheckConstraint("CK_RefreshTokens_Lifetime", "[ExpiresAtUtc] > [IssuedAtUtc]");
            table.HasCheckConstraint("CK_RefreshTokens_Consumption",
                "([ConsumedAtUtc] IS NULL AND [ReplacedByTokenId] IS NULL) OR ([ConsumedAtUtc] IS NOT NULL)");
            table.HasCheckConstraint("CK_RefreshTokens_Utc",
                $"{SecurityStorage.Utc("[IssuedAtUtc]")} AND {SecurityStorage.Utc("[ExpiresAtUtc]")} AND ({SecurityStorage.NullableUtc("[ConsumedAtUtc]")})");
        });
        builder.HasKey(static row => row.Id);
        builder.Property(static row => row.Id).ValueGeneratedNever();
        builder.Property(static row => row.TokenHash).HasColumnType("binary(32)").IsRequired();
        builder.HasIndex(static row => row.TokenHash).IsUnique();
        builder.HasIndex(static row => row.SessionId);
        builder.Property(static row => row.IssuedAtUtc).HasPrecision(7);
        builder.Property(static row => row.ExpiresAtUtc).HasPrecision(7);
        builder.Property(static row => row.ConsumedAtUtc).HasPrecision(7);
        builder.HasOne<SessionRow>().WithMany().HasForeignKey(static row => row.SessionId).OnDelete(DeleteBehavior.NoAction);
    }
}
