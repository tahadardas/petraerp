using System.Data;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Patterns;
using PetraERP.Application.Sales;
using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Sales;

public sealed partial class SqlSalesInvoiceService : ISalesInvoiceService
{
    private readonly FinancialDbContext context;
    private readonly IPatternService patternService;
    private readonly TimeProvider timeProvider;

    public SqlSalesInvoiceService(
        FinancialDbContext context,
        IPatternService patternService,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(patternService);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.context = context;
        this.patternService = patternService;
        this.timeProvider = timeProvider;
    }

    /// <summary>The ONE definition of the list filters; the list and previous/next/first/last navigation both use it.</summary>
    private static IQueryable<SalesInvoiceRow> ApplyListFilters(IQueryable<SalesInvoiceRow> source, SalesInvoiceSearchQuery query)
    {
        if (query.CustomerId.HasValue)
        {
            source = source.Where(i => i.CustomerId == query.CustomerId.Value);
        }

        if (query.WarehouseId.HasValue)
        {
            source = source.Where(i => i.WarehouseId == query.WarehouseId.Value);
        }

        if (query.PatternRevisionId.HasValue)
        {
            source = source.Where(i => i.PatternRevisionId == query.PatternRevisionId.Value);
        }

        if (query.Status.HasValue)
        {
            source = source.Where(i => i.Status == (int)query.Status.Value);
        }

        if (query.ReturnOfInvoiceId.HasValue)
        {
            source = source.Where(i => i.ReturnOfInvoiceId == query.ReturnOfInvoiceId.Value);
        }

        if (query.FromDateUtc.HasValue)
        {
            source = source.Where(i => i.InvoiceDateUtc >= query.FromDateUtc.Value);
        }

        if (query.ToDateUtc.HasValue)
        {
            source = source.Where(i => i.InvoiceDateUtc <= query.ToDateUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            // A draft has no number; the list shows "مسودة XXXXXXXX" (first 8 hex digits of its id), so that reference is searchable too.
            var draftReference = DraftReferenceOf(term);
            source = source.Where(i =>
                (i.InvoiceNumber != null && i.InvoiceNumber.Contains(term))
                || (i.Customer != null && (i.Customer.Code.Contains(term) || i.Customer.NameArabic.Contains(term)
                    || (i.Customer.NameEnglish != null && i.Customer.NameEnglish.Contains(term))))
                || (i.Notes != null && i.Notes.Contains(term))
                || (draftReference != null && i.InvoiceNumber == null && i.Id.ToString().StartsWith(draftReference)));
        }
        return source;
    }

    private static string? DraftReferenceOf(string term)
    {
        var hex = term.Replace("مسودة", string.Empty, StringComparison.Ordinal).Replace("#", string.Empty, StringComparison.Ordinal).Trim();
        return hex.Length is >= 4 and <= 8 && hex.All(char.IsAsciiHexDigit) ? hex : null;
    }

    public async Task<Guid?> GetNeighborInvoiceIdAsync(
        Guid companyId,
        Guid branchId,
        Guid? currentInvoiceId,
        SalesInvoiceNavigationDirection direction,
        SalesInvoiceSearchQuery? filter = null,
        CancellationToken cancellationToken = default)
    {
        var branchInvoices = context.SalesInvoices.AsNoTracking().Where(i => i.CompanyId == companyId && i.BranchId == branchId);
        var scoped = filter is null ? branchInvoices : ApplyListFilters(branchInvoices, filter);
        // The SAME total order the list uses (default for navigation without an explicit sort: date ascending, as before).
        var sort = filter?.Sort ?? NavigationDefaultSort;

        if (direction == SalesInvoiceNavigationDirection.First)
            return await ApplyOrdering(scoped, sort).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (direction == SalesInvoiceNavigationDirection.Last)
            return await ApplyOrdering(scoped, sort with { Descending = !sort.Descending }).Select(i => (Guid?)i.Id)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (currentInvoiceId is null) return null;
        // The origin may no longer match the filter (e.g. its status changed): it still anchors the position inside the filtered set.
        // An origin that no longer exists at all is reported explicitly (404) instead of guessing a position.
        return sort.Field switch
        {
            SalesInvoiceSortField.InvoiceNumber => await NeighborAsync(scoped, branchInvoices, sort, i => i.InvoiceNumber ?? string.Empty, currentInvoiceId.Value, direction, cancellationToken).ConfigureAwait(false),
            SalesInvoiceSortField.Pattern => await NeighborAsync(scoped, branchInvoices, sort, i => i.PatternRevision!.Pattern!.Code, currentInvoiceId.Value, direction, cancellationToken).ConfigureAwait(false),
            SalesInvoiceSortField.Customer => await NeighborAsync(scoped, branchInvoices, sort, i => i.Customer!.NameArabic, currentInvoiceId.Value, direction, cancellationToken).ConfigureAwait(false),
            SalesInvoiceSortField.Warehouse => await NeighborAsync(scoped, branchInvoices, sort, i => i.Warehouse!.Code, currentInvoiceId.Value, direction, cancellationToken).ConfigureAwait(false),
            SalesInvoiceSortField.PaymentMethod => await NeighborAsync(scoped, branchInvoices, sort, i => i.PaymentMethod, currentInvoiceId.Value, direction, cancellationToken).ConfigureAwait(false),
            SalesInvoiceSortField.GrandTotal => await NeighborAsync(scoped, branchInvoices, sort, i => i.GrandTotal, currentInvoiceId.Value, direction, cancellationToken).ConfigureAwait(false),
            SalesInvoiceSortField.Currency => await NeighborAsync(scoped, branchInvoices, sort, i => i.CurrencyCode, currentInvoiceId.Value, direction, cancellationToken).ConfigureAwait(false),
            SalesInvoiceSortField.Status => await NeighborAsync(scoped, branchInvoices, sort, i => i.Status, currentInvoiceId.Value, direction, cancellationToken).ConfigureAwait(false),
            _ => await NeighborAsync(scoped, branchInvoices, sort, i => i.InvoiceDateUtc, currentInvoiceId.Value, direction, cancellationToken).ConfigureAwait(false),
        };
    }

    private static readonly SalesInvoiceSort ListDefaultSort = new(SalesInvoiceSortField.Date, true);
    private static readonly SalesInvoiceSort NavigationDefaultSort = new(SalesInvoiceSortField.Date, false);

    /// <summary>
    /// Total order for list and navigation: the requested column, then (date, created-at, id) in the same direction.
    /// The id tie-breaker makes every position unique, so paging and previous/next never skip or repeat a row.
    /// </summary>
    internal static IOrderedQueryable<SalesInvoiceRow> ApplyOrdering(IQueryable<SalesInvoiceRow> source, SalesInvoiceSort sort) => sort.Field switch
    {
        SalesInvoiceSortField.InvoiceNumber => OrderByKey(source, i => i.InvoiceNumber ?? string.Empty, sort.Descending),
        SalesInvoiceSortField.Pattern => OrderByKey(source, i => i.PatternRevision!.Pattern!.Code, sort.Descending),
        SalesInvoiceSortField.Customer => OrderByKey(source, i => i.Customer!.NameArabic, sort.Descending),
        SalesInvoiceSortField.Warehouse => OrderByKey(source, i => i.Warehouse!.Code, sort.Descending),
        SalesInvoiceSortField.PaymentMethod => OrderByKey(source, i => i.PaymentMethod, sort.Descending),
        SalesInvoiceSortField.GrandTotal => OrderByKey(source, i => i.GrandTotal, sort.Descending),
        SalesInvoiceSortField.Currency => OrderByKey(source, i => i.CurrencyCode, sort.Descending),
        SalesInvoiceSortField.Status => OrderByKey(source, i => i.Status, sort.Descending),
        _ => sort.Descending
            ? source.OrderByDescending(i => i.InvoiceDateUtc).ThenByDescending(i => i.CreatedAtUtc).ThenByDescending(i => i.Id)
            : source.OrderBy(i => i.InvoiceDateUtc).ThenBy(i => i.CreatedAtUtc).ThenBy(i => i.Id),
    };

    private static IOrderedQueryable<SalesInvoiceRow> OrderByKey<TKey>(
        IQueryable<SalesInvoiceRow> source, Expression<Func<SalesInvoiceRow, TKey>> key, bool descending) =>
        descending
            ? source.OrderByDescending(key).ThenByDescending(i => i.InvoiceDateUtc).ThenByDescending(i => i.CreatedAtUtc).ThenByDescending(i => i.Id)
            : source.OrderBy(key).ThenBy(i => i.InvoiceDateUtc).ThenBy(i => i.CreatedAtUtc).ThenBy(i => i.Id);

    /// <summary>
    /// Keyset step: the first row strictly after (Next) or before (Previous) the origin in the requested total order, evaluated by
    /// SQL Server with the same comparison it uses for ORDER BY (so collation and uniqueidentifier ordering agree with the list).
    /// </summary>
    private static async Task<Guid?> NeighborAsync<TKey>(
        IQueryable<SalesInvoiceRow> scoped,
        IQueryable<SalesInvoiceRow> branchInvoices,
        SalesInvoiceSort sort,
        Expression<Func<SalesInvoiceRow, TKey>> key,
        Guid currentId,
        SalesInvoiceNavigationDirection direction,
        CancellationToken cancellationToken)
    {
        var cursor = await branchInvoices.Where(i => i.Id == currentId)
            .Select(i => new { i.InvoiceDateUtc, i.CreatedAtUtc, i.Id }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (cursor is null) throw new ResourceNotFoundException("SalesInvoice");
        var currentKey = await branchInvoices.Where(i => i.Id == currentId).Select(key).FirstAsync(cancellationToken).ConfigureAwait(false);

        var forward = direction == SalesInvoiceNavigationDirection.Next;
        var after = forward != sort.Descending; // ascending+forward or descending+backward => larger values come next
        var row = Expression.Parameter(typeof(SalesInvoiceRow), "i");
        var keyOfRow = new ParameterReplacer(key.Parameters[0], row).Visit(key.Body)!;
        var date = Expression.Property(row, nameof(SalesInvoiceRow.InvoiceDateUtc));
        var created = Expression.Property(row, nameof(SalesInvoiceRow.CreatedAtUtc));
        var id = Expression.Property(row, nameof(SalesInvoiceRow.Id));

        var tail = Expression.OrElse(Beyond(created, Expression.Constant(cursor.CreatedAtUtc), after),
            Expression.AndAlso(Expression.Equal(created, Expression.Constant(cursor.CreatedAtUtc)),
                Beyond(id, Expression.Constant(cursor.Id), after)));
        var withDate = Expression.OrElse(Beyond(date, Expression.Constant(cursor.InvoiceDateUtc), after),
            Expression.AndAlso(Expression.Equal(date, Expression.Constant(cursor.InvoiceDateUtc)), tail));
        var keyConstant = Expression.Constant(currentKey, typeof(TKey));
        var predicate = Expression.OrElse(Beyond(keyOfRow, keyConstant, after),
            Expression.AndAlso(Expression.Equal(keyOfRow, keyConstant), withDate));

        var travel = forward ? sort : sort with { Descending = !sort.Descending };
        return await ApplyOrdering(scoped.Where(Expression.Lambda<Func<SalesInvoiceRow, bool>>(predicate, row)), travel)
            .Select(i => (Guid?)i.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    private static BinaryExpression Beyond(Expression left, Expression right, bool greater)
    {
        if (left.Type == typeof(string))
        {
            var compare = Expression.Call(typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!, left, right);
            return greater ? Expression.GreaterThan(compare, Expression.Constant(0)) : Expression.LessThan(compare, Expression.Constant(0));
        }
        if (left.Type == typeof(Guid))
        {
            var compare = Expression.Call(left, typeof(Guid).GetMethod(nameof(Guid.CompareTo), [typeof(Guid)])!, right);
            return greater ? Expression.GreaterThan(compare, Expression.Constant(0)) : Expression.LessThan(compare, Expression.Constant(0));
        }
        return greater ? Expression.GreaterThan(left, right) : Expression.LessThan(left, right);
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    public async Task<SalesInvoicePageResult> GetInvoicesAsync(
        Guid companyId,
        Guid branchId,
        SalesInvoiceSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var baseQuery = context.SalesInvoices
            .AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.BranchId == branchId);

        baseQuery = ApplyListFilters(baseQuery, query);

        var totalCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 50 : query.PageSize;

        var dbQuery = baseQuery
            .Include(i => i.PatternRevision)
                .ThenInclude(r => r!.Pattern)
            .Include(i => i.Customer)
            .Include(i => i.Warehouse);

        var rows = await ApplyOrdering(dbQuery, query.Sort ?? ListDefaultSort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var creditCustomerIds = rows
            .Where(r => r.Status == (int)SalesInvoiceStatus.Posted && r.PaymentMethod == (int)PaymentMethod.Credit && r.ReversesInvoiceId == null && r.ReturnOfInvoiceId == null)
            .Select(r => r.CustomerId)
            .Distinct()
            .ToList();

        // One batched settlement read for every credit customer on this page (was one full read per customer: N+1).
        var settlementMap = new Dictionary<Guid, (decimal Paid, decimal Returned, decimal Outstanding)>();
        if (creditCustomerIds.Count > 0)
        {
            var pageInvoices = await PetraERP.Infrastructure.Treasury.SettlementQueries.LoadInvoicesAsync(
                context, companyId, branchId, VoucherKind.Receipt, null, null, null, cancellationToken, creditCustomerIds).ConfigureAwait(false);
            foreach (var inv in pageInvoices)
            {
                settlementMap[inv.Id] = (inv.Paid, inv.Returned, inv.Outstanding);
            }
        }

        var items = rows.Select(r => {
            decimal? settled = null, returnedAmt = null, outstanding = null;
            if (settlementMap.TryGetValue(r.Id, out var s))
            {
                settled = s.Paid;
                returnedAmt = s.Returned;
                outstanding = s.Outstanding;
            }
            else if (r.Status == (int)SalesInvoiceStatus.Posted && r.PaymentMethod == (int)PaymentMethod.Credit && r.ReversesInvoiceId == null && r.ReturnOfInvoiceId == null)
            {
                settled = 0m;
                returnedAmt = 0m;
                outstanding = r.GrandTotal;
            }

            return new SalesInvoiceSummaryResult(
                r.Id,
                r.CompanyId,
                r.BranchId,
                r.PatternRevisionId,
                r.PatternRevision?.Pattern?.Code ?? string.Empty,
                r.PatternRevision?.Pattern?.NameArabic ?? string.Empty,
                r.CustomerId,
                r.Customer?.Code ?? string.Empty,
                r.Customer?.NameArabic ?? string.Empty,
                r.WarehouseId,
                r.Warehouse?.Code ?? string.Empty,
                r.InvoiceNumber,
                r.InvoiceDateUtc,
                (PaymentMethod)r.PaymentMethod,
                r.CurrencyCode,
                r.ExchangeRate,
                (SalesInvoiceStatus)r.Status,
                r.SubTotal,
                r.DiscountTotal,
                r.TaxTotal,
                r.AdditionsTotal,
                r.GrandTotal,
                r.JournalEntryId,
                r.CreatedAtUtc,
                r.PostedAtUtc,
                r.RowVersion)
            {
                ReturnOfInvoiceId = r.ReturnOfInvoiceId,
                SettledAmount = settled,
                ReturnedAmount = returnedAmt,
                OutstandingAmount = outstanding
            };
        }).ToList();

        return new SalesInvoicePageResult(page, pageSize, totalCount, items);
    }

    public async Task<SalesInvoiceDetailResult> GetInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        var row = await context.SalesInvoices
            .AsNoTracking()
            .Include(i => i.PatternRevision)
                .ThenInclude(r => r!.Pattern)
            .Include(i => i.Customer)
            .Include(i => i.Warehouse)
            .Include(i => i.Lines)
                .ThenInclude(l => l.Item)
            .Include(i => i.Lines)
                .ThenInclude(l => l.Unit)
            .Include(i => i.Adjustments)
                .ThenInclude(a => a.Account)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == companyId && i.BranchId == branchId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            throw new ResourceNotFoundException("SalesInvoice");
        }

        var costCenterIds = row.Lines
            .Where(l => l.CostCenterId.HasValue)
            .Select(l => l.CostCenterId!.Value)
            .ToList();
        if (row.CostCenterId.HasValue)
        {
            costCenterIds.Add(row.CostCenterId.Value);
        }

        Dictionary<Guid, string>? costCenters = null;
        if (costCenterIds.Count > 0)
        {
            costCenters = await context.CostCenters
                .AsNoTracking()
                .Where(c => c.CompanyId == companyId && costCenterIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Code, cancellationToken)
                .ConfigureAwait(false);
        }

        var lineIds = row.Lines.Select(l => l.Id).ToList();
        var returned = await context.SalesInvoiceLines.AsNoTracking()
            .Where(l => l.SourceLineId != null && lineIds.Contains(l.SourceLineId.Value))
            .GroupBy(l => l.SourceLineId!.Value)
            .Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(g => g.Key, g => g.Quantity, cancellationToken)
            .ConfigureAwait(false);

        decimal? settled = null, returnedAmount = null, outstanding = null;
        if (!SalesDetailEnrichment.Suppressed && row.Status == (int)SalesInvoiceStatus.Posted && row.PaymentMethod == (int)PaymentMethod.Credit
            && row.ReversesInvoiceId == null && row.ReturnOfInvoiceId == null)
        {
            // Only this invoice, its returns and its reversal are needed for its own settlement figures: the composition is per original
            // invoice, so restricting the seed set returns the same row without composing every invoice of the customer (and without
            // the customer's open items, which can never carry this invoice's id).
            var invoices = await PetraERP.Infrastructure.Treasury.SettlementQueries.LoadInvoicesAsync(context, companyId, branchId,
                VoucherKind.Receipt, row.CustomerId, null, null, cancellationToken, documentId: row.Id).ConfigureAwait(false);
            var settlement = invoices.SingleOrDefault(i => i.Id == row.Id);
            settled = settlement?.Paid ?? 0m;
            returnedAmount = settlement?.Returned ?? 0m;
            outstanding = settlement?.Outstanding ?? 0m;
        }

        var detail = MapDetailResult(row, costCenters);
        var credit = !SalesDetailEnrichment.Suppressed && row.PaymentMethod == (int)PaymentMethod.Credit && row.ReturnOfInvoiceId is null && row.ReversesInvoiceId is null
            ? await LoadCreditSnapshotAsync(row, cancellationToken).ConfigureAwait(false) : null;
        var audit = await context.SalesCreditAudit.AsNoTracking().SingleOrDefaultAsync(a => a.InvoiceId == row.Id
            && a.CompanyId == companyId && a.BranchId == branchId, cancellationToken).ConfigureAwait(false);
        return detail with
        {
            Lines = detail.Lines.Select(l => l with { ReturnedQuantity = returned.GetValueOrDefault(l.Id) }).ToList(),
            SettledAmount = settled,
            ReturnedAmount = returnedAmount,
            OutstandingAmount = outstanding,
            CustomerIsCashCustomer = row.Customer?.IsCashCustomer ?? false,
            CreditBlocked = row.Customer?.IsCreditBlocked ?? false,
            CreditPolicyKind = row.Customer?.CreditPolicyKind ?? 0,
            CreditExposureBase = credit?.Exposure,
            CreditLimitBase = row.Customer?.CreditLimit,
            CreditProjectedExposureBase = credit?.Projected,
            CreditPolicyRequiresAction = credit?.RequiresAction ?? false,
            CreditOverrideApplied = audit?.IsOverride ?? false,
            CreditOverrideReason = audit?.Reason,
            CreditPolicyActorUserId = audit?.ActorUserId,
            CreditPolicyRecordedAtUtc = audit?.RecordedAtUtc,
        };
    }

    public async Task<SalesInvoiceDetailResult> CreateInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid userId,
        CreateSalesInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        command = command with { PatternRevisionId = await PetraERP.Infrastructure.CompanyOptions.SqlCompanyOptionsService.ResolveDefaultRevisionAsync(context,companyId,command.PatternRevisionId,PetraERP.Domain.Patterns.BusinessOperationKind.SalesInvoice,cancellationToken).ConfigureAwait(false) };
        var currencyTransaction = await PetraERP.Infrastructure.Currencies.CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var currencyTransactionLifetime = currencyTransaction.ConfigureAwait(false);

        var customer = await ResolveSalesCustomerAsync(companyId, command.CustomerId, command.PaymentMethod, cancellationToken).ConfigureAwait(false);

        var warehouse = await context.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == command.WarehouseId && w.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Warehouse");

        if (!warehouse.IsActive)
        {
            throw new BusinessRuleException("sales.warehouse_inactive", "Warehouse is inactive.");
        }

        var patternRev = await context.PatternRevisions
            .AsNoTracking()
            .Include(r => r.Pattern)
            .FirstOrDefaultAsync(r => r.Id == command.PatternRevisionId && r.Pattern!.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("PatternRevision");

        if (patternRev.State != (int)PatternRevisionState.Published)
        {
            throw new BusinessRuleException("sales.pattern_not_published", "Pattern revision is not published.");
        }

        if (patternRev.Pattern!.OperationKind != (int)BusinessOperationKind.SalesInvoice)
        {
            throw new BusinessRuleException("sales.pattern_operation_mismatch", "Pattern is not a sales invoice pattern.");
        }

        await ValidateDraftReferencesAsync(companyId, branchId, command.WarehouseId, command.CostCenterId, command.Lines, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var invoiceDate = command.InvoiceDateUtc ?? now;
        var currency = await CompanyCurrencies.ResolveAsync(context, companyId, "sales", command.CurrencyCode, command.ExchangeRate,
            DateOnly.FromDateTime(invoiceDate.UtcDateTime), customer.DefaultCurrencyCode,
            PatternCurrencyDefaults.DefaultCurrency(patternRev.ConfigurationJson), cancellationToken).ConfigureAwait(false);

        var customerAccountId = await ResolveCustomerAccountOverrideAsync(companyId, command.CustomerAccountId, command.PaymentMethod, customer,
            patternRev.ConfigurationJson, cancellationToken).ConfigureAwait(false);

        var aggregate = new SalesInvoice(
            command.DocumentId ?? Guid.NewGuid(),
            companyId,
            branchId,
            command.PatternRevisionId,
            customer.Id,
            command.WarehouseId,
            invoiceDate,
            command.PaymentMethod,
            currency.CurrencyCode,
            currency.Rate,
            command.CostCenterId,
            command.Notes,
            userId);

        if (command.Lines is not null)
        {
            var itemIds = command.Lines.Select(l => l.ItemId).Distinct().ToList();
            var items = await context.Items
                .AsNoTracking()
                .Where(i => itemIds.Contains(i.Id) && i.CompanyId == companyId)
                .ToDictionaryAsync(i => i.Id, cancellationToken)
                .ConfigureAwait(false);

            var unitIds = command.Lines.Select(l => l.UnitId).Distinct().ToList();
            var units = await context.UnitsOfMeasure
                .AsNoTracking()
                .Where(u => unitIds.Contains(u.Id) && u.CompanyId == companyId)
                .ToDictionaryAsync(u => u.Id, cancellationToken)
                .ConfigureAwait(false);

            foreach (var lineCmd in command.Lines)
            {
                if (!items.TryGetValue(lineCmd.ItemId, out var item) || !item.IsActive)
                {
                    throw new BusinessRuleException("sales.item_invalid", $"Item '{lineCmd.ItemId}' not found or inactive.");
                }

                if (!units.ContainsKey(lineCmd.UnitId))
                {
                    throw new BusinessRuleException("sales.unit_invalid", $"Unit '{lineCmd.UnitId}' not found.");
                }

                var line = new SalesInvoiceLine(
                    Guid.NewGuid(),
                    aggregate.Id,
                    lineCmd.ItemId,
                    lineCmd.UnitId,
                    lineCmd.Quantity,
                    lineCmd.UnitPrice,
                    lineCmd.DiscountPercentage,
                    lineCmd.DiscountAmount,
                    lineCmd.TaxPercentage,
                    lineCmd.TaxAmount,
                    lineCmd.PumpCost,
                    lineCmd.TransportCost,
                    lineCmd.CostCenterId,
                    lineCmd.WarehouseId,
                    lineCmd.Notes,
                    lineCmd.DisplayOrder,
                    lineCmd.ConcreteGrade);

                aggregate.AddLine(line);
            }
        }

        aggregate.RecalculateTotals();
        var adjustments = await BuildAdjustmentsAsync(companyId, aggregate.Id, aggregate.CurrencyCode, command.Adjustments, cancellationToken).ConfigureAwait(false);
        var grandTotal = Math.Round(aggregate.GrandTotal - adjustments.Discounts + adjustments.Additions, 4);
        RequireNonNegativeTotal(grandTotal);

        var invoiceRow = new SalesInvoiceRow
        {
            Id = aggregate.Id,
            CompanyId = aggregate.CompanyId,
            BranchId = aggregate.BranchId,
            PatternRevisionId = aggregate.PatternRevisionId,
            CustomerId = aggregate.CustomerId,
            WarehouseId = aggregate.WarehouseId,
            InvoiceNumber = null,
            InvoiceDateUtc = aggregate.InvoiceDateUtc,
            PaymentMethod = (int)aggregate.PaymentMethod,
            CurrencyCode = aggregate.CurrencyCode,
            ExchangeRate = aggregate.ExchangeRate,
            Status = (int)aggregate.Status,
            CostCenterId = aggregate.CostCenterId,
            Notes = aggregate.Notes,
            CustomerAccountId = customerAccountId,
            SellerCode = command.SellerCode,
            DistributorCode = command.DistributorCode,
            SubTotal = aggregate.SubTotal,
            DiscountTotal = aggregate.DiscountTotal + adjustments.Discounts,
            TaxTotal = aggregate.TaxTotal,
            AdditionsTotal = aggregate.AdditionsTotal + adjustments.Additions,
            GrandTotal = grandTotal,
            CreatedByUserId = userId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        foreach (var l in aggregate.Lines)
        {
            invoiceRow.Lines.Add(new SalesInvoiceLineRow
            {
                Id = l.Id,
                SalesInvoiceId = aggregate.Id,
                ItemId = l.ItemId,
                UnitId = l.UnitId,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                DiscountPercentage = l.DiscountPercentage,
                DiscountAmount = l.DiscountAmount,
                TaxPercentage = l.TaxPercentage,
                TaxAmount = l.TaxAmount,
                PumpCost = l.PumpCost,
                TransportCost = l.TransportCost,
                LineTotal = l.LineTotal,
                CostCenterId = l.CostCenterId,
                WarehouseId = l.WarehouseId,
                Notes = l.Notes,
                DisplayOrder = l.DisplayOrder,
                ConcreteGrade = l.ConcreteGrade,
            });
        }

        foreach (var adjustment in adjustments.Rows) invoiceRow.Adjustments.Add(adjustment);
        context.SalesInvoices.Add(invoiceRow);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await currencyTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await GetInvoiceAsync(companyId, branchId, aggregate.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SalesInvoiceDetailResult> UpdateInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        UpdateSalesInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var currencyTransaction = await PetraERP.Infrastructure.Currencies.CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var currencyTransactionLifetime = currencyTransaction.ConfigureAwait(false);

        var invoiceRow = await context.SalesInvoices
            .Include(i => i.Lines)
            .Include(i => i.Adjustments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == companyId && i.BranchId == branchId, cancellationToken)
            .ConfigureAwait(false);

        if (invoiceRow is null)
        {
            throw new ResourceNotFoundException("SalesInvoice");
        }

        RequireVersion(invoiceRow, command.ExpectedVersion);
        if (invoiceRow.Status != (int)SalesInvoiceStatus.Draft || PetraERP.Domain.Documents.DocumentLifecycle.IsImmutable(PetraERP.Domain.Documents.DocumentLifecycle.From((SalesInvoiceStatus)invoiceRow.Status)))
        {
            throw new BusinessRuleException("sales.invoice_not_editable", $"Invoice cannot be modified in status '{(SalesInvoiceStatus)invoiceRow.Status}'.");
        }

        var customer = await ResolveSalesCustomerAsync(companyId, command.CustomerId, command.PaymentMethod, cancellationToken).ConfigureAwait(false);

        var warehouse = await context.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == command.WarehouseId && w.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Warehouse");

        if (!warehouse.IsActive)
        {
            throw new BusinessRuleException("sales.warehouse_inactive", "Warehouse is inactive.");
        }

        await ValidateDraftReferencesAsync(companyId, branchId, command.WarehouseId, command.CostCenterId, command.Lines, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var patternJson = await context.PatternRevisions.AsNoTracking().Where(r => r.Id == invoiceRow.PatternRevisionId)
            .Select(r => r.ConfigurationJson).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var currency = await CompanyCurrencies.ResolveAsync(context, companyId, "sales", command.CurrencyCode, command.ExchangeRate,
            DateOnly.FromDateTime((command.InvoiceDateUtc ?? invoiceRow.InvoiceDateUtc).UtcDateTime), customer.DefaultCurrencyCode,
            PatternCurrencyDefaults.DefaultCurrency(patternJson), cancellationToken).ConfigureAwait(false);
        // Validate the discount/addition rows before any tracked state changes, so a rejected update leaves the draft untouched.
        var adjustments = await BuildAdjustmentsAsync(companyId, invoiceId, currency.CurrencyCode, command.Adjustments, cancellationToken).ConfigureAwait(false);
        var customerAccountId = await ResolveCustomerAccountOverrideAsync(companyId, command.CustomerAccountId, command.PaymentMethod, customer,
            patternJson, cancellationToken).ConfigureAwait(false);
        invoiceRow.CustomerAccountId = customerAccountId;
        invoiceRow.CustomerId = customer.Id;
        invoiceRow.WarehouseId = command.WarehouseId;
        if (command.InvoiceDateUtc.HasValue)
        {
            invoiceRow.InvoiceDateUtc = command.InvoiceDateUtc.Value;
        }
        invoiceRow.PaymentMethod = (int)command.PaymentMethod;
        invoiceRow.CurrencyCode = currency.CurrencyCode;
        invoiceRow.ExchangeRate = currency.Rate;
        invoiceRow.CostCenterId = command.CostCenterId;
        invoiceRow.Notes = command.Notes;
        invoiceRow.SellerCode = command.SellerCode;
        invoiceRow.DistributorCode = command.DistributorCode;
        invoiceRow.UpdatedAtUtc = now;

        context.SalesInvoiceLines.RemoveRange(invoiceRow.Lines);
        invoiceRow.Lines.Clear();
        context.SalesInvoiceAdjustments.RemoveRange(invoiceRow.Adjustments);
        invoiceRow.Adjustments.Clear();

        var subTotal = 0m;
        var discountTotal = 0m;
        var taxTotal = 0m;
        var additionsTotal = 0m;

        if (command.Lines is not null)
        {
            var itemIds = command.Lines.Select(l => l.ItemId).Distinct().ToList();
            var items = await context.Items
                .AsNoTracking()
                .Where(i => itemIds.Contains(i.Id) && i.CompanyId == companyId)
                .ToDictionaryAsync(i => i.Id, cancellationToken)
                .ConfigureAwait(false);

            var unitIds = command.Lines.Select(l => l.UnitId).Distinct().ToList();
            var units = await context.UnitsOfMeasure
                .AsNoTracking()
                .Where(u => unitIds.Contains(u.Id) && u.CompanyId == companyId)
                .ToDictionaryAsync(u => u.Id, cancellationToken)
                .ConfigureAwait(false);

            foreach (var lineCmd in command.Lines)
            {
                if (!items.TryGetValue(lineCmd.ItemId, out var item) || !item.IsActive)
                {
                    throw new BusinessRuleException("sales.item_invalid", $"Item '{lineCmd.ItemId}' not found or inactive.");
                }

                if (!units.ContainsKey(lineCmd.UnitId))
                {
                    throw new BusinessRuleException("sales.unit_invalid", $"Unit '{lineCmd.UnitId}' not found.");
                }

                var domainLine = new SalesInvoiceLine(
                    Guid.NewGuid(),
                    invoiceId,
                    lineCmd.ItemId,
                    lineCmd.UnitId,
                    lineCmd.Quantity,
                    lineCmd.UnitPrice,
                    lineCmd.DiscountPercentage,
                    lineCmd.DiscountAmount,
                    lineCmd.TaxPercentage,
                    lineCmd.TaxAmount,
                    lineCmd.PumpCost,
                    lineCmd.TransportCost,
                    lineCmd.CostCenterId,
                    lineCmd.WarehouseId,
                    lineCmd.Notes,
                    lineCmd.DisplayOrder,
                    lineCmd.ConcreteGrade);

                subTotal += Math.Round(domainLine.Quantity * domainLine.UnitPrice, 4, MidpointRounding.AwayFromZero);
                discountTotal += domainLine.DiscountAmount;
                taxTotal += domainLine.TaxAmount;
                additionsTotal += domainLine.PumpCost + domainLine.TransportCost;

                invoiceRow.Lines.Add(new SalesInvoiceLineRow
                {
                    Id = domainLine.Id,
                    SalesInvoiceId = invoiceId,
                    ItemId = domainLine.ItemId,
                    UnitId = domainLine.UnitId,
                    Quantity = domainLine.Quantity,
                    UnitPrice = domainLine.UnitPrice,
                    DiscountPercentage = domainLine.DiscountPercentage,
                    DiscountAmount = domainLine.DiscountAmount,
                    TaxPercentage = domainLine.TaxPercentage,
                    TaxAmount = domainLine.TaxAmount,
                    PumpCost = domainLine.PumpCost,
                    TransportCost = domainLine.TransportCost,
                    LineTotal = domainLine.LineTotal,
                    CostCenterId = domainLine.CostCenterId,
                    WarehouseId = domainLine.WarehouseId,
                    Notes = domainLine.Notes,
                    DisplayOrder = domainLine.DisplayOrder,
                    ConcreteGrade = domainLine.ConcreteGrade,
                });
            }
        }

        foreach (var adjustment in adjustments.Rows) invoiceRow.Adjustments.Add(adjustment);
        invoiceRow.SubTotal = Math.Round(subTotal, 4);
        invoiceRow.DiscountTotal = Math.Round(discountTotal + adjustments.Discounts, 4);
        invoiceRow.TaxTotal = Math.Round(taxTotal, 4);
        invoiceRow.AdditionsTotal = Math.Round(additionsTotal + adjustments.Additions, 4);
        invoiceRow.GrandTotal = Math.Round(invoiceRow.SubTotal - invoiceRow.DiscountTotal + invoiceRow.TaxTotal + invoiceRow.AdditionsTotal, 4);
        RequireNonNegativeTotal(invoiceRow.GrandTotal);

        await SaveVersionedAsync(cancellationToken).ConfigureAwait(false);

        await currencyTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await GetInvoiceAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SalesInvoiceDetailResult> ApproveInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        Guid userId,
        ConcurrencyToken expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedVersion);
        var invoiceRow = await context.SalesInvoices
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == companyId && i.BranchId == branchId, cancellationToken)
            .ConfigureAwait(false);

        if (invoiceRow is null)
        {
            throw new ResourceNotFoundException("SalesInvoice");
        }

        RequireVersion(invoiceRow, expectedVersion);
        if (invoiceRow.Status != (int)SalesInvoiceStatus.Draft || !PetraERP.Domain.Documents.DocumentLifecycle.CanTransition(PetraERP.Domain.Documents.AccountingDocumentKind.SalesInvoice, PetraERP.Domain.Documents.DocumentLifecycle.From((SalesInvoiceStatus)invoiceRow.Status), PetraERP.Domain.Documents.DocumentLifecycleState.Approved))
        {
            throw new BusinessRuleException("sales.invoice_cannot_approve", $"Invoice cannot be approved in status '{(SalesInvoiceStatus)invoiceRow.Status}'.");
        }

        var now = timeProvider.GetUtcNow();
        invoiceRow.Status = (int)SalesInvoiceStatus.Approved;
        invoiceRow.ApprovedByUserId = userId;
        invoiceRow.ApprovedAtUtc = now;
        invoiceRow.UpdatedAtUtc = now;

        await SaveVersionedAsync(cancellationToken).ConfigureAwait(false);

        return await GetInvoiceAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteDraftAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        ConcurrencyToken expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedVersion);
        var invoiceRow = await context.SalesInvoices
            .Include(i => i.Lines).Include(i => i.Adjustments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == companyId && i.BranchId == branchId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("SalesInvoice");

        RequireVersion(invoiceRow, expectedVersion);
        if (invoiceRow.Status != (int)SalesInvoiceStatus.Draft)
        {
            throw new BusinessRuleException("sales.invoice_not_draft", "Only a draft sales invoice can be deleted; a posted invoice must be reversed.");
        }

        context.SalesInvoices.Remove(invoiceRow);
        await SaveVersionedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rejects a stale caller version and makes the UPDATE itself conditional on that version,
    /// so a concurrent writer between this read and SaveChanges is also rejected.
    /// </summary>
    private void RequireVersion(SalesInvoiceRow invoiceRow, ConcurrencyToken expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(expectedVersion);
        if (!expectedVersion.Matches(invoiceRow.RowVersion))
        {
            throw new ConcurrencyConflictException("sales invoice");
        }

        context.Entry(invoiceRow).Property(static row => row.RowVersion).OriginalValue = expectedVersion.ToBytes();
    }

    private async Task SaveVersionedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("The sales invoice was changed by another operation. Reload it and try again.", exception);
        }
    }

    private static SalesInvoiceDetailResult MapDetailResult(SalesInvoiceRow r, Dictionary<Guid, string>? costCenterCodes = null)
    {
        var lines = r.Lines.OrderBy(l => l.DisplayOrder).Select(l => new SalesInvoiceLineResult(
            l.Id,
            l.SalesInvoiceId,
            l.ItemId,
            l.Item?.Code ?? string.Empty,
            l.Item?.NameArabic ?? string.Empty,
            l.UnitId,
            l.Unit?.Code ?? string.Empty,
            l.Quantity,
            l.UnitPrice,
            l.DiscountPercentage,
            l.DiscountAmount,
            l.TaxPercentage,
            l.TaxAmount,
            l.PumpCost,
            l.TransportCost,
            l.LineTotal,
            l.CostCenterId,
            l.CostCenterId.HasValue && costCenterCodes != null && costCenterCodes.TryGetValue(l.CostCenterId.Value, out var ccc) ? ccc : null,
            l.WarehouseId,
            null,
            l.Notes,
            l.DisplayOrder,
            l.ConcreteGrade) { SourceLineId = l.SourceLineId }).ToList();

        return new SalesInvoiceDetailResult(
            r.Id,
            r.CompanyId,
            r.BranchId,
            r.PatternRevisionId,
            r.PatternRevision?.Pattern?.Code ?? string.Empty,
            r.PatternRevision?.Pattern?.NameArabic ?? string.Empty,
            r.CustomerId,
            r.Customer?.Code ?? string.Empty,
            r.Customer?.NameArabic ?? string.Empty,
            r.WarehouseId,
            r.Warehouse?.Code ?? string.Empty,
            r.InvoiceNumber,
            r.InvoiceDateUtc,
            (PaymentMethod)r.PaymentMethod,
            r.CurrencyCode,
            r.ExchangeRate,
            (SalesInvoiceStatus)r.Status,
            r.CostCenterId,
            r.CostCenterId.HasValue && costCenterCodes != null && costCenterCodes.TryGetValue(r.CostCenterId.Value, out var cc) ? cc : null,
            r.Notes,
            r.SubTotal,
            r.DiscountTotal,
            r.TaxTotal,
            r.AdditionsTotal,
            r.GrandTotal,
            r.JournalEntryId,
            r.ReversedByInvoiceId,
            r.ReversesInvoiceId,
            r.CreatedByUserId,
            r.CreatedAtUtc,
            r.ApprovedByUserId,
            r.ApprovedAtUtc,
            r.PostedByUserId,
            r.PostedAtUtc,
            r.RowVersion,
            lines)
        {
            ReturnOfInvoiceId = r.ReturnOfInvoiceId,
            CustomerAccountId = r.CustomerAccountId,
            SellerCode = r.SellerCode,
            DistributorCode = r.DistributorCode,
            Adjustments = r.Adjustments.OrderBy(a => a.DisplayOrder).Select(MapAdjustment).ToList(),
        };
    }

}
