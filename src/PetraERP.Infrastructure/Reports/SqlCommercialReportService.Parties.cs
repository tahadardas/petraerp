using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Treasury;

namespace PetraERP.Infrastructure.Reports;

public sealed partial class SqlCommercialReportService
{
    private const int CreditPayment = 1;
    private const byte ReceiptKind = 1;
    private const byte PaymentKind = 2;
    private const byte VoucherReversed = 3;

    /// <summary>One receivable/payable effect in the party's currency. Debit increases what a customer owes; credit increases what we owe a supplier.</summary>
    private sealed record PartyEntry(DateTimeOffset DateUtc, string Type, Guid DocumentId, string? Number, string Description, decimal Debit, decimal Credit);

    public async Task<PartyStatementResult> GetPartyStatementAsync(LedgerReportScope scope, Guid partyId, PartyStatementKind kind, string currencyCode,
        DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        EnsureRange(fromDate, toDate);
        await EnsureScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        RequireStatementKind(kind);
        var currency = RequireCurrency(currencyCode);
        var party = await context.Parties.AsNoTracking().Where(p => p.Id == partyId && p.CompanyId == scope.CompanyId)
            .Select(p => new { p.Code, p.NameArabic, p.Role }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("party");
        if (!RoleMatches(party.Role, kind))
            throw new BusinessRuleException("reports.party_role_invalid", "The party does not have the requested customer/supplier role.", "kind");

        var end = EndUtc(toDate); var start = StartUtc(fromDate);
        var entries = (kind == PartyStatementKind.Customer
            ? await CustomerEntriesAsync(scope, [partyId], currency, end, cancellationToken).ConfigureAwait(false)
            : await SupplierEntriesAsync(scope, [partyId], currency, end, cancellationToken).ConfigureAwait(false))
            .Select(e => e.Entry).OrderBy(e => e.DateUtc).ThenBy(e => e.Number, StringComparer.Ordinal)
            .ThenBy(e => e.DocumentId).ThenBy(e => e.Type, StringComparer.Ordinal).ToList();
        decimal Signed(PartyEntry e) => kind == PartyStatementKind.Customer ? e.Debit - e.Credit : e.Credit - e.Debit;
        var opening = entries.Where(e => e.DateUtc < start).Sum(Signed);
        var period = entries.Where(e => e.DateUtc >= start).ToList();
        var balance = opening;
        var lines = new List<PartyStatementLine>();
        foreach (var e in period.Take(MaxLines))
        {
            balance += Signed(e);
            lines.Add(new(e.DateUtc, e.Type, e.DocumentId, e.Number, e.Description, e.Debit, e.Credit, balance));
        }
        var debit = period.Sum(e => e.Debit); var credit = period.Sum(e => e.Credit);
        return new(partyId, party.Code, party.NameArabic, kind, currency, fromDate, toDate, opening, lines, debit, credit,
            opening + (kind == PartyStatementKind.Customer ? debit - credit : credit - debit), period.Count > MaxLines);
    }

    public Task<AgingResult> GetAgingAsync(LedgerReportScope scope, PartyStatementKind kind, string currencyCode, DateOnly asOf,
        CancellationToken cancellationToken = default) => GetAgingCoreAsync(scope, null, kind, currencyCode, asOf, cancellationToken);

    public Task<AgingResult> GetPartyAgingAsync(LedgerReportScope scope, Guid partyId, PartyStatementKind kind, string currencyCode, DateOnly asOf,
        CancellationToken cancellationToken = default) => GetAgingCoreAsync(scope, partyId, kind, currencyCode, asOf, cancellationToken);

    private async Task<AgingResult> GetAgingCoreAsync(LedgerReportScope scope, Guid? selectedPartyId, PartyStatementKind kind, string currencyCode, DateOnly asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        RequireStatementKind(kind);
        if (selectedPartyId is { } selected && !await context.Parties.AnyAsync(p => p.Id == selected && p.CompanyId == scope.CompanyId
            && (p.Role == 3 || p.Role == (int)kind), cancellationToken).ConfigureAwait(false))
            throw new ResourceNotFoundException("party");
        var currency = RequireCurrency(currencyCode);
        var end = EndUtc(asOf);
        var invoices = await OpenItemsAsync(scope, currency, end, kind == PartyStatementKind.Customer ? VoucherKind.Receipt : VoucherKind.Payment,
            selectedPartyId, cancellationToken).ConfigureAwait(false);
        var partyIds = invoices.Items.Select(i => i.PartyId).Concat(invoices.Advances.Keys).Distinct().ToList();
        var parties = await context.Parties.AsNoTracking().Where(p => p.CompanyId == scope.CompanyId && partyIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Code, p.NameArabic, p.PaymentTermDays }).ToDictionaryAsync(p => p.Id, cancellationToken).ConfigureAwait(false);

        var documents = new List<AgingDocument>();
        var rows = new List<AgingRow>();
        foreach (var partyId in partyIds.Where(parties.ContainsKey).OrderBy(id => parties[id].Code, StringComparer.Ordinal))
        {
            var p = parties[partyId];
            decimal notDue = 0, d30 = 0, d60 = 0, d90 = 0, over = 0;
            foreach (var item in invoices.Items.Where(i => i.PartyId == partyId && i.Outstanding > 0m).OrderBy(i => i.Date))
            {
                var due = item.Date.AddDays(Math.Max(0, p.PaymentTermDays));
                var days = asOf.DayNumber - due.DayNumber;
                if (days <= 0) notDue += item.Outstanding;
                else if (days <= 30) d30 += item.Outstanding;
                else if (days <= 60) d60 += item.Outstanding;
                else if (days <= 90) d90 += item.Outstanding;
                else over += item.Outstanding;
                documents.Add(new(partyId, item.InvoiceId, item.Number, item.Date, due, Math.Max(0, days), item.Outstanding));
            }
            var advance = invoices.Advances.GetValueOrDefault(partyId);
            var net = notDue + d30 + d60 + d90 + over - advance;
            if (net != 0m || advance != 0m || notDue + d30 + d60 + d90 + over != 0m)
                rows.Add(new(partyId, p.Code, p.NameArabic, notDue, d30, d60, d90, over, advance, net));
        }
        var totals = new AgingRow(Guid.Empty, string.Empty, string.Empty, rows.Sum(r => r.NotDue), rows.Sum(r => r.Days1To30), rows.Sum(r => r.Days31To60),
            rows.Sum(r => r.Days61To90), rows.Sum(r => r.Over90), rows.Sum(r => r.Advance), rows.Sum(r => r.Net));
        return new(kind, asOf, currency, rows, documents, totals);
    }

    private sealed record OpenItem(Guid PartyId, Guid InvoiceId, string? Number, DateOnly Date, decimal Outstanding);

    private sealed record OpenItems(List<OpenItem> Items, Dictionary<Guid, decimal> Advances);

    /// <summary>Only explicit posted applications settle invoices. Unapplied receipts/payments remain advances; aging never invents FIFO allocations.</summary>
    private async Task<OpenItems> OpenItemsAsync(LedgerReportScope scope, string currency, DateTimeOffset end, VoucherKind kind, Guid? selectedPartyId,
        CancellationToken cancellationToken)
    {
        var invoices = await SettlementQueries.LoadInvoicesAsync(context, scope.CompanyId, scope.Consolidated ? null : scope.BranchId,
            kind, selectedPartyId, currency, end, cancellationToken).ConfigureAwait(false);
        var items = invoices.Where(i => i.Outstanding > 0m).Select(i => new OpenItem(i.PartyId, i.Id, i.Number,
            DateOnly.FromDateTime(i.DateUtc.UtcDateTime), i.Outstanding)).ToList();
        var vouchers = await VouchersInScope(scope).Where(v => v.Kind == (byte)kind && v.PartyId != null && !v.Lines.Any()
                && (selectedPartyId == null || v.PartyId == selectedPartyId)
                && v.CurrencyCode == currency && v.VoucherDateUtc < end && !(v.Status == VoucherReversed && v.ReversedAtUtc < end))
            .Select(v => new { v.Id, PartyId = v.PartyId!.Value, v.Amount }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var voucherIds = vouchers.Select(v => v.Id).ToList();
        var applications = await context.SettlementApplications.AsNoTracking()
            .Where(a => voucherIds.Contains(a.VoucherId) && a.Batch!.CompanyId == scope.CompanyId && a.Batch.DateUtc < end
                && (a.Batch.State == 1 || a.Batch.ReversedAtUtc >= end))
            .GroupBy(a => a.VoucherId).Select(g => new { Id = g.Key, Amount = g.Sum(a => a.SettlementAmount) })
            .ToDictionaryAsync(x => x.Id, x => x.Amount, cancellationToken).ConfigureAwait(false);
        var legacy = await context.VoucherAllocations.AsNoTracking().Where(a => voucherIds.Contains(a.VoucherId))
            .GroupBy(a => a.VoucherId).Select(g => new { Id = g.Key, Amount = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.Id, x => x.Amount, cancellationToken).ConfigureAwait(false);
        var advances = new Dictionary<Guid, decimal>();
        foreach (var v in vouchers)
        {
            var advance = v.Amount - applications.GetValueOrDefault(v.Id) - legacy.GetValueOrDefault(v.Id);
            if (advance != 0m) advances[v.PartyId] = advances.GetValueOrDefault(v.PartyId) + advance;
        }
        // Historical payment distributions identify the supplier but no invoice. Keep them unapplied rather than fabricate settlement dates.
        if (kind == VoucherKind.Payment)
            foreach (var payment in await PaymentLinesInScope(scope, currency, end, selectedPartyId)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
                advances[payment.PartyId] = advances.GetValueOrDefault(payment.PartyId) + payment.Amount;
        // An already-posted excess return is a credit, never a negative aging invoice.
        foreach (var invoice in invoices.Where(i => i.Outstanding < 0m))
            advances[invoice.PartyId] = advances.GetValueOrDefault(invoice.PartyId) - invoice.Outstanding;
        return new(items, advances);
    }

    private sealed record PartyEntryRow(Guid PartyId, PartyEntry Entry);

    private async Task<List<PartyEntryRow>> CustomerEntriesAsync(LedgerReportScope scope, List<Guid> parties, string currency, DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var sales = await SalesInScope(scope).Where(i => parties.Contains(i.CustomerId) && i.PaymentMethod == CreditPayment && i.CurrencyCode == currency
                && i.JournalEntryId != null && i.InvoiceDateUtc < end)
            .Select(i => new { i.Id, i.CustomerId, i.InvoiceNumber, i.InvoiceDateUtc, i.GrandTotal, i.ReversesInvoiceId, i.ReturnOfInvoiceId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var receipts = await VouchersInScope(scope).Where(v => v.Kind == ReceiptKind && v.PartyId != null && parties.Contains(v.PartyId.Value)
                && v.CurrencyCode == currency && v.JournalEntryId != null && v.VoucherDateUtc < end)
            .Select(v => new { v.Id, PartyId = v.PartyId!.Value, v.VoucherNumber, v.VoucherDateUtc, v.Amount, v.Status, v.ReversedAtUtc })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<PartyEntryRow>();
        foreach (var s in sales)
        {
            var (type, text, debit) = s.ReturnOfInvoiceId is not null ? ("SalesReturn", "مرتجع مبيعات / Sales return", false)
                : s.ReversesInvoiceId is not null ? ("SalesReversal", "عكس فاتورة مبيعات / Sales reversal", false) : ("SalesInvoice", "فاتورة مبيعات آجلة / Credit sale", true);
            rows.Add(new(s.CustomerId, new(s.InvoiceDateUtc, type, s.Id, s.InvoiceNumber, text, debit ? s.GrandTotal : 0m, debit ? 0m : s.GrandTotal)));
        }
        foreach (var r in receipts)
        {
            rows.Add(new(r.PartyId, new(r.VoucherDateUtc, "ReceiptVoucher", r.Id, r.VoucherNumber, "سند قبض / Receipt voucher", 0m, r.Amount)));
            if (r.Status == VoucherReversed && r.ReversedAtUtc is { } reversed && reversed < end)
                rows.Add(new(r.PartyId, new(reversed, "ReceiptVoucherReversal", r.Id, r.VoucherNumber, "عكس سند قبض / Receipt reversal", r.Amount, 0m)));
        }
        rows.AddRange(await CrossCurrencyEntriesAsync(scope, parties, currency, end, VoucherKind.Receipt, cancellationToken).ConfigureAwait(false));
        return rows;
    }

    private async Task<List<PartyEntryRow>> SupplierEntriesAsync(LedgerReportScope scope, List<Guid> parties, string currency, DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var purchases = await PurchasesInScope(scope).Where(i => parties.Contains(i.SupplierId) && i.PaymentMethod == CreditPayment && i.CurrencyCode == currency
                && i.JournalEntryId != null && i.InvoiceDateUtc < end)
            .Select(i => new { i.Id, i.SupplierId, i.InvoiceNumber, i.InvoiceDateUtc, i.GrandTotal, i.ReturnOfInvoiceId, i.ReversesInvoiceId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var payments = await VouchersInScope(scope).Where(v => v.Kind == PaymentKind && v.CurrencyCode == currency && v.JournalEntryId != null
                && v.VoucherDateUtc < end)
            .SelectMany(v => v.Lines.Where(l => l.PartyId != null && parties.Contains(l.PartyId.Value)),
                (v, l) => new { v.Id, PartyId = l.PartyId!.Value, v.VoucherNumber, v.VoucherDateUtc, l.Amount, v.Status, v.ReversedAtUtc })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var partyPayments = await VouchersInScope(scope).Where(v => v.Kind == PaymentKind && v.PartyId != null && !v.Lines.Any()
                && parties.Contains(v.PartyId.Value) && v.CurrencyCode == currency && v.VoucherDateUtc < end)
            .Select(v => new { v.Id, PartyId = v.PartyId!.Value, v.VoucherNumber, v.VoucherDateUtc, v.Amount, v.Status, v.ReversedAtUtc })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<PartyEntryRow>();
        foreach (var payment in partyPayments)
        {
            rows.Add(new(payment.PartyId, new(payment.VoucherDateUtc, "PaymentVoucher", payment.Id, payment.VoucherNumber,
                "سند دفع / Payment voucher", payment.Amount, 0m)));
            if (payment.Status == VoucherReversed && payment.ReversedAtUtc is { } reversed && reversed < end)
                rows.Add(new(payment.PartyId, new(reversed, "PaymentVoucherReversal", payment.Id, payment.VoucherNumber,
                    "عكس سند دفع / Payment reversal", 0m, payment.Amount)));
        }
        foreach (var p in purchases)
        {
            // A return and a reversal both reduce what the supplier is owed; only an ordinary purchase invoice adds to it.
            var isReturn = p.ReturnOfInvoiceId is not null;
            var isReversal = p.ReversesInvoiceId is not null;
            rows.Add(new(p.SupplierId, new(p.InvoiceDateUtc, isReturn ? "PurchaseReturn" : isReversal ? "PurchaseReversal" : "PurchaseInvoice", p.Id, p.InvoiceNumber,
                isReturn ? "مرتجع مشتريات / Purchase return" : isReversal ? "عكس فاتورة مشتريات / Purchase invoice reversal" : "فاتورة مشتريات آجلة / Credit purchase",
                isReturn || isReversal ? p.GrandTotal : 0m, isReturn || isReversal ? 0m : p.GrandTotal)));
        }
        foreach (var payment in payments)
        {
            rows.Add(new(payment.PartyId, new(payment.VoucherDateUtc, "PaymentVoucher", payment.Id, payment.VoucherNumber, "سند دفع / Payment voucher", payment.Amount, 0m)));
            if (payment.Status == VoucherReversed && payment.ReversedAtUtc is { } reversed && reversed < end)
                rows.Add(new(payment.PartyId, new(reversed, "PaymentVoucherReversal", payment.Id, payment.VoucherNumber, "عكس سند دفع / Payment reversal", 0m, payment.Amount)));
        }
        rows.AddRange(await CrossCurrencyEntriesAsync(scope, parties, currency, end, VoucherKind.Payment, cancellationToken).ConfigureAwait(false));
        return rows;
    }

    /// <summary>A cross-currency allocation transfers the party amount from settlement currency to invoice currency; same-currency allocations have no nominal net effect.</summary>
    private async Task<List<PartyEntryRow>> CrossCurrencyEntriesAsync(LedgerReportScope scope, List<Guid> parties, string currency,
        DateTimeOffset end, VoucherKind kind, CancellationToken cancellationToken)
    {
        var vouchers = VouchersInScope(scope).Where(v => v.Kind == (byte)kind && v.PartyId != null && parties.Contains(v.PartyId.Value));
        var applications = await (from a in context.SettlementApplications.AsNoTracking()
            join v in vouchers on a.VoucherId equals v.Id
            where a.Batch!.CompanyId == scope.CompanyId && a.Batch.DateUtc < end
                && (a.Batch.State == 1 || a.Batch.State == 2) && a.InvoiceCurrency != a.SettlementCurrency
                && (a.InvoiceCurrency == currency || a.SettlementCurrency == currency)
            select new { v.Id, PartyId = v.PartyId!.Value, v.VoucherNumber, a.Batch!.DateUtc, a.Batch.ReversedAtUtc,
                a.InvoiceCurrency, a.SettlementCurrency, a.InvoiceAmount, a.SettlementAmount })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<PartyEntryRow>();
        foreach (var a in applications)
        {
            var amount = a.SettlementCurrency == currency ? a.SettlementAmount : a.InvoiceAmount;
            var debit = kind == VoucherKind.Receipt ? a.SettlementCurrency == currency : a.InvoiceCurrency == currency;
            var type = kind == VoucherKind.Receipt ? "ReceiptVoucherSettlement" : "PaymentVoucherSettlement";
            rows.Add(new(a.PartyId, new(a.DateUtc, type, a.Id, a.VoucherNumber, "تخصيص بعملة مختلفة / Cross-currency settlement",
                debit ? amount : 0m, debit ? 0m : amount)));
            if (a.ReversedAtUtc is { } reversed && reversed < end)
                rows.Add(new(a.PartyId, new(reversed, type + "Reversal", a.Id, a.VoucherNumber, "عكس تخصيص / Settlement reversal",
                    debit ? 0m : amount, debit ? amount : 0m)));
        }
        return rows;
    }

    private sealed record PaymentLine(Guid PartyId, decimal Amount);

    /// <summary>Supplier payment lines effective at the cut-off: posted before it and not reversed before it.</summary>
    // The party filter runs on the row, before projection: a predicate on the constructed record cannot be translated to SQL.
    private IQueryable<PaymentLine> PaymentLinesInScope(LedgerReportScope scope, string currency, DateTimeOffset end, Guid? partyId) =>
        VouchersInScope(scope).Where(v => v.Kind == PaymentKind && v.CurrencyCode == currency && v.JournalEntryId != null && v.VoucherDateUtc < end
                && !(v.Status == VoucherReversed && v.ReversedAtUtc < end))
            .SelectMany(v => v.Lines.Where(l => l.PartyId != null && (partyId == null || l.PartyId == partyId)), (v, l) => new PaymentLine(l.PartyId!.Value, l.Amount));

    private IQueryable<Sales.SalesInvoiceRow> SalesInScope(LedgerReportScope scope)
    {
        var rows = context.SalesInvoices.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId
            && context.JournalEntries.Any(j => j.Id == i.JournalEntryId && j.CompanyId == scope.CompanyId && PostedStates.Contains(j.State)));
        return scope.Consolidated ? rows : rows.Where(i => i.BranchId == scope.BranchId);
    }

    private IQueryable<Purchases.PurchaseInvoiceRow> PurchasesInScope(LedgerReportScope scope)
    {
        var rows = context.PurchaseInvoices.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId
            && context.JournalEntries.Any(j => j.Id == i.JournalEntryId && j.CompanyId == scope.CompanyId && PostedStates.Contains(j.State)));
        return scope.Consolidated ? rows : rows.Where(i => i.BranchId == scope.BranchId);
    }

    private IQueryable<Treasury.VoucherRow> VouchersInScope(LedgerReportScope scope)
    {
        var rows = context.Vouchers.AsNoTracking().Where(v => v.CompanyId == scope.CompanyId
            && context.JournalEntries.Any(j => j.Id == v.JournalEntryId && j.CompanyId == scope.CompanyId && PostedStates.Contains(j.State)));
        return scope.Consolidated ? rows : rows.Where(v => v.BranchId == scope.BranchId);
    }

    private static bool RoleMatches(int role, PartyStatementKind kind) =>
        role == 3 || (kind == PartyStatementKind.Customer ? role == 1 : role == 2);

    private static void RequireStatementKind(PartyStatementKind kind)
    {
        if (kind is not (PartyStatementKind.Customer or PartyStatementKind.Supplier))
            throw new RequestValidationException("reports.party_role_invalid", "نوع كشف الحساب غير صالح / Invalid customer/supplier statement kind.", "kind");
    }

    private static string RequireCurrency(string? currencyCode)
    {
        var code = currencyCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
            throw new RequestValidationException("reports.currency_invalid", "A three-letter currency code is required.", "currencyCode");
        return code;
    }
}
