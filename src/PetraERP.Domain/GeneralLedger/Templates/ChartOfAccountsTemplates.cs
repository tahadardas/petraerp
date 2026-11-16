namespace PetraERP.Domain.GeneralLedger.Templates;

public sealed record TemplateAccount(string AccountCode, string AccountNameArabic, string? AccountNameEnglish,
    string? ParentCode, AccountType AccountType, bool PostingAllowed,
    AccountCurrencyMode CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly, CostCenterPolicy CostCenterPolicy = CostCenterPolicy.NotAllowed);

public sealed record ChartOfAccountsTemplate(Guid TemplateId, string TemplateCode, string TemplateNameArabic,
    string TemplateNameEnglish, string CountryCode, int TemplateVersion, string TemplateSource,
    IReadOnlyList<TemplateAccount> Accounts, IReadOnlyDictionary<string, string> DefaultAccountRoles)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Accounts is null || DefaultAccountRoles is null) return ["template.metadata_required"];
        if (TemplateId == Guid.Empty || string.IsNullOrWhiteSpace(TemplateCode) || TemplateVersion < 1
            || string.IsNullOrWhiteSpace(TemplateSource) || TemplateSource.Length > 200 || TemplateCode.Length > 64 || string.IsNullOrWhiteSpace(TemplateNameArabic))
            errors.Add("template.metadata_required");
        if (Accounts.Count is 0 or > 20000) errors.Add("template.account_count_invalid");
        var map = new Dictionary<string, TemplateAccount>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in Accounts)
        {
            if (string.IsNullOrWhiteSpace(a.AccountCode) || a.AccountCode.Length > 64 || string.IsNullOrWhiteSpace(a.AccountNameArabic)
                || a.AccountNameArabic.Length > 200 || !Enum.IsDefined(a.AccountType) || !Enum.IsDefined(a.CurrencyMode)
                || !Enum.IsDefined(a.CostCenterPolicy) || a.CurrencyMode == AccountCurrencyMode.FixedCurrency)
                errors.Add($"template.account_invalid:{a.AccountCode}");
            if (!map.TryAdd(a.AccountCode, a)) errors.Add($"template.code_duplicate:{a.AccountCode}");
        }
        foreach (var a in Accounts)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { a.AccountCode };
            var parent = a.ParentCode;
            while (parent is not null)
            {
                if (!visited.Add(parent)) { errors.Add($"template.parent_cycle:{a.AccountCode}"); break; }
                if (!map.TryGetValue(parent, out var p)) { errors.Add($"template.parent_missing:{a.AccountCode}"); break; }
                if (p.PostingAllowed || p.AccountType != a.AccountType) errors.Add($"template.parent_invalid:{a.AccountCode}");
                parent = p.ParentCode;
            }
        }
        foreach (var role in DefaultAccountRoles)
            if (!map.TryGetValue(role.Value, out var a) || !a.PostingAllowed) errors.Add($"template.role_invalid:{role.Key}");
        return errors.AsReadOnly();
    }

    public IReadOnlyList<TemplateAccount> OrderedAccounts()
    {
        var errors = Validate();
        if (errors.Count != 0) throw new ArgumentException(string.Join("; ", errors));
        var children = Accounts.Where(a => a.ParentCode is not null).ToLookup(a => a.ParentCode!, StringComparer.OrdinalIgnoreCase);
        var result = new List<TemplateAccount>();
        void Visit(TemplateAccount account)
        {
            result.Add(account);
            foreach (var child in children[account.AccountCode].OrderBy(a => a.AccountCode, StringComparer.Ordinal)) Visit(child);
        }
        foreach (var a in Accounts.Where(a => a.ParentCode is null).OrderBy(a => a.AccountCode, StringComparer.Ordinal)) Visit(a);
        return result.AsReadOnly();
    }
}

/// <summary>Petra's generic starter, not an official national chart. National data is registered only from verified sources.</summary>
public static class ChartOfAccountsTemplates
{
    public const string GenericCode = "PETRA-GENERIC";
    public static ChartOfAccountsTemplate Generic { get; } = new(
        Guid.Parse("207c9cd4-e7ee-46e9-9103-6f6a198cae26"), GenericCode,
        "دليل بترا العام للمباشرة — غير وطني رسمي", "Petra generic starter — not an official national standard", "ZZ", 1,
        "Petra ERP generic starter, version 1; internal operational roles only",
        [
            new("1", "الأصول", "Assets", null, AccountType.Assets, false),
            new("11", "الأصول المتداولة", "Current assets", "1", AccountType.Assets, false),
            new("1101", "الصندوق", "Cash", "11", AccountType.Assets, true),
            new("1102", "البنك", "Bank", "11", AccountType.Assets, true),
            new("1103", "ذمم العملاء", "Receivables", "11", AccountType.Assets, true, AccountCurrencyMode.AnyEnabledCurrency),
            new("1104", "المخزون", "Inventory", "11", AccountType.Assets, true),
            new("2", "الخصوم", "Liabilities", null, AccountType.Liabilities, false),
            new("2101", "ذمم الموردين", "Payables", "2", AccountType.Liabilities, true, AccountCurrencyMode.AnyEnabledCurrency),
            new("3", "حقوق الملكية", "Equity", null, AccountType.Equity, false),
            new("3101", "رأس المال", "Capital", "3", AccountType.Equity, true),
            new("4", "الإيرادات", "Revenue", null, AccountType.Revenue, false),
            new("4101", "المبيعات", "Sales", "4", AccountType.Revenue, true),
            new("4102", "أرباح فروقات الصرف", "FX gains", "4", AccountType.Revenue, true),
            new("5", "المصروفات", "Expenses", null, AccountType.Expenses, false),
            new("5101", "كلفة المبيعات", "Cost of goods sold", "5", AccountType.Expenses, true),
            new("5102", "خسائر فروقات الصرف", "FX losses", "5", AccountType.Expenses, true),
            new("5103", "مصروفات تشغيلية", "Operating expense", "5", AccountType.Expenses, true),
        ], new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Cash"]="1101", ["Bank"]="1102", ["Receivable"]="1103", ["Inventory"]="1104", ["Payable"]="2101",
            ["Revenue"]="4101", ["CostOfGoodsSold"]="5101", ["Expense"]="5103", ["FxGain"]="4102", ["FxLoss"]="5102",
        });
    public static IReadOnlyList<ChartOfAccountsTemplate> All { get; } = [Generic];
    public static ChartOfAccountsTemplate? Find(string? code) => All.SingleOrDefault(t => string.Equals(t.TemplateCode, code, StringComparison.OrdinalIgnoreCase));
}
