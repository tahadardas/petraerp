using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace PetraERP.Reporting.OpenSource;

/// <summary>RFC 4180 quoting, invariant lossless decimals, and UTF-8 with an optional Excel BOM.</summary>
public sealed class CsvReportExporter
{
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Exporters expose consistent instance entry points for application composition.")]
    public byte[] Export(ReportDocument report, bool includeBom = true)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(includeBom), 64 * 1024, leaveOpen: true))
        {
            writer.NewLine = "\r\n";
            WriteRow(writer, report.Columns.Select(x => ProtectFormula(x.Heading)));
            foreach (var row in report.Rows)
                WriteRow(writer, report.Columns.Select(column =>
                {
                    row.TryGetValue(column.Key, out var value);
                    var text = ReportValueFormatter.Lossless(value, column);
                    // Typed financial negatives are numbers. Text negatives are untrusted spreadsheet input.
                    return column.Kind == ReportValueKind.Text ? ProtectFormula(text) : text;
                }));
        }
        return stream.ToArray();
    }

    internal static string ProtectFormula(string value)
    {
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character) ||
                char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.Format) continue;
            return character is '=' or '+' or '-' or '@' ? "'" + value : value;
        }
        return value;
    }

    private static void WriteRow(TextWriter writer, IEnumerable<string> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first) writer.Write(',');
            first = false;
            writer.Write('"');
            writer.Write(value.Replace("\"", "\"\"", StringComparison.Ordinal));
            writer.Write('"');
        }
        writer.WriteLine();
    }
}
