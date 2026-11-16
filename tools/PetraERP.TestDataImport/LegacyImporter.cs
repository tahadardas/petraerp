using System.Globalization;
using System.Text;
using PetraERP.ApiClient;

namespace PetraERP.TestDataImport;

/// <summary>Imports Ameen 2026 master data and posted journals through the PetraERP API (test database only).</summary>
internal sealed partial class LegacyImporter(ApiSession session, ImportMap map, string legacy, string companyCode, string reportPath)
{
    private const string ZeroGuid = "00000000-0000-0000-0000-000000000000";
    private const string Reason = "استيراد بيانات تجريبية من الأمين 2026";
    private readonly StringBuilder report = new();
    private readonly List<string> rejected = [];
    private Guid companyId;
    private Guid branchId;
    private string baseCurrency = string.Empty;
    private Dictionary<string, IReadOnlyDictionary<string, string>> accounts = new(StringComparer.Ordinal);

    private PetraApiClient Api => session.Api;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        await ContextAsync(cancellationToken).ConfigureAwait(false);
        await CurrencyAsync(cancellationToken).ConfigureAwait(false);
        await FiscalYearAsync(cancellationToken).ConfigureAwait(false);
        await CostCentersAsync(cancellationToken).ConfigureAwait(false);
        await AccountsAsync(cancellationToken).ConfigureAwait(false);
        await WarehousesAsync(cancellationToken).ConfigureAwait(false);
        await ItemsAsync(cancellationToken).ConfigureAwait(false);
        await PartiesAsync(cancellationToken).ConfigureAwait(false);
        await JournalsAsync(cancellationToken).ConfigureAwait(false);
        Line($"Duration: {DateTimeOffset.UtcNow - started:hh\\:mm\\:ss}");
        Line(string.Empty);
        Line($"## Rejected records ({rejected.Count})");
        foreach (var item in rejected) Line("- " + item);
        await File.WriteAllTextAsync(reportPath, report.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        Console.WriteLine("Report: " + reportPath);
        if (rejected.Count != 0) Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{rejected.Count} records were rejected; see the report."));
        return rejected.Count;
    }

    private void Line(string text) { report.AppendLine(text); Console.WriteLine(text); }
    private static string Text(IReadOnlyDictionary<string, string> row, string column) => row.TryGetValue(column, out var value) ? value.Trim() : string.Empty;
    private static string Clip(string value, int length) => value.Length <= length ? value : value[..length];
    private static string Problem(Exception exception) => exception switch
    {
        PetraApiException<ProblemDetails> { Result: { } problem } api => $"HTTP {api.StatusCode}: {problem.Title} {problem.Detail} {Clip(api.Response ?? string.Empty, 300)}".Trim(),
        PetraApiException api => $"HTTP {api.StatusCode}: {Clip(string.IsNullOrEmpty(api.Response) ? api.Message : api.Response, 400)}",
        _ => exception.Message,
    };

    private async Task ContextAsync(CancellationToken cancellationToken)
    {
        var companies = await Api.ListMyCompaniesAsync(cancellationToken).ConfigureAwait(false);
        var company = companies.Items.SingleOrDefault(c => c.Code == companyCode) ?? throw new InvalidOperationException("Company " + companyCode + " not found for this user.");
        companyId = company.Id; baseCurrency = company.BaseCurrencyCode;
        branchId = (await Api.ListMyBranchesAsync(companyId, cancellationToken).ConfigureAwait(false)).Items[0].Id;
        Line($"# Ameen 2026 → PetraERP test import ({company.NameArabic}, base {baseCurrency})");
        Line(string.Empty);
        Line("TEST DATA ONLY. Source opened read-only: " + legacy);
        Line(string.Empty);
    }

    private async Task CurrencyAsync(CancellationToken cancellationToken)
    {
        // Ameen USD lines carry their own rate; the most frequent 2026 rate seeds the USD rate table for new test entries.
        var currencies = LegacyTable.Read(legacy, "my000");
        var usd = currencies.FirstOrDefault(c => Text(c, "Code") == "$");
        if (usd is null) return;
        var rate = LegacyTable.Read(legacy, "en000").Where(l => Text(l, "CurrencyGUID") == Text(usd, "GUID"))
            .GroupBy(l => Text(l, "CurrencyVal")).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        if (map.TryGet("currency", "USD", out _)) return;
        try
        {
            await Api.EnableCompanyCurrencyAsync(companyId, branchId, new EnableCurrencyRequest { Code = "USD", NameArabic = "دولار أمريكي", NameEnglish = "US Dollar", Reason = Reason }, cancellationToken).ConfigureAwait(false);
            if (rate is not null)
                await Api.RegisterExchangeRateAsync(companyId, branchId, new RegisterExchangeRateRequest
                {
                    CurrencyCode = "USD", Rate = decimal.Parse(rate, CultureInfo.InvariantCulture).ToString("0.########", CultureInfo.InvariantCulture),
                    EffectiveDate = new DateOnly(2026, 1, 1), Source = "Ameen 2026", Reason = Reason,
                }, cancellationToken).ConfigureAwait(false);
            map.Set("currency", "USD", Guid.Empty);
            Line($"- Currency USD enabled, rate {rate} {baseCurrency} from 2026-01-01");
        }
        catch (PetraApiException exception) { rejected.Add("currency USD: " + Problem(exception)); }
    }

    private async Task FiscalYearAsync(CancellationToken cancellationToken)
    {
        var years = await Api.ListFiscalYearsAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);
        if (years.Any(y => y.Code == "2026")) { Line("- Fiscal year 2026 exists"); return; }
        var periods = Enumerable.Range(1, 12).Select(month => new PeriodDefinitionRequest
        {
            Code = string.Create(CultureInfo.InvariantCulture, $"2026-{month:00}"), Start = new DateOnly(2026, month, 1),
            End = new DateOnly(2026, month, DateTime.DaysInMonth(2026, month)),
        }).ToArray();
        await Api.CreateFiscalYearAsync(companyId, branchId, new CreateFiscalYearRequest { Code = "2026", Start = new DateOnly(2026, 1, 1), End = new DateOnly(2026, 12, 31), Periods = periods }, cancellationToken).ConfigureAwait(false);
        Line("- Fiscal year 2026 created with 12 monthly periods");
    }

    private async Task CostCentersAsync(CancellationToken cancellationToken)
    {
        var created = 0;
        foreach (var row in LegacyTable.Read(legacy, "co000"))
        {
            var key = Text(row, "GUID");
            if (map.TryGet("cost", key, out _)) continue;
            try
            {
                var center = await Api.CreateCostCenterAsync(companyId, branchId, new CostCenterRequest
                { Code = Text(row, "Code"), NameArabic = Clip(Text(row, "Name"), 200), NameEnglish = Optional(Text(row, "LatinName")) }, cancellationToken).ConfigureAwait(false);
                map.Set("cost", key, center.Id); created++;
            }
            catch (PetraApiException exception) { rejected.Add($"cost center {Text(row, "Code")}: {Problem(exception)}"); }
        }
        Line($"- Cost centers created: {created}");
    }

    private static string? Optional(string value) => value.Length == 0 ? null : Clip(value, 200);

    private async Task AccountsAsync(CancellationToken cancellationToken)
    {
        var rows = LegacyTable.Read(legacy, "ac000").Where(r => Text(r, "Type") == "1").ToArray();
        accounts = rows.ToDictionary(r => Text(r, "GUID"), StringComparer.Ordinal);
        int Depth(IReadOnlyDictionary<string, string> row)
        {
            var depth = 0;
            while (accounts.TryGetValue(Text(row, "ParentGUID"), out var parent)) { row = parent; depth++; }
            return depth;
        }
        var parents = rows.Select(r => Text(r, "ParentGUID")).ToHashSet(StringComparer.Ordinal);
        var created = 0;
        foreach (var row in rows.OrderBy(Depth).ThenBy(r => Text(r, "Code"), StringComparer.Ordinal))
        {
            var key = Text(row, "GUID");
            if (map.TryGet("account", key, out _)) continue;
            // PetraERP requires a child to share its parent's type; Ameen nests equity (211) under fixed liabilities (21), so it becomes a root.
            Guid? parentId = Text(row, "Code") != "211" && map.TryGet("account", Text(row, "ParentGUID"), out var parent) ? parent : null;
            if (parentId is null && Text(row, "Code") != "211" && accounts.ContainsKey(Text(row, "ParentGUID")))
            { rejected.Add($"account {Text(row, "Code")}: parent account was not imported"); continue; }
            try
            {
                var account = await Api.CreateAccountAsync(companyId, branchId, new AccountRequest
                {
                    Code = Text(row, "Code"), NameArabic = Clip(Text(row, "Name"), 200), NameEnglish = Optional(Text(row, "LatinName")),
                    AccountType = Category(row), PostingAllowed = !parents.Contains(key), ParentId = parentId,
                    CostCenterPolicy = CostCenterPolicy.Optional, CurrencyMode = AccountCurrencyMode.AnyEnabledCurrency,
                }, cancellationToken).ConfigureAwait(false);
                map.Set("account", key, account.Id); created++;
            }
            catch (PetraApiException exception) { rejected.Add($"account {Text(row, "Code")}: {Problem(exception)}"); }
        }
        Line($"- Accounts created: {created} of {rows.Length}");
    }

    /// <summary>Root-based mapping reviewed against the 2026 chart; «حقوق الملكية» (211) and root 10 are equity.</summary>
    private AccountType Category(IReadOnlyDictionary<string, string> row)
    {
        var codes = new List<string>();
        for (var current = row; current is not null; current = accounts.GetValueOrDefault(Text(current, "ParentGUID"))) codes.Add(Text(current, "Code"));
        if (codes.Contains("211") || codes[^1] == "10") return AccountType.Equity;
        return codes[^1] switch
        {
            "1" => AccountType.Assets,
            "2" => AccountType.Liabilities,
            "6" or "7" => AccountType.Revenue,
            _ => AccountType.Expenses,
        };
    }
}
