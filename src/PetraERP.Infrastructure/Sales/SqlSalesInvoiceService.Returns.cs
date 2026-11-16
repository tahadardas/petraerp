using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

namespace PetraERP.Infrastructure.Sales;

public sealed partial class SqlSalesInvoiceService
{
    internal const string ReturnDocumentType = "SalesReturn";
    private const int MaxReturnLines = 200;

    public async Task<SalesInvoiceDetailResult> ReturnInvoiceAsync(Guid companyId, Guid branchId, Guid invoiceId, Guid userId,
        CreateSalesReturnCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = AuditReason.Create(command.Reason);
        var requested = NormalizeReturnLines(command.Lines);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            requested.Select(r => new[] { r.SourceLineId.ToString("D"), r.Quantity.ToString("F4", CultureInfo.InvariantCulture) })))));
        var key = SalesRequestKey("return", command.IdempotencyKey);
        var intent = await RegisterSalesIntentAsync(companyId, branchId, invoiceId, userId, key, PostingOperation.PostJournal, reason,
            cancellationToken, digest).ConfigureAwait(false);
        Guid returnId;
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                await LockSalesScopeAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);
                // UPDLOCK on the original serializes every return/reversal of this invoice: the over-return check below is race-free.
                var original = await LoadInvoiceForUpdateAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
                if (intent.CompletedAtUtc is not null)
                {
                    var done = await context.SalesInvoices.AsNoTracking().SingleOrDefaultAsync(i => i.CompanyId == companyId && i.BranchId == branchId
                        && i.ReturnOfInvoiceId == invoiceId && i.JournalEntryId == intent.OutcomeJournalId, cancellationToken).ConfigureAwait(false)
                        ?? throw new PostingIdempotencyConflictException(key, "The return request key is inconsistent with its invoice.");
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return await GetInvoiceAsync(companyId, branchId, done.Id, cancellationToken).ConfigureAwait(false);
                }
                if (original.ReturnOfInvoiceId.HasValue || original.ReversesInvoiceId.HasValue)
                    throw new BusinessRuleException("sales.return_source_invalid", "Only an original invoice can be returned.");
                if (original.ReversedByInvoiceId.HasValue)
                    throw new BusinessRuleException("sales.invoice_reversed", "A fully reversed invoice cannot also be returned.");
                if (original.Status != (int)SalesInvoiceStatus.Posted || original.JournalEntryId is null)
                    throw new BusinessRuleException("sales.invoice_not_posted", "Only a posted invoice can be returned.");
                // Header-level discounts/additions are not apportioned over partial returns yet; failing closed keeps the customer's open item exact.
                if (original.Adjustments.Any(a => a.CounterAccountId is null))
                    throw new BusinessRuleException("sales.return_with_adjustments_unsupported",
                        "An invoice with discount or addition rows cannot be partially returned; reverse the whole invoice instead.");
                VerifySalesIntentVersion(intent, original);

                var now = timeProvider.GetUtcNow(); var date = DateOnly.FromDateTime(now.UtcDateTime.Date);
                var fiscalYear = await RequireOpenSalesPeriodAsync(companyId, date, cancellationToken).ConfigureAwait(false);
                var plan = await PlanReturnLinesAsync(original, requested, cancellationToken).ConfigureAwait(false);
                if (original.PaymentMethod == (int)PaymentMethod.Credit)
                {
                    var settlements = await PetraERP.Infrastructure.Treasury.SettlementQueries.LoadInvoicesAsync(context, companyId, branchId,
                        PetraERP.Domain.Treasury.VoucherKind.Receipt, original.CustomerId, null, null, cancellationToken).ConfigureAwait(false);
                    if (plan.Sum(l => l.Amounts.LineTotal) > settlements.Single(i => i.Id == original.Id).Outstanding)
                        throw new BusinessRuleException("sales.return_exceeds_unsettled", "قيمة المرتجع تتجاوز غير المسدد؛ اعكس أو حرر تخصيصات القبض أولاً / The return exceeds the unsettled amount; reverse or release receipt settlements first.");
                }

                var revision = await context.PatternRevisions.AsNoTracking().SingleAsync(r => r.Id == original.PatternRevisionId, cancellationToken).ConfigureAwait(false);
                var config = CanonicalSalesRoles(JsonSerializer.Deserialize<PatternConfiguration>(revision.ConfigurationJson) ?? new());
                var number = await patternService.GenerateNextNumberAsync(PatternScope.Create(companyId, branchId, userId),
                    new GetNextPatternNumberCommand { PatternId = revision.PatternId, FiscalYearId = fiscalYear, RevisionId = revision.Id, AccountingDate = date, AllocationKey = key }, cancellationToken).ConfigureAwait(false);
                var company = await context.Companies.AsNoTracking().SingleAsync(c => c.Id == companyId, cancellationToken).ConfigureAwait(false);
                var customer = await context.Parties.AsNoTracking().SingleAsync(p => p.Id == original.CustomerId && p.CompanyId == companyId, cancellationToken).ConfigureAwait(false);

                var journalService = new SqlJournalEntryService(context, timeProvider);
                var journalScope = JournalEntryScope.Create(companyId, branchId, userId);
                var journal = await journalService.CreateDraftAsync(journalScope, new CreateJournalDraftCommand
                {
                    AccountingDate = date,
                    Description = $"مرتجع مبيعات {number.FormattedNumber} للفاتورة {original.InvoiceNumber} — {customer.NameArabic}",
                    Lines = BuildReturnJournal(original, plan, config, customer.ReceivableAccountId, company.BaseCurrencyCode)
                }, cancellationToken).ConfigureAwait(false);
                journal = await ApproveSalesJournalAsync(journalService, journalScope, journal, reason.Value, cancellationToken).ConfigureAwait(false);

                var document = CreateReturnDocument(original, plan, journal.Id, number.FormattedNumber, userId, reason.Value, now);
                context.SalesInvoices.Add(document);
                foreach (var (line, row) in plan.Zip(document.Lines))
                {
                    context.StockMovements.Add(new StockMovementRow
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        BranchId = branchId,
                        WarehouseId = line.Delivery.WarehouseId,
                        ItemId = line.Source.ItemId,
                        UnitId = line.Source.UnitId,
                        MovementKind = (int)StockMovementKind.SalesReturn,
                        Quantity = line.Quantity,
                        BaseQuantity = line.BaseQuantity,
                        UnitCost = line.Delivery.UnitCost,
                        TotalCost = line.Amounts.Cost,
                        MovementDateUtc = now,
                        DocumentType = ReturnDocumentType,
                        DocumentId = document.Id,
                        DocumentLineId = row.Id,
                        DocumentNumber = document.InvoiceNumber,
                        CostCenterId = line.Delivery.CostCenterId,
                        Notes = reason.Value,
                        CreatedByUserId = userId,
                        CreatedAtUtc = now
                    });
                }
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                returnId = document.Id;

                var request = PostingRequest.Post(PostingRequestScope.Create(companyId, branchId, userId), key, journal.Id,
                    ExpectedJournalVersion.FromBase64(journal.VersionBase64), reason);
                await new SqlPostingService(context).PostSalesAsync(returnId, request, cancellationToken).ConfigureAwait(false);
                await CompleteSalesIntentAsync(intent, journal.Id, cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch { context.ChangeTracker.Clear(); throw; }
        }
        return await GetInvoiceAsync(companyId, branchId, returnId, cancellationToken).ConfigureAwait(false);
    }

    private sealed record PlannedReturnLine(SalesInvoiceLineRow Source, StockMovementRow Delivery, decimal Quantity,
        decimal BaseQuantity, SalesLineAmounts Amounts);

    private static List<SalesReturnLineCommand> NormalizeReturnLines(IReadOnlyList<SalesReturnLineCommand>? lines)
    {
        if (lines is null || lines.Count == 0 || lines.Count > MaxReturnLines)
            throw new BusinessRuleException("sales.return_lines_required", $"A return requires between 1 and {MaxReturnLines} lines.", "lines");
        if (lines.Select(l => l.SourceLineId).Distinct().Count() != lines.Count || lines.Any(l => l.SourceLineId == Guid.Empty))
            throw new BusinessRuleException("sales.return_line_invalid", "Each original line can appear once per return.", "lines");
        if (lines.Any(l => l.Quantity <= 0m || decimal.Round(l.Quantity, 4) != l.Quantity))
            throw new BusinessRuleException(SalesReturnAllocation.QuantityInvalid, "Return quantities must be positive with at most four decimals.", "lines");
        return lines.OrderBy(l => l.SourceLineId).ToList();
    }

    private async Task<List<PlannedReturnLine>> PlanReturnLinesAsync(SalesInvoiceRow original, List<SalesReturnLineCommand> requested,
        CancellationToken cancellationToken)
    {
        var sources = original.Lines.ToDictionary(l => l.Id);
        if (requested.Any(r => !sources.ContainsKey(r.SourceLineId)))
            throw new BusinessRuleException("sales.return_line_invalid", "A return line does not belong to the original invoice.", "lines");
        var sourceIds = requested.Select(r => r.SourceLineId).ToList();
        var previous = await context.SalesInvoiceLines.AsNoTracking()
            .Where(l => l.SourceLineId != null && sourceIds.Contains(l.SourceLineId.Value)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var previousIds = previous.Select(l => l.Id).ToList();
        var previousMovements = await context.StockMovements.AsNoTracking()
            .Where(m => m.CompanyId == original.CompanyId && m.DocumentType == ReturnDocumentType && m.DocumentLineId != null && previousIds.Contains(m.DocumentLineId.Value))
            .ToDictionaryAsync(m => m.DocumentLineId!.Value, cancellationToken).ConfigureAwait(false);
        var deliveries = await context.StockMovements.AsNoTracking()
            .Where(m => m.CompanyId == original.CompanyId && m.BranchId == original.BranchId && m.DocumentId == original.Id
                && m.DocumentType == "SalesInvoice" && m.MovementKind == (int)StockMovementKind.SalesDelivery)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var plan = new List<PlannedReturnLine>();
        foreach (var request in requested)
        {
            var source = sources[request.SourceLineId];
            var delivery = DeliveryFor(original, source, deliveries);
            var earlier = previous.Where(l => l.SourceLineId == source.Id).ToList();
            var returnedQuantity = earlier.Sum(l => l.Quantity);
            if (request.Quantity > source.Quantity - returnedQuantity)
                throw new BusinessRuleException(SalesReturnAllocation.OverReturn,
                    "The return exceeds the quantity still returnable on the original line.", "lines");
            var earlierMovements = earlier.Select(l => previousMovements.TryGetValue(l.Id, out var m) ? m
                : throw new BusinessRuleException("sales.stock_history_invalid", "An earlier return has no stock movement.")).ToList();
            var returnedAmounts = earlier.Zip(earlierMovements).Aggregate(SalesLineAmounts.Zero, (sum, pair) => sum.Add(new(
                pair.First.LineTotal - pair.First.TaxAmount - pair.First.PumpCost - pair.First.TransportCost + pair.First.DiscountAmount,
                pair.First.DiscountAmount, pair.First.TaxAmount, pair.First.PumpCost, pair.First.TransportCost, pair.Second.TotalCost)));
            var sold = new SalesLineAmounts(Math.Round(source.Quantity * source.UnitPrice, 4, MidpointRounding.AwayFromZero),
                source.DiscountAmount, source.TaxAmount, source.PumpCost, source.TransportCost, delivery.TotalCost);
            var amounts = SalesReturnAllocation.Allocate(source.Quantity, sold, returnedQuantity, returnedAmounts, request.Quantity);

            var soldBase = -delivery.BaseQuantity;
            var baseQuantity = request.Quantity == source.Quantity - returnedQuantity
                ? soldBase - earlierMovements.Sum(m => m.BaseQuantity)
                : soldBase * request.Quantity / source.Quantity;
            if (baseQuantity <= 0m || decimal.Round(baseQuantity, 4) != baseQuantity)
                throw new BusinessRuleException("inventory.quantity_precision", "The returned base quantity must be positive and exact at stock precision.", "lines");
            plan.Add(new(source, delivery, request.Quantity, baseQuantity, amounts));
        }
        return plan;
    }

    // Postings made before DocumentLineId existed are matched by their line attributes; ambiguity at different costs fails closed.
    private static StockMovementRow DeliveryFor(SalesInvoiceRow original, SalesInvoiceLineRow source, List<StockMovementRow> deliveries)
    {
        var exact = deliveries.SingleOrDefault(m => m.DocumentLineId == source.Id);
        if (exact is not null) return exact;
        var candidates = deliveries.Where(m => m.DocumentLineId == null && m.ItemId == source.ItemId && m.UnitId == source.UnitId
            && m.Quantity == source.Quantity && m.WarehouseId == (source.WarehouseId ?? original.WarehouseId)).ToList();
        if (candidates.Count == 0 || candidates.Select(m => (m.UnitCost, m.TotalCost, m.BaseQuantity, m.CostCenterId)).Distinct().Count() != 1)
            throw new BusinessRuleException("sales.stock_history_invalid", "The original stock movement for this line cannot be identified.");
        return candidates[0];
    }

    private static SalesInvoiceRow CreateReturnDocument(SalesInvoiceRow original, List<PlannedReturnLine> plan, Guid journalId,
        string number, Guid userId, string reason, DateTimeOffset now)
    {
        var document = new SalesInvoiceRow
        {
            Id = Guid.NewGuid(),
            CompanyId = original.CompanyId,
            BranchId = original.BranchId,
            PatternRevisionId = original.PatternRevisionId,
            CustomerId = original.CustomerId,
            WarehouseId = original.WarehouseId,
            InvoiceNumber = number,
            InvoiceDateUtc = now,
            PaymentMethod = original.PaymentMethod,
            CurrencyCode = original.CurrencyCode,
            ExchangeRate = original.ExchangeRate,
            Status = (int)SalesInvoiceStatus.Posted,
            CostCenterId = original.CostCenterId,
            CustomerAccountId = original.CustomerAccountId,
            Notes = reason,
            SubTotal = plan.Sum(l => l.Amounts.SubTotal),
            DiscountTotal = plan.Sum(l => l.Amounts.Discount),
            TaxTotal = plan.Sum(l => l.Amounts.Tax),
            AdditionsTotal = plan.Sum(l => l.Amounts.Pump + l.Amounts.Transport),
            GrandTotal = plan.Sum(l => l.Amounts.LineTotal),
            JournalEntryId = journalId,
            ReturnOfInvoiceId = original.Id,
            CreatedByUserId = userId,
            PostedByUserId = userId,
            PostedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        document.Lines = plan.Select(l => new SalesInvoiceLineRow
        {
            Id = Guid.NewGuid(),
            SalesInvoiceId = document.Id,
            ItemId = l.Source.ItemId,
            UnitId = l.Source.UnitId,
            Quantity = l.Quantity,
            UnitPrice = l.Source.UnitPrice,
            DiscountPercentage = l.Source.DiscountPercentage,
            DiscountAmount = l.Amounts.Discount,
            TaxPercentage = l.Source.TaxPercentage,
            TaxAmount = l.Amounts.Tax,
            PumpCost = l.Amounts.Pump,
            TransportCost = l.Amounts.Transport,
            LineTotal = l.Amounts.LineTotal,
            CostCenterId = l.Source.CostCenterId,
            WarehouseId = l.Delivery.WarehouseId,
            DisplayOrder = l.Source.DisplayOrder,
            ConcreteGrade = l.Source.ConcreteGrade,
            SourceLineId = l.Source.Id
        }).ToList();
        return document;
    }

    // Mirrors the original posting at the original rate: cash returns credit Cash, credit returns credit the customer's receivable.
    private static List<JournalDraftLineInput> BuildReturnJournal(SalesInvoiceRow original, List<PlannedReturnLine> plan,
        PatternConfiguration config, Guid? receivable, string baseCurrency)
    {
        var lines = new List<JournalDraftLineInput>();
        Guid Resolve(string role, Guid? party = null) => PostingRuleResolver.Resolve(role, config, partyContractAccountId: party).AccountId;
        // A return goes back through the account the original invoice used: its own account is binding here, whatever the
        // pattern allows today, so the two documents always meet on one account.
        Guid ResolveControl(bool cashReturn) => PostingRuleResolver.Resolve(cashReturn ? "Cash" : "Receivable", config,
            enforcedAccountId: original.CustomerAccountId, partyContractAccountId: cashReturn ? null : receivable).AccountId;
        void Add(Guid account, decimal debit, decimal credit, string description, Guid? center, bool stock = false)
        {
            if (debit == 0 && credit == 0) return;
            lines.Add(new JournalDraftLineInput
            {
                AccountId = account,
                CostCenterId = center,
                TransactionCurrencyCode = stock ? baseCurrency : original.CurrencyCode,
                TransactionDebit = debit,
                TransactionCredit = credit,
                RateSourceCurrencyCode = stock ? baseCurrency : original.CurrencyCode,
                RateTargetCurrencyCode = baseCurrency,
                RateValue = stock ? 1m : original.ExchangeRate,
                Description = description
            });
        }
        var cash = original.PaymentMethod == (int)PaymentMethod.Cash;
        foreach (var group in plan.GroupBy(l => l.Source.CostCenterId ?? original.CostCenterId))
        {
            var amounts = group.Aggregate(SalesLineAmounts.Zero, (sum, l) => sum.Add(l.Amounts));
            var additions = amounts.Pump + amounts.Transport;
            Add(ResolveControl(cash), 0, amounts.LineTotal, cash ? "رد نقدية مرتجع البيع" : "تخفيض ذمة الزبون بالمرتجع", group.Key);
            if (amounts.Discount > 0) Add(Resolve("Discount"), 0, amounts.Discount, "عكس حسم البيع المرتجع", group.Key);
            Add(Resolve("Revenue"), amounts.SubTotal, 0, "مرتجع إيراد البيع", group.Key);
            if (amounts.Tax > 0) Add(Resolve("Tax"), amounts.Tax, 0, "ضريبة البيع المرتجع", group.Key);
            if (additions > 0) Add(Resolve(config.AccountMappings.Any(m => string.Equals(m.Role, "TransportRevenue", StringComparison.OrdinalIgnoreCase)) ? "TransportRevenue" : "Revenue"),
                additions, 0, "مرتجع أجور الضخ والنقل", group.Key);
        }
        var cogs = Resolve("COGS"); var inventory = Resolve("Inventory");
        foreach (var group in plan.GroupBy(l => l.Delivery.CostCenterId))
        {
            var cost = group.Sum(l => l.Amounts.Cost);
            Add(inventory, cost, 0, "مخزون مرتجع بالتكلفة الأصلية", group.Key, true);
            Add(cogs, 0, cost, "عكس تكلفة البضاعة المرتجعة", group.Key, true);
        }
        return lines;
    }
}
