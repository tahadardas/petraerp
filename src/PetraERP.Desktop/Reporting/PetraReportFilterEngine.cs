using System.Globalization;
using System.Text;
using PetraERP.ApiClient;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Reporting;

public enum PetraReportFilterLogic { And, Or }

public enum PetraReportFilterOperator
{
    Equals,
    NotEquals,
    Contains,
    DoesNotContain,
    StartsWith,
    EndsWith,
    IsEmpty,
    IsNotEmpty,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Between,
    In,
    NotIn,
}

public abstract record PetraReportFilterNode(bool IsEnabled = true);

public sealed record PetraReportFilterCondition(
    string ColumnKey,
    PetraReportFilterOperator Operator,
    string? Value = null,
    string? SecondValue = null,
    IReadOnlyList<string>? Values = null,
    bool Enabled = true) : PetraReportFilterNode(Enabled);

public sealed record PetraReportFilterGroup(
    PetraReportFilterLogic Logic,
    IReadOnlyList<PetraReportFilterNode> Children,
    bool Enabled = true) : PetraReportFilterNode(Enabled);

public sealed record PetraReportFilterValidation(bool IsValid, IReadOnlyList<string> Errors)
{
    public static PetraReportFilterValidation Success { get; } = new(true, []);
}

internal sealed record PetraReportSearchOptions(
    string Query,
    string? ColumnKey = null,
    bool CaseSensitive = false,
    bool NormalizeArabic = true,
    bool WholeCell = false);

internal sealed record PetraReportSearchMatch(
    IReadOnlyDictionary<string, object?> Row,
    string ColumnKey);

/// <summary>
/// Typed, bounded in-memory predicates for an already authorized immutable report snapshot.
/// It cannot execute SQL and it never changes the snapshot's authoritative summaries.
/// </summary>
internal static class PetraReportFilterEngine
{
    internal const int MaximumDepth = 8;
    internal const int MaximumConditions = 64;

    internal static PetraReportFilterValidation Validate(ReportDocument document, PetraReportFilterNode? root)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (root is null) return PetraReportFilterValidation.Success;
        var errors = new List<string>();
        var conditions = 0;
        ValidateNode(root, document.Columns.ToDictionary(column => column.Key, StringComparer.Ordinal), 1, ref conditions, errors);
        if (conditions > MaximumConditions) errors.Add($"A report filter supports at most {MaximumConditions.ToString(CultureInfo.InvariantCulture)} conditions.");
        return new(errors.Count == 0, errors.AsReadOnly());
    }

    internal static IReadOnlyList<IReadOnlyDictionary<string, object?>> Apply(
        ReportDocument document,
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        PetraReportFilterNode? root)
    {
        var validation = Validate(document, root);
        if (!validation.IsValid) throw new ArgumentException(string.Join(Environment.NewLine, validation.Errors), nameof(root));
        if (root is null || !root.IsEnabled) return rows.ToArray();
        var columns = document.Columns.ToDictionary(column => column.Key, StringComparer.Ordinal);
        return rows.Where(row => MatchNode(root, row, columns)).ToArray();
    }

    internal static IReadOnlyList<PetraReportSearchMatch> Search(
        ReportDocument document,
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        PetraReportSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Query)) return [];
        var columns = options.ColumnKey is null
            ? document.Columns
            : document.Columns.Where(column => string.Equals(column.Key, options.ColumnKey, StringComparison.Ordinal)).ToArray();
        if (columns.Count == 0) return [];
        var query = Normalize(options.Query.Trim(), options);
        var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var result = new List<PetraReportSearchMatch>();
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                row.TryGetValue(column.Key, out var value);
                var candidate = Normalize(ReportGridCellConverter.Format(value, column), options);
                var matches = options.WholeCell
                    ? string.Equals(candidate, query, comparison)
                    : candidate.Contains(query, comparison);
                if (matches) result.Add(new PetraReportSearchMatch(row, column.Key));
            }
        }
        return result.AsReadOnly();
    }

    private static void ValidateNode(
        PetraReportFilterNode node,
        IReadOnlyDictionary<string, ReportColumn> columns,
        int depth,
        ref int conditions,
        ICollection<string> errors)
    {
        if (depth > MaximumDepth) { errors.Add($"Filter nesting cannot exceed {MaximumDepth.ToString(CultureInfo.InvariantCulture)} levels."); return; }
        if (node is PetraReportFilterGroup group)
        {
            if (group.Children.Count == 0) errors.Add("A filter group cannot be empty.");
            foreach (var child in group.Children) ValidateNode(child, columns, depth + 1, ref conditions, errors);
            return;
        }
        if (node is not PetraReportFilterCondition condition) { errors.Add("Unknown filter node."); return; }
        conditions++;
        if (!columns.TryGetValue(condition.ColumnKey, out var column))
        {
            errors.Add("The filter references an unavailable report field: " + condition.ColumnKey);
            return;
        }
        var isListOperator = condition.Operator is PetraReportFilterOperator.In or PetraReportFilterOperator.NotIn;
        var valueRequired = !isListOperator && condition.Operator is not PetraReportFilterOperator.IsEmpty and not PetraReportFilterOperator.IsNotEmpty;
        if (valueRequired && string.IsNullOrWhiteSpace(condition.Value)) errors.Add(column.Heading + ": a value is required.");
        if (condition.Operator == PetraReportFilterOperator.Between && string.IsNullOrWhiteSpace(condition.SecondValue))
            errors.Add(column.Heading + ": a second value is required for Between.");
        if (isListOperator)
        {
            var list = condition.Values ?? (condition.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []);
            if (list.Count == 0) errors.Add(column.Heading + ": at least one value is required for In/NotIn.");
            foreach (var item in list)
            {
                if (item.Length == 0) continue; // the "(blank)" bucket of a header filter: matches null and empty values
                if (!TryParse(item, column.Kind, out _)) errors.Add(column.Heading + ": invalid item type in list: " + item);
            }
        }
        if (column.Kind == ReportValueKind.Text && condition.Operator is PetraReportFilterOperator.GreaterThan or
            PetraReportFilterOperator.GreaterThanOrEqual or PetraReportFilterOperator.LessThan or
            PetraReportFilterOperator.LessThanOrEqual or PetraReportFilterOperator.Between)
            errors.Add(column.Heading + ": the selected numeric/date operator is not valid for text.");
        if (column.Kind != ReportValueKind.Text && condition.Operator is PetraReportFilterOperator.Contains or
            PetraReportFilterOperator.DoesNotContain or PetraReportFilterOperator.StartsWith or PetraReportFilterOperator.EndsWith)
            errors.Add(column.Heading + ": the selected text operator is not valid for this field type.");
        if (!valueRequired || string.IsNullOrWhiteSpace(condition.Value)) return;
        if (!TryParse(condition.Value, column.Kind, out _)) errors.Add(column.Heading + ": the first value has an invalid type.");
        if (condition.Operator == PetraReportFilterOperator.Between &&
            !string.IsNullOrWhiteSpace(condition.SecondValue) && !TryParse(condition.SecondValue, column.Kind, out _))
            errors.Add(column.Heading + ": the second value has an invalid type.");
    }

    private static bool MatchNode(
        PetraReportFilterNode node,
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyDictionary<string, ReportColumn> columns)
    {
        if (!node.IsEnabled) return true;
        if (node is PetraReportFilterGroup group)
            return group.Logic == PetraReportFilterLogic.And
                ? group.Children.Where(child => child.IsEnabled).All(child => MatchNode(child, row, columns))
                : group.Children.Where(child => child.IsEnabled).Any(child => MatchNode(child, row, columns));
        var condition = (PetraReportFilterCondition)node;
        var column = columns[condition.ColumnKey];
        row.TryGetValue(condition.ColumnKey, out var actual);
        return MatchCondition(actual, column, condition);
    }

    private static bool MatchCondition(object? actual, ReportColumn column, PetraReportFilterCondition condition)
    {
        if (condition.Operator == PetraReportFilterOperator.IsEmpty) return actual is null || string.IsNullOrEmpty(Convert.ToString(actual, CultureInfo.InvariantCulture));
        if (condition.Operator == PetraReportFilterOperator.IsNotEmpty) return actual is not null && !string.IsNullOrEmpty(Convert.ToString(actual, CultureInfo.InvariantCulture));
        if (condition.Operator is PetraReportFilterOperator.In or PetraReportFilterOperator.NotIn)
        {
            // Hash lookup per row (built once per condition): a 100k-row report with thousands of selected values stays linear.
            var set = ListSetOf(condition, column);
            bool matched;
            if (actual is null || actual is string { Length: 0 }) matched = set.Blank;
            else if (column.Kind == ReportValueKind.Text)
                matched = set.Text.Contains(NormalizeArabic(Convert.ToString(actual, CultureInfo.InvariantCulture) ?? string.Empty));
            else if (column.Kind == ReportValueKind.Date) matched = set.Dates.Contains(ReportDocument.DateValue(actual));
            else matched = set.Numbers.Contains(ReportDocument.DecimalValue(actual));
            return condition.Operator == PetraReportFilterOperator.In ? matched : !matched;
        }
        if (actual is null) return condition.Operator == PetraReportFilterOperator.NotEquals;
        if (column.Kind == ReportValueKind.Text)
        {
            var left = NormalizeArabic(Convert.ToString(actual, CultureInfo.InvariantCulture) ?? string.Empty);
            var right = NormalizeArabic(condition.Value?.Trim() ?? string.Empty);
            return condition.Operator switch
            {
                PetraReportFilterOperator.Equals => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
                PetraReportFilterOperator.NotEquals => !string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
                PetraReportFilterOperator.Contains => left.Contains(right, StringComparison.OrdinalIgnoreCase),
                PetraReportFilterOperator.DoesNotContain => !left.Contains(right, StringComparison.OrdinalIgnoreCase),
                PetraReportFilterOperator.StartsWith => left.StartsWith(right, StringComparison.OrdinalIgnoreCase),
                PetraReportFilterOperator.EndsWith => left.EndsWith(right, StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }
        var leftComparableNonText = Comparable(actual, column.Kind);
        var first = Parse(condition.Value!, column.Kind);
        var comparison = leftComparableNonText.CompareTo(first);
        return condition.Operator switch
        {
            PetraReportFilterOperator.Equals => comparison == 0,
            PetraReportFilterOperator.NotEquals => comparison != 0,
            PetraReportFilterOperator.GreaterThan => comparison > 0,
            PetraReportFilterOperator.GreaterThanOrEqual => comparison >= 0,
            PetraReportFilterOperator.LessThan => comparison < 0,
            PetraReportFilterOperator.LessThanOrEqual => comparison <= 0,
            PetraReportFilterOperator.Between => comparison >= 0 && leftComparableNonText.CompareTo(Parse(condition.SecondValue!, column.Kind)) <= 0,
            _ => false,
        };
    }

    private sealed record ListSet(
        ReportValueKind Kind,
        bool Blank,
        HashSet<string> Text,
        HashSet<decimal> Numbers,
        HashSet<DateTime> Dates);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PetraReportFilterCondition, ListSet> ListSets = new();

    private static ListSet ListSetOf(PetraReportFilterCondition condition, ReportColumn column)
    {
        if (ListSets.TryGetValue(condition, out var cached) && cached.Kind == column.Kind) return cached;
        var list = condition.Values ?? (condition.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []);
        var text = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var numbers = new HashSet<decimal>();
        var dates = new HashSet<DateTime>();
        var blank = false;
        foreach (var item in list)
        {
            if (item.Length == 0) { blank = true; continue; }
            if (column.Kind == ReportValueKind.Text) text.Add(NormalizeArabic(item.Trim()));
            else if (TryParse(item, column.Kind, out var parsed) && parsed is not null)
            {
                if (parsed is DateTime date) dates.Add(date);
                else if (parsed is decimal number) numbers.Add(number);
            }
        }
        var built = new ListSet(column.Kind, blank, text, numbers, dates);
        ListSets.AddOrUpdate(condition, built);
        return built;
    }

    private static IComparable Comparable(object value, ReportValueKind kind) =>
        kind == ReportValueKind.Date ? ReportDocument.DateValue(value) : ReportDocument.DecimalValue(value);

    private static IComparable Parse(string value, ReportValueKind kind) => kind == ReportValueKind.Date
        ? DateTime.ParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None)
        : decimal.Parse(value.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

    private static bool TryParse(string value, ReportValueKind kind, out IComparable? parsed)
    {
        parsed = null;
        if (kind == ReportValueKind.Text) { parsed = value; return true; }
        if (kind == ReportValueKind.Date && DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        { parsed = date; return true; }
        if (kind != ReportValueKind.Date && decimal.TryParse(value.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number))
        { parsed = number; return true; }
        return false;
    }

    private static string Normalize(string value, PetraReportSearchOptions options) =>
        options.NormalizeArabic ? NormalizeArabic(value) : value;

    internal static string NormalizeArabic(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark) continue;
            builder.Append(character switch
            {
                '\u0622' or '\u0623' or '\u0625' or '\u0671' => '\u0627',
                '\u0649' => '\u064A',
                '\u0629' => '\u0647',
                '\u0640' => '\0',
                _ => character,
            });
        }
        return builder.Replace("\0", string.Empty).ToString().Normalize(NormalizationForm.FormC);
    }

    internal static string FormatExpression(PetraReportFilterNode? node, IReadOnlyDictionary<string, string>? columnHeadings = null)
    {
        if (node is null) return string.Empty;
        if (node is PetraReportFilterCondition cond)
        {
            var col = columnHeadings is not null && columnHeadings.TryGetValue(cond.ColumnKey, out var h) ? h : cond.ColumnKey;
            var op = cond.Operator switch
            {
                PetraReportFilterOperator.Equals => "=",
                PetraReportFilterOperator.NotEquals => "!=",
                PetraReportFilterOperator.GreaterThan => ">",
                PetraReportFilterOperator.GreaterThanOrEqual => ">=",
                PetraReportFilterOperator.LessThan => "<",
                PetraReportFilterOperator.LessThanOrEqual => "<=",
                PetraReportFilterOperator.Contains => "يحوي",
                PetraReportFilterOperator.DoesNotContain => "لا يحوي",
                PetraReportFilterOperator.StartsWith => "يبدأ بـ",
                PetraReportFilterOperator.EndsWith => "ينتهي بـ",
                PetraReportFilterOperator.IsEmpty => "فارغ",
                PetraReportFilterOperator.IsNotEmpty => "غير فارغ",
                PetraReportFilterOperator.Between => $"بين '{cond.Value}' و '{cond.SecondValue}'",
                PetraReportFilterOperator.In => $"ضمن ({string.Join(", ", cond.Values ?? (cond.Value is not null ? [cond.Value] : []))})",
                PetraReportFilterOperator.NotIn => $"ليس ضمن ({string.Join(", ", cond.Values ?? (cond.Value is not null ? [cond.Value] : []))})",
                _ => cond.Operator.ToString(),
            };
            if (cond.Operator is PetraReportFilterOperator.IsEmpty or PetraReportFilterOperator.IsNotEmpty or PetraReportFilterOperator.Between or PetraReportFilterOperator.In or PetraReportFilterOperator.NotIn)
                return $"[{col}] {op}";
            return $"[{col}] {op} '{cond.Value}'";
        }
        if (node is PetraReportFilterGroup grp)
        {
            var activeChildren = grp.Children.Where(c => c.IsEnabled).ToList();
            if (activeChildren.Count == 0) return string.Empty;
            if (activeChildren.Count == 1) return FormatExpression(activeChildren[0], columnHeadings);
            var logicStr = grp.Logic == PetraReportFilterLogic.And ? " AND " : " OR ";
            var formattedChildren = activeChildren.Select(c => FormatExpression(c, columnHeadings)).Where(s => !string.IsNullOrEmpty(s));
            return $"({string.Join(logicStr, formattedChildren)})";
        }
        return string.Empty;
    }
}

public static class PetraReportFilterConverter
{
    public static ProfessionalReportFilterGroup? ToContract(PetraReportFilterNode? node)
    {
        if (node is null || !node.IsEnabled) return null;
        if (node is PetraReportFilterCondition cond)
        {
            if (!Enum.TryParse<ProfessionalReportField>(cond.ColumnKey, true, out var field))
                return null;
            var contractCond = new ProfessionalReportFilterCondition
            {
                Field = field,
                Operator = (ProfessionalFilterOperator)cond.Operator,
                Value = cond.Value,
                SecondValue = cond.SecondValue,
                Values = cond.Values ?? [],
            };
            return new ProfessionalReportFilterGroup
            {
                Logic = ProfessionalFilterLogic.And,
                Conditions = [contractCond],
                Groups = [],
            };
        }
        if (node is PetraReportFilterGroup group)
        {
            var conditions = new List<ProfessionalReportFilterCondition>();
            var groups = new List<ProfessionalReportFilterGroup>();
            foreach (var child in group.Children.Where(c => c.IsEnabled))
            {
                if (child is PetraReportFilterCondition c)
                {
                    if (Enum.TryParse<ProfessionalReportField>(c.ColumnKey, true, out var f))
                    {
                        conditions.Add(new ProfessionalReportFilterCondition
                        {
                            Field = f,
                            Operator = (ProfessionalFilterOperator)c.Operator,
                            Value = c.Value,
                            SecondValue = c.SecondValue,
                            Values = c.Values ?? [],
                        });
                    }
                }
                else if (child is PetraReportFilterGroup g)
                {
                    var converted = ToContract(g);
                    if (converted is not null && ((converted.Conditions?.Count ?? 0) > 0 || (converted.Groups?.Count ?? 0) > 0))
                        groups.Add(converted);
                }
            }
            if (conditions.Count == 0 && groups.Count == 0) return null;
            return new ProfessionalReportFilterGroup
            {
                Logic = group.Logic == PetraReportFilterLogic.And ? ProfessionalFilterLogic.And : ProfessionalFilterLogic.Or,
                Conditions = conditions.AsReadOnly(),
                Groups = groups.AsReadOnly(),
            };
        }
        return null;
    }

    public static PetraReportFilterNode? FromContract(ProfessionalReportFilterGroup? group)
    {
        if (group is null) return null;
        var children = new List<PetraReportFilterNode>();
        if (group.Conditions is not null)
        {
            foreach (var cond in group.Conditions)
            {
                children.Add(new PetraReportFilterCondition(
                    ColumnKey: cond.Field.ToString(),
                    Operator: (PetraReportFilterOperator)cond.Operator,
                    Value: cond.Value,
                    SecondValue: cond.SecondValue,
                    Values: cond.Values));
            }
        }
        if (group.Groups is not null)
        {
            foreach (var sub in group.Groups)
            {
                var subNode = FromContract(sub);
                if (subNode is not null) children.Add(subNode);
            }
        }
        if (children.Count == 0) return null;
        if (children.Count == 1 && (group.Groups is null || group.Groups.Count == 0)) return children[0];
        return new PetraReportFilterGroup(
            group.Logic == ProfessionalFilterLogic.And ? PetraReportFilterLogic.And : PetraReportFilterLogic.Or,
            children.AsReadOnly());
    }
}
