using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Patterns;
using PetraERP.Application.Purchases;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Purchases;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Purchases;

public sealed partial class SqlPurchaseInvoiceService(FinancialDbContext context, IPatternService patternService, TimeProvider timeProvider)
    : IPurchaseInvoiceService
{
    private const int MaxLines = 200;

    public async Task<PurchasePageResult> ListAsync(Guid companyId, Guid branchId, PurchaseSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Page < 1 || query.PageSize is < 1 or > 200 || (long)(query.Page - 1) * query.PageSize > int.MaxValue)
            throw new RequestValidationException("purchases.page", "Page must be positive and pageSize between 1 and 200.", "page");
        var rows = context.PurchaseInvoices.AsNoTracking().Where(i => i.CompanyId == companyId && i.BranchId == branchId);
        if (query.SupplierId is { } supplier) rows = rows.Where(i => i.SupplierId == supplier);
        if (query.Status is { } status) rows = rows.Where(i => i.Status == (int)status);
        if (query.FromDateUtc is { } from) rows = rows.Where(i => i.InvoiceDateUtc >= from);
        if (query.ToDateUtc is { } to) rows = rows.Where(i => i.InvoiceDateUtc <= to);
        if (query.ReturnOfInvoiceId is { } original) rows = rows.Where(i => i.ReturnOfInvoiceId == original);
        var total = await rows.CountAsync(cancellationToken).ConfigureAwait(false);
        var rawItems = await rows.OrderByDescending(i => i.InvoiceDateUtc).ThenBy(i => i.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(i => new PurchaseSummaryResult(i.Id, i.InvoiceNumber, i.SupplierReference, i.InvoiceDateUtc, i.SupplierId, i.Supplier!.NameArabic,
                i.CurrencyCode, i.GrandTotal, (PurchaseInvoiceStatus)i.Status, i.ReturnOfInvoiceId, i.RowVersion))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var creditSupplierIds = rawItems.Where(i => i.Status == PurchaseInvoiceStatus.Posted && i.ReturnOfInvoiceId == null)
            .Select(i => i.SupplierId).Distinct().ToList();
        var settlementMap = new Dictionary<Guid, (decimal Paid, decimal Returned, decimal Outstanding)>();
        if (creditSupplierIds.Count > 0)
        {
            var pageInvoices = await PetraERP.Infrastructure.Treasury.SettlementQueries.LoadInvoicesAsync(
                context, companyId, branchId, PetraERP.Domain.Treasury.VoucherKind.Payment, null, null, null, cancellationToken, creditSupplierIds).ConfigureAwait(false);
            foreach (var inv in pageInvoices)
            {
                settlementMap[inv.Id] = (inv.Paid, inv.Returned, inv.Outstanding);
            }
        }

        var items = rawItems.Select(i => {
            decimal? settled = null, returnedAmt = null, outstanding = null;
            if (settlementMap.TryGetValue(i.Id, out var s))
            {
                settled = s.Paid;
                returnedAmt = s.Returned;
                outstanding = s.Outstanding;
            }
            else if (i.Status == PurchaseInvoiceStatus.Posted && i.ReturnOfInvoiceId == null)
            {
                settled = 0m;
                returnedAmt = 0m;
                outstanding = i.GrandTotal;
            }
            return i with { SettledAmount = settled, ReturnedAmount = returnedAmt, OutstandingAmount = outstanding };
        }).ToList();

        return new(query.Page, query.PageSize, total, items);
    }

    public async Task<PurchaseDetailResult> GetAsync(Guid companyId, Guid branchId, Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var r = await context.PurchaseInvoices.AsNoTracking()
            .Include(i => i.PatternRevision!).ThenInclude(p => p.Pattern)
            .Include(i => i.Supplier).Include(i => i.Warehouse)
            .Include(i => i.Lines).ThenInclude(l => l.Item)
            .Include(i => i.Lines).ThenInclude(l => l.Unit)
            .Include(i => i.Adjustments).ThenInclude(a => a.Account)
            .AsSplitQuery()
            .SingleOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == companyId && i.BranchId == branchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("PurchaseInvoice");
        var lineIds = r.Lines.Select(l => l.Id).ToList();
        var returned = await context.PurchaseInvoiceLines.AsNoTracking().Where(l => l.SourceLineId != null && lineIds.Contains(l.SourceLineId.Value))
            .GroupBy(l => l.SourceLineId!.Value).Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(g => g.Key, g => g.Quantity, cancellationToken).ConfigureAwait(false);

        decimal? settled = null, returnedAmt = null, outstanding = null;
        if (r.Status == (int)PurchaseInvoiceStatus.Posted && r.PaymentMethod == (int)PaymentMethod.Credit && r.ReturnOfInvoiceId == null)
        {
            var supplierInvoices = await PetraERP.Infrastructure.Treasury.SettlementQueries.LoadInvoicesAsync(
                context, companyId, branchId, PetraERP.Domain.Treasury.VoucherKind.Payment, r.SupplierId, null, null, cancellationToken,
                documentId: r.Id).ConfigureAwait(false);
            var s = supplierInvoices.SingleOrDefault(inv => inv.Id == r.Id);
            settled = s?.Paid ?? 0m;
            returnedAmt = s?.Returned ?? 0m;
            outstanding = s?.Outstanding ?? 0m;
        }

        return new PurchaseDetailResult(r.Id, r.CompanyId, r.BranchId, r.PatternRevisionId, r.PatternRevision?.Pattern?.Code ?? string.Empty,
            r.PatternRevision?.Pattern?.NameArabic ?? string.Empty, r.SupplierId, r.Supplier?.Code ?? string.Empty, r.Supplier?.NameArabic ?? string.Empty,
            r.WarehouseId, r.Warehouse?.Code ?? string.Empty, r.InvoiceNumber, r.SupplierReference, r.InvoiceDateUtc, (PaymentMethod)r.PaymentMethod,
            r.CurrencyCode, r.ExchangeRate, (PurchaseInvoiceStatus)r.Status, r.Notes, r.SubTotal, r.DiscountTotal, r.TaxTotal, r.GrandTotal,
            r.JournalEntryId, r.ReturnOfInvoiceId, r.PostedAtUtc, r.RowVersion,
            r.Lines.OrderBy(l => l.DisplayOrder).ThenBy(l => l.Id).Select(l => new PurchaseLineResult(l.Id, l.ItemId, l.Item?.Code ?? string.Empty,
                l.Item?.NameArabic ?? string.Empty, l.UnitId, l.Unit?.Code ?? string.Empty, l.Quantity, l.UnitCost, l.DiscountAmount, l.TaxAmount,
                l.LineTotal, l.CostCenterId, l.Notes, l.DisplayOrder, l.SourceLineId, returned.GetValueOrDefault(l.Id))).ToList())
        {
            VarianceAccountId = r.VarianceAccountId,
            ReversedByInvoiceId = r.ReversedByInvoiceId,
            ReversesInvoiceId = r.ReversesInvoiceId,
            AdditionsTotal = r.AdditionsTotal,
            SellerCode = r.SellerCode,
            DistributorCode = r.DistributorCode,
            Adjustments = r.Adjustments.OrderBy(a => a.DisplayOrder).Select(a => new PurchaseAdjustmentResult(a.Id, (PetraERP.Domain.Sales.SalesInvoiceAdjustmentKind)a.Kind, a.AccountId,
                a.Account?.Code ?? string.Empty, a.Account?.NameArabic ?? string.Empty, a.Amount, a.Percentage, a.Notes, a.CostCenterId, a.DisplayOrder, a.CounterAccountId)).ToList(),
            SettledAmount = settled,
            ReturnedAmount = returnedAmt,
            OutstandingAmount = outstanding
        };
    }

    public async Task<PurchaseDetailResult> CreateDraftAsync(Guid companyId, Guid branchId, Guid userId, SavePurchaseInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        command = command with { PatternRevisionId = await PetraERP.Infrastructure.CompanyOptions.SqlCompanyOptionsService.ResolveDefaultRevisionAsync(context,companyId,command.PatternRevisionId,PetraERP.Domain.Patterns.BusinessOperationKind.PurchaseInvoice,cancellationToken).ConfigureAwait(false) };
        var currencyTransaction = await PetraERP.Infrastructure.Currencies.CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var currencyTransactionLifetime = currencyTransaction.ConfigureAwait(false);
        var validation = await ValidateAsync(companyId, branchId, command, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var row = new PurchaseInvoiceRow
        {
            Id = command.DocumentId ?? Guid.NewGuid(), CompanyId = companyId, BranchId = branchId, Status = (int)PurchaseInvoiceStatus.Draft, CreatedByUserId = userId, CreatedAtUtc = now,
        };
        Apply(row, command with { CurrencyCode = validation.Currency, ExchangeRate = validation.Rate }, now);
        context.PurchaseInvoices.Add(row);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await currencyTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(companyId, branchId, row.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PurchaseDetailResult> UpdateDraftAsync(Guid companyId, Guid branchId, Guid invoiceId, SavePurchaseInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var currencyTransaction = await PetraERP.Infrastructure.Currencies.CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var currencyTransactionLifetime = currencyTransaction.ConfigureAwait(false);
        var expected = command.ExpectedVersion ?? throw new RequestValidationException("purchases.version_required", "ExpectedVersion is required.", "expectedVersion");
        var row = await LoadDraftAsync(companyId, branchId, invoiceId, expected, cancellationToken).ConfigureAwait(false);
        var validation = await ValidateAsync(companyId, branchId, command, cancellationToken).ConfigureAwait(false);
        context.PurchaseInvoiceLines.RemoveRange(row.Lines);
        row.Lines.Clear();
        context.PurchaseInvoiceAdjustments.RemoveRange(row.Adjustments);
        row.Adjustments.Clear();
        Apply(row, command with { CurrencyCode = validation.Currency, ExchangeRate = validation.Rate }, timeProvider.GetUtcNow());
        await SaveVersionedAsync(cancellationToken).ConfigureAwait(false);
        await currencyTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PurchaseDetailResult> ApproveAsync(Guid companyId, Guid branchId, Guid invoiceId, Guid userId, ApprovePurchaseInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var row = await LoadDraftAsync(companyId, branchId, invoiceId, command.ExpectedVersion, cancellationToken).ConfigureAwait(false);
        await ValidateAsync(companyId, branchId, ToCommand(row), cancellationToken).ConfigureAwait(false);
        PetraERP.Domain.Documents.DocumentLifecycle.EnsureTransition(PetraERP.Domain.Documents.AccountingDocumentKind.PurchaseInvoice, PetraERP.Domain.Documents.DocumentLifecycle.From((PurchaseInvoiceStatus)row.Status), PetraERP.Domain.Documents.DocumentLifecycleState.Approved);
        row.Status = (int)PurchaseInvoiceStatus.Approved;
        row.UpdatedAtUtc = timeProvider.GetUtcNow();
        await SaveVersionedAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteDraftAsync(Guid companyId, Guid branchId, Guid invoiceId, ConcurrencyToken expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedVersion);
        var row = await LoadDraftAsync(companyId, branchId, invoiceId, expectedVersion, cancellationToken).ConfigureAwait(false);
        context.PurchaseInvoices.Remove(row);
        await SaveVersionedAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<PurchaseInvoiceRow> LoadDraftAsync(Guid companyId, Guid branchId, Guid invoiceId, ConcurrencyToken expected, CancellationToken cancellationToken)
    {
        var row = await context.PurchaseInvoices.Include(i => i.Lines).Include(i => i.Adjustments)
            .SingleOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == companyId && i.BranchId == branchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("PurchaseInvoice");
        if (!expected.Matches(row.RowVersion)) throw new ConcurrencyConflictException("purchase invoice");
        if (row.Status != (int)PurchaseInvoiceStatus.Draft || PetraERP.Domain.Documents.DocumentLifecycle.IsImmutable(PetraERP.Domain.Documents.DocumentLifecycle.From((PurchaseInvoiceStatus)row.Status)))
            throw new BusinessRuleException("purchases.invoice_not_draft", "Only a draft purchase invoice can be changed or deleted.");
        context.Entry(row).Property(static r => r.RowVersion).OriginalValue = expected.ToBytes();
        return row;
    }

    private async Task SaveVersionedAsync(CancellationToken cancellationToken)
    {
        try { await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false); }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("The purchase invoice was changed by another operation. Reload it and try again.", exception);
        }
    }

    private static void Apply(PurchaseInvoiceRow row, SavePurchaseInvoiceCommand command, DateTimeOffset now)
    {
        row.PatternRevisionId = command.PatternRevisionId; row.SupplierId = command.SupplierId; row.WarehouseId = command.WarehouseId;
        row.InvoiceDateUtc = command.InvoiceDateUtc.ToUniversalTime(); row.PaymentMethod = (int)command.PaymentMethod;
        // Always the resolved values (see ValidateAsync): the rate actually used is stored and never re-read from the register.
        row.CurrencyCode = command.CurrencyCode!; row.ExchangeRate = command.ExchangeRate!.Value;
        row.SupplierReference = string.IsNullOrWhiteSpace(command.SupplierReference) ? null : command.SupplierReference.Trim();
        row.Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim();
        row.SellerCode = command.SellerCode; row.DistributorCode = command.DistributorCode;
        row.UpdatedAtUtc = now;
        var order = 0;
        foreach (var l in command.Lines)
        {
            var money = PurchaseLineMoney.Create(l.Quantity, l.UnitCost, l.DiscountAmount, l.TaxAmount);
            row.Lines.Add(new PurchaseInvoiceLineRow
            {
                Id = Guid.NewGuid(), PurchaseInvoiceId = row.Id, ItemId = l.ItemId, UnitId = l.UnitId, Quantity = l.Quantity, UnitCost = l.UnitCost,
                DiscountAmount = l.DiscountAmount, TaxAmount = l.TaxAmount, LineTotal = money.Total, CostCenterId = l.CostCenterId,
                Notes = string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim(), DisplayOrder = order++,
            });
        }
        var adjustmentOrder = 0;
        foreach (var a in command.Adjustments ?? [])
            row.Adjustments.Add(new PurchaseInvoiceAdjustmentRow
            {
                Id = Guid.NewGuid(), PurchaseInvoiceId = row.Id, Kind = (int)a.Kind, AccountId = a.AccountId, CounterAccountId = a.CounterAccountId, Amount = a.Amount,
                Percentage = a.Percentage, Notes = string.IsNullOrWhiteSpace(a.Notes) ? null : a.Notes.Trim(), CostCenterId = a.CostCenterId,
                DisplayOrder = a.DisplayOrder != 0 ? a.DisplayOrder : ++adjustmentOrder,
            });
        // Rows with a counter account are booked between two accounts and never touch the supplier's balance or the totals.
        var counted = row.Adjustments.Where(a => a.CounterAccountId is null).ToList();
        row.SubTotal = row.Lines.Sum(l => decimal.Round(l.Quantity * l.UnitCost, 4, MidpointRounding.AwayFromZero));
        row.DiscountTotal = row.Lines.Sum(l => l.DiscountAmount) + counted.Where(a => a.Kind == (int)PetraERP.Domain.Sales.SalesInvoiceAdjustmentKind.Discount).Sum(a => a.Amount);
        row.TaxTotal = row.Lines.Sum(l => l.TaxAmount);
        row.AdditionsTotal = counted.Where(a => a.Kind == (int)PetraERP.Domain.Sales.SalesInvoiceAdjustmentKind.Addition).Sum(a => a.Amount);
        row.GrandTotal = row.SubTotal - row.DiscountTotal + row.TaxTotal + row.AdditionsTotal;
        if (row.GrandTotal < 0m)
            throw new BusinessRuleException("purchases.adjustments_exceed_total", "The discounts exceed the invoice total.", "adjustments");
    }

    /// <summary>Server validation, repeated under lock when posting. Returns the resolved settlement account and pattern settings.</summary>
    private async Task<PurchaseValidation> ValidateAsync(Guid companyId, Guid branchId, SavePurchaseInvoiceCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.PaymentMethod))
            throw new RequestValidationException("purchases.payment_method_invalid", "The payment method must be Credit or Cash.", "paymentMethod");
        var lines = command.Lines ?? [];
        if (lines.Count is 0 or > MaxLines)
            throw new BusinessRuleException("purchases.lines_required", $"A purchase invoice requires between 1 and {MaxLines} lines.", "lines");
        foreach (var line in lines) Rule(() => PurchaseLineMoney.Create(line.Quantity, line.UnitCost, line.DiscountAmount, line.TaxAmount), "lines");

        var revision = await context.PatternRevisions.AsNoTracking().Include(r => r.Pattern)
            .SingleOrDefaultAsync(r => r.Id == command.PatternRevisionId && r.Pattern!.CompanyId == companyId, cancellationToken).ConfigureAwait(false);
        if (revision is null || revision.State != (int)PatternRevisionState.Published || !revision.Pattern!.IsActive
            || revision.Pattern.OperationKind != (int)BusinessOperationKind.PurchaseInvoice
            || !await context.PatternBranchGrants.AnyAsync(g => g.PatternId == revision.PatternId && g.BranchId == branchId, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("purchases.pattern_invalid", "The pattern must be a published, active purchase pattern for this branch.", "patternRevisionId");
        var config = JsonSerializer.Deserialize<PatternConfiguration>(revision.ConfigurationJson) ?? new();

        var supplier = await context.Parties.AsNoTracking().SingleOrDefaultAsync(p => p.Id == command.SupplierId && p.CompanyId == companyId, cancellationToken).ConfigureAwait(false);
        if (supplier is null || !supplier.IsActive || supplier.Role is not (2 or 3))
            throw new BusinessRuleException("purchases.supplier_invalid", "A purchase requires an active supplier of this company.", "supplierId");
        if (supplier.PayableAccountId == null)
        {
            await new PetraERP.Infrastructure.Parties.SqlPartyService(context).SyncMissingPartyAccountsAsync(companyId, cancellationToken).ConfigureAwait(false);
            supplier = await context.Parties.AsNoTracking().SingleOrDefaultAsync(p => p.Id == command.SupplierId && p.CompanyId == companyId, cancellationToken).ConfigureAwait(false) ?? supplier;
        }
        var settlement = command.PaymentMethod == PaymentMethod.Cash
            ? Rule(() => PostingRuleResolver.Resolve("Cash", config).AccountId, "paymentMethod")
            : Rule(() => PostingRuleResolver.Resolve("Payable", config, partyContractAccountId: supplier.PayableAccountId).AccountId, "supplierId");
        var inventory = Rule(() => PostingRuleResolver.Resolve("Inventory", config).AccountId, "patternRevisionId");
        Guid? tax = lines.Any(l => l.TaxAmount > 0) ? Rule(() => PostingRuleResolver.Resolve("Tax", config).AccountId, "patternRevisionId") : null;

        if (!await context.Warehouses.AnyAsync(w => w.Id == command.WarehouseId && w.CompanyId == companyId && w.BranchId == branchId && w.IsActive, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("purchases.warehouse_invalid", "The warehouse must be active in this company and branch.", "warehouseId");
        var currency = await CompanyCurrencies.ResolveAsync(context, companyId, "purchases", command.CurrencyCode, command.ExchangeRate,
            DateOnly.FromDateTime(command.InvoiceDateUtc.UtcDateTime), supplier.DefaultCurrencyCode,
            PatternCurrencyDefaults.DefaultCurrency(revision.ConfigurationJson), cancellationToken).ConfigureAwait(false);

        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await context.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id) && i.CompanyId == companyId)
            .ToDictionaryAsync(i => i.Id, cancellationToken).ConfigureAwait(false);
        var unitIds = lines.Select(l => l.UnitId).Distinct().ToList();
        var units = await context.UnitsOfMeasure.AsNoTracking().CountAsync(u => unitIds.Contains(u.Id) && u.CompanyId == companyId, cancellationToken).ConfigureAwait(false);
        if (units != unitIds.Count)
            throw new BusinessRuleException("purchases.unit_invalid", "Every unit must belong to this company.", "lines");
        foreach (var line in lines)
        {
            if (!items.TryGetValue(line.ItemId, out var item) || !item.IsActive)
                throw new BusinessRuleException("purchases.item_invalid", "Every item must be active in this company.", "lines");
            if (item.CostMethod is not ((int)CostMethod.WeightedAverage or (int)CostMethod.Fifo))
                throw new BusinessRuleException("purchases.cost_method_unsupported", "The item's cost method is not supported.", "lines");
            await Inventory.InventoryUnits.BaseQuantityAsync(context, companyId, item.Id, line.UnitId, item.BaseUnitId, line.Quantity, cancellationToken).ConfigureAwait(false);
        }
        var centers = lines.Where(l => l.CostCenterId.HasValue).Select(l => l.CostCenterId!.Value).Distinct().ToList();
        if (centers.Count > 0 && await context.CostCenters.CountAsync(c => centers.Contains(c.Id) && c.CompanyId == companyId && c.IsActive, cancellationToken).ConfigureAwait(false) != centers.Count)
            throw new BusinessRuleException("purchases.cost_center_invalid", "Every cost center must be active in this company.", "lines");
        await PetraERP.Infrastructure.Sales.InvoiceAdjustmentRules.ValidateAsync(context, companyId, currency.CurrencyCode, "purchases",
            (command.Adjustments ?? []).Select(a => new PetraERP.Infrastructure.Sales.AdjustmentInput(a.Kind, a.AccountId, a.CounterAccountId, a.Amount, a.Percentage, a.CostCenterId)).ToList(), cancellationToken).ConfigureAwait(false);
        return new(currency.BaseCurrencyCode, settlement, inventory, tax, revision.PatternId, revision.Id) { Currency = currency.CurrencyCode, Rate = currency.Rate };
    }

    private static T Rule<T>(Func<T> action, string field)
    {
        try { return action(); }
        catch (InvalidOperationException exception) when (RuleViolation.CodeOf(exception) is { } code)
        {
            throw new BusinessRuleException(code, exception.Message, field);
        }
    }

    private sealed record PurchaseValidation(string BaseCurrency, Guid SettlementAccountId, Guid InventoryAccountId, Guid? TaxAccountId,
        Guid PatternId, Guid RevisionId)
    {
        /// <summary>The resolved document currency and the rate actually used.</summary>
        public string Currency { get; init; } = string.Empty;
        public decimal Rate { get; init; } = 1m;
    }
}
