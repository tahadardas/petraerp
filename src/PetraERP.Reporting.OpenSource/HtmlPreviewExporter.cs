using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;

namespace PetraERP.Reporting.OpenSource;

/// <summary>Safe preview of the SAME immutable dataset. Not a print-layout PDF preview.</summary>
public sealed class HtmlPreviewExporter
{
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Exporters expose consistent instance entry points for application composition.")]
    public string Export(ReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var html = new StringBuilder("<!doctype html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\">")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">")
            .Append("<style>body{font:13px 'Segoe UI',Tahoma,sans-serif;background:#f6f8fa;color:#192b3b;padding:18px}")
            .Append("header{border-bottom:2px solid #1f6074;padding-bottom:12px}h1{font-size:20px;margin:0 0 8px}")
            .Append("table{width:100%;border-collapse:collapse;background:white;margin-top:18px}th{background:#e7eff4}")
            .Append("th,td{padding:9px 7px;border:1px solid #d7e0e5;text-align:right;white-space:pre-wrap}")
            .Append("tbody tr:nth-child(even){background:#f6f8fa}.num{direction:ltr;text-align:left;font-variant-numeric:tabular-nums}")
            .Append("@media print{body{background:white;padding:0}thead{display:table-header-group}}</style></head><body><header><h1>")
            .Append(E(report.Title)).Append("</h1><div>").Append(E(report.CompanyLabel)).Append(" — ")
            .Append(E(report.PeriodLabel)).Append(" — ").Append(E(report.CurrencyCode)).Append("</div></header><table><thead><tr>");
        foreach (var column in report.Columns) html.Append("<th>").Append(E(column.Heading)).Append("</th>");
        html.Append("</tr></thead><tbody>");
        foreach (var row in report.Rows)
        {
            html.Append("<tr>");
            foreach (var column in report.Columns)
            {
                row.TryGetValue(column.Key, out var value);
                var numeric = column.Kind is ReportValueKind.Amount or ReportValueKind.Quantity or ReportValueKind.Integer;
                html.Append(numeric ? "<td class=\"num\">" : "<td>")
                    .Append(E(ReportValueFormatter.Display(value, column))).Append("</td>");
            }
            html.Append("</tr>");
        }
        html.Append("</tbody></table><section>");
        foreach (var measure in report.Summaries)
            html.Append("<p>").Append(E(measure.Label)).Append(": <bdi>")
                .Append(E(measure.Value.ToString(CultureInfo.InvariantCulture))).Append(' ')
                .Append(E(measure.CurrencyCode)).Append("</bdi></p>");
        html.Append("</section><small> ").Append(E(report.ExecutionId.ToString("D"))).Append(" | ")
            .Append(E(report.GeneratedAtUtc.ToString("u", CultureInfo.InvariantCulture)))
            .Append("</small></body></html>");
        return html.ToString();
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
