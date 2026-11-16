using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

internal sealed class UserConfiguration : IEntityTypeConfiguration<UserRow>
{
    public void Configure(EntityTypeBuilder<UserRow> builder)
    {
        builder.ToTable("Users", SecurityStorage.Schema, table =>
        {
            table.HasCheckConstraint("CK_Users_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_Users_UserName",
                $"LEN([UserName]) BETWEEN 3 AND 64 AND {SecurityStorage.AsciiIdentifier("[UserName]", "-A-Za-z0-9._")}");
            table.HasCheckConstraint("CK_Users_NormalizedUserName", "[NormalizedUserName] = UPPER([UserName])");
            table.HasCheckConstraint("CK_Users_DisplayNames",
                $"({StorageConventions.NormalizedRequired("[DisplayNameArabic]")}) AND ([DisplayNameEnglish] IS NULL OR ({StorageConventions.NormalizedRequired("[DisplayNameEnglish]")}))");
            table.HasCheckConstraint("CK_Users_PasswordAlgorithm", "[PasswordAlgorithm] = 1");
            table.HasCheckConstraint("CK_Users_PasswordIterations", "[PasswordIterations] >= 100000");
            table.HasCheckConstraint("CK_Users_PasswordMaterial", "DATALENGTH([PasswordSalt]) = 32 AND DATALENGTH([PasswordHash]) = 64");
            table.HasCheckConstraint("CK_Users_AccessFailedCount", "[AccessFailedCount] >= 0");
            table.HasCheckConstraint("CK_Users_SecurityStamp", SecurityStorage.NonEmptyGuid("[SecurityStamp]"));
            table.HasCheckConstraint("CK_Users_Timestamps",
                $"{SecurityStorage.Utc("[PasswordChangedAtUtc]")} AND ({SecurityStorage.NullableUtc("[LockoutEndUtc]")}) AND ({SecurityStorage.NullableUtc("[LastLoginAtUtc]")})");
            StorageConventions.UtcCheck(table, "CK_Users_Utc");
        });
        builder.HasKey(static row => row.Id);
        builder.Property(static row => row.Id).ValueGeneratedNever();
        builder.Property(static row => row.UserName).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(static row => row.NormalizedUserName).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.HasIndex(static row => row.NormalizedUserName).IsUnique();
        builder.Property(static row => row.DisplayNameArabic).HasMaxLength(200).IsRequired();
        builder.Property(static row => row.DisplayNameEnglish).HasMaxLength(200);
        builder.Property(static row => row.PasswordAlgorithm).HasColumnType("tinyint");
        builder.Property(static row => row.PasswordSalt).HasColumnType("binary(32)").IsRequired();
        builder.Property(static row => row.PasswordHash).HasColumnType("binary(64)").IsRequired();
        builder.Property(static row => row.PasswordChangedAtUtc).HasPrecision(7);
        builder.Property(static row => row.LockoutEndUtc).HasPrecision(7);
        builder.Property(static row => row.LastLoginAtUtc).HasPrecision(7);
        StorageConventions.Tracking(builder);
    }
}
