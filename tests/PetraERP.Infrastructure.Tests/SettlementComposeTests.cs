using System.Diagnostics;
using System.Globalization;
using PetraERP.Application.Common;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Treasury;
using Xunit;
using Seed = PetraERP.Infrastructure.Treasury.SettlementQueries.Seed;
using ControlLine = PetraERP.Infrastructure.Treasury.SettlementQueries.ControlLine;
using AppliedLine = PetraERP.Infrastructure.Treasury.SettlementQueries.AppliedLine;
using LegacyLine = PetraERP.Infrastructure.Treasury.SettlementQueries.LegacyLine;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// P05: the hash-lookup composition of open invoice balances must be value-identical to the previous per-invoice scan
/// (the scan is kept below, verbatim, as the oracle), including its two failure modes, and must scale linearly.
/// </summary>
public sealed class SettlementComposeTests
{
    private static readonly Guid Account = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private sealed record Data(List<Seed> Source, List<ControlLine> Controls, List<AppliedLine> Apps, List<LegacyLine> Legacy);

    private static Data Build(int invoices, int seedValue, decimal rate = 1m)
    {
        var random = new Random(seedValue);
        var source = new List<Seed>(); var controls = new List<ControlLine>(); var apps = new List<AppliedLine>(); var legacy = new List<LegacyLine>();
        var party = Guid.NewGuid(); var branch = Guid.NewGuid();
        for (var n = 0; n < invoices; n++)
        {
            var id = Guid.NewGuid(); var journal = Guid.NewGuid();
            source.Add(new Seed(id, party, branch, "INV-" + n.ToString("D6", CultureInfo.InvariantCulture), DateTimeOffset.UnixEpoch.AddDays(n), "USD", rate, 100m, journal, null, null));
            controls.Add(new ControlLine(journal, Account, 100m * rate, 0m));
            var roll = random.Next(100);
            if (roll < 10)
            {
                var returnJournal = Guid.NewGuid();
                source.Add(new Seed(Guid.NewGuid(), party, branch, null, DateTimeOffset.UnixEpoch, "USD", rate, 10m, returnJournal, id, null));
                controls.Add(new ControlLine(returnJournal, Account, 0m, 10m * rate));
            }
            else if (roll < 12)
            {
                var reversalJournal = Guid.NewGuid();
                source.Add(new Seed(Guid.NewGuid(), party, branch, null, DateTimeOffset.UnixEpoch, "USD", rate, 100m, reversalJournal, null, id));
                controls.Add(new ControlLine(reversalJournal, Account, 0m, 100m * rate));
            }
            if (random.Next(100) < 30) { apps.Add(new AppliedLine(id, 20m, 20m * rate)); if (random.Next(2) == 0) apps.Add(new AppliedLine(id, 5m, 5m * rate)); }
            if (random.Next(100) < 10) legacy.Add(new LegacyLine(id, 10m));
        }
        return new Data(source, controls, apps, legacy);
    }

    /// <summary>The previous implementation (per-invoice scans), copied unchanged.</summary>
    private static List<SettlementInvoice> Reference(VoucherKind kind, Data d)
    {
        var source = d.Source; var controls = d.Controls; var apps = d.Apps; var legacy = d.Legacy;
        var originals = source.Where(i => i.Source is null && i.Reverses is null && !source.Any(r => r.Reverses == i.Id)).ToArray();
        var result = new List<SettlementInvoice>();
        foreach (var invoice in originals)
        {
            var originalControls = controls.Where(l => l.JournalId == invoice.JournalId).ToArray();
            var accountIds = originalControls.Select(l => l.AccountId).Distinct().ToArray();
            if (accountIds.Length != 1)
                throw new BusinessRuleException("treasury.control_provenance", "تعذر تحديد حساب ذمة الفاتورة من قيدها؛ يلزم مراجعة المصدر / Invoice control-account provenance is missing or ambiguous; review the original journal.");
            var returns = source.Where(r => r.Source == invoice.Id).ToArray();
            var returned = returns.Sum(r => r.Total);
            var appPaid = apps.Where(a => a.Id == invoice.Id).ToArray();
            var oldPaid = legacy.Where(a => a.Id == invoice.Id).Sum(a => a.Amount);
            var paid = oldPaid + appPaid.Sum(a => a.InvoiceAmount);
            var returnJournals = returns.Select(r => r.JournalId).ToArray();
            var originalBase = kind == VoucherKind.Receipt ? originalControls.Sum(l => l.BaseDebit - l.BaseCredit) : originalControls.Sum(l => l.BaseCredit - l.BaseDebit);
            var returnedBase = controls.Where(l => returnJournals.Contains(l.JournalId)).Sum(l => kind == VoucherKind.Receipt ? l.BaseCredit - l.BaseDebit : l.BaseDebit - l.BaseCredit);
            var remainingBase = originalBase - returnedBase - appPaid.Sum(a => a.BookedBaseAmount) - legacy.Where(a => a.Id == invoice.Id).Sum(a => DocumentCurrencyPolicy.ToBase(a.Amount, invoice.Rate));
            var remaining = invoice.Total - returned - paid;
            if (remaining < 0 || remainingBase < 0 || remaining == 0 && remainingBase != 0)
                throw new BusinessRuleException("treasury.reconciliation_required", "تخصيصات الفاتورة أو قيمتها الدفترية غير متطابقة؛ راجع حركاتها قبل المتابعة / Invoice allocations and carrying balance require reconciliation before continuing.");
            result.Add(new(invoice.Id, invoice.PartyId, invoice.BranchId, invoice.Number, invoice.DateUtc, invoice.CurrencyCode, invoice.Rate,
                invoice.Total, returned, paid, remaining, remainingBase, accountIds[0], IsOpenItem: false));
        }
        return result;
    }

    private static List<SettlementInvoice> Optimised(VoucherKind kind, Data d) =>
        SettlementQueries.Compose(kind, d.Source, SettlementQueries.SelectOriginals(d.Source), d.Controls, d.Apps, d.Legacy);

    [Theory]
    [InlineData(VoucherKind.Receipt, 1, 1.0)]
    [InlineData(VoucherKind.Receipt, 2, 3.75)]
    [InlineData(VoucherKind.Payment, 3, 1.0)]
    [InlineData(VoucherKind.Payment, 4, 0.00012345)]
    public void CompositionIsValueIdenticalToThePreviousScan(VoucherKind kind, int seed, double rate)
    {
        var data = Build(1500, seed, (decimal)rate);
        // The Payment kind reads the opposite control side; flip the synthetic control lines so the balances stay valid for both kinds.
        if (kind == VoucherKind.Payment)
            data = data with { Controls = data.Controls.Select(l => l with { BaseDebit = l.BaseCredit, BaseCredit = l.BaseDebit }).ToList() };
        var expected = Reference(kind, data);
        var actual = Optimised(kind, data);
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++) Assert.Equal(expected[i], actual[i]);
        Assert.Contains(actual, r => r.Returned > 0);
        Assert.Contains(actual, r => r.Paid > 0);
        Assert.DoesNotContain(actual, r => r.IsOpenItem);
    }

    [Fact]
    public void BothFailureModesAreRaisedExactlyAsBefore()
    {
        var missing = Build(5, 7);
        missing.Controls.RemoveAt(0); // an invoice without a control line in its own journal
        var a = Assert.Throws<BusinessRuleException>(() => Reference(VoucherKind.Receipt, missing));
        var b = Assert.Throws<BusinessRuleException>(() => Optimised(VoucherKind.Receipt, missing));
        Assert.Equal(a.Message, b.Message);

        var over = Build(5, 8);
        over.Apps.Add(new AppliedLine(over.Source[0].Id, 1000m, 1000m)); // paid more than the invoice
        a = Assert.Throws<BusinessRuleException>(() => Reference(VoucherKind.Receipt, over));
        b = Assert.Throws<BusinessRuleException>(() => Optimised(VoucherKind.Receipt, over));
        Assert.Equal(a.Message, b.Message);

        var split = Build(5, 9);
        split.Controls.Add(new ControlLine(split.Source[0].JournalId, Guid.NewGuid(), 1m, 0m)); // two control accounts => ambiguous
        a = Assert.Throws<BusinessRuleException>(() => Reference(VoucherKind.Receipt, split));
        b = Assert.Throws<BusinessRuleException>(() => Optimised(VoucherKind.Receipt, split));
        Assert.Equal(a.Message, b.Message);
    }

    [Fact]
    public void ReversedInvoicesAndReturnsAreNotOriginals()
    {
        var data = Build(400, 11);
        var originals = SettlementQueries.SelectOriginals(data.Source);
        var reversedTargets = data.Source.Where(s => s.Reverses is not null).Select(s => s.Reverses!.Value).ToHashSet();
        Assert.NotEmpty(reversedTargets);
        Assert.All(originals, o => { Assert.Null(o.Source); Assert.Null(o.Reverses); Assert.DoesNotContain(o.Id, reversedTargets); });
        Assert.Equal(400 - reversedTargets.Count, originals.Length);
    }

    /// <summary>Measurement (not a claim): P50/P95 of the in-memory composition, previous scan vs hash lookups. Output path: PETRAERP_BENCH_OUT.</summary>
    [Fact(Explicit = true)]
    [Trait("Category", "Benchmark")]
    public void MeasureCompositionTimes()
    {
        var lines = new List<string> { "invoices;implementation;p50_ms;p95_ms;runs" };
        foreach (var size in new[] { 500, 2000, 8000, 20000 })
        {
            var data = Build(size, 42);
            _ = Optimised(VoucherKind.Receipt, data); _ = size <= 8000 ? Reference(VoucherKind.Receipt, data) : null; // warm-up
            foreach (var (name, run) in new (string, Func<List<SettlementInvoice>>)[] { ("previous_scan", () => Reference(VoucherKind.Receipt, data)), ("hash_lookup", () => Optimised(VoucherKind.Receipt, data)) })
            {
                if (name == "previous_scan" && size > 8000) { lines.Add($"{size};{name};skipped(too slow);;0"); continue; }
                var runs = size <= 2000 ? 15 : 7;
                var samples = new List<double>();
                for (var i = 0; i < runs; i++) { var sw = Stopwatch.StartNew(); _ = run(); sw.Stop(); samples.Add(sw.Elapsed.TotalMilliseconds); }
                samples.Sort();
                double Pct(double p) => samples[Math.Min(samples.Count - 1, (int)Math.Ceiling(p * samples.Count) - 1)];
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{size};{name};{Pct(0.5):F2};{Pct(0.95):F2};{runs}"));
            }
        }
        File.WriteAllLines(Environment.GetEnvironmentVariable("PETRAERP_BENCH_OUT") ?? Path.Combine(Path.GetTempPath(), "petra_compose_bench.csv"), lines);
    }
}
