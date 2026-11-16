using System.Text.Json;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;

namespace PetraERP.Desktop.Tests;

public sealed class ReconciliationReportExportTests
{
    [Fact]
    public void ExportPreservesDisplayedAmountsArabicAndReportIdentity()
    {
        var company = Guid.NewGuid();
        var report = new ReconciliationReportResponse
        {
            CompanyId = company,
            FiscalYearId = Guid.NewGuid(),
            TotalChecks = 1,
            Findings = [new ReconciliationFindingResponse
            {
                CheckCode = "CHECK_01", Severity = "BLOCKING", SourceAmount = "1150.0000", CalculatedAmount = "1100.0000",
                Difference = "-50.0000", Explanation = "فرق، \"المطابقة\"", SuggestedAction = "راجع الحساب",
            }],
        };

        var json = ReconciliationReportExport.ToJson(report);
        var copy = JsonSerializer.Deserialize<ReconciliationReportResponse>(json);
        var csv = ReconciliationReportExport.ToCsv(report);

        Assert.NotNull(copy);
        Assert.Equal(company, copy.CompanyId);
        Assert.Equal("1150.0000", Assert.Single(copy.Findings).SourceAmount);
        Assert.Contains("\"-50.0000\",\"1150.0000\",\"1100.0000\"", csv);
        Assert.Contains("فرق، \"\"المطابقة\"\"", csv);
    }

    [Fact]
    public void CsvEscapesTextFormulasWithoutChangingNegativeAmounts()
    {
        var report = new ReconciliationReportResponse
        {
            Findings = [new ReconciliationFindingResponse { CheckCode = "=CMD", Severity = "WARNING", Difference = "-12.0000", Explanation = "  +صيغة" }],
        };

        var csv = ReconciliationReportExport.ToCsv(report);

        Assert.Contains("\"'=CMD\"", csv);
        Assert.Contains("\"-12.0000\"", csv);
        Assert.Contains("\"'  +صيغة\"", csv);
    }
}
