using System.IO.Compression;
using System.Text;

namespace PetraERP.TestDataImport;

/// <summary>
/// Streaming reader for the supplied gzip CSV export (UTF-8, RFC 4180 quoting, multiline fields).
/// Unquoted empty fields are SQL NULL and quoted empty fields are empty strings; this importer only
/// needs text values, so both read as <see cref="string.Empty"/>. The source file is opened read-only.
/// </summary>
internal static class LegacyTable
{
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Read(string directory, string table)
    {
        var path = Path.Combine(directory, "DATA", "dbo__" + table + ".csv.gz");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var records = Records(reader).GetEnumerator();
        if (!records.MoveNext()) throw new InvalidDataException(table + ": missing header.");
        var header = records.Current;
        var rows = new List<IReadOnlyDictionary<string, string>>();
        while (records.MoveNext())
        {
            var values = records.Current;
            if (values.Count != header.Count) throw new InvalidDataException($"{table}: record {rows.Count + 1} has {values.Count} fields, expected {header.Count}.");
            var row = new Dictionary<string, string>(header.Count, StringComparer.Ordinal);
            for (var index = 0; index < header.Count; index++) row[header[index]] = values[index];
            rows.Add(row);
        }
        return rows;
    }

    private static IEnumerable<List<string>> Records(TextReader reader)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var any = false;
        int next;
        while ((next = reader.Read()) >= 0)
        {
            var character = (char)next;
            any = true;
            if (quoted)
            {
                if (character != '"') { field.Append(character); continue; }
                if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                else quoted = false;
                continue;
            }
            switch (character)
            {
                case '"': quoted = true; break;
                case ',': fields.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    fields.Add(field.ToString()); field.Clear();
                    yield return fields;
                    fields = []; any = false;
                    break;
                default: field.Append(character); break;
            }
        }
        if (quoted) throw new InvalidDataException("Unterminated quoted field.");
        if (any) { fields.Add(field.ToString()); yield return fields; }
    }
}
