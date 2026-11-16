using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PetraERP.Reporting.OpenSource;

[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Integer describes the exported value kind and preserves the supplied contract.")]
public enum ReportValueKind { Text, Date, Amount, Quantity, Integer }

public sealed record ReportColumn(string Key, string Heading, ReportValueKind Kind = ReportValueKind.Text, int DecimalPlaces = 2);

/// <summary>Authorized execution metadata supplied by the query owner, never inferred by exporters.</summary>
public sealed record ReportExecutionContext(Guid CompanyId, Guid FiscalYearId, Guid? BranchId = null, Guid? ActorId = null);

/// <summary>An authoritative server measure; renderers never derive it from displayed rows.</summary>
public sealed record ReportSummaryValue(string Key, string Label, decimal Value, string CurrencyCode);

/// <summary>
/// Immutable, complete export snapshot. The API owns authorization, scope, row completeness and
/// financial measures. Display filters and hidden columns never change these measures here.
/// </summary>
public sealed class ReportDocument
{
    public const int MaximumRows = 100_000;
    public const int MaximumColumns = 32;
    private static readonly Regex KeyFormat = new("^[A-Za-z][A-Za-z0-9_]{0,31}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public string ReportCode { get; }
    public string Title { get; }
    public string CompanyLabel { get; }
    public string PeriodLabel { get; }
    public string CurrencyCode { get; }
    public IReadOnlyList<ReportColumn> Columns { get; }
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; }
    public IReadOnlyDictionary<string, string> Filters { get; }
    public IReadOnlyList<ReportSummaryValue> Summaries { get; }
    public DateTimeOffset GeneratedAtUtc { get; }
    public Guid ExecutionId { get; }
    public ReportExecutionContext? Context { get; }

    public ReportDocument(string reportCode, string title, string companyLabel, string periodLabel, string currencyCode,
        IEnumerable<ReportColumn> columns, IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        IDictionary<string, string>? filters = null, DateTimeOffset? generatedAtUtc = null,
        Guid? executionId = null, ReportExecutionContext? context = null, IEnumerable<ReportSummaryValue>? summaries = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        ReportCode = reportCode;
        Title = title;
        CompanyLabel = companyLabel ?? string.Empty;
        PeriodLabel = periodLabel ?? string.Empty;
        CurrencyCode = currencyCode ?? string.Empty;
        Context = context;
        ExecutionId = executionId ?? Guid.NewGuid();
        if (ExecutionId == Guid.Empty) throw new ArgumentException("An execution ID must not be empty.", nameof(executionId));

        var sourceColumns = columns.Take(MaximumColumns + 1).ToArray();
        if (sourceColumns.Length is < 1 or > MaximumColumns || sourceColumns.Any(x => x is null ||
                !KeyFormat.IsMatch(x.Key) || string.IsNullOrWhiteSpace(x.Heading) ||
                x.DecimalPlaces is < 0 or > 8 || !Enum.IsDefined(x.Kind)) ||
            sourceColumns.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != sourceColumns.Length)
            throw new ArgumentException("Column definitions must have 1-32 unique safe keys and valid kinds.", nameof(columns));
        Columns = Array.AsReadOnly(sourceColumns);

        // Bounded enumeration rejects an unbounded producer without allocating its entire result.
        var copied = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var original in rows)
        {
            if (copied.Count == MaximumRows)
                throw new ArgumentException("Report exceeds 100,000 rows. Request a narrower complete snapshot.", nameof(rows));
            ArgumentNullException.ThrowIfNull(original);
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var column in Columns)
            {
                original.TryGetValue(column.Key, out var value);
                values.Add(column.Key, Normalize(value, column));
            }
            copied.Add(new ReadOnlyDictionary<string, object?>(values));
        }
        Rows = copied.AsReadOnly();
        Filters = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(filters ?? new Dictionary<string, string>(), StringComparer.Ordinal));
        var measures = summaries?.ToArray() ?? [];
        if (measures.Any(x => x is null || !KeyFormat.IsMatch(x.Key) || string.IsNullOrWhiteSpace(x.Label) ||
                string.IsNullOrWhiteSpace(x.CurrencyCode)) ||
            measures.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != measures.Length)
            throw new ArgumentException("Authoritative measures require unique safe keys, labels and currency codes.", nameof(summaries));
        Summaries = Array.AsReadOnly(measures);
        GeneratedAtUtc = (generatedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
    }

    private static object? Normalize(object? value, ReportColumn column)
    {
        if (value is null) return null;
        if (column.Kind == ReportValueKind.Date) return DateValue(value);
        if (column.Kind != ReportValueKind.Text)
        {
            var number = DecimalValue(value);
            if (column.Kind == ReportValueKind.Integer && decimal.Truncate(number) != number)
                throw new ArgumentException($"'{column.Key}' requires an integral decimal value.", nameof(value));
            return number;
        }
        return value switch
        {
            string text => text,
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            _ => throw new ArgumentException($"Unsupported mutable or nondecimal value for '{column.Key}'.", nameof(value))
        };
    }

    public static decimal DecimalValue(object? value) => value switch
    {
        decimal d => d,
        int i => i,
        long l => l,
        string s when TryParseExactDecimal(s, out var result) => result,
        _ => throw new FormatException("Numeric report values must be decimals or invariant decimal strings; null is not zero.")
    };

    private static bool TryParseExactDecimal(string text, out decimal value)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value)) return false;
        // Decimal.TryParse can silently round a long fraction or underflow to zero. A normalized
        // lexical round trip rejects either case while permitting insignificant trailing zeroes.
        return string.Equals(CanonicalDecimal(text), CanonicalDecimal(value.ToString(CultureInfo.InvariantCulture)), StringComparison.Ordinal);
    }

    private static string CanonicalDecimal(string value)
    {
        var negative = value.StartsWith('-');
        var magnitude = value.TrimStart('+', '-');
        var separator = magnitude.IndexOf('.');
        var integral = (separator < 0 ? magnitude : magnitude[..separator]).TrimStart('0');
        var fractional = separator < 0 ? string.Empty : magnitude[(separator + 1)..].TrimEnd('0');
        if (integral.Length == 0) integral = "0";
        var normalized = fractional.Length == 0 ? integral : integral + "." + fractional;
        return negative && normalized != "0" ? "-" + normalized : normalized;
    }

    public static DateTime DateValue(object? value) => value switch
    {
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        DateTime dt => dt,
        string s when DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            => d.ToDateTime(TimeOnly.MinValue),
        _ => throw new FormatException("Date report values must be DateOnly, DateTime or yyyy-MM-dd.")
    };
}
