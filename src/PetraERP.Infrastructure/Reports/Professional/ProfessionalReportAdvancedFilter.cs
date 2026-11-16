using System.Globalization;
using System.Text;
using PetraERP.Application.Reports.Professional;

namespace PetraERP.Infrastructure.Reports.Professional;

/// <summary>
/// Evaluates a validated typed expression against the complete authorized server snapshot.
/// It deliberately runs after authoritative balances are calculated so presentation criteria
/// can never redefine the accounting opening/closing balance or bypass the report scope.
/// </summary>
internal static class ProfessionalReportAdvancedFilter
{
    public static ProfessionalReportExecutionResponse Apply(ProfessionalReportExecutionResponse report, ProfessionalReportRequest request)
    {
        if (request.AdvancedFilter is not { } filter) return report;

        var rows = report.Rows.Where(row => Matches(filter, row)).ToArray();
        // Trial balance presentation rows include rolled parent aggregates; official sums must never double-count them.
        var financialRows = request.Kind == ProfessionalReportKind.TrialBalance ? rows.Where(row => !row.IsAggregate).ToArray() : rows;
        var filteredDebit = financialRows.Sum(row => row.Debit);
        var filteredCredit = financialRows.Sum(row => row.Credit);
        var supplier = request.Kind == ProfessionalReportKind.SupplierStatement;
        var sign = supplier ? -1m : 1m;
        var display = request.BalancePresentation == ProfessionalBalancePresentation.Actual
            ? report.Summary.ActualOpening
            : 0m;

        if (request.Kind != ProfessionalReportKind.TrialBalance)
        {
            for (var index = 0; index < rows.Length; index++)
            {
                display += sign * (rows[index].Debit - rows[index].Credit);
                rows[index] = rows[index] with { DisplayBalance = display };
            }
        }

        var visibleDocuments = rows.Where(row => row.DocumentId is not null).Select(row => row.DocumentId!.Value).ToHashSet();
        var summary = report.Summary with
        {
            FilteredDebit = filteredDebit,
            FilteredCredit = filteredCredit,
            DisplayClosing = request.Kind == ProfessionalReportKind.TrialBalance
                ? financialRows.Sum(row => row.DisplayBalance)
                : (request.BalancePresentation == ProfessionalBalancePresentation.Actual ? report.Summary.ActualOpening : 0m)
                    + sign * (filteredDebit - filteredCredit),
            RowCount = rows.Length,
        };
        return report with
        {
            Rows = rows,
            Summary = summary,
            InvoiceDetails = report.InvoiceDetails.Where(detail => visibleDocuments.Contains(detail.InvoiceId)).ToArray(),
            Notes = report.Notes.Append("الشروط المتقدمة طُبقت في الخادم على اللقطة المصرح بها؛ الرصيد الفعلي والإجماليات المحاسبية لم تتغير.").ToArray(),
        };
    }

    private static bool Matches(ProfessionalReportFilterGroup group, ProfessionalReportRowDto row)
    {
        IEnumerable<bool> results = group.Conditions.Select(condition => Matches(condition, row))
            .Concat(group.Groups.Select(child => Matches(child, row)));
        return group.Logic == ProfessionalFilterLogic.And ? results.All(value => value) : results.Any(value => value);
    }

    private static bool Matches(ProfessionalReportFilterCondition condition, ProfessionalReportRowDto row)
    {
        var cell = GetCell(condition.Field, row);
        if (condition.Operator == ProfessionalFilterOperator.IsEmpty) return cell.IsEmpty;
        if (condition.Operator == ProfessionalFilterOperator.IsNotEmpty) return !cell.IsEmpty;
        if (condition.Operator is ProfessionalFilterOperator.In or ProfessionalFilterOperator.NotIn)
        {
            var included = condition.Values.Any(value => EqualsValue(cell, value));
            return condition.Operator == ProfessionalFilterOperator.In ? included : !included;
        }
        if (cell.IsEmpty || condition.Value is null) return false;

        var comparison = Compare(cell, condition.Value);
        return condition.Operator switch
        {
            ProfessionalFilterOperator.Equals => comparison == 0,
            ProfessionalFilterOperator.NotEquals => comparison != 0,
            ProfessionalFilterOperator.Contains => Normalize(cell.Text).Contains(Normalize(condition.Value), StringComparison.Ordinal),
            ProfessionalFilterOperator.DoesNotContain => !Normalize(cell.Text).Contains(Normalize(condition.Value), StringComparison.Ordinal),
            ProfessionalFilterOperator.StartsWith => Normalize(cell.Text).StartsWith(Normalize(condition.Value), StringComparison.Ordinal),
            ProfessionalFilterOperator.EndsWith => Normalize(cell.Text).EndsWith(Normalize(condition.Value), StringComparison.Ordinal),
            ProfessionalFilterOperator.GreaterThan => comparison > 0,
            ProfessionalFilterOperator.GreaterThanOrEqual => comparison >= 0,
            ProfessionalFilterOperator.LessThan => comparison < 0,
            ProfessionalFilterOperator.LessThanOrEqual => comparison <= 0,
            ProfessionalFilterOperator.Between when condition.SecondValue is not null => comparison >= 0 && Compare(cell, condition.SecondValue) <= 0,
            _ => false,
        };
    }

    private static bool EqualsValue(FilterCell cell, string value) => !cell.IsEmpty && Compare(cell, value) == 0;

    private static int Compare(FilterCell cell, string value) => cell.Kind switch
    {
        ProfessionalReportCatalog.FilterValueKind.Number => cell.Number!.Value.CompareTo(decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture)),
        ProfessionalReportCatalog.FilterValueKind.Date => cell.Date!.Value.CompareTo(DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture)),
        ProfessionalReportCatalog.FilterValueKind.Enum => ((int)cell.Source!.Value).CompareTo(ParseSource(value)),
        _ => string.Compare(Normalize(cell.Text), Normalize(value), StringComparison.Ordinal),
    };

    private static int ParseSource(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
        ? number
        : (int)Enum.Parse<ProfessionalReportSource>(value, true);

    private static FilterCell GetCell(ProfessionalReportField field, ProfessionalReportRowDto row) => field switch
    {
        ProfessionalReportField.Date => FilterCell.From(row.Date),
        ProfessionalReportField.PostingSequence => FilterCell.From(row.PostingSequence is { } sequence ? sequence : null),
        ProfessionalReportField.AccountCode => FilterCell.From(row.AccountCode),
        ProfessionalReportField.AccountName => FilterCell.From(row.AccountName),
        ProfessionalReportField.Description => FilterCell.From(row.Description),
        ProfessionalReportField.Source => FilterCell.From(row.Source),
        ProfessionalReportField.DocumentNumber => FilterCell.From(row.DocumentNumber),
        ProfessionalReportField.Currency => FilterCell.From(row.CurrencyCode),
        ProfessionalReportField.Debit => FilterCell.From(row.Debit),
        ProfessionalReportField.Credit => FilterCell.From(row.Credit),
        ProfessionalReportField.ActualBalance => FilterCell.From(row.ActualBalance),
        ProfessionalReportField.DisplayBalance => FilterCell.From(row.DisplayBalance),
        ProfessionalReportField.Opening => FilterCell.From(row.Opening),
        ProfessionalReportField.Closing => FilterCell.From(row.Closing),
        ProfessionalReportField.CostCenter => FilterCell.From(row.CostCenterCode),
        ProfessionalReportField.PartyCode => FilterCell.From(row.PartyCode),
        ProfessionalReportField.PartyName => FilterCell.From(row.PartyName),
        ProfessionalReportField.OppositeAccount => FilterCell.From(row.OppositeAccountCode),
        ProfessionalReportField.TransactionDebit => FilterCell.From(row.TransactionDebit),
        ProfessionalReportField.TransactionCredit => FilterCell.From(row.TransactionCredit),
        ProfessionalReportField.OpeningDebit => FilterCell.From(row.OpeningDebit),
        ProfessionalReportField.OpeningCredit => FilterCell.From(row.OpeningCredit),
        ProfessionalReportField.ClosingDebit => FilterCell.From(row.ClosingDebit),
        ProfessionalReportField.ClosingCredit => FilterCell.From(row.ClosingCredit),
        ProfessionalReportField.TransactionCurrency => FilterCell.From(row.TransactionCurrencyCode),
        _ => FilterCell.From((string?)null),
    };

    private static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark || character == '\u0640') continue;
            builder.Append(character switch { '\u0622' or '\u0623' or '\u0625' => '\u0627', '\u0649' => '\u064A', _ => char.ToUpperInvariant(character) });
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private readonly record struct FilterCell(ProfessionalReportCatalog.FilterValueKind Kind, string? Text, decimal? Number, DateOnly? Date,
        ProfessionalReportSource? Source)
    {
        public bool IsEmpty => Text is null && Number is null && Date is null && Source is null;
        public static FilterCell From(string? value) => new(ProfessionalReportCatalog.FilterValueKind.Text, string.IsNullOrWhiteSpace(value) ? null : value, null, null, null);
        public static FilterCell From(decimal value) => new(ProfessionalReportCatalog.FilterValueKind.Number, null, value, null, null);
        public static FilterCell From(long? value) => new(ProfessionalReportCatalog.FilterValueKind.Number, null, value, null, null);
        public static FilterCell From(DateOnly? value) => new(ProfessionalReportCatalog.FilterValueKind.Date, null, null, value, null);
        public static FilterCell From(ProfessionalReportSource? value) => new(ProfessionalReportCatalog.FilterValueKind.Enum, null, null, null, value);
    }
}
