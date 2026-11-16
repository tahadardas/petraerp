using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports.Professional;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Parties.OpenItems;

namespace PetraERP.Infrastructure.Reports.Professional;

public sealed partial class SqlProfessionalReportService
{
    public async Task<IReadOnlyList<string>> GetPartyActivityCurrenciesAsync(LedgerReportScope scope, Guid fiscalYearId, Guid partyId,
        ProfessionalReportKind kind, DateOnly asOfDate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Consolidated || !ProfessionalReportCatalog.IsPartyKind(kind))
            throw new RequestValidationException("reporting.parameters_invalid", "A branch and a party statement kind are required.", "kind");
        await RequireAsync(scope, PermissionCatalog.ReportsView, cancellationToken).ConfigureAwait(false);
        await RequireAsync(scope, PermissionCatalog.JournalsView, cancellationToken).ConfigureAwait(false);
        await RequireAsync(scope, PermissionCatalog.PartiesView, cancellationToken).ConfigureAwait(false);
        if (kind == ProfessionalReportKind.CombinedPartyStatement)
        {
            // Activity currencies of the combined statement are the union of the party's customer-side and supplier-side currencies.
            var partyRole = (await context.Parties.AsNoTracking().Where(p => p.Id == partyId && p.CompanyId == scope.CompanyId).Select(p => (int?)p.Role)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)) ?? throw new ResourceNotFoundException("party");
            var union = new List<string>();
            if (partyRole is 1 or 3) union.AddRange(await GetPartyActivityCurrenciesAsync(scope, fiscalYearId, partyId, ProfessionalReportKind.CustomerStatement, asOfDate, cancellationToken).ConfigureAwait(false));
            if (partyRole is 2 or 3) union.AddRange(await GetPartyActivityCurrenciesAsync(scope, fiscalYearId, partyId, ProfessionalReportKind.SupplierStatement, asOfDate, cancellationToken).ConfigureAwait(false));
            return union.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToArray();
        }
        var supplier = kind == ProfessionalReportKind.SupplierStatement;
        await RequireAsync(scope, supplier ? PermissionCatalog.PurchaseInvoicesView : PermissionCatalog.SalesInvoicesView, cancellationToken).ConfigureAwait(false);
        await RequireAsync(scope, PermissionCatalog.TreasuryVouchersView, cancellationToken).ConfigureAwait(false);
        var year = await context.FiscalYears.AsNoTracking().SingleOrDefaultAsync(y => y.Id == fiscalYearId && y.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("fiscal year");
        if (asOfDate < year.RangeStart || asOfDate > year.RangeEnd)
            throw new RequestValidationException("reporting.year_range_invalid", "The end date must be within the fiscal year.", "to");
        var party = await context.Parties.AsNoTracking().SingleOrDefaultAsync(p => p.Id == partyId && p.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("party");
        if (party.Role != 3 && party.Role != (supplier ? 2 : 1))
            throw new RequestValidationException("reporting.party_role_invalid", "The party does not have the requested statement role.", "partyId");
        var end = End(asOfDate);
        var posted = context.JournalEntries.AsNoTracking().Where(j => j.CompanyId == scope.CompanyId && j.BranchId == scope.BranchId && PostedStates.Contains(j.State));
        var invoices = supplier
            ? await context.PurchaseInvoices.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId && i.BranchId == scope.BranchId && i.SupplierId == partyId
                && i.PaymentMethod == 1 && i.InvoiceDateUtc < end && posted.Any(j => j.Id == i.JournalEntryId)).Select(i => i.CurrencyCode).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false)
            : await context.SalesInvoices.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId && i.BranchId == scope.BranchId && i.CustomerId == partyId
                && i.PaymentMethod == 1 && i.InvoiceDateUtc < end && posted.Any(j => j.Id == i.JournalEntryId)).Select(i => i.CurrencyCode).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        var voucherKind = supplier ? (byte)2 : (byte)1;
        var vouchers = await context.Vouchers.AsNoTracking().Where(v => v.CompanyId == scope.CompanyId && v.BranchId == scope.BranchId && v.Kind == voucherKind
            && v.VoucherDateUtc < end && posted.Any(j => j.Id == v.JournalEntryId)
            && ((v.PartyId == partyId && !v.Lines.Any()) || v.Lines.Any(l => l.PartyId == partyId)))
            .Select(v => v.CurrencyCode).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        var settlements = context.SettlementApplications.AsNoTracking().Where(a => a.Batch!.CompanyId == scope.CompanyId && a.Batch.BranchId == scope.BranchId
            && (a.Batch.State == 1 || a.Batch.State == 2) && a.Batch.DateUtc < end && a.Voucher!.PartyId == partyId && a.Voucher.Kind == voucherKind
            && posted.Any(j => j.Id == a.Voucher.JournalEntryId) && a.InvoiceCurrency != a.SettlementCurrency);
        var invoiceCurrencies = await settlements.Select(a => a.InvoiceCurrency).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        var settlementCurrencies = await settlements.Select(a => a.SettlementCurrency).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        var partyType = supplier ? (byte)OpenItemPartyType.Supplier : (byte)OpenItemPartyType.Customer;
        var openings = await context.OpenItems.AsNoTracking().Where(o => o.CompanyId == scope.CompanyId && o.BranchId == scope.BranchId && o.PartyId == partyId
            && o.PartyType == partyType && o.OriginType != (byte)OpenItemOriginType.CurrentYearInvoice && o.State != (byte)OpenItemState.Cancelled
            && o.OriginDocumentDateUtc < end).Select(o => o.CurrencyCode).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        // Columns use different SQL collations; merge only distinct codes after each server-side query.
        return invoices.Concat(vouchers).Concat(invoiceCurrencies).Concat(settlementCurrencies).Concat(openings)
            .Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToArray();
    }
}
