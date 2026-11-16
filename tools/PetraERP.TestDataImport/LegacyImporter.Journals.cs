using System.Globalization;
using PetraERP.ApiClient;

namespace PetraERP.TestDataImport;

internal sealed partial class LegacyImporter
{
    private const decimal Scale = 0.0001m;

    private async Task JournalsAsync(CancellationToken cancellationToken)
    {
        var headers = LegacyTable.Read(legacy, "ce000");
        var lines = LegacyTable.Read(legacy, "en000").GroupBy(l => Text(l, "ParentGUID"), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => int.Parse(Text(l, "Number"), CultureInfo.InvariantCulture)).ToArray(), StringComparer.Ordinal);
        var ordered = headers.OrderBy(h => Text(h, "Date"), StringComparer.Ordinal).ThenBy(h => long.Parse(Text(h, "Number"), CultureInfo.InvariantCulture)).ToArray();
        var posted = 0; var adjusted = new List<string>(); var progress = 0;
        decimal totalDebit = 0m;
        foreach (var header in ordered)
        {
            progress++;
            var key = Text(header, "GUID");
            var number = Text(header, "Number");
            try
            {
                var draft = Lines(lines[key], header, out var residue);
                totalDebit += draft.Sum(l => decimal.Parse(l.TransactionDebit, CultureInfo.InvariantCulture));
                if (map.TryGet("journal-posted", key, out _)) continue;
                if (residue != 0m) adjusted.Add(string.Create(CultureInfo.InvariantCulture, $"#{number} ({Text(header, "Date")[..10]}): {residue:0.0000}"));
                var journal = map.TryGet("journal", key, out var existing)
                    ? await Api.GetJournalAsync(companyId, branchId, existing, cancellationToken).ConfigureAwait(false)
                    : await Api.CreateJournalDraftAsync(companyId, branchId, new CreateJournalDraftRequest
                    {
                        AccountingDate = DateOnly.ParseExact(Text(header, "Date")[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Description = Clip($"[أمين 2026 #{number}] {Text(header, "Notes")}".Trim(), 1000), Lines = draft,
                    }, cancellationToken).ConfigureAwait(false);
                map.Set("journal", key, journal.Id);
                if (journal.State == JournalState.Draft)
                    journal = await Api.ValidateJournalDraftAsync(companyId, branchId, journal.Id, new JournalActionRequest { ExpectedVersion = journal.Version, Reason = Reason }, cancellationToken).ConfigureAwait(false);
                if (journal.State == JournalState.Validated)
                    journal = await Api.ApproveJournalDraftAsync(companyId, branchId, journal.Id, new JournalActionRequest { ExpectedVersion = journal.Version, Reason = Reason }, cancellationToken).ConfigureAwait(false);
                if (journal.State == JournalState.Approved)
                    await Api.PostJournalAsync(companyId, branchId, journal.Id, new PostJournalRequest { ExpectedVersion = journal.Version, Reason = Reason }, Guid.Parse(key).ToString("D"), cancellationToken).ConfigureAwait(false);
                map.Set("journal-posted", key, journal.Id); posted++;
            }
            catch (Exception exception) when (exception is PetraApiException or KeyNotFoundException or FormatException)
            { rejected.Add($"journal #{number} ({Text(header, "Date")[..10]}): {Problem(exception)}"); }
            if (progress % 250 == 0) Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  journals {progress}/{ordered.Length}"));
        }
        Line(string.Create(CultureInfo.InvariantCulture, $"- Journals posted this run: {posted}; total in source: {ordered.Length}; source debit total after 4-dp rounding: {totalDebit:N4} {baseCurrency}"));
        Line(string.Empty);
        Line($"## Rounding adjustments ({adjusted.Count})");
        Line("Ameen stores FLOAT amounts. After rounding each line to 4 decimals these journals differed by the residue below; it was added to the largest line of the smaller side so the journal balances. TEST DATA ONLY.");
        foreach (var item in adjusted) Line("- " + item);
    }

    /// <summary>Base-currency lines from Ameen's stored base amounts, rounded to 4 decimals; residue goes to the largest smaller-side line.</summary>
    private List<JournalLineDraftDto> Lines(IReadOnlyList<IReadOnlyDictionary<string, string>> source, IReadOnlyDictionary<string, string> header, out decimal residue)
    {
        var rows = source.Select(l => new
        {
            Account = map.Get("account", Text(l, "AccountGUID")),
            Cost = Text(l, "CostGUID") is var cost && cost != ZeroGuid ? map.Get("cost", cost) : (Guid?)null,
            Debit = Math.Round(decimal.Parse(Text(l, "Debit"), NumberStyles.Float, CultureInfo.InvariantCulture), 4, MidpointRounding.AwayFromZero),
            Credit = Math.Round(decimal.Parse(Text(l, "Credit"), NumberStyles.Float, CultureInfo.InvariantCulture), 4, MidpointRounding.AwayFromZero),
            Notes = Text(l, "Notes"),
        }).Where(l => l.Debit != 0m || l.Credit != 0m).ToList();
        residue = rows.Sum(l => l.Debit) - rows.Sum(l => l.Credit);
        var debits = rows.Select(l => l.Debit).ToArray(); var credits = rows.Select(l => l.Credit).ToArray();
        if (residue > 0m) { var index = Array.IndexOf(credits, credits.Max()); credits[index] += residue; }
        else if (residue < 0m) { var index = Array.IndexOf(debits, debits.Max()); debits[index] -= residue; }
        var fallback = Text(header, "Notes") is { Length: > 0 } notes ? notes : "قيد مستورد من الأمين";
        return rows.Select((l, index) => new JournalLineDraftDto
        {
            AccountId = l.Account, CostCenterId = l.Cost, TransactionCurrencyCode = baseCurrency,
            TransactionDebit = debits[index].ToString("0.0000", CultureInfo.InvariantCulture), TransactionCredit = credits[index].ToString("0.0000", CultureInfo.InvariantCulture),
            Description = Clip(l.Notes.Length != 0 ? l.Notes : fallback, 1000),
        }).ToList();
    }
}
