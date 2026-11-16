using Microsoft.EntityFrameworkCore;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.FinancialValues;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Parties;

internal sealed record PartyFinancialBalance(decimal Receivable, decimal Payable)
{
    public decimal ReceivableDebit { get; init; }
    public decimal ReceivableCredit { get; init; }
    public decimal PayableDebit { get; init; }
    public decimal PayableCredit { get; init; }
}

/// <summary>
/// Party balances derive from approved source documents and their posted control effects, never from the total of a shared
/// account. Canonical source line provenance identifies historic control accounts even after a party mapping changes.
/// Manual journals have no party dimension: they remain unassigned in the GL and must be reconciled separately, never
/// silently spread among parties. Receivable and payable are intentionally separate, including a party with Both roles.
/// </summary>
internal static class PartyFinancialFacts
{
    private static readonly byte[] PostedStates = [(byte)JournalEntryState.Posted, (byte)JournalEntryState.Reversed];
    private static readonly string[] SalesControlLabels = ["ذمم/نقد البيع", "تخفيض ذمة الزبون بالمرتجع"];
    private static readonly string[] TreasuryControlLabels =
    [
        "تسوية ذمم / Settlement control", "دفعة مقدمة / Party advance", "تطبيق دفعة مقدمة / Advance release",
        "تقريب ذمم / Control rounding", "تحصيل فواتير مخصصة", "دفعة مقدمة من الزبون",
    ];

    private sealed record Effect(Guid PartyId, bool Receivable, decimal Debit, decimal Credit);

    internal static async Task<Dictionary<Guid, PartyFinancialBalance>> BaseBalancesAsync(FinancialDbContext context, Guid companyId,
        IReadOnlyCollection<Guid> partyIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(partyIds);
        if (partyIds.Count == 0) return [];
        var ids = partyIds.Distinct().ToList();
        var lines = context.JournalLines.AsNoTracking().Where(l => l.CompanyId == companyId && l.Journal!.CompanyId == companyId
            && PostedStates.Contains(l.Journal.State));
        var effects = await (from invoice in context.SalesInvoices.AsNoTracking()
            join line in lines on invoice.JournalEntryId equals (Guid?)line.JournalId
            where invoice.CompanyId == companyId && ids.Contains(invoice.CustomerId) && invoice.PaymentMethod == 1
                && (SalesControlLabels.Contains(line.Description) || context.JournalLines.Any(original => original.Id == line.ReversesLineId
                    && original.CompanyId == companyId && SalesControlLabels.Contains(original.Description)))
            select new Effect(invoice.CustomerId, true, line.BaseDebit, line.BaseCredit))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        effects.AddRange(await (from invoice in context.PurchaseInvoices.AsNoTracking()
            join line in lines on invoice.JournalEntryId equals (Guid?)line.JournalId
            where invoice.CompanyId == companyId && ids.Contains(invoice.SupplierId) && invoice.PaymentMethod == 1
                && (line.Description == "ذمة المورد" || context.JournalLines.Any(original => original.Id == line.ReversesLineId
                    && original.CompanyId == companyId && original.Description == "ذمة المورد"))
            select new Effect(invoice.SupplierId, false, line.BaseDebit, line.BaseCredit))
            .ToListAsync(cancellationToken).ConfigureAwait(false));

        var vouchers = context.Vouchers.AsNoTracking().Where(v => v.CompanyId == companyId && v.PartyId != null
            && ids.Contains(v.PartyId.Value) && !v.Lines.Any());
        effects.AddRange(await (from voucher in vouchers
            join line in lines on voucher.JournalEntryId equals (Guid?)line.JournalId
            where TreasuryControlLabels.Contains(line.Description)
            select new Effect(voucher.PartyId!.Value, voucher.Kind == 1, line.BaseDebit, line.BaseCredit))
            .ToListAsync(cancellationToken).ConfigureAwait(false));
        effects.AddRange(await (from voucher in vouchers
            join line in lines on voucher.ReversalJournalEntryId equals (Guid?)line.JournalId
            where context.JournalLines.Any(original => original.Id == line.ReversesLineId && original.CompanyId == companyId
                && TreasuryControlLabels.Contains(original.Description))
            select new Effect(voucher.PartyId!.Value, voucher.Kind == 1, line.BaseDebit, line.BaseCredit))
            .ToListAsync(cancellationToken).ConfigureAwait(false));

        // Initial applications are part of the voucher journal above. Later batches have their own linked journal/reversal.
        var batches = from batch in context.SettlementBatches.AsNoTracking()
            join voucher in vouchers on batch.VoucherId equals voucher.Id
            where batch.CompanyId == companyId && !batch.IsInitial
            select new { batch.JournalId, batch.ReversalJournalId, PartyId = voucher.PartyId!.Value, voucher.Kind };
        effects.AddRange(await (from batch in batches
            join line in lines on batch.JournalId equals (Guid?)line.JournalId
            where TreasuryControlLabels.Contains(line.Description)
            select new Effect(batch.PartyId, batch.Kind == 1, line.BaseDebit, line.BaseCredit))
            .ToListAsync(cancellationToken).ConfigureAwait(false));
        effects.AddRange(await (from batch in batches
            join line in lines on batch.ReversalJournalId equals (Guid?)line.JournalId
            where context.JournalLines.Any(original => original.Id == line.ReversesLineId && original.CompanyId == companyId
                && TreasuryControlLabels.Contains(original.Description))
            select new Effect(batch.PartyId, batch.Kind == 1, line.BaseDebit, line.BaseCredit))
            .ToListAsync(cancellationToken).ConfigureAwait(false));

        // Old distribution vouchers carry a party per source line, not per journal header. Their immutable source line
        // and frozen voucher rate reproduce that exact posted debit; no current party account is used to infer ownership.
        var distributions = await context.Vouchers.AsNoTracking().Where(v => v.CompanyId == companyId && v.Kind == 2
                && context.JournalEntries.Any(j => j.Id == v.JournalEntryId && j.CompanyId == companyId && PostedStates.Contains(j.State)))
            .SelectMany(v => v.Lines.Where(l => l.PartyId != null && ids.Contains(l.PartyId.Value)),
                (v, line) => new { PartyId = line.PartyId!.Value, line.Amount, v.ExchangeRate, v.ReversalJournalEntryId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var reversalIds = distributions.Where(d => d.ReversalJournalEntryId != null).Select(d => d.ReversalJournalEntryId!.Value).Distinct().ToList();
        var postedReversals = await context.JournalEntries.AsNoTracking().Where(j => j.CompanyId == companyId && reversalIds.Contains(j.Id)
                && PostedStates.Contains(j.State)).Select(j => j.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var d in distributions)
        {
            var amount = DocumentCurrencyPolicy.ToBase(d.Amount, d.ExchangeRate);
            effects.Add(new(d.PartyId, false, amount, 0m));
            if (d.ReversalJournalEntryId is { } reversal && postedReversals.Contains(reversal))
                effects.Add(new(d.PartyId, false, 0m, amount));
        }
        return ids.ToDictionary(id => id, id =>
        {
            var party = effects.Where(e => e.PartyId == id).ToList();
            var recDebit = party.Where(e => e.Receivable).Sum(e => e.Debit);
            var recCredit = party.Where(e => e.Receivable).Sum(e => e.Credit);
            var payDebit = party.Where(e => !e.Receivable).Sum(e => e.Debit);
            var payCredit = party.Where(e => !e.Receivable).Sum(e => e.Credit);
            return new PartyFinancialBalance(recDebit - recCredit, payCredit - payDebit)
            { ReceivableDebit = recDebit, ReceivableCredit = recCredit, PayableDebit = payDebit, PayableCredit = payCredit };
        });
    }
}
