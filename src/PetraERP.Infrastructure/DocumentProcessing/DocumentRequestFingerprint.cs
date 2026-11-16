using PetraERP.Application.Common;
using PetraERP.Application.Purchases;
using PetraERP.Application.Sales;
using PetraERP.Application.Treasury;
using System.Globalization;

namespace PetraERP.Infrastructure.DocumentProcessing;

/// <summary>
/// Server-side canonical comparison of a retried save against the document its request key already created.
/// The deterministic document id makes the key durable; this class makes sure the retry carries the same financial
/// content. Any financially significant difference is a client bug and is rejected, never merged silently.
/// Values the server may legitimately default when the caller leaves them out (null currency, rate, cost centre) are
/// compared only when the caller supplied them.
/// </summary>
internal static class DocumentRequestFingerprint
{
    // No tolerance: one cent or the last supported exchange-rate digit is a different request. This comparer is only the
    // fallback for documents that have no persisted DocumentRequestBindingRow (created before the binding existed, or a
    // host without the annual context factory); bound keys are decided by the canonical hash, not by this class.
    private const decimal AmountTolerance = 0m;
    private const decimal ExactTolerance = 0m;

    private static BusinessRuleException Conflict(string field) => new("document.request_key_conflict",
        $"This request key already saved a different document ({field}). Use a new key for a new document.");

    private static bool Near(decimal saved, decimal sent, decimal tolerance) => Math.Abs(saved - sent) <= tolerance;
    private static bool SameText(string? saved, string? sent) => string.Equals(saved?.Trim(), sent?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool SameDay(DateTimeOffset saved, DateTimeOffset sent) => saved.UtcDateTime.Date == sent.UtcDateTime.Date;
    private static string N(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);

    private static void SameLines<T>(IReadOnlyList<T> saved, IReadOnlyList<T> sent, Func<T, string> key, Func<T, T, bool> equal, string field)
    {
        if (saved.Count != sent.Count) throw Conflict(field + " count");
        var left = saved.OrderBy(key, StringComparer.Ordinal).ToArray();
        var right = sent.OrderBy(key, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < left.Length; i++)
            if (!equal(left[i], right[i])) throw Conflict(field);
    }

    // ── Sales invoice ───────────────────────────────────────────────────────────────────────────────────────────
    public static void EnsureSame(SalesInvoiceDetailResult existing, CreateSalesInvoiceCommand command)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(command);
        if (existing.PatternRevisionId != command.PatternRevisionId) throw Conflict("pattern");
        if (existing.CustomerId != command.CustomerId) throw Conflict("customer");
        if (existing.WarehouseId != command.WarehouseId) throw Conflict("warehouse");
        if (existing.PaymentMethod != command.PaymentMethod) throw Conflict("payment method");
        if (command.InvoiceDateUtc is { } date && !SameDay(existing.InvoiceDateUtc, date)) throw Conflict("document date");
        if (!string.IsNullOrWhiteSpace(command.CurrencyCode) && !SameText(existing.CurrencyCode, command.CurrencyCode)) throw Conflict("currency");
        if (command.ExchangeRate is { } rate && !Near(existing.ExchangeRate, rate, ExactTolerance)) throw Conflict("exchange rate");
        if (command.CostCenterId is { } center && existing.CostCenterId != center) throw Conflict("cost center");

        var saved = existing.Lines.Select(l => new SalesLine(l.ItemId, l.UnitId, l.Quantity, l.UnitPrice, l.DiscountPercentage, l.DiscountAmount,
            l.TaxPercentage, l.TaxAmount, l.PumpCost, l.TransportCost, l.CostCenterId, l.WarehouseId, l.ConcreteGrade)).ToArray();
        var sent = (command.Lines ?? []).Select(l => new SalesLine(l.ItemId, l.UnitId, l.Quantity, l.UnitPrice, l.DiscountPercentage, l.DiscountAmount,
            l.TaxPercentage, l.TaxAmount, l.PumpCost, l.TransportCost, l.CostCenterId, l.WarehouseId, l.ConcreteGrade)).ToArray();
        SameLines(saved, sent, SalesKey, SalesLineEqual, "line amounts");
        var savedAdjustments = existing.Adjustments.Select(AdjustmentKey).Order(StringComparer.Ordinal).ToArray();
        var sentAdjustments = (command.Adjustments ?? []).Select(a => $"{(int)a.Kind}:{a.AccountId:N}:{a.CounterAccountId:N}:{N(a.Amount)}").Order(StringComparer.Ordinal).ToArray();
        if (!savedAdjustments.SequenceEqual(sentAdjustments, StringComparer.Ordinal)) throw Conflict("discounts and additions");
    }

    private static string AdjustmentKey(PetraERP.Application.Sales.SalesInvoiceAdjustmentResult a) => $"{(int)a.Kind}:{a.AccountId:N}:{a.CounterAccountId:N}:{N(a.Amount)}";

    private sealed record SalesLine(Guid Item, Guid Unit, decimal Qty, decimal Price, decimal DiscPct, decimal DiscAmt, decimal TaxPct,
        decimal TaxAmt, decimal Pump, decimal Transport, Guid? CostCenter, Guid? Warehouse, string? Grade);

    private static string ItemKey(Guid item, Guid unit, decimal qty, decimal price) => $"{item:N}:{unit:N}:{N(qty)}:{N(price)}";

    private static string SalesKey(SalesLine l) =>
        string.Join('|', "S", ItemKey(l.Item, l.Unit, l.Qty, l.Price), N(l.DiscPct), N(l.TaxPct), N(l.Pump), N(l.Transport));

    private static bool SalesLineEqual(SalesLine saved, SalesLine sent) =>
        saved.Item == sent.Item && saved.Unit == sent.Unit
        && Near(saved.Qty, sent.Qty, ExactTolerance) && Near(saved.Price, sent.Price, ExactTolerance)
        && Near(saved.DiscPct, sent.DiscPct, ExactTolerance) && Near(saved.DiscAmt, sent.DiscAmt, AmountTolerance)
        && Near(saved.TaxPct, sent.TaxPct, ExactTolerance) && Near(saved.TaxAmt, sent.TaxAmt, AmountTolerance)
        && Near(saved.Pump, sent.Pump, ExactTolerance) && Near(saved.Transport, sent.Transport, ExactTolerance)
        && (sent.CostCenter is null || saved.CostCenter == sent.CostCenter)
        && (sent.Warehouse is null || saved.Warehouse == sent.Warehouse)
        && (string.IsNullOrWhiteSpace(sent.Grade) || SameText(saved.Grade, sent.Grade));

    // ── Purchase invoice ────────────────────────────────────────────────────────────────────────────────────────
    public static void EnsureSame(PurchaseDetailResult existing, SavePurchaseInvoiceCommand command)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(command);
        if (existing.PatternRevisionId != command.PatternRevisionId) throw Conflict("pattern");
        if (existing.SupplierId != command.SupplierId) throw Conflict("supplier");
        if (existing.WarehouseId != command.WarehouseId) throw Conflict("warehouse");
        if (existing.PaymentMethod != command.PaymentMethod) throw Conflict("payment method");
        if (!SameDay(existing.InvoiceDateUtc, command.InvoiceDateUtc)) throw Conflict("document date");
        if (!string.IsNullOrWhiteSpace(command.CurrencyCode) && !SameText(existing.CurrencyCode, command.CurrencyCode)) throw Conflict("currency");
        if (command.ExchangeRate is { } rate && !Near(existing.ExchangeRate, rate, ExactTolerance)) throw Conflict("exchange rate");

        var saved = existing.Lines.Select(l => new PurchaseLine(l.ItemId, l.UnitId, l.Quantity, l.UnitCost, l.DiscountAmount, l.TaxAmount, l.CostCenterId)).ToArray();
        var sent = command.Lines.Select(l => new PurchaseLine(l.ItemId, l.UnitId, l.Quantity, l.UnitCost, l.DiscountAmount, l.TaxAmount, l.CostCenterId)).ToArray();
        SameLines(saved, sent, l => "P|" + ItemKey(l.Item, l.Unit, l.Qty, l.Cost), PurchaseLineEqual, "lines");
        var savedAdjustments = existing.Adjustments.Select(a => $"{(int)a.Kind}:{a.AccountId:N}:{a.CounterAccountId:N}:{N(a.Amount)}").Order(StringComparer.Ordinal).ToArray();
        var sentAdjustments = (command.Adjustments ?? []).Select(a => $"{(int)a.Kind}:{a.AccountId:N}:{a.CounterAccountId:N}:{N(a.Amount)}").Order(StringComparer.Ordinal).ToArray();
        if (!savedAdjustments.SequenceEqual(sentAdjustments, StringComparer.Ordinal)) throw Conflict("discounts and additions");
    }

    private sealed record PurchaseLine(Guid Item, Guid Unit, decimal Qty, decimal Cost, decimal DiscAmt, decimal TaxAmt, Guid? CostCenter);

    private static bool PurchaseLineEqual(PurchaseLine saved, PurchaseLine sent) =>
        saved.Item == sent.Item && saved.Unit == sent.Unit
        && Near(saved.Qty, sent.Qty, ExactTolerance) && Near(saved.Cost, sent.Cost, ExactTolerance)
        && Near(saved.DiscAmt, sent.DiscAmt, AmountTolerance) && Near(saved.TaxAmt, sent.TaxAmt, AmountTolerance)
        && (sent.CostCenter is null || saved.CostCenter == sent.CostCenter);

    // ── Treasury voucher ────────────────────────────────────────────────────────────────────────────────────────
    public static void EnsureSame(VoucherDetailResult existing, SaveVoucherCommand command)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(command);
        if (existing.Kind != command.Kind) throw Conflict("voucher kind");
        if (existing.PatternRevisionId != command.PatternRevisionId) throw Conflict("pattern");
        if (existing.PartyId != command.PartyId) throw Conflict("party");
        if (existing.SettlementMethod != command.SettlementMethod) throw Conflict("settlement method");
        if (!SameDay(existing.VoucherDateUtc, command.VoucherDateUtc)) throw Conflict("document date");
        if (!string.IsNullOrWhiteSpace(command.CurrencyCode) && !SameText(existing.CurrencyCode, command.CurrencyCode)) throw Conflict("currency");
        if (command.ExchangeRate is { } rate && !Near(existing.ExchangeRate, rate, ExactTolerance)) throw Conflict("exchange rate");
        if (!Near(existing.Amount, command.Amount, ExactTolerance)) throw Conflict("amount");
        if (!SameText(existing.SourceNumber ?? string.Empty, command.SourceNumber?.Trim() ?? string.Empty) || existing.SourceDate != command.SourceDate) throw Conflict("source document");

        // Allocation destinations. Legacy sales allocations and modern settlement allocations have separate semantics and are
        // compared separately. The modern allocation is ONE representation: the plan rows while they exist, otherwise the
        // initial settlement batch applications (which are what the plans became after posting) - never both, or a posted
        // retry would count every allocation twice.
        static Dictionary<Guid, decimal> Sum(IEnumerable<(Guid Target, decimal Amount)> values) =>
            values.GroupBy(v => v.Target).ToDictionary(g => g.Key, g => g.Sum(v => v.Amount));
        static bool SameDestinations(Dictionary<Guid, decimal> saved, Dictionary<Guid, decimal> sent) =>
            saved.Count == sent.Count && sent.All(pair => saved.TryGetValue(pair.Key, out var amount) && Near(amount, pair.Value, AmountTolerance));
        var savedLegacy = Sum(existing.Allocations.Select(a => (a.SalesInvoiceId, a.Amount)));
        var savedModern = existing.InvoiceAllocations.Count > 0
            ? Sum(existing.InvoiceAllocations.Select(a => (a.InvoiceId, a.SettlementAmount)))
            : Sum(existing.SettlementBatches.Where(b => b.IsInitial).SelectMany(b => b.Applications).Select(a => (a.InvoiceId, a.SettlementAmount)));
        if (!SameDestinations(savedLegacy, Sum(command.Allocations.Select(a => (a.SalesInvoiceId, a.Amount))))
            || !SameDestinations(savedModern, Sum(command.InvoiceAllocations.Select(a => (a.InvoiceId, a.SettlementAmount)))))
            throw Conflict("allocation destinations");

        var savedLines = existing.Lines.Select(l => new VoucherLine(l.PartyId, l.AccountId, l.Amount, l.CostCenterId)).ToArray();
        var sentLines = command.Lines.Select(l => new VoucherLine(l.PartyId, l.AccountId, l.Amount, l.CostCenterId)).ToArray();
        SameLines(savedLines, sentLines, l => $"V|{l.Party:N}|{l.Account:N}|{N(l.Amount)}",
            (s, c) => s.Party == c.Party && s.Account == c.Account && Near(s.Amount, c.Amount, ExactTolerance)
                      && (c.CostCenter is null || s.CostCenter == c.CostCenter), "payment lines");
    }

    private sealed record VoucherLine(Guid? Party, Guid? Account, decimal Amount, Guid? CostCenter);
}
