using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Purchases;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Purchases;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Treasury;

namespace PetraERP.Infrastructure.Purchases;

public sealed partial class SqlPurchaseInvoiceService
{
    /// <summary>
    /// Reverses a posted purchase invoice. The posted document is never edited: a linked reversal document carries the same amounts, the
    /// journal is mirrored line for line from the original, and every received quantity goes back out at the cost it arrived at. The
    /// original then reads Reversed and names its reversal, so both stay provable and the supplier's payable returns to zero.
    /// </summary>
    public async Task<PurchaseDetailResult> ReverseInvoiceAsync(Guid companyId, Guid branchId, Guid invoiceId, Guid userId,
        ReversePurchaseInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireKey(command.IdempotencyKey);
        var reason = AuditReason.Create(command.Reason);

        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                await LockScopeAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);
                var original = await LoadForUpdateAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);

                // The request key is bound to this invoice, so an ambiguous retry replays the same reversal instead of posting a second one.
                var existing = await context.PurchaseInvoices.AsNoTracking().Include(i => i.Lines)
                    .SingleOrDefaultAsync(i => i.CompanyId == companyId && i.BranchId == branchId && i.PostingKey == command.IdempotencyKey,
                        cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    if (existing.ReversesInvoiceId != invoiceId || existing.CreatedByUserId != userId || existing.Notes != reason.Value)
                        throw new ResourceConflictException("purchases.reversal_key_conflict",
                            "مفتاح الطلب يخص عملية أخرى. / The request key already belongs to a different purchase reversal.");
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return await GetAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
                }

                if (original.ReversesInvoiceId is not null)
                    throw new BusinessRuleException("purchases.reversal_document_not_reversible",
                        "مستند العكس نفسه لا يُعكس؛ الأصل هو المرجع. / A reversal document is not itself reversible; correct the original instead.");
                if (original.ReturnOfInvoiceId is not null)
                    throw new BusinessRuleException("purchases.reversal_document_not_reversible",
                        "مرتجع الشراء لا يُعكس؛ عالج الفاتورة الأصلية. / A purchase return cannot be reversed; work from the original invoice.");
                if (original.ReversedByInvoiceId is not null)
                    throw new BusinessRuleException("purchases.invoice_already_reversed",
                        "هذه الفاتورة معكوسة بالفعل. / This invoice has already been reversed.");
                if (original.Status != (int)PurchaseInvoiceStatus.Posted || original.JournalEntryId is null)
                    throw new BusinessRuleException("purchases.invoice_not_posted", "Only a posted purchase invoice can be reversed.");
                // A posted return has already moved part of the payable and the stock back; a full reversal would move it a second time.
                if (await context.PurchaseInvoices.AsNoTracking().AnyAsync(i => i.CompanyId == companyId && i.BranchId == branchId
                        && (i.ReturnOfInvoiceId == invoiceId || i.ReversesInvoiceId == invoiceId), cancellationToken).ConfigureAwait(false))
                    throw new BusinessRuleException("purchases.invoice_has_returns",
                        "الفاتورة لها مرتجعات أو عكسات مرحّلة؛ عالجها أولاً. / The invoice has posted returns or a reversal; handle those first.");
                if (!command.ExpectedVersion.Matches(original.RowVersion)) throw new ConcurrencyConflictException("purchase invoice");

                // Paying the supplier is a separate posted fact; reversing the invoice underneath it would strand that payment.
                if (original.PaymentMethod == (int)PaymentMethod.Credit)
                {
                    var open = await SettlementQueries.LoadInvoicesAsync(context, companyId, branchId, VoucherKind.Payment, original.SupplierId,
                        original.CurrencyCode, null, cancellationToken).ConfigureAwait(false);
                    if (open.SingleOrDefault(i => i.Id == invoiceId) is { } settled && settled.Paid > 0m)
                        throw new BusinessRuleException("purchases.invoice_has_settlements",
                            "اعكس أو حرر تخصيصات الدفع أولاً ثم اعكس الفاتورة / Reverse or release the payment allocations before reversing this invoice.");
                }

                var now = timeProvider.GetUtcNow();
                var date = DateOnly.FromDateTime(now.UtcDateTime.Date);
                var fiscalYear = await RequireOpenPeriodAsync(companyId, date, cancellationToken).ConfigureAwait(false);
                var validation = await ValidateAsync(companyId, branchId, ToCommand(original), cancellationToken).ConfigureAwait(false);

                var journalService = new SqlJournalEntryService(context, timeProvider);
                var scope = JournalEntryScope.Create(companyId, branchId, userId);
                var originalJournal = await journalService.GetJournalAsync(scope, original.JournalEntryId.Value, cancellationToken).ConfigureAwait(false);
                var journal = await journalService.CreateReversalDraftAsync(scope, new CreateReversalDraftCommand
                {
                    OriginalJournalId = originalJournal.Id,
                    OriginalExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(originalJournal.VersionBase64)),
                    AccountingDate = date,
                    Reason = reason.Value,
                }, cancellationToken).ConfigureAwait(false);
                // The reversal draft must be validated before it can be approved, exactly as a new purchase journal is.
                var validated = await journalService.ValidateDraftAsync(scope, journal.Id, new JournalDraftActionCommand
                {
                    ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(journal.VersionBase64)),
                    Reason = reason.Value,
                }, cancellationToken).ConfigureAwait(false);
                journal = await journalService.ApproveDraftAsync(scope, journal.Id, new JournalDraftActionCommand
                {
                    ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(validated.VersionBase64)),
                    Reason = reason.Value,
                }, cancellationToken).ConfigureAwait(false);

                var reversal = CloneReversal(original, journal.Id, userId, reason.Value, now, command.IdempotencyKey);
                reversal.InvoiceNumber = await NextNumberAsync(companyId, branchId, userId, validation, fiscalYear, reversal.Id, date, null,
                    cancellationToken).ConfigureAwait(false);

                // Each received quantity goes back out at the cost it came in at, naming the movement it undoes.
                var receipts = await context.StockMovements.AsNoTracking()
                    .Where(m => m.CompanyId == companyId && m.BranchId == branchId && m.DocumentId == invoiceId
                        && m.DocumentType == InvoiceDocumentType && m.MovementKind == (int)StockMovementKind.PurchaseReceipt)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                var originalLines = original.Lines.OrderBy(l => l.DisplayOrder).ToList();
                var reversalLines = reversal.Lines.OrderBy(l => l.DisplayOrder).ToList();
                if (receipts.Count != originalLines.Count || receipts.Any(m => m.DocumentLineId is null))
                    throw new BusinessRuleException("purchases.stock_history_invalid",
                        "حركات المخزون الأصلية لا تطابق سطور الفاتورة. / The original stock movements do not reconcile to the invoice lines.");
                var lineBySource = originalLines.Zip(reversalLines, (source, mirror) => (source.Id, mirror))
                    .ToDictionary(pair => pair.Id, pair => pair.mirror);
                // Stock is checked against what remains after the earlier lines of this same reversal, not the opening balance per line.
                var remaining = new Dictionary<Guid, decimal>();
                foreach (var receipt in receipts)
                {
                    if (!lineBySource.TryGetValue(receipt.DocumentLineId!.Value, out var mirrored))
                        throw new BusinessRuleException("purchases.stock_history_invalid",
                            "تعذر ربط حركة الأصل بسطر الفاتورة / Original movements cannot be linked to invoice lines.");
                    if (!remaining.TryGetValue(receipt.ItemId, out var held))
                        held = (await InventoryMovementGuard.BalanceAsync(context, companyId, receipt.WarehouseId, receipt.ItemId, cancellationToken)
                            .ConfigureAwait(false)).Quantity;
                    if (held < receipt.BaseQuantity)
                        throw new BusinessRuleException("inventory.negative_stock_blocked",
                            "البضاعة صُرفت بالفعل؛ لا تكفي الكمية المتاحة لإرجاعها. / Stock has moved on; the reversal cannot issue this quantity.");
                    remaining[receipt.ItemId] = held - receipt.BaseQuantity;
                    context.StockMovements.Add(new StockMovementRow
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = receipt.CompanyId,
                        BranchId = receipt.BranchId,
                        WarehouseId = receipt.WarehouseId,
                        ItemId = receipt.ItemId,
                        UnitId = receipt.UnitId,
                        MovementKind = (int)StockMovementKind.PurchaseReturn,
                        Quantity = receipt.Quantity,
                        BaseQuantity = -receipt.BaseQuantity,
                        UnitCost = receipt.UnitCost,
                        TotalCost = receipt.TotalCost,
                        MovementDateUtc = now,
                        DocumentType = ReturnDocumentType,
                        DocumentId = reversal.Id,
                        DocumentLineId = mirrored.Id,
                        ReversesMovementId = receipt.Id,
                        DocumentNumber = reversal.InvoiceNumber,
                        CostCenterId = receipt.CostCenterId,
                        CreatedByUserId = userId,
                        CreatedAtUtc = now,
                        Notes = reason.Value,
                    });
                }

                // The original is already loaded under the update lock; re-reading it here would drop the pending movements and
                // the reversal from the unit of work, so its state is updated in place.
                original.Status = (int)PurchaseInvoiceStatus.Reversed;
                original.ReversedByInvoiceId = reversal.Id;
                original.UpdatedAtUtc = now;
                context.PurchaseInvoices.Add(reversal);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                var postingKey = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"PetraERP.Purchases.reverse:{reversal.Id:N}"))[..16]);
                var request = PostingRequest.Reverse(PostingRequestScope.Create(companyId, branchId, userId), postingKey, journal.Id,
                    ExpectedJournalVersion.FromBase64(journal.VersionBase64), originalJournal.Id,
                    ExpectedJournalVersion.FromBase64(originalJournal.VersionBase64), reason);
                await new SqlPostingService(context).PostPurchaseAsync(invoiceId, request, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                context.ChangeTracker.Clear();
                throw;
            }
        }

        return await GetAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The reversal document carries the original's amounts as positive magnitudes — the same convention the stock movements already use,
    /// where <see cref="PurchaseInvoiceRow.ReversesInvoiceId"/> is what makes them reduce the supplier's payable instead of adding to it.
    /// </summary>
    private static PurchaseInvoiceRow CloneReversal(PurchaseInvoiceRow original, Guid journalId, Guid userId, string reason, DateTimeOffset now,
        string idempotencyKey)
    {
        var reversal = new PurchaseInvoiceRow
        {
            Id = Guid.NewGuid(),
            CompanyId = original.CompanyId,
            BranchId = original.BranchId,
            PatternRevisionId = original.PatternRevisionId,
            SupplierId = original.SupplierId,
            WarehouseId = original.WarehouseId,
            InvoiceDateUtc = now,
            PaymentMethod = original.PaymentMethod,
            CurrencyCode = original.CurrencyCode,
            ExchangeRate = original.ExchangeRate,
            Status = (int)PurchaseInvoiceStatus.Posted,
            Notes = reason,
            SubTotal = original.SubTotal,
            DiscountTotal = original.DiscountTotal,
            TaxTotal = original.TaxTotal,
            AdditionsTotal = original.AdditionsTotal,
            GrandTotal = original.GrandTotal,
            JournalEntryId = journalId,
            ReversesInvoiceId = original.Id,
            PostingKey = idempotencyKey,
            VarianceAccountId = original.VarianceAccountId,
            SellerCode = original.SellerCode,
            DistributorCode = original.DistributorCode,
            CreatedByUserId = userId,
            PostedByUserId = userId,
            PostedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        reversal.Lines = original.Lines.OrderBy(l => l.DisplayOrder).Select(l => new PurchaseInvoiceLineRow
        {
            Id = Guid.NewGuid(),
            PurchaseInvoiceId = reversal.Id,
            ItemId = l.ItemId,
            UnitId = l.UnitId,
            Quantity = l.Quantity,
            UnitCost = l.UnitCost,
            DiscountAmount = l.DiscountAmount,
            TaxAmount = l.TaxAmount,
            LineTotal = l.LineTotal,
            CostCenterId = l.CostCenterId,
            Notes = l.Notes,
            DisplayOrder = l.DisplayOrder,
        }).ToArray();
        reversal.Adjustments = original.Adjustments.OrderBy(a => a.DisplayOrder).Select(a => new PurchaseInvoiceAdjustmentRow
        {
            Id = Guid.NewGuid(),
            PurchaseInvoiceId = reversal.Id,
            Kind = a.Kind,
            AccountId = a.AccountId,
            CounterAccountId = a.CounterAccountId,
            Amount = a.Amount,
            Percentage = a.Percentage,
            Notes = a.Notes,
            CostCenterId = a.CostCenterId,
            DisplayOrder = a.DisplayOrder,
        }).ToArray();
        return reversal;
    }
}
