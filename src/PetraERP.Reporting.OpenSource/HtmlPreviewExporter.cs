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
            .Append("<style>")
            .Append("body{font:13px -apple-system,BlinkMacSystemFont,'Segoe UI','Segoe UI Web (Arabic)',Tahoma,sans-serif;background:#f8fafc;color:#0f172a;margin:0;padding:24px}")
            .Append(".report-container{max-width:1200px;margin:0 auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:8px;box-shadow:0 1px 3px rgba(0,0,0,0.05);padding:24px}")
            .Append("header{border-bottom:2px solid #1e3a8a;padding-bottom:14px;margin-bottom:18px}")
            .Append("h1{font-size:20px;font-weight:700;color:#0f172a;margin:0 0 6px}")
            .Append(".meta-info{color:#475569;font-size:12.5px;font-weight:500}")
            .Append(".table-wrapper{overflow-x:auto;margin-top:16px;border:1px solid #cbd5e1;border-radius:6px}")
            .Append("table{width:100%;border-collapse:collapse;background:#ffffff}")
            .Append("th{background:#f1f5f9;color:#1e293b;font-weight:600;font-size:12.5px;padding:10px 8px;border:1px solid #cbd5e1;text-align:right;white-space:nowrap}")
            .Append("td{padding:8px 8px;border:1px solid #e2e8f0;text-align:right;font-size:12.5px;white-space:pre-wrap}")
            .Append("tbody tr:nth-child(even){background:#f8fafc}")
            .Append("tbody tr:hover{background:#f1f5f9}")
            .Append(".num{direction:ltr;text-align:left;font-variant-numeric:tabular-nums;font-family:'Cascadia Code','Consolas',monospace;font-size:12px}")
            .Append(".summary-section{margin-top:20px;display:flex;flex-wrap:wrap;gap:12px;justify-content:flex-end}")
            .Append(".summary-card{background:#f8fafc;border:1px solid #cbd5e1;border-radius:6px;padding:8px 14px;min-width:160px;text-align:right}")
            .Append(".summary-card p{margin:0;font-size:12px;color:#475569}")
            .Append(".summary-card strong{font-size:13.5px;color:#0f172a;display:block;margin-top:2px}")
            .Append("footer{margin-top:24px;padding-top:10px;border-top:1px solid #e2e8f0;color:#64748b;font-size:11px;display:flex;justify-content:space-between}")
            .Append("@media print{body{background:#ffffff;padding:0}.report-container{border:none;box-shadow:none;padding:0}thead{display:table-header-group}tr{page-break-inside:avoid}}")
            .Append("</style></head><body><div class=\"report-container\"><header><h1>")
            .Append(E(report.Title)).Append("</h1><div class=\"meta-info\">").Append(E(report.CompanyLabel)).Append(" — ")
            .Append(E(report.PeriodLabel)).Append(" — ").Append(E(report.CurrencyCode)).Append("</div></header><div class=\"table-wrapper\"><table><thead><tr>");
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
        html.Append("</tbody></table></div><section class=\"summary-section\">");
        foreach (var measure in report.Summaries)
            html.Append("<div class=\"summary-card\"><p>").Append(E(measure.Label)).Append("</p><strong><bdi>")
                .Append(E(measure.Value.ToString(CultureInfo.InvariantCulture))).Append(' ')
                .Append(E(measure.CurrencyCode)).Append("</bdi></strong></div>");
        html.Append("</section><footer><small>معرف التقرير: ").Append(E(report.ExecutionId.ToString("D"))).Append("</small><small>")
            .Append(E(report.GeneratedAtUtc.ToString("u", CultureInfo.InvariantCulture)))
            .Append("</small></footer></div></body></html>");
        return html.ToString();
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
