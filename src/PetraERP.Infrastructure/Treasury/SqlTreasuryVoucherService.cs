using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Patterns;
using PetraERP.Application.Treasury;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Treasury;

public sealed partial class SqlTreasuryVoucherService(FinancialDbContext context, IPatternService patternService, TimeProvider timeProvider)
    : ITreasuryVoucherService
{
    private const int MaxDetailRows = 200;

    public async Task<VoucherPageResult> ListAsync(Guid companyId, Guid branchId, VoucherSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Page < 1 || query.PageSize is < 1 or > 200 || (long)(query.Page - 1) * query.PageSize > int.MaxValue)
            throw new RequestValidationException("treasury.page", "Page must be positive and pageSize between 1 and 200.", "page");
        var rows = context.Vouchers.AsNoTracking().Where(v => v.CompanyId == companyId && v.BranchId == branchId);
        if (query.Kind is { } kind) rows = rows.Where(v => v.Kind == (byte)kind);
        if (query.PartyId is { } party) rows = rows.Where(v => v.PartyId == party || v.Lines.Any(l => l.PartyId == party));
        if (query.Status is { } status) rows = rows.Where(v => v.Status == (byte)status);
        if (query.FromDateUtc is { } from) rows = rows.Where(v => v.VoucherDateUtc >= from);
        if (query.ToDateUtc is { } to) rows = rows.Where(v => v.VoucherDateUtc <= to);
        if (query.SalesInvoiceId is { } invoice) rows = rows.Where(v => v.Allocations.Any(a => a.SalesInvoiceId == invoice) || v.AllocationPlans.Any(p => p.SalesInvoiceId == invoice) || context.SettlementApplications.Any(a => a.VoucherId == v.Id && a.SalesInvoiceId == invoice));
        if (query.JournalEntryId is { } journal) rows = rows.Where(v => v.JournalEntryId == journal || v.ReversalJournalEntryId == journal);
        var total = await rows.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await rows.OrderByDescending(v => v.VoucherDateUtc).ThenBy(v => v.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(v => new VoucherSummaryResult(v.Id, (VoucherKind)v.Kind, v.VoucherNumber, v.VoucherDateUtc, v.PartyId,
                v.Party == null ? null : v.Party.NameArabic, v.CurrencyCode, v.Amount, (VoucherStatus)v.Status, v.JournalEntryId, v.RowVersion))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new(query.Page, query.PageSize, total, items);
    }

    public async Task<VoucherDetailResult> GetAsync(Guid companyId, Guid branchId, Guid voucherId, CancellationToken cancellationToken = default)
    {
        var v = await context.Vouchers.AsNoTracking()
            .Include(r => r.PatternRevision!).ThenInclude(r => r.Pattern)
            .Include(r => r.Party)
            .Include(r => r.Allocations).ThenInclude(a => a.Invoice)
            .Include(r => r.Lines).Include(r => r.AllocationPlans)
            .AsSplitQuery()
            .SingleOrDefaultAsync(r => r.Id == voucherId && r.CompanyId == companyId && r.BranchId == branchId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("Voucher");
        var partyIds = v.Lines.Where(l => l.PartyId != null).Select(l => l.PartyId!.Value).ToList();
        var accountIds = v.Lines.Where(l => l.AccountId != null).Select(l => l.AccountId!.Value).ToList();
        var parties = await context.Parties.AsNoTracking().Where(p => p.CompanyId == companyId && partyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.NameArabic, cancellationToken).ConfigureAwait(false);
        var accounts = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Code, cancellationToken).ConfigureAwait(false);
        var allocated = v.Allocations.Sum(a => a.Amount);
        var detail = new VoucherDetailResult(v.Id, v.CompanyId, v.BranchId, (VoucherKind)v.Kind, v.PatternRevisionId,
            v.PatternRevision?.Pattern?.Code ?? string.Empty, v.PatternRevision?.Pattern?.NameArabic ?? string.Empty,
            v.PartyId, v.Party?.Code, v.Party?.NameArabic, (SettlementMethod)v.SettlementMethod, v.VoucherNumber, v.VoucherDateUtc,
            v.CurrencyCode, v.ExchangeRate, v.Amount, allocated, v.Kind == (byte)VoucherKind.Receipt ? v.Amount - allocated : 0m,
            (VoucherStatus)v.Status, v.Notes, v.JournalEntryId, v.ReversalJournalEntryId, v.ReversalReason, v.PostedAtUtc, v.ReversedAtUtc,
            v.RowVersion,
            v.Allocations.OrderBy(a => a.Invoice!.InvoiceDateUtc).ThenBy(a => a.Id)
                .Select(a => new VoucherAllocationResult(a.Id, a.SalesInvoiceId, a.Invoice?.InvoiceNumber, a.Invoice?.InvoiceDateUtc ?? default, a.Amount)).ToList(),
            v.Lines.OrderBy(l => l.DisplayOrder).ThenBy(l => l.Id).Select(l => new VoucherLineResult(l.Id, l.PartyId,
                l.PartyId is { } p ? parties.GetValueOrDefault(p) : null, l.AccountId, l.AccountId is { } a ? accounts.GetValueOrDefault(a) : null,
                l.CostCenterId, l.Amount, l.Description, l.DisplayOrder)).ToList());
        var actorIds = new[] { v.CreatedByUserId, v.PostedByUserId ?? Guid.Empty, v.ReversedByUserId ?? Guid.Empty }.Where(id => id != Guid.Empty);
        var actors = await context.ActorNamesAsync(actorIds, cancellationToken).ConfigureAwait(false);
        string? NameOf(Guid? id) => id is { } actor ? actors.GetValueOrDefault(actor)?.DisplayNameArabic : null;
        detail = detail with { SourceNumber = v.SourceNumber, SourceDate = v.SourceDate, Audit = new VoucherAuditResult(v.CreatedAtUtc, NameOf(v.CreatedByUserId), v.UpdatedAtUtc,
            NameOf(v.PostedByUserId), v.PostedAtUtc, NameOf(v.ReversedByUserId), v.ReversedAtUtc) };
        return await EnrichSettlementsAsync(detail,v,cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutstandingInvoiceResult>> GetOutstandingInvoicesAsync(Guid companyId, Guid branchId, Guid customerId,
        string currencyCode, CancellationToken cancellationToken = default)
    {
        if (!await context.Parties.AnyAsync(p => p.Id == customerId && p.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
            throw new ResourceNotFoundException("Customer");
        var rows = await SettlementQueries.LoadInvoicesAsync(context,companyId,branchId,VoucherKind.Receipt,customerId,currencyCode,null,cancellationToken).ConfigureAwait(false);
        return rows.Where(i=>i.Outstanding>0).OrderBy(i=>i.DateUtc).Take(MaxDetailRows)
            .Select(i=>new OutstandingInvoiceResult(i.Id,i.Number,i.DateUtc,i.CurrencyCode,i.Total,i.Returned,i.Paid,i.Outstanding)).ToArray();
    }

}
