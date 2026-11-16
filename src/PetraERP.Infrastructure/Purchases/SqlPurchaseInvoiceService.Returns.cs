using System.Data;

using Microsoft.EntityFrameworkCore;

using PetraERP.Application.Common;

using PetraERP.Application.Purchases;

using PetraERP.Domain.Inventory;

using PetraERP.Domain.Purchases;

using PetraERP.Domain.Sales;

using PetraERP.Infrastructure.Inventory;



namespace PetraERP.Infrastructure.Purchases;



public sealed partial class SqlPurchaseInvoiceService

{

    public async Task<PurchaseDetailResult> ReturnAsync(Guid companyId, Guid branchId, Guid invoiceId, Guid userId, CreatePurchaseReturnCommand command,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(command);

        RequireKey(command.IdempotencyKey);

        var reason = Domain.Primitives.AuditReason.Create(command.Reason);

        var requested = NormalizeReturnLines(command.Lines);

        Guid returnId;

        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);

        await using (transaction.ConfigureAwait(false))

        {

            try

            {

                await LockScopeAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);

                // UPDLOCK on the original serializes all returns of this invoice: the over-return check below is race-free.

                var original = await LoadForUpdateAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
                // Header-level discount/addition rows are not apportioned over partial returns yet; failing closed keeps the supplier's open item exact.
                if (original.Adjustments.Any(a => a.CounterAccountId is null))
                    throw new BusinessRuleException("purchases.return_with_adjustments_unsupported",
                        "An invoice with discount or addition rows cannot be partially returned; reverse the whole invoice instead.");

                var existing = await context.PurchaseInvoices.AsNoTracking().Include(i => i.Lines)

                    .SingleOrDefaultAsync(i => i.CompanyId == companyId && i.BranchId == branchId && i.PostingKey == command.IdempotencyKey, cancellationToken).ConfigureAwait(false);

                if (existing is not null)

                {

                    var same = existing.ReturnOfInvoiceId == invoiceId && existing.VarianceAccountId == command.VarianceAccountId

                        && existing.CreatedByUserId == userId && existing.Notes == reason.Value && existing.Lines.Count == requested.Count

                        && existing.Lines.OrderBy(l => l.SourceLineId).Zip(requested).All(p => p.First.SourceLineId == p.Second.SourceLineId && p.First.Quantity == p.Second.Quantity);

                    if (!same) throw new ResourceConflictException("purchases.return_key_conflict", "The request key already belongs to a different purchase document.");

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    return await GetAsync(companyId, branchId, existing.Id, cancellationToken).ConfigureAwait(false);

                }

                if (original.ReturnOfInvoiceId is not null || original.ReversesInvoiceId is not null)

                    throw new BusinessRuleException("purchases.return_source_invalid",
                        "لا يُرتجع إلا مستند أصلي؛ المرتجع والعكس ليسا أصلًا. / Only an original purchase invoice can be returned.");

                if (original.Status != (int)PurchaseInvoiceStatus.Posted || original.JournalEntryId is null)

                    throw new BusinessRuleException("purchases.invoice_not_posted", "Only a posted purchase invoice can be returned.");



                var validation = await ValidateAsync(companyId, branchId, ToCommand(original), cancellationToken).ConfigureAwait(false);

                var now = timeProvider.GetUtcNow(); var date = DateOnly.FromDateTime(now.UtcDateTime.Date);

                var fiscalYear = await RequireOpenPeriodAsync(companyId, date, cancellationToken).ConfigureAwait(false);

                var document = await PlanReturnAsync(original, requested, userId, reason.Value, command.IdempotencyKey, now, cancellationToken).ConfigureAwait(false);

                if(original.PaymentMethod==(int)PaymentMethod.Credit)

                {

                    var open=await PetraERP.Infrastructure.Treasury.SettlementQueries.LoadInvoicesAsync(context,companyId,branchId,

                        PetraERP.Domain.Treasury.VoucherKind.Payment,original.SupplierId,original.CurrencyCode,null,cancellationToken).ConfigureAwait(false);

                    var outstanding=open.SingleOrDefault(i=>i.Id==invoiceId)?.Outstanding ?? 0m;

                    if(document.GrandTotal>outstanding)

                        throw new BusinessRuleException("purchases.return_settled", "اعكس تخصيص الدفع أولاً؛ المرتجع يتجاوز ذمة الفاتورة المتبقية / Release the payment allocation first; the return exceeds the invoice's outstanding payable.");

                }

                document.VarianceAccountId = command.VarianceAccountId;

                var lines = document.Lines.OrderBy(l => l.DisplayOrder).ToList();

                var amounts = PurchaseCosting.Allocate(original.ExchangeRate, lines.Select(l => new PurchaseLineMoney(l.LineTotal - l.TaxAmount + l.DiscountAmount, l.DiscountAmount, l.TaxAmount)).ToList());

                document.InvoiceNumber = await NextNumberAsync(companyId, branchId, userId, validation, fiscalYear, new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("PurchaseReturn:"+command.IdempotencyKey))[..16]), date, null, cancellationToken).ConfigureAwait(false);



                var movements = new List<StockMovementRow>();

                var remaining = new Dictionary<Guid, InventoryMovementGuard.Balance>();

                var wacCosts = new List<decimal>();

                var returnedItemIds = lines.Select(l => l.ItemId).Distinct().ToList();

                var fifoItems = (await context.Items.AsNoTracking().Where(x => x.CompanyId == companyId && returnedItemIds.Contains(x.Id) && x.CostMethod == (int)CostMethod.Fifo)

                    .Select(x => x.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();

                var fifoQuoter = new FifoQuoter(context);

                for (var i = 0; i < lines.Count; i++)

                {

                    var baseQuantity = await ReturnBaseQuantityAsync(original, lines[i], cancellationToken).ConfigureAwait(false);

                    if (fifoItems.Contains(lines[i].ItemId))

                    {

                        if (await fifoQuoter.AvailableAsync(companyId, original.WarehouseId, lines[i].ItemId, cancellationToken).ConfigureAwait(false) < baseQuantity)

                            throw new BusinessRuleException("inventory.negative_stock_blocked", "الرصيد لا يكفي لمرتجع المشتريات / Insufficient stock for purchase return.");

                        var fifoCost = await fifoQuoter.IssueAsync(companyId, original.WarehouseId, lines[i].ItemId, baseQuantity, cancellationToken).ConfigureAwait(false);

                        wacCosts.Add(fifoCost);

                        movements.Add(Movement(document, lines[i], StockMovementKind.PurchaseReturn, ReturnDocumentType, document.Id, document.InvoiceNumber,

                            baseQuantity, fifoCost, userId, now));

                        continue;

                    }

                    if (!remaining.TryGetValue(lines[i].ItemId, out var balance))

                        balance = await InventoryMovementGuard.BalanceAsync(context, companyId, original.WarehouseId, lines[i].ItemId, cancellationToken).ConfigureAwait(false);

                    if (balance.Quantity < baseQuantity || balance.Quantity <= 0 || balance.Value < 0)

                        throw new BusinessRuleException("inventory.negative_stock_blocked", "الرصيد لا يكفي لمرتجع المشتريات / Insufficient stock for purchase return.");

                    var wac = balance.Cost(baseQuantity);

                    wacCosts.Add(wac);

                    remaining[lines[i].ItemId] = new(balance.Quantity - baseQuantity, balance.Value - wac, balance.LastSequence);

                    movements.Add(Movement(document, lines[i], StockMovementKind.PurchaseReturn, ReturnDocumentType, document.Id, document.InvoiceNumber,

                        baseQuantity, wac, userId, now));

                }

                if (command.VarianceAccountId is { } variance && (variance == validation.InventoryAccountId

                    || !await context.Accounts.AnyAsync(a => a.Id == variance && a.CompanyId == companyId && a.IsActive && a.PostingAllowed, cancellationToken).ConfigureAwait(false)))

                    throw new BusinessRuleException("inventory.purchase_variance_account", "حساب فرق التكلفة غير فعال أو خارج الشركة أو هو حساب المخزون نفسه / The variance account is inactive, outside company scope or is the inventory account.", "varianceAccountId");

                if (wacCosts.Sum() != amounts.LineCosts.Sum() && command.VarianceAccountId is null)

                    throw new BusinessRuleException("inventory.purchase_variance_account", "اختر حساب فرق تكلفة مرتجع الشراء؛ المخزون يخرج بتكلفة WAC والذمة بالسعر الأصلي / Select a purchase return variance account; stock leaves at WAC and supplier settlement uses the original price.", "varianceAccountId");

                await RequireStockAsync(companyId, original.WarehouseId, movements, cancellationToken).ConfigureAwait(false);

                var supplier = await context.Parties.AsNoTracking().Where(p => p.Id == original.SupplierId).Select(p => p.NameArabic).SingleAsync(cancellationToken).ConfigureAwait(false);

                var resolver = await ItemAccountResolver.CreateAsync(context, companyId, lines.Select(l => l.ItemId), null, branchId, date, cancellationToken).ConfigureAwait(false);

                var journal = await CreateJournalAsync(companyId, branchId, userId, date,

                    $"مرتجع مشتريات {document.InvoiceNumber} للفاتورة {original.InvoiceNumber} — {supplier}",

                    BuildJournal(document, lines, amounts, validation, isReturn: true, wacCosts, command.VarianceAccountId, resolver), reason.Value, cancellationToken).ConfigureAwait(false);



                context.ChangeTracker.Clear();

                document.JournalEntryId = journal.Id;

                context.PurchaseInvoices.Add(document);

                context.StockMovements.AddRange(movements);

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                returnId = document.Id;

                await PostJournalAsync(companyId, branchId, userId, returnId, journal, reason.Value, cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            }

            catch { context.ChangeTracker.Clear(); throw; }

        }

        return await GetAsync(companyId, branchId, returnId, cancellationToken).ConfigureAwait(false);

    }



    private static List<PurchaseReturnLineCommand> NormalizeReturnLines(IReadOnlyList<PurchaseReturnLineCommand>? lines)

    {

        if (lines is null || lines.Count is 0 or > MaxLines)

            throw new BusinessRuleException("purchases.return_lines_required", $"A return requires between 1 and {MaxLines} lines.", "lines");

        if (lines.Select(l => l.SourceLineId).Distinct().Count() != lines.Count || lines.Any(l => l.SourceLineId == Guid.Empty))

            throw new BusinessRuleException("purchases.return_line_invalid", "Each original line can appear once per return.", "lines");

        if (lines.Any(l => l.Quantity <= 0m || decimal.Round(l.Quantity, 4) != l.Quantity))

            throw new BusinessRuleException("purchases.return_quantity_invalid", "Return quantities must be positive with at most four decimals.", "lines");

        return lines.OrderBy(l => l.SourceLineId).ToList();

    }



    /// <summary>Builds the return document at the original price, discount, tax and rate; the last return of a line takes the exact remainder.</summary>

    private async Task<PurchaseInvoiceRow> PlanReturnAsync(PurchaseInvoiceRow original, List<PurchaseReturnLineCommand> requested, Guid userId,

        string reason, string key, DateTimeOffset now, CancellationToken cancellationToken)

    {

        var sources = original.Lines.ToDictionary(l => l.Id);

        if (requested.Any(r => !sources.ContainsKey(r.SourceLineId)))

            throw new BusinessRuleException("purchases.return_line_invalid", "A return line does not belong to the original invoice.", "lines");

        var ids = requested.Select(r => r.SourceLineId).ToList();

        var previous = await context.PurchaseInvoiceLines.AsNoTracking().Where(l => l.SourceLineId != null && ids.Contains(l.SourceLineId.Value))

            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var document = new PurchaseInvoiceRow

        {

            Id = Guid.NewGuid(), CompanyId = original.CompanyId, BranchId = original.BranchId, PatternRevisionId = original.PatternRevisionId,

            SupplierId = original.SupplierId, WarehouseId = original.WarehouseId, SupplierReference = original.SupplierReference, InvoiceDateUtc = now,

            PaymentMethod = original.PaymentMethod, CurrencyCode = original.CurrencyCode, ExchangeRate = original.ExchangeRate,

            Status = (int)PurchaseInvoiceStatus.Posted, Notes = reason, ReturnOfInvoiceId = original.Id, PostingKey = key,

            CreatedByUserId = userId, PostedByUserId = userId, PostedAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now,

        };

        foreach (var request in requested)

        {

            var source = sources[request.SourceLineId];

            var earlier = previous.Where(l => l.SourceLineId == source.Id).ToList();

            var returnedQuantity = earlier.Sum(l => l.Quantity);

            if (request.Quantity > source.Quantity - returnedQuantity)

                throw new BusinessRuleException("purchases.over_return", "The return exceeds the quantity still returnable on the original line.", "lines");

            var sold = new SalesLineAmounts(decimal.Round(source.Quantity * source.UnitCost, 4, MidpointRounding.AwayFromZero), source.DiscountAmount,

                source.TaxAmount, 0m, 0m, 0m);

            var before = earlier.Aggregate(SalesLineAmounts.Zero, (sum, l) =>

                sum.Add(new(l.LineTotal - l.TaxAmount + l.DiscountAmount, l.DiscountAmount, l.TaxAmount, 0m, 0m, 0m)));

            var part = SalesReturnAllocation.Allocate(source.Quantity, sold, returnedQuantity, before, request.Quantity);

            document.Lines.Add(new PurchaseInvoiceLineRow

            {

                Id = Guid.NewGuid(), PurchaseInvoiceId = document.Id, ItemId = source.ItemId, UnitId = source.UnitId, Quantity = request.Quantity,

                UnitCost = source.UnitCost, DiscountAmount = part.Discount, TaxAmount = part.Tax, LineTotal = part.SubTotal - part.Discount + part.Tax,

                CostCenterId = source.CostCenterId, DisplayOrder = source.DisplayOrder, SourceLineId = source.Id,

            });

        }

        document.SubTotal = document.Lines.Sum(l => l.LineTotal - l.TaxAmount + l.DiscountAmount);

        document.DiscountTotal = document.Lines.Sum(l => l.DiscountAmount);

        document.TaxTotal = document.Lines.Sum(l => l.TaxAmount);

        document.GrandTotal = document.SubTotal - document.DiscountTotal + document.TaxTotal;

        return document;

    }



    /// <summary>Base quantity proportional to the original receipt; the return that exhausts the line takes the exact remainder.</summary>

    private async Task<decimal> ReturnBaseQuantityAsync(PurchaseInvoiceRow original, PurchaseInvoiceLineRow line, CancellationToken cancellationToken)

    {

        var source = original.Lines.Single(l => l.Id == line.SourceLineId);

        var receipt = await context.StockMovements.AsNoTracking().SingleOrDefaultAsync(m => m.CompanyId == original.CompanyId

            && m.DocumentType == InvoiceDocumentType && m.DocumentLineId == source.Id, cancellationToken).ConfigureAwait(false)

            ?? throw new BusinessRuleException("purchases.stock_history_invalid", "The original receipt movement for this line is missing.");

        var earlierLines = await context.PurchaseInvoiceLines.AsNoTracking().Where(l => l.SourceLineId == source.Id).Select(l => new { l.Id, l.Quantity })

            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var earlierIds = earlierLines.Select(l => (Guid?)l.Id).ToList();

        var earlierBase = await context.StockMovements.AsNoTracking().Where(m => m.CompanyId == original.CompanyId && m.DocumentType == ReturnDocumentType

            && earlierIds.Contains(m.DocumentLineId)).SumAsync(m => -m.BaseQuantity, cancellationToken).ConfigureAwait(false);

        var exhausts = earlierLines.Sum(l => l.Quantity) + line.Quantity == source.Quantity;

        var quantity = exhausts ? receipt.BaseQuantity - earlierBase : receipt.BaseQuantity * line.Quantity / source.Quantity;

        if (quantity <= 0m || decimal.Round(quantity, 4) != quantity)

            throw new BusinessRuleException("inventory.quantity_precision", "The returned base quantity must be positive and exact at stock precision.", "lines");

        return quantity;

    }



    /// <summary>Negative stock is blocked: the warehouse must still hold every returned base quantity (under the scope lock).</summary>

    private async Task RequireStockAsync(Guid companyId, Guid warehouseId, List<StockMovementRow> movements, CancellationToken cancellationToken)

    {

        foreach (var item in movements.GroupBy(m => m.ItemId))

        {

            var onHand = await context.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && m.WarehouseId == warehouseId && m.ItemId == item.Key)

                .SumAsync(m => m.BaseQuantity, cancellationToken).ConfigureAwait(false);

            if (onHand + item.Sum(m => m.BaseQuantity) < 0m)

                throw new BusinessRuleException("inventory.negative_stock_blocked", "The return would make the warehouse stock negative.", "lines");

        }

    }

}

