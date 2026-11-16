using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Accounting;

internal static class ReconciliationReportExport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string ToJson(ReconciliationReportResponse report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    public static string ToCsv(ReconciliationReportResponse report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var output = new StringBuilder();
        output.AppendLine("CheckCode,Severity,Difference,SourceAmount,CalculatedAmount,Explanation,SuggestedAction");
        foreach (var finding in report.Findings)
        {
            output.AppendJoin(',', Cell(finding.CheckCode, true), Cell(finding.Severity, true), Cell(finding.Difference, false),
                Cell(finding.SourceAmount, false), Cell(finding.CalculatedAmount, false), Cell(finding.Explanation, true), Cell(finding.SuggestedAction, true));
            output.AppendLine();
        }
        return output.ToString();
    }

    private static string Cell(string? value, bool text)
    {
        value ??= string.Empty;
        var trimmed = value.TrimStart();
        if (text && trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
