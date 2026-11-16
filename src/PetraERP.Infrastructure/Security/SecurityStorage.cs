namespace PetraERP.Infrastructure.Security;

internal static class SecurityStorage
{
    internal const string Schema = "security";
    internal const string AuditAppendOnlyTrigger = "TR_SecurityAuditEvents_AppendOnly";

    internal static string NonEmptyGuid(string column) => $"{column} <> '00000000-0000-0000-0000-000000000000'";

    internal static string Utc(string column) => $"DATEPART(TZOFFSET, {column}) = 0";

    internal static string NullableUtc(string column) => $"{column} IS NULL OR DATEPART(TZOFFSET, {column}) = 0";

    /// <summary>ASCII login/code characters under a binary collation, so ranges are code-point exact.</summary>
    internal static string AsciiIdentifier(string column, string allowed) =>
        $"{column} COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^{allowed}]%'";
}
