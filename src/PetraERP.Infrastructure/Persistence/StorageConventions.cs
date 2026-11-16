using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetraERP.Infrastructure.Persistence;

internal static class StorageConventions
{
    internal const string OrdinalCollation = "Latin1_General_100_BIN2";
    internal const string NonemptyId = "[Id] <> '00000000-0000-0000-0000-000000000000'";
    private const string TrimCharacters = "NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288)";

    internal static string NormalizedRequired(string column) =>
        $"DATALENGTH({column}) > 0 AND DATALENGTH({column}) = DATALENGTH(TRIM({TrimCharacters} FROM {column}))";

    internal static string NormalizedNames =>
        $"({NormalizedRequired("[NameArabic]")}) AND ([NameEnglish] IS NULL OR ({NormalizedRequired("[NameEnglish]")}))";

    internal static void Tracking<T>(EntityTypeBuilder<T> builder) where T : class, ITrackedStorageRow
    {
        builder.Property(static row => row.CreatedAtUtc).HasPrecision(7).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(static row => row.UpdatedAtUtc).HasPrecision(7).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(static row => row.RowVersion).IsRowVersion();
    }

    internal static void UtcCheck<T>(TableBuilder<T> table, string name) where T : class =>
        table.HasCheckConstraint(name, "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
}
