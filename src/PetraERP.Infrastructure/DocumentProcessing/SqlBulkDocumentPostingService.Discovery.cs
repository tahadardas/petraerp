using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.DocumentProcessing;
using PetraERP.Application.Security;
using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Purchases;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.DocumentProcessing;

public sealed partial class SqlBulkDocumentPostingService
{
    /// <summary>
    /// Ceiling on the documents one bulk-posting request may queue. Filters are applied server-side, so narrowing the period, the
    /// document types or the search is always available; the number exists so the page answers with a reason instead of hanging.
    /// </summary>
    private const int DiscoveryLimit = 20_000;

    private sealed record Candidate(
        BulkPostingDocumentType DocumentType,
        Guid DocumentId,
        string? DocumentNumber,
        DateTimeOffset DocumentDateUtc,
        string? PartyName,
        decimal? Amount,
        string? CurrencyCode,
        decimal ExchangeRate,
        int Status,
        string StatusName,
        byte[] RowVersion,
        bool Special,
        int Kind = 0,
        bool Missing = false);

    private sealed record PeriodWindow(DateOnly Start, DateOnly End, byte State);

    private sealed record Classified(Candidate Candidate, BulkPostingClassification Classification, bool Postable, string Reason);

    public async Task<BulkPostingPreviewResult> PreviewAsync(Guid companyId, Guid branchId, Guid userId,
        BulkPostingFilter filter, CancellationToken cancellationToken = default)
    {
        ValidateScope(companyId, branchId, userId);
        ArgumentNullException.ThrowIfNull(filter);
        var candidates = await DiscoverAsync(companyId, branchId, filter, cancellationToken).ConfigureAwait(false);
        var classified = await ClassifyAsync(companyId, branchId, userId, candidates, cancellationToken).ConfigureAwait(false);

        var items = classified
            .OrderBy(c => c.Candidate.DocumentDateUtc)
            .ThenBy(c => (int)c.Candidate.DocumentType)
            .ThenBy(c => c.Candidate.DocumentNumber, StringComparer.Ordinal)
            .ThenBy(c => c.Candidate.DocumentId)
            .Select(c => new BulkPostingPreviewItem(
                c.Candidate.DocumentType,
                c.Candidate.DocumentId,
                c.Candidate.DocumentNumber,
                c.Candidate.DocumentDateUtc,
                c.Candidate.PartyName,
                c.Candidate.Amount,
                c.Candidate.CurrencyCode,
                c.Candidate.StatusName,
                c.Classification,
                c.Postable,
                c.Reason,
                Convert.ToBase64String(c.Candidate.RowVersion)))
            .ToArray();

        var classifications = items
            .GroupBy(i => i.Classification)
            .OrderBy(g => (int)g.Key)
            .Select(g => new BulkPostingClassificationCount(g.Key, g.Count()))
            .ToArray();
        var ready = items.Count(i => i.Classification == BulkPostingClassification.ReadyForPosting);
        var approval = items.Count(i => i.Classification == BulkPostingClassification.CanBeApprovedAutomatically);
        return new BulkPostingPreviewResult(items.Length, ready, approval,
            items.Length - ready - approval, classifications, items);
    }

    private async Task<IReadOnlyList<Candidate>> DiscoverAsync(Guid companyId, Guid branchId, BulkPostingFilter filter,
        CancellationToken cancellationToken)
    {
        var types = (filter.DocumentTypes is { Count: > 0 } selected ? selected : [])
            .Distinct().ToHashSet();
        if (types.Count == 0)
        {
            types =
            [
                BulkPostingDocumentType.SalesInvoice, BulkPostingDocumentType.PurchaseInvoice,
                BulkPostingDocumentType.Voucher, BulkPostingDocumentType.Journal,
                BulkPostingDocumentType.InventoryDocument,
            ];
        }

        var search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim();
        var result = new List<Candidate>();        if (types.Contains(BulkPostingDocumentType.SalesInvoice))
            result.AddRange(await DiscoverSalesAsync(companyId, branchId, filter, search, cancellationToken).ConfigureAwait(false));
        if (types.Contains(BulkPostingDocumentType.PurchaseInvoice))
            result.AddRange(await DiscoverPurchasesAsync(companyId, branchId, filter, search, cancellationToken).ConfigureAwait(false));
        if (types.Contains(BulkPostingDocumentType.Voucher))
            result.AddRange(await DiscoverVouchersAsync(companyId, branchId, filter, search, cancellationToken).ConfigureAwait(false));
        if (types.Contains(BulkPostingDocumentType.Journal))
            result.AddRange(await DiscoverJournalsAsync(companyId, branchId, filter, search, cancellationToken).ConfigureAwait(false));
        if (types.Contains(BulkPostingDocumentType.InventoryDocument))
            result.AddRange(await DiscoverInventoryAsync(companyId, branchId, filter, search, cancellationToken).ConfigureAwait(false));
        // Discovery reads at most DiscoveryLimit + 1 rows per type. Past that the queue is not something an operator can review or a
        // request can finish, so the page is told to narrow the filter instead of being handed a silently truncated list.
        if (result.Count > DiscoveryLimit)
            throw new BusinessRuleException("posting.too_many_documents",
                $"عدد المستندات المطابقة يتجاوز {DiscoveryLimit:N0}؛ ضيّق التصفية (الفترة أو نوع المستند أو البحث) ثم أعيد المحاولة. "
                + "/ Too many documents match this filter; narrow the period, document types or search and try again.");
        return result;
    }

    private async Task<IReadOnlyList<Candidate>> ReadRequestedAsync(Guid companyId, Guid branchId,
        IReadOnlyList<BulkPostingDocumentReference> references, CancellationToken cancellationToken)
    {
        var distinct = references.Where(r => r.DocumentId != Guid.Empty).Distinct().ToArray();
        if (distinct.Length == 0) return [];
        var result = new List<Candidate>();
        foreach (var group in distinct.GroupBy(r => r.DocumentType))
        {
            var ids = group.Select(r => r.DocumentId).ToHashSet();
            var rows = group.Key switch
            {
                BulkPostingDocumentType.SalesInvoice => await ReadSalesAsync(companyId, branchId, ids, cancellationToken).ConfigureAwait(false),
                BulkPostingDocumentType.PurchaseInvoice => await ReadPurchasesAsync(companyId, branchId, ids, cancellationToken).ConfigureAwait(false),
                BulkPostingDocumentType.Voucher => await ReadVouchersAsync(companyId, branchId, ids, cancellationToken).ConfigureAwait(false),
                BulkPostingDocumentType.Journal => await ReadJournalsAsync(companyId, branchId, ids, cancellationToken).ConfigureAwait(false),
                BulkPostingDocumentType.InventoryDocument => await ReadInventoryAsync(companyId, branchId, ids, cancellationToken).ConfigureAwait(false),
                _ => [],
            };
            foreach (var id in ids)
            {
                result.Add(rows.FirstOrDefault(r => r.DocumentId == id) ?? new Candidate(group.Key, id, null,
                    time.GetUtcNow(), null, null, null, 1m, -1, "Missing", [], false, Missing: true));
            }
        }
        return result;
    }

    private async Task<IReadOnlyList<Candidate>> DiscoverSalesAsync(Guid companyId, Guid branchId,
        BulkPostingFilter filter, string? search, CancellationToken cancellationToken)
    {
        var query = context.SalesInvoices.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.BranchId == branchId);
        if (filter.FromDateUtc is { } from) query = query.Where(i => i.InvoiceDateUtc >= from);
        if (filter.ToDateUtc is { } to) query = query.Where(i => i.InvoiceDateUtc <= to);
        if (search is not null)
            query = query.Where(i => (i.InvoiceNumber != null && EF.Functions.Like(i.InvoiceNumber, $"%{search}%"))
                || (i.Customer != null && EF.Functions.Like(i.Customer.NameArabic, $"%{search}%")));
        var rows = await query.Take(DiscoveryLimit + 1).Select(i => new
        {
            i.Id, i.InvoiceNumber, i.InvoiceDateUtc, Party = i.Customer == null ? null : i.Customer.NameArabic,
            i.GrandTotal, i.CurrencyCode, i.ExchangeRate, i.Status, i.RowVersion,
            Special = i.ReturnOfInvoiceId != null || i.ReversesInvoiceId != null || i.ReversedByInvoiceId != null,
        }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(i => new Candidate(BulkPostingDocumentType.SalesInvoice, i.Id, i.InvoiceNumber, i.InvoiceDateUtc,
            i.Party, i.GrandTotal, i.CurrencyCode, i.ExchangeRate, i.Status, Name<SalesInvoiceStatus>(i.Status),
            i.RowVersion, i.Special)).ToArray();
    }

    private async Task<IReadOnlyList<Candidate>> ReadSalesAsync(Guid companyId, Guid branchId, HashSet<Guid> ids,
        CancellationToken cancellationToken)
    {
        var rows = await context.SalesInvoices.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.BranchId == branchId && ids.Contains(i.Id))
            .Select(i => new
            {
                i.Id, i.InvoiceNumber, i.InvoiceDateUtc, Party = i.Customer == null ? null : i.Customer.NameArabic,
                i.GrandTotal, i.CurrencyCode, i.ExchangeRate, i.Status, i.RowVersion,
                Special = i.ReturnOfInvoiceId != null || i.ReversesInvoiceId != null || i.ReversedByInvoiceId != null,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(i => new Candidate(BulkPostingDocumentType.SalesInvoice, i.Id, i.InvoiceNumber, i.InvoiceDateUtc,
            i.Party, i.GrandTotal, i.CurrencyCode, i.ExchangeRate, i.Status, Name<SalesInvoiceStatus>(i.Status),
            i.RowVersion, i.Special)).ToArray();
    }

    private async Task<IReadOnlyList<Candidate>> DiscoverPurchasesAsync(Guid companyId, Guid branchId,
        BulkPostingFilter filter, string? search, CancellationToken cancellationToken)
    {
        var query = context.PurchaseInvoices.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.BranchId == branchId);
        if (filter.FromDateUtc is { } from) query = query.Where(i => i.InvoiceDateUtc >= from);
        if (filter.ToDateUtc is { } to) query = query.Where(i => i.InvoiceDateUtc <= to);
        if (search is not null)
            query = query.Where(i => (i.InvoiceNumber != null && EF.Functions.Like(i.InvoiceNumber, $"%{search}%"))
                || (i.SupplierReference != null && EF.Functions.Like(i.SupplierReference, $"%{search}%"))
                || (i.Supplier != null && EF.Functions.Like(i.Supplier.NameArabic, $"%{search}%")));
        var rows = await query.Take(DiscoveryLimit + 1).Select(i => new
        {
            i.Id, i.InvoiceNumber, i.InvoiceDateUtc, Party = i.Supplier == null ? null : i.Supplier.NameArabic,
            i.GrandTotal, i.CurrencyCode, i.ExchangeRate, i.Status, i.RowVersion,
            Special = i.ReturnOfInvoiceId != null,
        }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(i => new Candidate(BulkPostingDocumentType.PurchaseInvoice, i.Id, i.InvoiceNumber, i.InvoiceDateUtc,
            i.Party, i.GrandTotal, i.CurrencyCode, i.ExchangeRate, i.Status, Name<PurchaseInvoiceStatus>(i.Status),
            i.RowVersion, i.Special)).ToArray();
    }

    private async Task<IReadOnlyList<Candidate>> ReadPurchasesAsync(Guid companyId, Guid branchId, HashSet<Guid> ids,
        CancellationToken cancellationToken)
    {
        var rows = await context.PurchaseInvoices.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.BranchId == branchId && ids.Contains(i.Id))
            .Select(i => new
            {
                i.Id, i.InvoiceNumber, i.InvoiceDateUtc, Party = i.Supplier == null ? null : i.Supplier.NameArabic,
                i.GrandTotal, i.CurrencyCode, i.ExchangeRate, i.Status, i.RowVersion,
                Special = i.ReturnOfInvoiceId != null,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(i => new Candidate(BulkPostingDocumentType.PurchaseInvoice, i.Id, i.InvoiceNumber, i.InvoiceDateUtc,
            i.Party, i.GrandTotal, i.CurrencyCode, i.ExchangeRate, i.Status, Name<PurchaseInvoiceStatus>(i.Status),
            i.RowVersion, i.Special)).ToArray();
    }

    private async Task<IReadOnlyList<Candidate>> DiscoverVouchersAsync(Guid companyId, Guid branchId,
        BulkPostingFilter filter, string? search, CancellationToken cancellationToken)
    {
        var query = context.Vouchers.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.BranchId == branchId);
        if (filter.FromDateUtc is { } from) query = query.Where(i => i.VoucherDateUtc >= from);
        if (filter.ToDateUtc is { } to) query = query.Where(i => i.VoucherDateUtc <= to);
        if (search is not null)
            query = query.Where(i => (i.VoucherNumber != null && EF.Functions.Like(i.VoucherNumber, $"%{search}%"))
                || (i.Party != null && EF.Functions.Like(i.Party.NameArabic, $"%{search}%"))
                || (i.Notes != null && EF.Functions.Like(i.Notes, $"%{search}%")));
        var rows = await query.Take(DiscoveryLimit + 1).Select(i => new
        {
            i.Id, i.VoucherNumber, i.VoucherDateUtc, Party = i.Party == null ? null : i.Party.NameArabic,
            i.Amount, i.CurrencyCode, i.ExchangeRate, i.Status, i.RowVersion,
            Special = i.ReversalReason != null && i.Status != (byte)VoucherStatus.Reversed,
        }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(i => new Candidate(BulkPostingDocumentType.Voucher, i.Id, i.VoucherNumber, i.VoucherDateUtc,
            i.Party, i.Amount, i.CurrencyCode, i.ExchangeRate, i.Status, Name<VoucherStatus>(i.Status),
            i.RowVersion, i.Special)).ToArray();
    }

    private async Task<IReadOnlyList<Candidate>> ReadVouchersAsync(Guid companyId, Guid branchId, HashSet<Guid> ids,
        CancellationToken cancellationToken)
    {
        var rows = await context.Vouchers.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.BranchId == branchId && ids.Contains(i.Id))
            .Select(i => new
            {
                i.Id, i.VoucherNumber, i.VoucherDateUtc, Party = i.Party == null ? null : i.Party.NameArabic,
                i.Amount, i.CurrencyCode, i.ExchangeRate, i.Status, i.RowVersion,
                Special = i.ReversalReason != null && i.Status != (byte)VoucherStatus.Reversed,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(i => new Candidate(BulkPostingDocumentType.Voucher, i.Id, i.VoucherNumber, i.VoucherDateUtc,
            i.Party, i.Amount, i.CurrencyCode, i.ExchangeRate, i.Status, Name<VoucherStatus>(i.Status),
            i.RowVersion, i.Special)).ToArray();
    }

    private async Task<IReadOnlyList<Candidate>> DiscoverJournalsAsync(Guid companyId, Guid branchId,
        BulkPostingFilter filter, string? search, CancellationToken cancellationToken)
    {
        var query = context.JournalEntries.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.BranchId == branchId);
        if (filter.FromDateUtc is { } from)
        {
            var day = DateOnly.FromDateTime(from.UtcDateTime);
            query = query.Where(i => i.AccountingDate >= day);
        }
        if (filter.ToDateUtc is { } to)
        {
            var day = DateOnly.FromDateTime(to.UtcDateTime);
            query = query.Where(i => i.AccountingDate <= day);
        }
        if (search is not null) query = query.Where(i => EF.Functions.Like(i.Description, $"%{search}%"));
        var rows = await query.Take(DiscoveryLimit + 1).Select(i => new
        {
            i.Id, i.AccountingDate, i.Description, i.State, i.RowVersion,
            Amount = i.Lines.Sum(l => (decimal?)l.BaseDebit) ?? 0m,
            Special = i.ReversesEntryId != null || i.CorrectsEntryId != null,
        }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(i => new Candidate(BulkPostingDocumentType.Journal, i.Id, i.Description,
            new DateTimeOffset(i.AccountingDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            null, i.Amount, null, 1m, i.State, Name<JournalEntryState>(i.State), i.RowVersion, i.Special)).ToArray();
    }

    private async Task<IReadOnlyList<Candidate>> ReadJournalsAsync(Guid companyId, Guid branchId, HashSet<Guid> ids,
        CancellationToken cancellationToken)
    {
        var rows = await context.JournalEntries.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.BranchId == branchId && ids.Contains(i.Id))
            .Select(i => new
            {
                i.Id, i.AccountingDate, i.Description, i.State, i.RowVersion,
                Amount = i.Lines.Sum(l => (decimal?)l.BaseDebit) ?? 0m,
                Special = i.ReversesEntryId != null || i.CorrectsEntryId != null,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(i => new Candidate(BulkPostingDocumentType.Journal, i.Id, i.Description,
            new DateTimeOffset(i.AccountingDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            null, i.Amount, null, 1m, i.State, Name<JournalEntryState>(i.State), i.RowVersion, i.Special)).ToArray();
    }

    private async Task<IReadOnlyList<Candidate>> DiscoverInventoryAsync(Guid companyId, Guid branchId,
        BulkPostingFilter filter, string? search, CancellationToken cancellationToken)
    {
        var query = context.InventoryDocuments.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.BranchId == branchId);
        if (filter.FromDateUtc is { } from) query = query.Where(i => i.DateUtc >= from);
        if (filter.ToDateUtc is { } to) query = query.Where(i => i.DateUtc <= to);
        if (search is not null)
            query = query.Where(i => (i.Number != null && EF.Functions.Like(i.Number, $"%{search}%"))
                || EF.Functions.Like(i.Reason, $"%{search}%"));
        var rows = await query.Take(DiscoveryLimit + 1).Select(i => new
        {
            i.Id, i.Number, i.DateUtc, i.Kind, i.State, i.RowVersion,
            Amount = i.Lines.Sum(l => (decimal?)l.Cost) ?? 0m,
            Special = i.ReversedBy != null || i.ReversalJournalId != null,
        }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(i => new Candidate(BulkPostingDocumentType.InventoryDocument, i.Id, i.Number, i.DateUtc,
            null, i.Amount, null, 1m, i.State, Name<InventoryDocumentState>(i.State), i.RowVersion, i.Special,
            Kind: i.Kind)).ToArray();
    }

    private async Task<IReadOnlyList<Candidate>> ReadInventoryAsync(Guid companyId, Guid branchId, HashSet<Guid> ids,
        CancellationToken cancellationToken)
    {
        var rows = await context.InventoryDocuments.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.BranchId == branchId && ids.Contains(i.Id))
            .Select(i => new
            {
                i.Id, i.Number, i.DateUtc, i.Kind, i.State, i.RowVersion,
                Amount = i.Lines.Sum(l => (decimal?)l.Cost) ?? 0m,
                Special = i.ReversedBy != null || i.ReversalJournalId != null,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(i => new Candidate(BulkPostingDocumentType.InventoryDocument, i.Id, i.Number, i.DateUtc,
            null, i.Amount, null, 1m, i.State, Name<InventoryDocumentState>(i.State), i.RowVersion, i.Special,
            Kind: i.Kind)).ToArray();
    }

    private async Task<IReadOnlyList<Classified>> ClassifyAsync(Guid companyId, Guid branchId, Guid userId,
        IReadOnlyList<Candidate> candidates, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0) return [];
        var company = await context.Companies.AsNoTracking()
            .Where(c => c.Id == companyId).Select(c => new { c.BaseCurrencyCode })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Company");
        var additional = await context.CompanyAdditionalCurrencies.AsNoTracking()
            .Where(c => c.CompanyId == companyId).Select(c => c.CurrencyCode)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var currencies = new HashSet<string>(additional, StringComparer.Ordinal) { company.BaseCurrencyCode };
        var from = DateOnly.FromDateTime(candidates.Min(c => c.DocumentDateUtc).UtcDateTime);
        var to = DateOnly.FromDateTime(candidates.Max(c => c.DocumentDateUtc).UtcDateTime);
        var periods = await context.AccountingPeriods.AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.RangeStart <= to && p.RangeEnd >= from)
            .Select(p => new PeriodWindow(p.RangeStart, p.RangeEnd, p.State))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var grants = new Dictionary<string, bool>(StringComparer.Ordinal);
        async Task<bool> HasAsync(string code)
        {
            if (!grants.TryGetValue(code, out var granted))
            {
                granted = await access.EvaluatePermissionAsync(userId, PermissionCatalog.Get(code), companyId, branchId,
                    cancellationToken).ConfigureAwait(false) == AccessDecision.Granted;
                grants[code] = granted;
            }
            return granted;
        }

        var result = new List<Classified>(candidates.Count);
        foreach (var candidate in candidates)
        {
            result.Add(await ClassifyOneAsync(candidate, currencies, periods, HasAsync).ConfigureAwait(false));
        }
        return result;
    }

    private static async Task<Classified> ClassifyOneAsync(Candidate c, HashSet<string> currencies,
        IReadOnlyList<PeriodWindow> periods, Func<string, Task<bool>> has)
    {
        if (c.Missing)
            return new(c, BulkPostingClassification.IneligibleDocumentState, false,
                "المستند غير موجود داخل نطاق الشركة والفرع المحدد. / The document is not in this company and branch scope.");

        var alreadyPosted = c.DocumentType switch
        {
            BulkPostingDocumentType.SalesInvoice => c.Status == (int)SalesInvoiceStatus.Posted,
            BulkPostingDocumentType.PurchaseInvoice => c.Status == (int)PurchaseInvoiceStatus.Posted,
            BulkPostingDocumentType.Voucher => c.Status == (int)VoucherStatus.Posted,
            BulkPostingDocumentType.Journal => c.Status == (int)JournalEntryState.Posted,
            BulkPostingDocumentType.InventoryDocument => c.Status == (int)InventoryDocumentState.Posted,
            _ => false,
        };
        if (alreadyPosted)
            return new(c, BulkPostingClassification.AlreadyPosted, false,
                "المستند مُرحَّل مسبقاً؛ لن تُنشأ حركة مالية أو مخزنية جديدة. / Already posted; no new financial or stock effect will be created.");

        if (c.Special)
            return new(c, BulkPostingClassification.UnsupportedSpecialDocumentType, false,
                "مرتجع أو عكس أو مستند قديم يتطلب معالجة فردية ولا يُرحَّل جماعياً. / A return, reversal or legacy document requires individual handling.");

        if (c.DocumentType is BulkPostingDocumentType.SalesInvoice or BulkPostingDocumentType.PurchaseInvoice
                or BulkPostingDocumentType.Voucher
            && (string.IsNullOrWhiteSpace(c.CurrencyCode) || c.ExchangeRate <= 0 || !currencies.Contains(c.CurrencyCode)))
            return new(c, BulkPostingClassification.InvalidCurrencyOrExchangeRate, false,
                "العملة أو سعر الصرف غير صالح لهذا المستند. / The document currency or exchange rate is invalid.");

        var day = DateOnly.FromDateTime(c.DocumentDateUtc.UtcDateTime);
        var period = periods.FirstOrDefault(p => p.Start <= day && p.End >= day);
        if (period is null)
            return new(c, BulkPostingClassification.ClosedAccountingPeriod, false,
                "لا توجد فترة محاسبية تغطي تاريخ المستند. / No accounting period covers the document date.");
        if (period.State != (byte)AccountingPeriodState.Open)
            return new(c, BulkPostingClassification.ClosedAccountingPeriod, false,
                "الفترة المحاسبية مغلقة أو مغلقة جزئياً؛ لا يمكن الترحيل. / The accounting period is closed or soft-closed.");

        return c.DocumentType switch
        {
            BulkPostingDocumentType.SalesInvoice => await ClassifySalesAsync(c, has).ConfigureAwait(false),
            BulkPostingDocumentType.PurchaseInvoice => await ClassifyPurchaseAsync(c, has).ConfigureAwait(false),
            BulkPostingDocumentType.Voucher => await ClassifyVoucherAsync(c, has).ConfigureAwait(false),
            BulkPostingDocumentType.Journal => await ClassifyJournalAsync(c, has).ConfigureAwait(false),
            BulkPostingDocumentType.InventoryDocument => await ClassifyInventoryAsync(c, has).ConfigureAwait(false),
            _ => new(c, BulkPostingClassification.Unknown, false, "نوع المستند غير معروف. / Unknown document type."),
        };
    }

    private static async Task<Classified> ClassifySalesAsync(Candidate c, Func<string, Task<bool>> has)
    {
        if (c.Status == (int)SalesInvoiceStatus.Draft)
        {
            if (!await has(PermissionCatalog.SalesInvoicesApprove).ConfigureAwait(false)
                || !await has(PermissionCatalog.SalesInvoicesPost).ConfigureAwait(false))
                return Missing(c);
            return new(c, BulkPostingClassification.CanBeApprovedAutomatically, true,
                "ستُعتمد الفاتورة ثم تُرحَّل بصلاحياتك الحالية. / The invoice will be approved and posted with your current grants.");
        }
        if (c.Status == (int)SalesInvoiceStatus.Approved)
            return await has(PermissionCatalog.SalesInvoicesPost).ConfigureAwait(false)
                ? Ready(c)
                : Missing(c);
        return Ineligible(c, "حالة فاتورة المبيعات لا تسمح بالترحيل. / The sales invoice state is not postable.");
    }

    private static async Task<Classified> ClassifyPurchaseAsync(Candidate c, Func<string, Task<bool>> has)
    {
        if (c.Status == (int)PurchaseInvoiceStatus.Draft)
        {
            if (!await has(PermissionCatalog.PurchaseInvoicesApprove).ConfigureAwait(false)
                || !await has(PermissionCatalog.PurchaseInvoicesPost).ConfigureAwait(false))
                return Missing(c);
            return new(c, BulkPostingClassification.CanBeApprovedAutomatically, true,
                "ستُعتمد فاتورة الشراء ثم تُرحَّل بصلاحياتك الحالية. / The purchase invoice will be approved and posted with your current grants.");
        }
        if (c.Status == (int)PurchaseInvoiceStatus.Approved)
            return await has(PermissionCatalog.PurchaseInvoicesPost).ConfigureAwait(false)
                ? Ready(c)
                : Missing(c);
        return Ineligible(c, "حالة فاتورة الشراء لا تسمح بالترحيل. / The purchase invoice state is not postable.");
    }

    private static async Task<Classified> ClassifyVoucherAsync(Candidate c, Func<string, Task<bool>> has)
    {
        if (c.Status == (int)VoucherStatus.Draft)
        {
            if (!await has(PermissionCatalog.TreasuryVouchersApprove).ConfigureAwait(false)
                || !await has(PermissionCatalog.TreasuryVouchersPost).ConfigureAwait(false))
                return Missing(c);
            return new(c, BulkPostingClassification.CanBeApprovedAutomatically, true,
                "سيُعتمد السند ثم يُرحَّل بصلاحياتك الحالية. / The voucher will be approved and posted with your current grants.");
        }
        if (c.Status == (int)VoucherStatus.Approved)
            return await has(PermissionCatalog.TreasuryVouchersPost).ConfigureAwait(false)
                ? Ready(c)
                : Missing(c);
        return Ineligible(c, "حالة السند لا تسمح بالترحيل. / The voucher state is not postable.");
    }

    private static async Task<Classified> ClassifyJournalAsync(Candidate c, Func<string, Task<bool>> has)
    {
        if (c.Status == (int)JournalEntryState.Draft)
            return new(c, BulkPostingClassification.Incomplete, false,
                "القيد مسودة غير مُتحقَّق منها؛ تحقق منه يدوياً أولاً. / The journal is an unvalidated draft; validate it first.");
        if (c.Status == (int)JournalEntryState.Validated)
        {
            if (!await has(PermissionCatalog.JournalsApprove).ConfigureAwait(false)
                || !await has(PermissionCatalog.JournalsPost).ConfigureAwait(false))
                return Missing(c);
            return new(c, BulkPostingClassification.CanBeApprovedAutomatically, true,
                "سيُعتمد القيد ثم يُرحَّل بصلاحياتك الحالية. / The journal will be approved and posted with your current grants.");
        }
        if (c.Status == (int)JournalEntryState.Approved)
            return await has(PermissionCatalog.JournalsPost).ConfigureAwait(false)
                ? Ready(c)
                : Missing(c);
        return Ineligible(c, "حالة القيد لا تسمح بالترحيل. / The journal state is not postable.");
    }

    private static async Task<Classified> ClassifyInventoryAsync(Candidate c, Func<string, Task<bool>> has)
    {
        if (c.Status != (int)InventoryDocumentState.Draft)
            return Ineligible(c, "حالة مستند المخزون لا تسمح بالترحيل. / The inventory document state is not postable.");
        var permission = c.Kind == (int)InventoryDocumentKind.Transfer
            ? PermissionCatalog.InventoryTransfersManage
            : PermissionCatalog.InventoryAdjustmentsManage;
        if (!await has(permission).ConfigureAwait(false)) return Missing(c);
        return new(c, BulkPostingClassification.ReadyForPosting, true,
            "مستند المخزون جاهز للترحيل؛ لا يوجد اعتماد منفصل لمستندات المخزون. / The inventory document posts directly without a separate approval state.");
    }

    private static Classified Ready(Candidate c) => new(c, BulkPostingClassification.ReadyForPosting, true,
        "جاهز للترحيل. / Ready to post.");

    private static Classified Missing(Candidate c) => new(c, BulkPostingClassification.MissingPermission, false,
        "لا تملك صلاحيات الاعتماد والترحيل المطلوبة لهذا المستند. / You do not hold the required approve and post grants.");

    private static Classified Ineligible(Candidate c, string reason) =>
        new(c, BulkPostingClassification.IneligibleDocumentState, false, reason);

    private static string Name<T>(int status) where T : struct, Enum =>
        Enum.IsDefined(typeof(T), status) ? ((T)(object)status).ToString() : status.ToString(CultureInfo.InvariantCulture);
}
