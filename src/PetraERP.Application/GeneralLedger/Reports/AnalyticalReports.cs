namespace PetraERP.Application.GeneralLedger.Reports;

public enum AnalyticalColumnKind : byte
{
    Text = 1,
    Date = 2,
    Amount = 3,
    Quantity = 4,
}

public sealed record AnalyticalColumn(string Key, string TitleArabic, string TitleEnglish, AnalyticalColumnKind Kind);

/// <summary>Generic tabular report: cells are pre-formatted invariant strings (amounts use '.' and no grouping), so every client prints identical numbers.</summary>
public sealed record AnalyticalReport(string ReportId, string TitleArabic, string TitleEnglish, string BaseCurrencyCode, IReadOnlyList<AnalyticalColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<string>? Totals, IReadOnlyList<string> Notes);

/// <param name="CurrencyCode">Transaction currency filter (currency reports).</param>
/// <param name="AccountId">Single-account filter (account-scoped reports).</param>
public sealed record AnalyticalQuery(DateOnly? From, DateOnly? To, string? CurrencyCode, Guid? AccountId);

/// <summary>Computed-in-SQL report suite on posted journal lines. Every report is a pure read; nothing is persisted.</summary>
public interface IAnalyticalReportService
{
    IReadOnlyList<string> SupportedReportIds { get; }
    Task<AnalyticalReport> RunAsync(LedgerReportScope scope, string reportId, AnalyticalQuery query, CancellationToken cancellationToken);
}
