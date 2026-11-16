namespace PetraERP.Contracts.Financial;

public sealed record AnalyticalColumnDto
{
    public string Key { get; init; } = string.Empty;
    public string TitleArabic { get; init; } = string.Empty;
    public string TitleEnglish { get; init; } = string.Empty;
    /// <summary>Text, Date, Amount or Quantity.</summary>
    public string Kind { get; init; } = "Text";
}

public sealed record AnalyticalRowDto
{
    public List<string> Cells { get; init; } = [];
}

public sealed record AnalyticalReportResponse
{
    public string ReportId { get; init; } = string.Empty;
    public string TitleArabic { get; init; } = string.Empty;
    public string TitleEnglish { get; init; } = string.Empty;
    public string BaseCurrencyCode { get; init; } = string.Empty;
    public List<AnalyticalColumnDto> Columns { get; init; } = [];
    public List<AnalyticalRowDto> Rows { get; init; } = [];
    public List<string>? Totals { get; init; }
    public List<string> Notes { get; init; } = [];
}
