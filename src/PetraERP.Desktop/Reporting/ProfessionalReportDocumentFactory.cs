using System.Globalization;
using PetraERP.ApiClient;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Reporting;

/// <summary>Only maps server values to immutable render inputs. It never sums or recalculates accounting amounts.</summary>
public static class ProfessionalReportDocumentFactory
{
    public static IReadOnlyList<ReportDocument> CreateSections(ProfessionalPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var main = Create(request);
        var result = new List<ReportDocument> { main };
        var snapshot = request.Snapshot;
        if (snapshot.InvoiceDetails.Count != 0)
        {
            ReportColumn[] columns =
            [
                new("InvoiceNumber", "رقم الفاتورة / Invoice"), new("ItemCode", "رمز المادة / Item code"),
                new("ItemName", "اسم المادة / Item name"), new("UnitCode", "الوحدة / Unit"),
                new("Quantity", "الكمية / Quantity", ReportValueKind.Quantity, 4),
                new("UnitPrice", "سعر الوحدة / Unit price", ReportValueKind.Amount, 4),
                new("Discount", "الحسم / Discount", ReportValueKind.Amount, 4), new("Tax", "الضريبة / Tax", ReportValueKind.Amount, 4),
                new("LineTotal", "إجمالي السطر / Line total", ReportValueKind.Amount, 4), new("CurrencyCode", "العملة / Currency"),
            ];
            var rows = snapshot.InvoiceDetails.Select(line => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["InvoiceNumber"] = line.InvoiceNumber, ["ItemCode"] = line.ItemCode, ["ItemName"] = line.ItemName, ["UnitCode"] = line.UnitCode,
                ["Quantity"] = Number(line.Quantity), ["UnitPrice"] = Number(line.UnitPrice), ["Discount"] = Number(line.Discount),
                ["Tax"] = Number(line.Tax), ["LineTotal"] = Number(line.LineTotal), ["CurrencyCode"] = line.CurrencyCode,
            });
            var filters = main.Filters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            filters["Appendix / ملحق"] = "تفاصيل مستقلة؛ لا تجمع مبالغها مع دفتر الحساب / Separate invoice detail, never add to ledger totals";
            result.Add(new ReportDocument(main.ReportCode + "InvoiceDetails", "تفاصيل الفواتير / Invoice details", main.CompanyLabel, main.PeriodLabel,
                main.CurrencyCode, columns, rows, filters, main.GeneratedAtUtc, main.ExecutionId, main.Context));
        }
        if (snapshot.Aging is { } aging)
        {
            ReportColumn[] columns =
            [
                new("AsOf", "حتى تاريخ / As of", ReportValueKind.Date), new("CurrencyCode", "العملة / Currency"),
                new("NotDue", "غير مستحق / Not due", ReportValueKind.Amount, 4), new("Days1To30", "1–30 يوم / days", ReportValueKind.Amount, 4),
                new("Days31To60", "31–60 يوم / days", ReportValueKind.Amount, 4), new("Days61To90", "61–90 يوم / days", ReportValueKind.Amount, 4),
                new("Over90", "أكثر من 90 يوم / Over 90 days", ReportValueKind.Amount, 4), new("Advance", "دفعات مقدمة / Advance", ReportValueKind.Amount, 4),
                new("Net", "الصافي / Net", ReportValueKind.Amount, 4),
            ];
            IReadOnlyDictionary<string, object?>[] rows =
            [
                new Dictionary<string, object?>
                {
                    ["AsOf"] = aging.AsOf, ["CurrencyCode"] = aging.CurrencyCode, ["NotDue"] = Number(aging.NotDue), ["Days1To30"] = Number(aging.Days1To30),
                    ["Days31To60"] = Number(aging.Days31To60), ["Days61To90"] = Number(aging.Days61To90), ["Over90"] = Number(aging.Over90),
                    ["Advance"] = Number(aging.Advance), ["Net"] = Number(aging.Net),
                },
            ];
            result.Add(new ReportDocument(main.ReportCode + "Aging", "أعمار الذمم / Aging", main.CompanyLabel, main.PeriodLabel, aging.CurrencyCode,
                columns, rows, main.Filters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), main.GeneratedAtUtc, main.ExecutionId, main.Context));
        }
        return result;
    }

    public static ReportDocument Create(ProfessionalPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = request.Snapshot;
        var columns = new List<ReportColumn>();
        foreach (var field in snapshot.SelectedFields)
        {
            var heading = request.Headings.GetValueOrDefault(field, field.ToString());
            columns.Add(new ReportColumn(field.ToString(), heading, Kind(field), Kind(field) == ReportValueKind.Amount ? 4 : 2));
        }
        var hasOriginalAmounts = snapshot.SelectedFields.Any(field => field is ProfessionalReportField.TransactionDebit or ProfessionalReportField.TransactionCredit);
        var showGroup = snapshot.Parameters.Grouping is { } grouping && grouping != ProfessionalReportGrouping.None;
        if (showGroup) columns.Insert(0, new ReportColumn("ReportGroup", "المجموعة / Group"));
        if (hasOriginalAmounts && columns.All(column => column.Key != "TransactionCurrency"))
            columns.Add(new ReportColumn("TransactionCurrencyCode", "العملة الأصلية / Original currency"));
        var rows = snapshot.Rows.Select(row => Map(row, snapshot.SelectedFields, hasOriginalAmounts && columns.All(column => column.Key != "TransactionCurrency"), showGroup)).ToArray();
        var summaries = Summaries(snapshot).ToArray();
        var filters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Fiscal year / السنة"] = snapshot.FiscalYearCode,
            ["Branch / الفرع"] = snapshot.BranchName,
            ["Subject / الحساب"] = snapshot.SubjectCode + " · " + snapshot.SubjectName,
            ["Sources / المصادر"] = string.Join(", ", snapshot.Parameters.Sources ?? []),
            ["Currency basis / أساس العملة"] = (snapshot.Parameters.CurrencyBasis ?? ProfessionalCurrencyBasis.Base).ToString(),
            ["Balance perspective / منظور الرصيد"] = (snapshot.Parameters.BalancePresentation ?? ProfessionalBalancePresentation.Actual).ToString(),
            ["Contains / البيان يحوي"] = snapshot.Parameters.DescriptionContains ?? string.Empty,
            ["Does not contain / لا يحوي"] = snapshot.Parameters.DescriptionNotContains ?? string.Empty,
            ["Grouping / التجميع"] = (snapshot.Parameters.Grouping ?? ProfessionalReportGrouping.None).ToString(),
            ["Order / الترتيب"] = (snapshot.Parameters.Order ?? (snapshot.Parameters.Kind == ProfessionalReportKind.TrialBalance ? ProfessionalReportOrder.AccountCode : ProfessionalReportOrder.Date)).ToString(),
            ["Balance convention / طبيعة الرصيد"] = snapshot.BalanceConvention,
            ["Rows / عدد السجلات"] = snapshot.Summary.RowCount.ToString(CultureInfo.InvariantCulture),
        };
        for (var index = 0; index < snapshot.Notes.Count; index++) filters.Add("Note" + index.ToString(CultureInfo.InvariantCulture), snapshot.Notes[index]);
        return new ReportDocument(snapshot.Parameters.Kind.ToString(), request.Title, snapshot.CompanyName,
            snapshot.Parameters.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " → " + snapshot.Parameters.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            snapshot.CurrencyCode, columns, rows, filters, snapshot.GeneratedAtUtc, snapshot.ExecutionId,
            new ReportExecutionContext(snapshot.CompanyId, snapshot.FiscalYearId, snapshot.BranchId, request.ActorId), summaries);
    }

    private static ReportValueKind Kind(ProfessionalReportField field) => field switch
    {
        ProfessionalReportField.Date => ReportValueKind.Date,
        ProfessionalReportField.PostingSequence => ReportValueKind.Integer,
        ProfessionalReportField.Debit or ProfessionalReportField.Credit or ProfessionalReportField.ActualBalance or ProfessionalReportField.DisplayBalance or
        ProfessionalReportField.Opening or ProfessionalReportField.Closing or ProfessionalReportField.TransactionDebit or ProfessionalReportField.TransactionCredit or
        ProfessionalReportField.OpeningDebit or ProfessionalReportField.OpeningCredit or ProfessionalReportField.ClosingDebit or ProfessionalReportField.ClosingCredit => ReportValueKind.Amount,
        _ => ReportValueKind.Text,
    };

    private static Dictionary<string, object?> Map(ProfessionalReportRowDto row, IEnumerable<ProfessionalReportField> fields, bool addOriginalCurrency, bool showGroup)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            values.Add(field.ToString(), field switch
            {
                ProfessionalReportField.Date => row.Date,
                ProfessionalReportField.PostingSequence => row.PostingSequence,
                ProfessionalReportField.AccountCode => row.AccountCode,
                ProfessionalReportField.AccountName => row.AccountName,
                ProfessionalReportField.Description => row.Description,
                ProfessionalReportField.Source => row.Source?.ToString(),
                ProfessionalReportField.DocumentNumber => row.DocumentNumber,
                ProfessionalReportField.Currency => row.CurrencyCode,
                ProfessionalReportField.TransactionCurrency => row.TransactionCurrencyCode,
                ProfessionalReportField.Debit => Number(row.Debit),
                ProfessionalReportField.Credit => Number(row.Credit),
                ProfessionalReportField.ActualBalance => Number(row.ActualBalance),
                ProfessionalReportField.DisplayBalance => Number(row.DisplayBalance),
                ProfessionalReportField.Opening => Number(row.Opening),
                ProfessionalReportField.Closing => Number(row.Closing),
                ProfessionalReportField.OpeningDebit => Number(row.OpeningDebit),
                ProfessionalReportField.OpeningCredit => Number(row.OpeningCredit),
                ProfessionalReportField.ClosingDebit => Number(row.ClosingDebit),
                ProfessionalReportField.ClosingCredit => Number(row.ClosingCredit),
                ProfessionalReportField.CostCenter => row.CostCenterCode,
                ProfessionalReportField.PartyCode => row.PartyCode,
                ProfessionalReportField.PartyName => row.PartyName,
                ProfessionalReportField.OppositeAccount => row.OppositeAccountCode,
                ProfessionalReportField.TransactionDebit => Number(row.TransactionDebit),
                ProfessionalReportField.TransactionCredit => Number(row.TransactionCredit),
                _ => throw new InvalidOperationException("The server returned an unsupported report field."),
            });
        }
        if (addOriginalCurrency) values.Add("TransactionCurrencyCode", row.TransactionCurrencyCode);
        if (showGroup) values.Add("ReportGroup", row.GroupKey);
        return values;
    }

    private static IEnumerable<ReportSummaryValue> Summaries(ProfessionalReportExecutionResponse snapshot)
    {
        var value = snapshot.Summary;
        yield return Summary("ActualOpening", "الرصيد الافتتاحي الحقيقي / Actual opening", value.ActualOpening);
        yield return Summary("ActualPeriodDebit", "مدين المدة الحقيقي / Actual period debit", value.ActualPeriodDebit);
        yield return Summary("ActualPeriodCredit", "دائن المدة الحقيقي / Actual period credit", value.ActualPeriodCredit);
        yield return Summary("ActualClosing", "الرصيد النهائي الحقيقي / Actual closing", value.ActualClosing);
        yield return Summary("FilteredDebit", "مدين الحركات المصفاة / Filtered debit", value.FilteredDebit);
        yield return Summary("FilteredCredit", "دائن الحركات المصفاة / Filtered credit", value.FilteredCredit);
        yield return Summary("DisplayClosing", "الرصيد المعروض / Display closing", value.DisplayClosing);
        if (snapshot.Parameters.Kind is ProfessionalReportKind.TrialBalance or ProfessionalReportKind.CurrencyBalance)
        {
            yield return Summary("OpeningDebit", "افتتاحي مدين / Opening debit", value.OpeningDebit);
            yield return Summary("OpeningCredit", "افتتاحي دائن / Opening credit", value.OpeningCredit);
            yield return Summary("ClosingDebit", "ختامي مدين / Closing debit", value.ClosingDebit);
            yield return Summary("ClosingCredit", "ختامي دائن / Closing credit", value.ClosingCredit);
        }
        ReportSummaryValue Summary(string key, string label, string amount) => new(key, label, Number(amount), snapshot.CurrencyCode);
    }

    private static decimal Number(string value) => decimal.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}
