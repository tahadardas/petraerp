using PetraERP.Application.Common;

namespace PetraERP.Application.Reports.Professional;

/// <summary>Explicit allowlist shared by catalog and request validation; unsupported choices never run silently.</summary>
public static class ProfessionalReportCatalog
{
    public const int MaximumRows = 100_000;
    public const int MaximumFilterDepth = 6;
    public const int MaximumFilterNodes = 64;
    public const int MaximumFilterValues = 100;
    public const string DescriptionScope = "SQL Unicode substring on journal and line descriptions; party statements search persisted document notes. SQL Server database collation applies; text is not normalized.";

    /// <summary>True for every party (subledger) statement, including the combined customer + supplier one.</summary>
    public static bool IsPartyKind(ProfessionalReportKind kind) =>
        kind is ProfessionalReportKind.CustomerStatement or ProfessionalReportKind.SupplierStatement or ProfessionalReportKind.CombinedPartyStatement;

    public static ProfessionalReportDefinitionDto Define(ProfessionalReportKind kind)
    {
        var party = IsPartyKind(kind);
        var trial = kind == ProfessionalReportKind.TrialBalance;
        var currencyBalance = kind == ProfessionalReportKind.CurrencyBalance;
        var summarized = trial || currencyBalance;
        var sourceValues = party
            ? kind == ProfessionalReportKind.CustomerStatement
                ? new[] { ProfessionalReportSource.SalesInvoice, ProfessionalReportSource.SalesReturn, ProfessionalReportSource.ReceiptVoucher, ProfessionalReportSource.Settlement }
                : kind == ProfessionalReportKind.CombinedPartyStatement
                    ? [ProfessionalReportSource.SalesInvoice, ProfessionalReportSource.SalesReturn, ProfessionalReportSource.ReceiptVoucher,
                        ProfessionalReportSource.PurchaseInvoice, ProfessionalReportSource.PurchaseReturn, ProfessionalReportSource.PaymentVoucher, ProfessionalReportSource.Settlement]
                    : [ProfessionalReportSource.PurchaseInvoice, ProfessionalReportSource.PurchaseReturn, ProfessionalReportSource.PaymentVoucher, ProfessionalReportSource.Settlement]
            : Enum.GetValues<ProfessionalReportSource>().Where(s => s != ProfessionalReportSource.ManualJournal).ToArray();
        var fields = Enum.GetValues<ProfessionalReportField>().Select(field =>
        {
            var supported = field switch
            {
                ProfessionalReportField.PartyCode or ProfessionalReportField.PartyName => party,
                ProfessionalReportField.Opening or ProfessionalReportField.Closing or ProfessionalReportField.OpeningDebit
                    or ProfessionalReportField.OpeningCredit or ProfessionalReportField.ClosingDebit or ProfessionalReportField.ClosingCredit => summarized,
                ProfessionalReportField.CostCenter or ProfessionalReportField.OppositeAccount => !party && !summarized,
                ProfessionalReportField.TransactionCurrency => !party && !trial,
                ProfessionalReportField.Description => !summarized,
                ProfessionalReportField.Date or ProfessionalReportField.PostingSequence or ProfessionalReportField.Source
                    or ProfessionalReportField.DocumentNumber => !summarized,
                ProfessionalReportField.TransactionDebit or ProfessionalReportField.TransactionCredit => !trial,
                _ => true,
            };
            return new ProfessionalFieldDefinitionDto(field, FieldLabel(field), supported,
                supported && (field is ProfessionalReportField.Date or ProfessionalReportField.AccountCode or ProfessionalReportField.AccountName
                    or ProfessionalReportField.Description or ProfessionalReportField.Debit or ProfessionalReportField.Credit
                    or ProfessionalReportField.ActualBalance or ProfessionalReportField.OpeningDebit or ProfessionalReportField.OpeningCredit
                    or ProfessionalReportField.ClosingDebit or ProfessionalReportField.ClosingCredit
                    || currencyBalance && field is ProfessionalReportField.TransactionCurrency or ProfessionalReportField.TransactionDebit or ProfessionalReportField.TransactionCredit),
                supported ? null : "هذا الحقل لا ينطبق على مصدر بيانات هذا التقرير.");
        }).ToArray();
        var options = Enum.GetValues<ProfessionalReportOption>().Select(option =>
        {
            var supported = option switch
            {
                ProfessionalReportOption.CommercialPapers or ProfessionalReportOption.ExchangeEquivalence => false,
                ProfessionalReportOption.InvoiceLineDetails => party,
                ProfessionalReportOption.Aging => party && kind != ProfessionalReportKind.CombinedPartyStatement,
                ProfessionalReportOption.CostCenter or ProfessionalReportOption.OppositeAccount => !party,
                ProfessionalReportOption.AccountLevel or ProfessionalReportOption.IncludeParents or ProfessionalReportOption.IncludePostingAccounts
                    or ProfessionalReportOption.IncludeEmptyAccounts => trial,
                ProfessionalReportOption.OnlyNonZero => summarized,
                ProfessionalReportOption.Monthly => !summarized,
                _ => true,
            };
            return new ProfessionalOptionDefinitionDto(option, OptionLabel(option), supported,
                supported ? null : "غير مدعوم في هذا المسار؛ لا يتم استنتاجه أو احتسابه من الصورة.");
        }).ToArray();
        var (ar, en) = kind switch
        {
            ProfessionalReportKind.AccountLedger => ("دفتر الأستاذ", "Account ledger"),
            ProfessionalReportKind.CustomerStatement => ("كشف حساب الزبون", "Customer statement"),
            ProfessionalReportKind.SupplierStatement => ("كشف حساب المورد", "Supplier statement"),
            ProfessionalReportKind.CombinedPartyStatement => ("كشف حساب موحد (زبون ومورد)", "Combined party statement"),
            ProfessionalReportKind.CurrencyBalance => ("ميزان العملات", "Currency balance"),
            ProfessionalReportKind.TrialBalance => ("ميزان المراجعة", "Trial balance"),
            _ => throw new RequestValidationException("reporting.kind_invalid", "نوع التقرير المحدد غير معروف. / Unknown report kind.", "kind"),
        };
        return new ProfessionalReportDefinitionDto
        {
            Kind = kind, NameArabic = ar, NameEnglish = en, Fields = fields, Options = options,
            Sources = Enum.GetValues<ProfessionalReportSource>().Select(source => new ProfessionalSourceDefinitionDto(source, SourceLabel(source),
                sourceValues.Contains(source), sourceValues.Contains(source) ? null : source == ProfessionalReportSource.ManualJournal
                    ? "لا يوجد في النواة معرف منشأ يثبت أن القيد أدخل يدوياً؛ القيود غير المرتبطة تظهر كمصدر غير مصنف."
                    : "هذا المصدر لا يخص نوع كشف الحساب المحدد.")).ToArray(),
        };
    }

    public static ProfessionalReportRequest Validate(ProfessionalReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Sources is null || request.Fields is null || request.PatternRevisionIds is null) throw Invalid("Report collections cannot be null.");
        var definition = Define(request.Kind);
        if (request.FiscalYearId == Guid.Empty || request.To < request.From)
            throw Invalid("A fiscal year and an ordered date range are required.");
        if (!Enum.IsDefined(request.CurrencyBasis) || !Enum.IsDefined(request.Grouping) || !Enum.IsDefined(request.Order)
            || !Enum.IsDefined(request.BalancePresentation)) throw Invalid("Unknown typed report option.");
        if (request.Sources.Count == 0 || request.Sources.Count > 10 || request.Sources.Any(source => !definition.Sources.Any(s => s.Source == source && s.Supported)))
            throw Invalid("Choose at least one supported source.");
        if (request.Fields.Count > 25 || request.Fields.Any(field => !definition.Fields.Any(f => f.Field == field && f.Supported)))
            throw Invalid("One or more selected columns are not supported by this report.");
        if (request.FromSequence < 1 || request.ToSequence < 1 || request.ToSequence < request.FromSequence)
            throw Invalid("Invalid posting sequence range.");
        if (request.AccountLevel is < 1 or > 100 || request.PostingTo < request.PostingFrom || request.CreatedTo < request.CreatedFrom)
            throw Invalid("نطاقات التقرير غير صحيحة: مستوى الحساب بين 1 و100 ولا يسبق تاريخ النهاية بدايته. / Invalid report range: account level must be between 1 and 100 and an end date cannot precede its start date.");
        if (request.AccountCategory is { } category && !Enum.IsDefined(category)) throw Invalid("تصنيف الحسابات المختار غير معروف. / Invalid account category.");
        if (request.Kind is not (ProfessionalReportKind.TrialBalance or ProfessionalReportKind.CurrencyBalance) && request.AccountCategory is not null) throw Invalid("تصنيف الحسابات خاص بميزان المراجعة فقط. / Account category applies only to trial balance.");
        // Trial balance has exactly one valid order (account code); a defaulted or stale client value is normalized, not rejected.
        if (request.DescriptionContains?.Length > 500 || request.DescriptionNotContains?.Length > 500)
            throw Invalid("فلتر الوصف مسموح بـ500 حرف كحد أقصى. / Description filters may contain at most 500 characters.");
        if (request.PatternRevisionIds.Count > 100 || request.PatternRevisionIds.Any(id => id == Guid.Empty))
            throw Invalid("اختر حتى 100 نسخة نمط مستند صحيحة. / Choose at most 100 valid pattern revision identifiers.");
        var party = IsPartyKind(request.Kind);
        if (!party && (request.IncludeInvoiceDetails || request.IncludeAging))
            throw Invalid("تفاصيل الفواتير وأعمار الذمم خاصتان بكشوف الأطراف فقط. / Invoice details and aging apply only to party statements.");
        if (request.Kind == ProfessionalReportKind.CombinedPartyStatement && request.IncludeAging)
            throw Invalid("أعمار الذمم غير متاحة في الكشف الموحد؛ استخدم كشف الزبون أو كشف المورد. / Aging is not available in the combined statement.");
        if (party && (request.PartyId is null || request.PartyId == Guid.Empty || request.AccountId is not null || request.CostCenterId is not null || request.OppositeAccountId is not null))
            throw Invalid("تحديد الطرف إلزامي؛ فلاتر الحساب والحساب المقابل ومركز التكلفة لا تنطبق على هذا الكشف المستندي. / A party is required; account, opposite-account and cost-center filters do not apply to this documentary subledger.");
        if (party && (request.CurrencyBasis != ProfessionalCurrencyBasis.Transaction || string.IsNullOrWhiteSpace(request.CurrencyCode)))
            throw Invalid("كشف الطرف يتطلب عملة حركة واحدة محددة صراحة؛ لا تُستبدل أي عملة تلقائياً. / Party statements require one explicit transaction currency; no currency is substituted automatically.");
        if (!party && request.PartyId is not null) throw Invalid("اختيار الطرف خاص بكشوف الأطراف فقط. / Party selection applies only to party statements.");
        if (request.Kind == ProfessionalReportKind.AccountLedger && request.AccountId == Guid.Empty)
            throw Invalid("اختيار الحساب إلزامي لدفتر الأستاذ. / An account is required.");
        if (request.Kind != ProfessionalReportKind.TrialBalance
            && (request.AccountLevel is not null || request.IncludeParents || !request.IncludePostingAccounts || request.IncludeEmptyAccounts
                || (request.OnlyNonZero && request.Kind != ProfessionalReportKind.CurrencyBalance)))
            throw Invalid("خيارات عرض الحسابات خاصة بميزان المراجعة فقط. / Account presentation options apply only to trial balance.");
        if (request.Kind == ProfessionalReportKind.CurrencyBalance
            && (request.CurrencyBasis != ProfessionalCurrencyBasis.Base || request.DisplayCurrencyCode is not null))
            throw Invalid("Currency balance is always reported per transaction currency with its local equivalent; use the currency filter instead of a display currency.");
        if (request.Kind is ProfessionalReportKind.TrialBalance or ProfessionalReportKind.CurrencyBalance && (request.Grouping != ProfessionalReportGrouping.None || request.BalancePresentation != ProfessionalBalancePresentation.Actual))
            throw Invalid("ميزان المراجعة مجمّع فعلاً حسب الحساب؛ الرصيد العرضي الجاري لا ينطبق عليه. / Trial balance already groups by account; running display balance does not apply.");
        if (request.CurrencyBasis == ProfessionalCurrencyBasis.Transaction && string.IsNullOrWhiteSpace(request.CurrencyCode))
            throw Invalid("عملة الحركة يجب تحديدها صراحة؛ لا تُجمع العملات المختلفة معاً. / Transaction currency must be explicit; currencies cannot be summed together.");
        var fields = request.Fields.Count == 0 ? definition.Fields.Where(f => f.DefaultSelected).Select(f => f.Field).ToArray() : request.Fields.Distinct().ToArray();
        if (!party && request.Kind != ProfessionalReportKind.TrialBalance
            && fields.Any(f => f is ProfessionalReportField.TransactionDebit or ProfessionalReportField.TransactionCredit))
            fields = fields.Append(ProfessionalReportField.TransactionCurrency).Distinct().ToArray();
        var advancedFilter = request.AdvancedFilter is null ? null : ValidateAdvancedFilter(request.AdvancedFilter, definition);
        return request with
        {
            Sources = request.Sources.Distinct().ToArray(),
            Fields = fields,
            Order = request.Kind is ProfessionalReportKind.TrialBalance or ProfessionalReportKind.CurrencyBalance ? ProfessionalReportOrder.AccountCode : request.Order,
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode) ? null : request.CurrencyCode.Trim().ToUpperInvariant(),
            DisplayCurrencyCode = string.IsNullOrWhiteSpace(request.DisplayCurrencyCode) ? null : request.DisplayCurrencyCode.Trim().ToUpperInvariant(),
            DescriptionContains = string.IsNullOrEmpty(request.DescriptionContains) ? null : request.DescriptionContains,
            DescriptionNotContains = string.IsNullOrEmpty(request.DescriptionNotContains) ? null : request.DescriptionNotContains,
            AdvancedFilter = advancedFilter,
        };
    }

    private static ProfessionalReportFilterGroup? ValidateAdvancedFilter(ProfessionalReportFilterGroup root, ProfessionalReportDefinitionDto definition)
    {
        var nodes = 0;
        var normalized = ValidateGroup(root, 1);
        return normalized.Conditions.Count == 0 && normalized.Groups.Count == 0 ? null : normalized;

        ProfessionalReportFilterGroup ValidateGroup(ProfessionalReportFilterGroup group, int depth)
        {
            if (depth > MaximumFilterDepth) throw Invalid($"المرشحات المتقدمة تدعم {MaximumFilterDepth} مستوى تداخل كحد أقصى. / Advanced filters support at most {MaximumFilterDepth} nested groups.");
            if (!Enum.IsDefined(group.Logic)) throw Invalid("منطق مجموعة المرشح المتقدم غير معروف. / Unknown advanced-filter group logic.");
            if (group.Conditions is null || group.Groups is null) throw Invalid("لا يمكن ترك مجموعات المرشح المتقدم فارغة. / Advanced-filter collections cannot be null.");
            if (group.Conditions.Count + group.Groups.Count > MaximumFilterNodes) throw Invalid("مجموعة المرشح المتقدم تتجاوز الحد المسموح. / An advanced-filter group is too large.");

            var conditions = group.Conditions.Select(ValidateCondition).ToArray();
            var groups = group.Groups.Select(value => ValidateGroup(value, depth + 1)).ToArray();
            if ((nodes += conditions.Length + groups.Length) > MaximumFilterNodes)
                throw Invalid($"المرشحات المتقدمة تدعم {MaximumFilterNodes} شرطاً ومجموعة كحد أقصى. / Advanced filters support at most {MaximumFilterNodes} conditions and groups.");
            return group with { Conditions = conditions, Groups = groups };
        }

        ProfessionalReportFilterCondition ValidateCondition(ProfessionalReportFilterCondition condition)
        {
            if (!Enum.IsDefined(condition.Field) || !Enum.IsDefined(condition.Operator)) throw Invalid("حقل أو عامل تشغيل المرشح المتقدم غير معروف. / Unknown advanced-filter field or operator.");
            if (!definition.Fields.Any(value => value.Field == condition.Field && value.Supported))
                throw Invalid("حقل المرشح المتقدم غير متاح لهذا التقرير. / An advanced-filter field is not available for this report.");
            if (condition.Values is null || condition.Values.Count > MaximumFilterValues)
                throw Invalid($"المرشح الواحد يسمح بـ{MaximumFilterValues} قيمة مختارة كحد أقصى. / A filter may contain at most {MaximumFilterValues} selected values.");

            var values = condition.Values.Select(NormalizeValue).Where(value => value is not null).Select(value => value!).Distinct(StringComparer.Ordinal).ToArray();
            var value = NormalizeValue(condition.Value);
            var second = NormalizeValue(condition.SecondValue);
            var kind = FilterKind(condition.Field);
            var allowed = kind switch
            {
                FilterValueKind.Text => condition.Operator is ProfessionalFilterOperator.Equals or ProfessionalFilterOperator.NotEquals
                    or ProfessionalFilterOperator.Contains or ProfessionalFilterOperator.DoesNotContain or ProfessionalFilterOperator.StartsWith
                    or ProfessionalFilterOperator.EndsWith or ProfessionalFilterOperator.IsEmpty or ProfessionalFilterOperator.IsNotEmpty
                    or ProfessionalFilterOperator.In or ProfessionalFilterOperator.NotIn,
                FilterValueKind.Enum => condition.Operator is ProfessionalFilterOperator.Equals or ProfessionalFilterOperator.NotEquals
                    or ProfessionalFilterOperator.IsEmpty or ProfessionalFilterOperator.IsNotEmpty or ProfessionalFilterOperator.In or ProfessionalFilterOperator.NotIn,
                _ => condition.Operator is ProfessionalFilterOperator.Equals or ProfessionalFilterOperator.NotEquals or ProfessionalFilterOperator.GreaterThan
                    or ProfessionalFilterOperator.GreaterThanOrEqual or ProfessionalFilterOperator.LessThan or ProfessionalFilterOperator.LessThanOrEqual
                    or ProfessionalFilterOperator.Between or ProfessionalFilterOperator.IsEmpty or ProfessionalFilterOperator.IsNotEmpty,
            };
            if (!allowed) throw Invalid("عامل التشغيل المحدد لا يناسب نوع حقل المرشح المتقدم. / The selected operator is not valid for the advanced-filter field type.");
            if (condition.Operator is ProfessionalFilterOperator.In or ProfessionalFilterOperator.NotIn)
            {
                if (values.Length == 0) throw Invalid("مرشح القيم المحددة يتطلب قيمة واحدة على الأقل. / Selected-value filters require at least one value.");
                foreach (var item in values) ValidateTypedValue(kind, item, condition.Field);
            }
            else if (condition.Operator is not (ProfessionalFilterOperator.IsEmpty or ProfessionalFilterOperator.IsNotEmpty))
            {
                if (value is null) throw Invalid("قيمة المرشح المتقدم مطلوبة. / The advanced-filter value is required.");
                ValidateTypedValue(kind, value, condition.Field);
                if (condition.Operator == ProfessionalFilterOperator.Between)
                {
                    if (second is null) throw Invalid("عامل «بين» يتطلب قيمتين. / Between requires two values.");
                    ValidateTypedValue(kind, second, condition.Field);
                }
            }
            return condition with { Value = value, SecondValue = second, Values = values };
        }
    }

    private static string? NormalizeValue(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > 500) throw Invalid("قيم المرشحات المتقدمة حتى 500 حرف كحد أقصى. / Advanced-filter values may contain at most 500 characters.");
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static void ValidateTypedValue(FilterValueKind kind, string value, ProfessionalReportField field)
    {
        if (kind == FilterValueKind.Date && !DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _))
            throw Invalid("تواريخ المرشحات يجب أن تكون بصيغة yyyy-MM-dd. / Date filter values must use yyyy-MM-dd.");
        if (kind == FilterValueKind.Number && !decimal.TryParse(value, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out _))
            throw Invalid("القيم الرقمية تستخدم الأرقام اللاتينية والفارزة للتنقيط والنقطة للعشر. / Numeric filter values must use ASCII digits, comma grouping and a dot decimal separator.");
        if (field == ProfessionalReportField.Source && !int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var sourceValue) && !Enum.TryParse<ProfessionalReportSource>(value, true, out _))
            throw Invalid("قيم المصدر يجب أن تحدد مصدراً مدعوماً للتقرير. / Source filter values must identify a supported report source.");
        if (field == ProfessionalReportField.Source && int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out sourceValue) && !Enum.IsDefined((ProfessionalReportSource)sourceValue))
            throw Invalid("قيم المصدر يجب أن تحدد مصدراً مدعوماً للتقرير. / Source filter values must identify a supported report source.");
    }

    public static FilterValueKind FilterKind(ProfessionalReportField field) => field switch
    {
        ProfessionalReportField.Date => FilterValueKind.Date,
        ProfessionalReportField.Source => FilterValueKind.Enum,
        ProfessionalReportField.PostingSequence or ProfessionalReportField.Debit or ProfessionalReportField.Credit
            or ProfessionalReportField.ActualBalance or ProfessionalReportField.DisplayBalance or ProfessionalReportField.Opening
            or ProfessionalReportField.Closing or ProfessionalReportField.TransactionDebit or ProfessionalReportField.TransactionCredit
            or ProfessionalReportField.OpeningDebit or ProfessionalReportField.OpeningCredit or ProfessionalReportField.ClosingDebit
            or ProfessionalReportField.ClosingCredit => FilterValueKind.Number,
        _ => FilterValueKind.Text,
    };

    public enum FilterValueKind { Text, Number, Date, Enum }

    private static RequestValidationException Invalid(string message) => new("reporting.parameters_invalid", message, "parameters");

    public static string SourceLabel(ProfessionalReportSource source) => source switch
    {
        ProfessionalReportSource.ManualJournal => "قيود يومية",
        ProfessionalReportSource.SalesInvoice => "فواتير مبيعات",
        ProfessionalReportSource.SalesReturn => "مرتجعات وعكس المبيعات",
        ProfessionalReportSource.PurchaseInvoice => "فواتير مشتريات",
        ProfessionalReportSource.PurchaseReturn => "مرتجعات المشتريات",
        ProfessionalReportSource.ReceiptVoucher => "سندات قبض وعكسها",
        ProfessionalReportSource.PaymentVoucher => "سندات دفع وعكسها",
        ProfessionalReportSource.InventoryDocument => "مستندات مخزنية",
        ProfessionalReportSource.Settlement => "تخصيص وتسوية عملات",
        ProfessionalReportSource.UnclassifiedJournal => "قيود مرحّلة غير مصنفة المنشأ",
        _ => source.ToString(),
    };
    public static string FieldLabel(ProfessionalReportField field) => field switch
    {
        ProfessionalReportField.Date => "التاريخ", ProfessionalReportField.PostingSequence => "رقم القيد",
        ProfessionalReportField.AccountCode => "رمز الحساب", ProfessionalReportField.AccountName => "اسم الحساب",
        ProfessionalReportField.Description => "البيان", ProfessionalReportField.Source => "المصدر",
        ProfessionalReportField.DocumentNumber => "رقم المستند", ProfessionalReportField.Currency => "العملة",
        ProfessionalReportField.Debit => "مدين", ProfessionalReportField.Credit => "دائن", ProfessionalReportField.ActualBalance => "الرصيد الفعلي",
        ProfessionalReportField.DisplayBalance => "رصيد العرض المصفى", ProfessionalReportField.Opening => "الرصيد السابق", ProfessionalReportField.Closing => "الرصيد النهائي",
        ProfessionalReportField.CostCenter => "مركز الكلفة", ProfessionalReportField.PartyCode => "رمز الطرف", ProfessionalReportField.PartyName => "اسم الطرف",
        ProfessionalReportField.OppositeAccount => "الحساب المقابل", ProfessionalReportField.TransactionDebit => "مدين الحركة", ProfessionalReportField.TransactionCredit => "دائن الحركة",
        ProfessionalReportField.OpeningDebit => "رصيد سابق مدين", ProfessionalReportField.OpeningCredit => "رصيد سابق دائن",
        ProfessionalReportField.ClosingDebit => "رصيد نهائي مدين", ProfessionalReportField.ClosingCredit => "رصيد نهائي دائن",
        ProfessionalReportField.TransactionCurrency => "العملة الأصلية",
        _ => field.ToString(),
    };
    private static string OptionLabel(ProfessionalReportOption option) => option switch
    {
        ProfessionalReportOption.DescriptionContains => "البيان يحوي", ProfessionalReportOption.DescriptionNotContains => "البيان لا يحوي",
        ProfessionalReportOption.PostingDate => "تاريخ الترحيل", ProfessionalReportOption.CreatedDate => "تاريخ الإضافة",
        ProfessionalReportOption.OppositeAccount => "الحساب المقابل", ProfessionalReportOption.CostCenter => "مركز الكلفة",
        ProfessionalReportOption.AccountLevel => "مستوى الحساب", ProfessionalReportOption.IncludeParents => "إظهار الحسابات الرئيسية",
        ProfessionalReportOption.IncludePostingAccounts => "إظهار الحسابات الفرعية", ProfessionalReportOption.IncludeEmptyAccounts => "إظهار الحسابات الفارغة",
        ProfessionalReportOption.OnlyNonZero => "الحسابات المرصدة فقط", ProfessionalReportOption.InvoiceLineDetails => "تفاصيل مواد الفواتير",
        ProfessionalReportOption.Aging => "أعمار الذمم", ProfessionalReportOption.CommercialPapers => "الأوراق المالية",
        ProfessionalReportOption.ExchangeEquivalence => "تعادل مخصص", ProfessionalReportOption.Monthly => "تجميع شهري",
        _ => option.ToString(),
    };
}
