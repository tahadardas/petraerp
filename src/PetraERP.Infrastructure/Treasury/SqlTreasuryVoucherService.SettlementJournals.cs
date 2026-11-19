using PetraERP.Application.GeneralLedger;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Treasury;

namespace PetraERP.Infrastructure.Treasury;

public sealed partial class SqlTreasuryVoucherService
{
    private static JournalDraftLineInput MoneyLine(Guid account, string currency, string baseCurrency, decimal rate, decimal amount, bool debit, string description, Guid? center=null)
        => new() { AccountId=account,CostCenterId=center,TransactionCurrencyCode=currency,RateSourceCurrencyCode=currency,RateTargetCurrencyCode=baseCurrency,
            RateValue=rate,TransactionDebit=debit?amount:0m,TransactionCredit=debit?0m:amount,Description=description };

    private static void ControlLeg(List<JournalDraftLineInput> lines, Guid account, string currency, string baseCurrency, decimal rate,
        decimal amount, decimal bookedBase, bool debit, string label)
    {
        lines.Add(MoneyLine(account,currency,baseCurrency,rate,amount,debit,label));
        var correction=bookedBase-DocumentCurrencyPolicy.ToBase(amount,rate);
        if (correction!=0)
            lines.Add(MoneyLine(account,baseCurrency,baseCurrency,1m,Math.Abs(correction),correction>0?debit:!debit,"تقريب ذمم / Control rounding"));
    }

    private static void ExchangeDifference(List<JournalDraftLineInput> lines, PatternConfiguration config, string baseCurrency, decimal gain)
    {
        if (gain==0) return;
        var role=gain>0?PatternAccountRoles.FxGain:PatternAccountRoles.FxLoss;
        var mapping=config.AccountMappings.FirstOrDefault(m=>string.Equals(PatternAccountRoles.Normalize(m.Role),role,StringComparison.Ordinal))
            ?? throw SettlementError("fx_mapping_missing","اربط حساب فرق ربح أو خسارة الصرف في النمط","Map the realized FX gain/loss account on the voucher pattern.");
        lines.Add(MoneyLine(mapping.AccountId,baseCurrency,baseCurrency,1m,Math.Abs(gain),gain<0,gain>0?"ربح فرق صرف محقق":"خسارة فرق صرف محققة"));
    }

    private static List<JournalDraftLineInput> BuildVoucherJournal(VoucherRow voucher,VoucherValidation validation)
    {
        var lines=new List<JournalDraftLineInput>();
        var receipt=voucher.Kind==(byte)VoucherKind.Receipt;
        var paired=validation.ReceivableAccountId is null && validation.Configuration.EntryPolicy.PairedVoucherLines;
        if (!paired) lines.Add(MoneyLine(validation.SettlementAccountId,voucher.CurrencyCode,validation.BaseCurrency,voucher.ExchangeRate,voucher.Amount,receipt,receipt?"مقبوضات":"مدفوعات"));
        if (validation.ReceivableAccountId is not { } partyAccount)
        {
            decimal debited=0;
            var rows=voucher.Lines.OrderBy(l=>l.DisplayOrder).ToArray();
            for(var i=0;i<rows.Length;i++)
            {
                // Al-Ameen shape (pattern option): the main account is booked once per row, right after the row's own line.
                if (paired) lines.Add(MoneyLine(validation.SettlementAccountId,voucher.CurrencyCode,validation.BaseCurrency,voucher.ExchangeRate,rows[i].Amount,receipt,receipt?"مقبوضات":"مدفوعات"));
                lines.Add(MoneyLine(validation.LineTargets[i],voucher.CurrencyCode,validation.BaseCurrency,voucher.ExchangeRate,rows[i].Amount,true,rows[i].Description??"مدفوعات",rows[i].CostCenterId));
                debited+=DocumentCurrencyPolicy.ToBase(rows[i].Amount,voucher.ExchangeRate);
            }
            ExchangeDifference(lines,validation.Configuration,validation.BaseCurrency,debited-DocumentCurrencyPolicy.ToBase(voucher.Amount,voucher.ExchangeRate));
            return lines;
        }
        foreach(var p in validation.Prepared)
            ControlLeg(lines,p.Invoice.AccountId,p.Invoice.CurrencyCode,validation.BaseCurrency,p.Invoice.ExchangeRate,p.Input.InvoiceAmount,p.BookedBase,!receipt,"تسوية ذمم / Settlement control");
        var advanceBase=DocumentCurrencyPolicy.ToBase(voucher.Amount,voucher.ExchangeRate)-validation.Prepared.Sum(p=>p.SettledBase);
        if(validation.Advance>0)
        {
            if(advanceBase<=0) throw SettlementError("settlement_precision","الدفعة المقدمة أصغر من دقة الأساس","Advance is below base-currency precision.");
            ControlLeg(lines,partyAccount,voucher.CurrencyCode,validation.BaseCurrency,voucher.ExchangeRate,validation.Advance,advanceBase,!receipt,"دفعة مقدمة / Party advance");
        }
        var carrying=validation.Prepared.Sum(p=>p.BookedBase)+advanceBase;
        var cash=DocumentCurrencyPolicy.ToBase(voucher.Amount,voucher.ExchangeRate);
        ExchangeDifference(lines,validation.Configuration,validation.BaseCurrency,receipt?cash-carrying:carrying-cash);
        return lines;
    }

    private static List<JournalDraftLineInput> BuildAdvanceJournal(VoucherRow voucher,VoucherValidation validation,IReadOnlyList<PreparedSettlement> prepared,Guid sourceAccount)
    {
        var lines=new List<JournalDraftLineInput>();var receipt=voucher.Kind==(byte)VoucherKind.Receipt;
        foreach(var p in prepared)
        {
            ControlLeg(lines,sourceAccount,voucher.CurrencyCode,validation.BaseCurrency,voucher.ExchangeRate,p.Input.SettlementAmount,p.SettledBase,receipt,"تطبيق دفعة مقدمة / Advance release");
            ControlLeg(lines,p.Invoice.AccountId,p.Invoice.CurrencyCode,validation.BaseCurrency,p.Invoice.ExchangeRate,p.Input.InvoiceAmount,p.BookedBase,!receipt,"تسوية ذمم / Settlement control");
        }
        var settled=prepared.Sum(p=>p.SettledBase);var booked=prepared.Sum(p=>p.BookedBase);
        ExchangeDifference(lines,validation.Configuration,validation.BaseCurrency,receipt?settled-booked:booked-settled);
        return lines;
    }
}
