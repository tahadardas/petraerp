using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Patterns;
using PetraERP.Application.Sales;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Patterns;

namespace PetraERP.Infrastructure.Sales;

public sealed partial class SqlSalesInvoiceService
{
    public async Task<SalesInvoiceDetailResult> PostInvoiceAsync(Guid companyId, Guid branchId, Guid invoiceId, Guid userId,
        PostSalesInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var key = SalesRequestKey("post", command.IdempotencyKey);
        var reason = AuditReason.Create(string.IsNullOrWhiteSpace(command.Notes) ? "ترحيل فاتورة مبيعات" : command.Notes);
        var creditPayload = !string.IsNullOrWhiteSpace(command.ManualNumber) || command.ConfirmCreditWarning || command.OverrideCreditPolicy || !string.IsNullOrWhiteSpace(command.CreditOverrideReason)
            ? (string.IsNullOrWhiteSpace(command.ManualNumber)
                ? JsonSerializer.Serialize(new { command.ConfirmCreditWarning, command.OverrideCreditPolicy, Reason = command.CreditOverrideReason?.Trim() })
                : JsonSerializer.Serialize(new { command.ConfirmCreditWarning, command.OverrideCreditPolicy, Reason = command.CreditOverrideReason?.Trim(), ManualNumber = command.ManualNumber.Trim() })) : null;
        var intent = await RegisterSalesIntentAsync(companyId, branchId, invoiceId, userId, key, PostingOperation.PostJournal, reason, cancellationToken, creditPayload).ConfigureAwait(false);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                await LockSalesScopeAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);
                var invoice = await LoadInvoiceForUpdateAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
                var scope = PostingRequestScope.Create(companyId, branchId, userId);
                var registered = await context.PostingRequests.AsNoTracking().SingleOrDefaultAsync(r => r.CompanyId == companyId
                    && r.BranchId == branchId && r.Operation == (byte)PostingOperation.PostJournal && r.IdempotencyKey == key, cancellationToken).ConfigureAwait(false);
                if (registered is not null)
                {
                    if (registered.JournalId != invoice.JournalEntryId || registered.CompletedAtUtc is null)
                        throw new PostingIdempotencyConflictException(key, "The request key belongs to another invoice or incomplete operation.");
                    var replay = PostingRequest.Post(scope, key, registered.JournalId,
                        ExpectedJournalVersion.FromBase64(registered.ExpectedVersionBase64), reason);
                    await new SqlPostingService(context).PostSalesAsync(invoice.Id, replay, cancellationToken).ConfigureAwait(false);
                    var replayResult = await GetInvoiceAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return replayResult;
                }
                if (invoice.Status is not ((int)SalesInvoiceStatus.Draft or (int)SalesInvoiceStatus.Approved)
                    || !PetraERP.Domain.Documents.DocumentLifecycle.CanTransition(PetraERP.Domain.Documents.AccountingDocumentKind.SalesInvoice, PetraERP.Domain.Documents.DocumentLifecycle.From((SalesInvoiceStatus)invoice.Status), PetraERP.Domain.Documents.DocumentLifecycleState.Posted))
                    throw new BusinessRuleException("sales.invoice_cannot_post", "The invoice is not eligible for a new posting request.");
                VerifySalesIntentVersion(intent, invoice);
                var date = DateOnly.FromDateTime(invoice.InvoiceDateUtc.Date);
                var fiscalYear = await RequireOpenSalesPeriodAsync(companyId, date, cancellationToken).ConfigureAwait(false);
                await ValidateStoredReferencesAsync(invoice, cancellationToken).ConfigureAwait(false);
                ValidateStoredTotals(invoice);
                await EnforceSalesCreditPolicyAsync(invoice, userId, key, command, cancellationToken).ConfigureAwait(false);
                var revision = await context.PatternRevisions.AsNoTracking().Include(r => r.Pattern)
                    .SingleAsync(r => r.Id == invoice.PatternRevisionId, cancellationToken).ConfigureAwait(false);
                if (revision.Pattern!.CompanyId != companyId || !revision.Pattern.IsActive
                    || !await context.PatternBranchGrants.AnyAsync(g => g.PatternId == revision.PatternId && g.BranchId == branchId, cancellationToken).ConfigureAwait(false))
                    throw new BusinessRuleException("sales.pattern_invalid", "The invoice pattern is inactive or unavailable in this scope.");
                if (await context.StockMovements.AsNoTracking().AnyAsync(m => m.CompanyId == companyId && m.DocumentId == invoice.Id && m.DocumentType == "SalesInvoice", cancellationToken).ConfigureAwait(false))
                    throw new BusinessRuleException("sales.stock_already_delivered", "Stock movements have already been posted for this invoice.");

                var config = CanonicalSalesRoles(JsonSerializer.Deserialize<PatternConfiguration>(revision.ConfigurationJson) ?? new());
                if (config.ApprovalPolicy.RequiresApproval && invoice.Status != (int)SalesInvoiceStatus.Approved
                    && (!config.ApprovalPolicy.ThresholdAmount.HasValue || invoice.GrandTotal >= config.ApprovalPolicy.ThresholdAmount.Value))
                    throw new BusinessRuleException("sales.approval_required", "The pattern requires invoice approval before posting.");

                if (config.AllowedPaymentMethods.Count > 0 && !config.AllowedPaymentMethods.Contains(((PaymentMethod)invoice.PaymentMethod).ToString(), StringComparer.OrdinalIgnoreCase))
                    throw new BusinessRuleException("sales.payment_method_not_allowed", "The payment method is not permitted by the document pattern.");

                if (config.AllowedWarehouseIds.Count > 0)
                {
                    if (!config.AllowedWarehouseIds.Contains(invoice.WarehouseId))
                        throw new BusinessRuleException("sales.warehouse_not_allowed", "The invoice warehouse is not permitted by the document pattern.");
                    if (invoice.Lines.Any(l => l.WarehouseId.HasValue && !config.AllowedWarehouseIds.Contains(l.WarehouseId.Value)))
                        throw new BusinessRuleException("sales.warehouse_not_allowed", "A line warehouse is not permitted by the document pattern.");
                }

                if (config.PricingPolicy?.MaxDiscountPercentage > 0)
                {
                    if (invoice.Lines.Any(l => l.DiscountPercentage > config.PricingPolicy.MaxDiscountPercentage))
                        throw new BusinessRuleException("sales.discount_exceeds_policy", "Line discount exceeds pattern pricing policy maximum.");
                }
                var companyPolicy = await PetraERP.Infrastructure.CompanyOptions.SqlCompanyOptionsService.EffectiveAsync(context,companyId,cancellationToken).ConfigureAwait(false);
                if(companyPolicy.RequireDocumentApproval && invoice.Status != (int)SalesInvoiceStatus.Approved)
                    throw new BusinessRuleException("company.approval_required","Company policy requires document approval before posting.");
                if(!companyPolicy.AllowCreditSales && invoice.PaymentMethod == (int)PaymentMethod.Credit)
                    throw new BusinessRuleException("company.credit_not_allowed","Company policy does not allow credit documents for this operation.");
                var number = await patternService.GenerateNextNumberAsync(PatternScope.Create(companyId, branchId, userId),
                    new GetNextPatternNumberCommand { PatternId = revision.PatternId, FiscalYearId = fiscalYear, RevisionId = revision.Id, AccountingDate = date, AllocationKey = key, ManualNumber = command.ManualNumber }, cancellationToken).ConfigureAwait(false);
                var resolver = await ItemAccountResolver.CreateAsync(context, companyId, invoice.Lines.Select(l => l.ItemId), config, branchId, date, cancellationToken).ConfigureAwait(false);
                // Keep the approved header unchanged during per-line stock saves; persist its complete posted state once.
                var cost = await DeliverStockAsync(invoice, number.FormattedNumber, userId, resolver, companyPolicy, command, cancellationToken).ConfigureAwait(false);
                var company = await context.Companies.AsNoTracking().SingleAsync(c => c.Id == companyId, cancellationToken).ConfigureAwait(false);
                var customer = await context.Parties.AsNoTracking().SingleAsync(p => p.Id == invoice.CustomerId && p.CompanyId == companyId && p.IsActive, cancellationToken).ConfigureAwait(false);
                if (customer.ReceivableAccountId == null)
                {
                    await new PetraERP.Infrastructure.Parties.SqlPartyService(context).SyncMissingPartyAccountsAsync(companyId, cancellationToken).ConfigureAwait(false);
                    customer = await context.Parties.AsNoTracking().SingleAsync(p => p.Id == invoice.CustomerId && p.CompanyId == companyId && p.IsActive, cancellationToken).ConfigureAwait(false);
                }
                var journalService = new SqlJournalEntryService(context, timeProvider);
                var journalScope = JournalEntryScope.Create(companyId, branchId, userId);
                var journal = await journalService.CreateDraftAsync(journalScope, new CreateJournalDraftCommand
                {
                    AccountingDate = date,
                    Description = $"فاتورة مبيعات {number.FormattedNumber} — {customer.NameArabic}",
                    Lines = BuildSalesJournal(invoice, config, customer.ReceivableAccountId, company.BaseCurrencyCode, cost, resolver)
                }, cancellationToken).ConfigureAwait(false);
                journal = await ApproveSalesJournalAsync(journalService, journalScope, journal, reason.Value, cancellationToken).ConfigureAwait(false);
                // The canonical posting engine requires a persisted source association. Stage the complete header
                // transition under this same transaction, as purchases do, rather than intermediate approved edits.
                invoice = await LoadInvoiceForUpdateAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
                invoice.InvoiceNumber = number.FormattedNumber;
                invoice.JournalEntryId = journal.Id;
                invoice.Status = (int)SalesInvoiceStatus.Posted;
                invoice.PostedByUserId = userId; invoice.PostedAtUtc = timeProvider.GetUtcNow(); invoice.UpdatedAtUtc = timeProvider.GetUtcNow();
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                var request = PostingRequest.Post(scope, key, journal.Id, ExpectedJournalVersion.FromBase64(journal.VersionBase64), reason);
                await new SqlPostingService(context).PostSalesAsync(invoice.Id, request, cancellationToken).ConfigureAwait(false);
                await CompleteSalesIntentAsync(intent, journal.Id, cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                // Hydrate credit/settlement aggregates while the company lock still orders invoice and journal reads.
                // A post-commit SERIALIZABLE response read can otherwise deadlock with the next posting's writes.
                var result = await GetInvoiceAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch { context.ChangeTracker.Clear(); throw; }
        }
    }

    private sealed record SalesCostAllocation(Guid? CostCenterId, Guid CogsAccountId, Guid InventoryAccountId, decimal Amount);

    private static void ValidateStoredTotals(SalesInvoiceRow invoice)
    {
        var subtotal = invoice.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 4, MidpointRounding.AwayFromZero));
        var discount = invoice.Lines.Sum(l => l.DiscountAmount) + AdjustmentDiscounts(invoice);
        var tax = invoice.Lines.Sum(l => l.TaxAmount);
        var additions = invoice.Lines.Sum(l => l.PumpCost + l.TransportCost) + AdjustmentAdditions(invoice);
        if (invoice.SubTotal != subtotal || invoice.DiscountTotal != discount || invoice.TaxTotal != tax
            || invoice.AdditionsTotal != additions || invoice.GrandTotal != subtotal - discount + tax + additions)
            throw new BusinessRuleException("sales.totals_invalid", "Invoice totals must reconcile to the authoritative lines before posting.");
    }

    private async Task<List<SalesCostAllocation>> DeliverStockAsync(SalesInvoiceRow invoice, string documentNumber, Guid userId,
        ItemAccountResolver resolver, PetraERP.Application.CompanyOptions.CompanyFinancialPolicy companyPolicy, PostSalesInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        if (invoice.Lines.Count == 0) throw new BusinessRuleException("sales.lines_required", "An invoice requires lines.");
        var allocations = new List<SalesCostAllocation>();
        var negativeActorVerified = false;
        foreach (var line in invoice.Lines.OrderBy(l => l.ItemId).ThenBy(l => l.WarehouseId).ThenBy(l => l.Id))
        {
            var item = await context.Items.AsNoTracking().SingleAsync(i => i.Id == line.ItemId && i.CompanyId == invoice.CompanyId && i.IsActive, cancellationToken).ConfigureAwait(false);
            if (item.CostMethod is not ((int)CostMethod.WeightedAverage or (int)CostMethod.Fifo))
                throw new BusinessRuleException("sales.cost_method_unsupported", "The item's cost method is not supported.");
            var warehouse = line.WarehouseId ?? invoice.WarehouseId;
            var quantity = await SalesBaseQuantityAsync(invoice.CompanyId, item.Id, line.UnitId, item.BaseUnitId, line.Quantity, cancellationToken).ConfigureAwait(false);
            decimal cost;
            decimal unitCost;
            string? authorizedNegativeReason = null;
            if (item.CostMethod == (int)CostMethod.Fifo)
            {
                // FIFO: the issue is costed from the oldest receipt layers; the movement guard re-derives and verifies the same allocation.
                var quoter = new FifoQuoter(context);
                var available = await quoter.AvailableAsync(invoice.CompanyId, warehouse, item.Id, cancellationToken).ConfigureAwait(false);
                if (quantity > available)
                    throw await InsufficientStockAsync(invoice, item, warehouse, quantity, available, cancellationToken).ConfigureAwait(false);
                cost = await quoter.IssueAsync(invoice.CompanyId, warehouse, item.Id, quantity, cancellationToken).ConfigureAwait(false);
                unitCost = Math.Round(cost / quantity, 4, MidpointRounding.ToEven);
            }
            else
            {
            var stock = await context.StockMovements.AsNoTracking().Where(m => m.CompanyId == invoice.CompanyId && m.WarehouseId == warehouse && m.ItemId == item.Id)
                .GroupBy(m => m.ItemId).Select(g => new { Quantity = g.Sum(m => m.BaseQuantity), Cost = g.Sum(m => m.BaseQuantity < 0 ? -m.TotalCost : m.TotalCost) })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            var positiveStock = stock is { Quantity: > 0 } ? stock.Quantity : 0m;
            var stockValue = positiveStock > 0 ? stock!.Cost : 0m;
            if (stockValue < 0)
                throw new BusinessRuleException("sales.stock_cost_invalid", "Stock cost must reconcile before sale.");
            if (quantity > positiveStock)
            {
                if (companyPolicy.NegativeIssuePolicy != PetraERP.Application.CompanyOptions.NegativeIssuePolicy.AuthorizedException)
                    throw await InsufficientStockAsync(invoice, item, warehouse, quantity, stock?.Quantity ?? 0m, cancellationToken).ConfigureAwait(false);
                // The company enabled negative issue: an explicit reason is optional (Al-Ameen parity); a default is recorded for audit.
                var reason = command.NegativeStockReason?.Trim();
                if (string.IsNullOrWhiteSpace(reason)) reason = "سماح بالإخراج بالسالب حسب سياسة الشركة / negative issue allowed by company policy";
                if (reason.Length > 500)
                    throw new BusinessRuleException("inventory.negative_reason_required",
                        "أدخل سبباً من 1 إلى 500 حرف للسماح بالإخراج بالسالب / Enter a 1-500 character reason to allow the negative issue.",
                        "negativeStockReason");
                if (!negativeActorVerified)
                {
                    await new SqlPostingService(context).VerifyInventoryNegativeActorAsync(
                        PostingRequestScope.Create(invoice.CompanyId, invoice.BranchId, userId), cancellationToken).ConfigureAwait(false);
                    negativeActorVerified = true;
                }
                // Reference cost: last receipt in this warehouse, else last receipt in any warehouse of the company, else zero
                // (the item was never received; the operator corrects the cost with a later receipt/adjustment, as in Al-Ameen).
                var lastCost = await LastInboundUnitCostAsync(invoice.CompanyId, warehouse, item.Id, cancellationToken).ConfigureAwait(false)
                    ?? await context.StockMovements.AsNoTracking()
                        .Where(m => m.CompanyId == invoice.CompanyId && m.ItemId == item.Id && m.BaseQuantity > 0 && m.UnitCost > 0)
                        .OrderByDescending(m => m.RecordedSequence).Select(m => (decimal?)m.UnitCost)
                        .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                    ?? 0m;
                if (lastCost <= 0 && stockValue <= 0)
                    reason += " | بلا تكلفة مرجعية / no reference cost";
                var shortfall = quantity - positiveStock;
                const decimal maximum = WeightedAverageCosting.MaximumStoredValue;
                if (reason.Length > 500) reason = reason[..500];
                if (shortfall > maximum || lastCost > maximum || lastCost > 0 && shortfall > maximum / lastCost)
                    throw new BusinessRuleException("inventory.valuation_precision", "تعذر حساب تكلفة الإخراج بالسالب ضمن حدود الدقة / The negative-issue cost cannot be represented at storage precision.");
                cost = Math.Round(stockValue + Math.Round(shortfall * lastCost, 4, MidpointRounding.ToEven), 4, MidpointRounding.ToEven);
                if (cost > maximum)
                    throw new BusinessRuleException("inventory.valuation_precision", "تعذر حساب تكلفة الإخراج بالسالب ضمن حدود الدقة / The negative-issue cost cannot be represented at storage precision.");
                unitCost = Math.Round(cost / quantity, 4, MidpointRounding.ToEven);
                context.AuthorizeNegativeStock(invoice.CompanyId, invoice.BranchId, reason, warehouse, item.Id);
                authorizedNegativeReason = reason;
            }
            else
            {
                if (!WeightedAverageCosting.TryValue(positiveStock, stockValue, quantity, out cost))
                    throw new BusinessRuleException("inventory.valuation_precision", "تعذر حساب WAC ضمن حدود الدقة / WAC cannot be represented at storage precision.");
                unitCost = Math.Round(stockValue / positiveStock, 4, MidpointRounding.ToEven);
            }
            }
            
            var cogsAccount = resolver.Resolve(item.Id, "COGS");
            var inventoryAccount = resolver.Resolve(item.Id, "Inventory");
            allocations.Add(new(line.CostCenterId ?? invoice.CostCenterId, cogsAccount, inventoryAccount, cost));
            context.StockMovements.Add(new StockMovementRow
            {
                Id = Guid.NewGuid(),
                CompanyId = invoice.CompanyId,
                BranchId = invoice.BranchId,
                WarehouseId = warehouse,
                ItemId = item.Id,
                UnitId = line.UnitId,
                MovementKind = (int)StockMovementKind.SalesDelivery,
                Quantity = line.Quantity,
                BaseQuantity = -quantity,
                UnitCost = unitCost,
                TotalCost = cost,
                MovementDateUtc = invoice.InvoiceDateUtc,
                DocumentType = "SalesInvoice",
                DocumentLineId = line.Id,
                DocumentId = invoice.Id,
                DocumentNumber = documentNumber,
                CostCenterId = line.CostCenterId ?? invoice.CostCenterId,
                Notes = authorizedNegativeReason is null ? line.Notes : AuthorizedNegativeNote(line.Notes, authorizedNegativeReason),
                CreatedByUserId = userId,
                CreatedAtUtc = timeProvider.GetUtcNow()
            });
            // Subsequent lines of the same item must see the quantity/cost already consumed inside this transaction.
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return allocations;
    }

    private async Task<BusinessRuleException> InsufficientStockAsync(SalesInvoiceRow invoice, ItemRow item, Guid warehouse,
        decimal required, decimal available, CancellationToken cancellationToken)
    {
        // Name the item, warehouse and quantities: the operator cannot act on "negative stock" alone.
        var warehouseLabel = await context.Warehouses.AsNoTracking()
            .Where(w => w.Id == warehouse && w.CompanyId == invoice.CompanyId)
            .Select(w => new { w.Code, w.NameArabic })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var warehouseText = warehouseLabel is null ? warehouse.ToString() : $"{warehouseLabel.Code} — {warehouseLabel.NameArabic}";
        var requiredText = required.ToString("0.####", CultureInfo.InvariantCulture);
        var availableText = available.ToString("0.####", CultureInfo.InvariantCulture);
        return new BusinessRuleException("inventory.negative_stock_blocked",
            $"الرصيد غير كافٍ للمادة {item.Code} — {item.NameArabic} في المستودع {warehouseText}: المطلوب {requiredText} والمتاح {availableText} بالوحدة الأساسية."
            + $" / Insufficient stock for item {item.Code} in warehouse {warehouseText}: required {requiredText}, available {availableText} in the base unit.",
            "lines");
    }

    /// <summary>The last inbound unit cost is the valuation basis for quantities the warehouse never held. It is never
    /// silently zero: the operator must have received the item at least once so the negative issue has a real cost.</summary>
    private async Task<decimal?> LastInboundUnitCostAsync(Guid companyId, Guid warehouseId, Guid itemId, CancellationToken cancellationToken)
        => await context.StockMovements.AsNoTracking()
            .Where(m => m.CompanyId == companyId && m.WarehouseId == warehouseId && m.ItemId == itemId && m.BaseQuantity > 0 && m.UnitCost > 0)
            .OrderByDescending(m => m.RecordedSequence)
            .Select(m => (decimal?)m.UnitCost)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    private static string AuthorizedNegativeNote(string? lineNotes, string reason)
    {
        const int maximum = 500;
        const string prefix = "إخراج سالب مصرح / authorized negative issue: ";
        var marker = prefix + reason;
        if (marker.Length > maximum) marker = marker[..maximum];
        if (string.IsNullOrWhiteSpace(lineNotes)) return marker;
        var combined = lineNotes + " | " + marker;
        return combined.Length <= maximum ? combined : marker;
    }

    private static List<JournalDraftLineInput> BuildSalesJournal(SalesInvoiceRow invoice, PatternConfiguration config,
        Guid? receivable, string baseCurrency, List<SalesCostAllocation> costs, ItemAccountResolver resolver)
    {
        var lines = new List<JournalDraftLineInput>();
        Guid ResolvePattern(string role, Guid? party = null) => PostingRuleResolver.Resolve(role, config, partyContractAccountId: party).AccountId;
        // The account that carries the invoice total: the invoice's own account when the pattern permits that override (the
        // resolver refuses it otherwise), else the pattern cash account or the customer's receivable account.
        var cashInvoice = invoice.PaymentMethod == (int)PaymentMethod.Cash;
        Guid ResolveControl() => ControlAccount(() => PostingRuleResolver.Resolve(cashInvoice ? "Cash" : "Receivable", config,
            documentOverrideAccountId: invoice.CustomerAccountId, partyContractAccountId: cashInvoice ? null : receivable).AccountId);
        void Add(Guid account, decimal debit, decimal credit, string description, Guid? center, bool stock = false)
        {
            if (debit == 0 && credit == 0) return;
            lines.Add(new JournalDraftLineInput
            {
                AccountId = account,
                CostCenterId = center,
                TransactionCurrencyCode = stock ? baseCurrency : invoice.CurrencyCode,
                TransactionDebit = debit,
                TransactionCredit = credit,
                RateSourceCurrencyCode = stock ? baseCurrency : invoice.CurrencyCode,
                RateTargetCurrencyCode = baseCurrency,
                RateValue = stock ? 1m : invoice.ExchangeRate,
                Description = description
            });
        }
        foreach (var group in invoice.Lines.GroupBy(line => line.CostCenterId ?? invoice.CostCenterId))
        {
            var center = group.Key;
            var subtotal = group.Sum(line => Math.Round(line.Quantity * line.UnitPrice, 4, MidpointRounding.AwayFromZero));
            var discount = group.Sum(line => line.DiscountAmount);
            var tax = group.Sum(line => line.TaxAmount);
            var additions = group.Sum(line => line.PumpCost + line.TransportCost);
            Add(ResolveControl(), subtotal - discount + tax + additions, 0, "ذمم/نقد البيع", center);
        }

        foreach (var group in invoice.Lines.GroupBy(line => (AccountId: resolver.Resolve(line.ItemId, "Revenue"), CostCenterId: line.CostCenterId ?? invoice.CostCenterId)))
        {
            var subtotal = group.Sum(line => Math.Round(line.Quantity * line.UnitPrice, 4, MidpointRounding.AwayFromZero));
            Add(group.Key.AccountId, 0, subtotal, "إيراد البيع", group.Key.CostCenterId);
        }

        foreach (var group in invoice.Lines.Where(l => l.DiscountAmount > 0).GroupBy(line => (AccountId: resolver.Resolve(line.ItemId, "Discount"), CostCenterId: line.CostCenterId ?? invoice.CostCenterId)))
        {
            var discount = group.Sum(line => line.DiscountAmount);
            Add(group.Key.AccountId, discount, 0, "حسم البيع", group.Key.CostCenterId);
        }

        foreach (var group in invoice.Lines.Where(l => l.TaxAmount > 0).GroupBy(line => (AccountId: resolver.Resolve(line.ItemId, "Tax"), CostCenterId: line.CostCenterId ?? invoice.CostCenterId)))
        {
            var tax = group.Sum(line => line.TaxAmount);
            Add(group.Key.AccountId, 0, tax, "ضريبة البيع", group.Key.CostCenterId);
        }

        foreach (var group in invoice.Lines.Where(l => (l.PumpCost + l.TransportCost) > 0).GroupBy(line => line.CostCenterId ?? invoice.CostCenterId))
        {
            var additions = group.Sum(line => line.PumpCost + line.TransportCost);
            var additionsRole = config.AccountMappings.Any(m => string.Equals(m.Role, "TransportRevenue", StringComparison.OrdinalIgnoreCase)) ? "TransportRevenue" : "Revenue";
            Add(ResolvePattern(additionsRole), 0, additions, "أجور الضخ والنقل", group.Key);
        }

        // Document-level discounts and additions: each row books its own account against the customer's receivable (or cash), so the
        // receivable debit still equals the invoice's grand total.
        var counterAccount = ResolveControl();
        foreach (var adjustment in invoice.Adjustments.OrderBy(a => a.DisplayOrder))
        {
            if (adjustment.CounterAccountId is { } counterAccountId)
            {
                // Booked directly between its two accounts: the customer's balance and the invoice total are untouched.
                if (adjustment.Kind == (int)SalesInvoiceAdjustmentKind.Discount)
                {
                    Add(adjustment.AccountId, adjustment.Amount, 0, adjustment.Notes ?? "حسم على الفاتورة", adjustment.CostCenterId);
                    Add(counterAccountId, 0, adjustment.Amount, adjustment.Notes ?? "حسم على الفاتورة (حساب مقابل)", null);
                }
                else
                {
                    Add(counterAccountId, adjustment.Amount, 0, adjustment.Notes ?? "إضافة على الفاتورة (حساب مقابل)", null);
                    Add(adjustment.AccountId, 0, adjustment.Amount, adjustment.Notes ?? "إضافة على الفاتورة", adjustment.CostCenterId);
                }

                continue;
            }

            var counterCenter = adjustment.CostCenterId ?? invoice.CostCenterId;
            if (adjustment.Kind == (int)SalesInvoiceAdjustmentKind.Discount)
            {
                Add(adjustment.AccountId, adjustment.Amount, 0, adjustment.Notes ?? "حسم على الفاتورة", adjustment.CostCenterId);
                Add(counterAccount, 0, adjustment.Amount, "تخفيض الذمة/النقد بحسم الفاتورة", counterCenter);
            }
            else
            {
                Add(counterAccount, adjustment.Amount, 0, "زيادة الذمة/النقد بإضافة على الفاتورة", counterCenter);
                Add(adjustment.AccountId, 0, adjustment.Amount, adjustment.Notes ?? "إضافة على الفاتورة", adjustment.CostCenterId);
            }
        }

        foreach (var group in costs.GroupBy(allocation => (allocation.CostCenterId, allocation.CogsAccountId, allocation.InventoryAccountId)))
        {
            var cost = group.Sum(allocation => allocation.Amount);
            Add(group.Key.CogsAccountId, cost, 0, "تكلفة البضاعة المباعة", group.Key.CostCenterId, stock: true);
            Add(group.Key.InventoryAccountId, 0, cost, "مخزون مباع", group.Key.CostCenterId, stock: true);
        }
        return lines;
    }
}
