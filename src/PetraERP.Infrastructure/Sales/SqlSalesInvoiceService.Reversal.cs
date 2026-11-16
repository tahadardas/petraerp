using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Sales;
using PetraERP.Application.Patterns;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;

namespace PetraERP.Infrastructure.Sales;

public sealed partial class SqlSalesInvoiceService
{
    public async Task<SalesInvoiceDetailResult> ReverseInvoiceAsync(Guid companyId, Guid branchId, Guid invoiceId, Guid userId,
        ReverseSalesInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = AuditReason.Create(command.Reason);
        var key = SalesRequestKey("reverse", $"{invoiceId:N}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reason.Value)))}");
        var intent = await RegisterSalesIntentAsync(companyId, branchId, invoiceId, userId, key, PostingOperation.PostJournalReversal, reason, cancellationToken).ConfigureAwait(false);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                await LockSalesScopeAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);
                var invoice = await LoadInvoiceForUpdateAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
                var scope = PostingRequestScope.Create(companyId, branchId, userId);
                var registered = await context.PostingRequests.AsNoTracking().SingleOrDefaultAsync(r => r.CompanyId == companyId && r.BranchId == branchId
                    && r.Operation == (byte)PostingOperation.PostJournalReversal && r.IdempotencyKey == key, cancellationToken).ConfigureAwait(false);
                if (registered is not null)
                {
                    if (registered.OriginalJournalId != invoice.JournalEntryId || registered.CompletedAtUtc is null)
                        throw new PostingIdempotencyConflictException(key, "Reversal request is inconsistent with its invoice.");
                    var replay = PostingRequest.Reverse(scope, key, registered.JournalId, ExpectedJournalVersion.FromBase64(registered.ExpectedVersionBase64),
                        registered.OriginalJournalId!.Value, ExpectedJournalVersion.FromBase64(registered.OriginalVersionBase64!), reason);
                    await new SqlPostingService(context).PostSalesAsync(invoiceId, replay, cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return await GetInvoiceAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
                }
                if (invoice.Status != (int)SalesInvoiceStatus.Posted || !PetraERP.Domain.Documents.DocumentLifecycle.CanTransition(PetraERP.Domain.Documents.AccountingDocumentKind.SalesInvoice, PetraERP.Domain.Documents.DocumentLifecycle.From((SalesInvoiceStatus)invoice.Status), PetraERP.Domain.Documents.DocumentLifecycleState.Reversed) || invoice.JournalEntryId is null || invoice.ReversesInvoiceId.HasValue
                    || invoice.ReturnOfInvoiceId.HasValue)
                    throw new BusinessRuleException("sales.invoice_not_posted", "Only an original posted invoice can be reversed.");
                // Partial returns already reversed part of the sale; a full reversal would double the credit and the stock.
                if (await context.SalesInvoices.AnyAsync(i => i.ReturnOfInvoiceId == invoiceId, cancellationToken).ConfigureAwait(false))
                    throw new BusinessRuleException("sales.invoice_has_returns", "An invoice with posted returns cannot be fully reversed.");
                VerifySalesIntentVersion(intent, invoice);
                if (invoice.PaymentMethod == (int)PaymentMethod.Credit)
                {
                    var settlements = await PetraERP.Infrastructure.Treasury.SettlementQueries.LoadInvoicesAsync(context, companyId, branchId,
                        PetraERP.Domain.Treasury.VoucherKind.Receipt, invoice.CustomerId, null, null, cancellationToken).ConfigureAwait(false);
                    if (settlements.Single(i => i.Id == invoice.Id).Paid > 0m)
                        throw new BusinessRuleException("sales.invoice_has_settlements", "اعكس أو حرر تخصيصات القبض أولاً ثم اعكس الفاتورة / Reverse or release receipt settlements before reversing this invoice.");
                }
                var now = timeProvider.GetUtcNow(); var date = DateOnly.FromDateTime(now.Date);
                var fiscalYear = await RequireOpenSalesPeriodAsync(companyId, date, cancellationToken).ConfigureAwait(false);
                var journalService = new SqlJournalEntryService(context, timeProvider);
                var journalScope = JournalEntryScope.Create(companyId, branchId, userId);
                var original = await journalService.GetJournalAsync(journalScope, invoice.JournalEntryId.Value, cancellationToken).ConfigureAwait(false);
                var journal = await journalService.CreateReversalDraftAsync(journalScope, new CreateReversalDraftCommand
                { OriginalJournalId = original.Id, OriginalExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(original.VersionBase64)), AccountingDate = date, Reason = reason.Value }, cancellationToken).ConfigureAwait(false);
                journal = await ApproveSalesJournalAsync(journalService, journalScope, journal, reason.Value, cancellationToken).ConfigureAwait(false);
                var request = PostingRequest.Reverse(scope, key, journal.Id, ExpectedJournalVersion.FromBase64(journal.VersionBase64), original.Id,
                    ExpectedJournalVersion.FromBase64(original.VersionBase64), reason);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await new SqlPostingService(context).PostSalesAsync(invoiceId, request, cancellationToken).ConfigureAwait(false);
                invoice = await LoadInvoiceForUpdateAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
                var reversal = CloneReversal(invoice, journal.Id, userId, reason.Value, now);
                var revision = await context.PatternRevisions.AsNoTracking().SingleAsync(r => r.Id == invoice.PatternRevisionId, cancellationToken).ConfigureAwait(false);
                var number = await patternService.GenerateNextNumberAsync(PatternScope.Create(companyId, branchId, userId),
                    new GetNextPatternNumberCommand { PatternId = revision.PatternId, FiscalYearId = fiscalYear, RevisionId = revision.Id, AccountingDate = date, AllocationKey = key }, cancellationToken).ConfigureAwait(false);
                reversal.InvoiceNumber = number.FormattedNumber;
                var deliveries = await context.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && m.BranchId == branchId
                    && m.DocumentId == invoiceId && m.DocumentType == "SalesInvoice" && m.MovementKind == (int)StockMovementKind.SalesDelivery).ToListAsync(cancellationToken).ConfigureAwait(false);
                if (deliveries.Count != invoice.Lines.Count) throw new BusinessRuleException("sales.stock_history_invalid", "Original stock movements do not reconcile to invoice lines.");
                var reversalLines = invoice.Lines.Zip(reversal.Lines).ToDictionary(pair => pair.First.Id, pair => pair.Second.Id);
                if (deliveries.Any(m => m.DocumentLineId is null || !reversalLines.ContainsKey(m.DocumentLineId.Value)))
                    throw new BusinessRuleException("sales.stock_history_invalid", "تعذر ربط حركة الأصل بسطر الفاتورة / Original movements cannot be linked to invoice lines.");
                foreach (var movement in deliveries)
                    context.StockMovements.Add(new StockMovementRow
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        BranchId = branchId,
                        WarehouseId = movement.WarehouseId,
                        ItemId = movement.ItemId,
                        UnitId = movement.UnitId,
                        MovementKind = (int)StockMovementKind.SalesReturn,
                        Quantity = movement.Quantity,
                        BaseQuantity = -movement.BaseQuantity,
                        UnitCost = movement.UnitCost,
                        TotalCost = movement.TotalCost,
                        MovementDateUtc = now,
                        DocumentType = "SalesInvoiceReturn",
                        DocumentId = reversal.Id,
                        DocumentLineId = reversalLines[movement.DocumentLineId!.Value],
                        ReversesMovementId = movement.Id,
                        DocumentNumber = reversal.InvoiceNumber,
                        CostCenterId = movement.CostCenterId,
                        CreatedAtUtc = now,
                        CreatedByUserId = userId,
                        Notes = reason.Value
                    });
                context.SalesInvoices.Add(reversal); invoice.Status = (int)SalesInvoiceStatus.Reversed; invoice.ReversedByInvoiceId = reversal.Id; invoice.UpdatedAtUtc = now;
                await CompleteSalesIntentAsync(intent, journal.Id, cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch { context.ChangeTracker.Clear(); throw; }
        }
        return await GetInvoiceAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
    }

    // Amounts are positive magnitudes; ReversesInvoiceId carries credit/reversal semantics, matching stock movement conventions.
    private static SalesInvoiceRow CloneReversal(SalesInvoiceRow original, Guid journalId, Guid userId, string reason, DateTimeOffset now)
    {
        var reversal = new SalesInvoiceRow
        {
            Id = Guid.NewGuid(),
            CompanyId = original.CompanyId,
            BranchId = original.BranchId,
            PatternRevisionId = original.PatternRevisionId,
            CustomerId = original.CustomerId,
            WarehouseId = original.WarehouseId,
            InvoiceDateUtc = now,
            PaymentMethod = original.PaymentMethod,
            CurrencyCode = original.CurrencyCode,
            ExchangeRate = original.ExchangeRate,
            Status = (int)SalesInvoiceStatus.Posted,
            CostCenterId = original.CostCenterId,
            CustomerAccountId = original.CustomerAccountId,
            Notes = reason,
            SubTotal = original.SubTotal,
            DiscountTotal = original.DiscountTotal,
            TaxTotal = original.TaxTotal,
            AdditionsTotal = original.AdditionsTotal,
            GrandTotal = original.GrandTotal,
            JournalEntryId = journalId,
            ReversesInvoiceId = original.Id,
            CreatedByUserId = userId,
            PostedByUserId = userId,
            PostedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        reversal.Lines = original.Lines.Select(l => new SalesInvoiceLineRow
        {
            Id = Guid.NewGuid(),
            SalesInvoiceId = reversal.Id,
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
            ConcreteGrade = l.ConcreteGrade
        }).ToArray();
        reversal.Adjustments = original.Adjustments.Select(a => new SalesInvoiceAdjustmentRow
        {
            Id = Guid.NewGuid(), SalesInvoiceId = reversal.Id, Kind = a.Kind, AccountId = a.AccountId, CounterAccountId = a.CounterAccountId, Amount = a.Amount,
            Percentage = a.Percentage, Notes = a.Notes, CostCenterId = a.CostCenterId, DisplayOrder = a.DisplayOrder,
        }).ToArray();
        return reversal;
    }
}
