using System.Text.Json;

using PetraERP.Domain.Patterns;

using System.Data;

using System.Security.Cryptography;

using System.Text;

using Microsoft.EntityFrameworkCore;

using PetraERP.Application.Common;

using PetraERP.Application.GeneralLedger;

using PetraERP.Application.GeneralLedger.Posting;

using PetraERP.Application.Patterns;

using PetraERP.Application.Purchases;

using PetraERP.Domain.AccountingCalendars;

using PetraERP.Domain.Inventory;

using PetraERP.Domain.Primitives;

using PetraERP.Domain.Purchases;

using PetraERP.Domain.Sales;

using PetraERP.Infrastructure.GeneralLedger;

using PetraERP.Infrastructure.Inventory;



namespace PetraERP.Infrastructure.Purchases;



public sealed partial class SqlPurchaseInvoiceService

{

    internal const string InvoiceDocumentType = "PurchaseInvoice";

    internal const string ReturnDocumentType = "PurchaseReturn";



    public async Task<PurchaseDetailResult> PostAsync(Guid companyId, Guid branchId, Guid invoiceId, Guid userId, PostPurchaseInvoiceCommand command,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(command);

        RequireKey(command.IdempotencyKey);

        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);

        await using (transaction.ConfigureAwait(false))

        {

            try

            {

                await LockScopeAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);

                var invoice = await LoadForUpdateAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);

                if (invoice.PostingKey == command.IdempotencyKey)

                    await new PetraERP.Infrastructure.Patterns.SqlPatternService(context).VerifyManualReplayAsync(companyId, PetraERP.Infrastructure.Patterns.SqlPatternService.NumberAllocationKey("purchase-post", invoiceId, command.IdempotencyKey), command.ManualNumber, cancellationToken).ConfigureAwait(false);

                if (invoice.Status != (int)PurchaseInvoiceStatus.Draft && invoice.Status != (int)PurchaseInvoiceStatus.Approved
                    || !PetraERP.Domain.Documents.DocumentLifecycle.CanTransition(PetraERP.Domain.Documents.AccountingDocumentKind.PurchaseInvoice, PetraERP.Domain.Documents.DocumentLifecycle.From((PurchaseInvoiceStatus)invoice.Status), PetraERP.Domain.Documents.DocumentLifecycleState.Posted))

                {

                    if (invoice.ReturnOfInvoiceId is null && string.Equals(invoice.PostingKey, command.IdempotencyKey, StringComparison.Ordinal))

                    {

                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                        return await GetAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);

                    }

                    throw new BusinessRuleException("purchases.invoice_not_draft", "The purchase invoice is already posted or not eligible for posting.");

                }

                if (await context.StockMovements.AsNoTracking().AnyAsync(m => m.CompanyId == companyId && m.DocumentId == invoice.Id && m.DocumentType == InvoiceDocumentType, cancellationToken).ConfigureAwait(false))

                {

                    throw new BusinessRuleException("purchases.stock_already_received", "Stock movements have already been posted for this invoice.");

                }

                if (!command.ExpectedVersion.Matches(invoice.RowVersion)) throw new ConcurrencyConflictException("purchase invoice");

                var revision = await context.PatternRevisions.AsNoTracking().Include(r => r.Pattern)

                    .SingleAsync(r => r.Id == invoice.PatternRevisionId, cancellationToken).ConfigureAwait(false);

                var config = JsonSerializer.Deserialize<PatternConfiguration>(revision.ConfigurationJson) ?? new();

                if (config.ApprovalPolicy.RequiresApproval && invoice.Status != (int)PurchaseInvoiceStatus.Approved

                    && (!config.ApprovalPolicy.ThresholdAmount.HasValue || invoice.GrandTotal >= config.ApprovalPolicy.ThresholdAmount.Value))

                {

                    throw new BusinessRuleException("purchases.approval_required", "The pattern requires invoice approval before posting.");

                }

                if (config.AllowedPaymentMethods.Count > 0 && !config.AllowedPaymentMethods.Contains(((PaymentMethod)invoice.PaymentMethod).ToString(), StringComparer.OrdinalIgnoreCase))

                    throw new BusinessRuleException("purchases.payment_method_not_allowed", "The payment method is not permitted by the document pattern.");

                if (config.AllowedWarehouseIds.Count > 0 && !config.AllowedWarehouseIds.Contains(invoice.WarehouseId))

                    throw new BusinessRuleException("purchases.warehouse_not_allowed", "The invoice warehouse is not permitted by the document pattern.");



                var validation = await ValidateAsync(companyId, branchId, ToCommand(invoice), cancellationToken).ConfigureAwait(false);

                var date = DateOnly.FromDateTime(invoice.InvoiceDateUtc.UtcDateTime.Date);

                var fiscalYear = await RequireOpenPeriodAsync(companyId, date, cancellationToken).ConfigureAwait(false);

                var lines = invoice.Lines.OrderBy(l => l.DisplayOrder).ThenBy(l => l.Id).ToList();

                var money = lines.Select(l => PurchaseLineMoney.Create(l.Quantity, l.UnitCost, l.DiscountAmount, l.TaxAmount)).ToList();

                var amounts = PurchaseCosting.Allocate(invoice.ExchangeRate, money);

                var companyPolicy = await PetraERP.Infrastructure.CompanyOptions.SqlCompanyOptionsService.EffectiveAsync(context,companyId,cancellationToken).ConfigureAwait(false);

                if(companyPolicy.RequireDocumentApproval && invoice.Status != (int)PurchaseInvoiceStatus.Approved)

                    throw new BusinessRuleException("company.approval_required","Company policy requires document approval before posting.");

                if(!companyPolicy.AllowCreditPurchases && invoice.PaymentMethod == (int)PaymentMethod.Credit)

                    throw new BusinessRuleException("company.credit_not_allowed","Company policy does not allow credit documents for this operation.");

                var number = await NextNumberAsync(companyId, branchId, userId, validation, fiscalYear, PetraERP.Infrastructure.Patterns.SqlPatternService.NumberAllocationKey("purchase-post", invoiceId, command.IdempotencyKey), date, command.ManualNumber, cancellationToken).ConfigureAwait(false);



                var movements = new List<StockMovementRow>();

                for (var i = 0; i < lines.Count; i++)

                {

                    var line = lines[i];

                    var item = await context.Items.AsNoTracking().SingleAsync(x => x.Id == line.ItemId, cancellationToken).ConfigureAwait(false);

                    var baseQuantity = await InventoryUnits.BaseQuantityAsync(context, companyId, item.Id, line.UnitId, item.BaseUnitId, line.Quantity, cancellationToken).ConfigureAwait(false);

                    movements.Add(Movement(invoice, line, StockMovementKind.PurchaseReceipt, InvoiceDocumentType, invoice.Id, number, baseQuantity,

                        amounts.LineCosts[i], userId, invoice.InvoiceDateUtc));

                }

                var supplier = await context.Parties.AsNoTracking().Where(p => p.Id == invoice.SupplierId).Select(p => p.NameArabic).SingleAsync(cancellationToken).ConfigureAwait(false);

                var resolver = await ItemAccountResolver.CreateAsync(context, companyId, lines.Select(l => l.ItemId), config, branchId, date, cancellationToken).ConfigureAwait(false);

                var journal = await CreateJournalAsync(companyId, branchId, userId, date, $"فاتورة مشتريات {number} — {supplier}",

                    BuildJournal(invoice, lines, amounts, validation, isReturn: false, resolver: resolver, detailed: config.EntryPolicy.DetailedJournalLines), invoice.Notes ?? "ترحيل فاتورة مشتريات", cancellationToken).ConfigureAwait(false);



                invoice = await LoadForUpdateAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);

                var now = timeProvider.GetUtcNow();

                invoice.InvoiceNumber = number; invoice.JournalEntryId = journal.Id; invoice.PostingKey = command.IdempotencyKey;

                invoice.Status = (int)PurchaseInvoiceStatus.Posted; invoice.PostedByUserId = userId; invoice.PostedAtUtc = now; invoice.UpdatedAtUtc = now;

                context.StockMovements.AddRange(movements);

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await PostJournalAsync(companyId, branchId, userId, invoiceId, journal, invoice.Notes ?? "ترحيل فاتورة مشتريات", cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            }

            catch { context.ChangeTracker.Clear(); throw; }

        }

        return await GetAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);

    }



    /// <summary>

    /// Debit inventory (base currency, the exact stock cost) and recoverable tax; credit the supplier payable or cash with the gross.

    /// A return mirrors this. Using the remainder for inventory keeps the journal balanced in base currency to the last decimal.

    /// </summary>

    private static List<JournalDraftLineInput> BuildJournal(PurchaseInvoiceRow header, IReadOnlyList<PurchaseInvoiceLineRow> lines,

        PurchaseBaseAmounts amounts, PurchaseValidation validation, bool isReturn, List<decimal>? stockCosts = null, Guid? varianceAccountId = null, ItemAccountResolver? resolver = null, bool detailed = false)

    {

        var journal = new List<JournalDraftLineInput>();

        void Add(Guid account, decimal amount, bool debit, string description, Guid? center, bool baseCurrency)

        {

            if (amount == 0) return;

            if (isReturn) debit = !debit;

            journal.Add(new JournalDraftLineInput

            {

                AccountId = account, CostCenterId = center,

                TransactionCurrencyCode = baseCurrency ? validation.BaseCurrency : header.CurrencyCode,

                TransactionDebit = debit ? amount : 0, TransactionCredit = debit ? 0 : amount,

                RateSourceCurrencyCode = baseCurrency ? validation.BaseCurrency : header.CurrencyCode,

                RateTargetCurrencyCode = validation.BaseCurrency,

                RateValue = baseCurrency ? 1m : header.ExchangeRate,

                Description = description

            });

        }

        foreach (var group in lines.Select((line, index) => (

            CostCenterId: line.CostCenterId,

            AccountId: resolver?.Resolve(line.ItemId, "Inventory", fallbackAccountId: validation.InventoryAccountId) ?? validation.InventoryAccountId,

            Cost: stockCosts is null ? amounts.LineCosts[index] : stockCosts[index]))

            .GroupForJournal(x => (x.CostCenterId, x.AccountId), detailed))

        {

            Add(group.Key.AccountId, group.Sum(x => x.Cost), debit: true, isReturn ? "مخزون مرتجع للمورد" : "مخزون مشترى", group.Key.CostCenterId, baseCurrency: true);

        }

        var variance = stockCosts is null ? 0 : amounts.LineCosts.Sum() - stockCosts.Sum();

        if (variance != 0)

            Add(varianceAccountId ?? throw new BusinessRuleException("inventory.purchase_variance_account", "حساب فرق التكلفة مطلوب / A cost variance account is required."),

                Math.Abs(variance), debit: variance > 0, "فرق تكلفة مرتجع الشراء / Purchase return cost variance", null, baseCurrency: true);

        var tax = lines.Sum(l => l.TaxAmount);

        if (tax > 0) Add(validation.TaxAccountId!.Value, tax, debit: true, isReturn ? "عكس ضريبة مشتريات مرتجعة" : "ضريبة مشتريات قابلة للخصم", null, baseCurrency: false);

        var gross = lines.Sum(l => l.LineTotal);

        Add(validation.SettlementAccountId, gross, debit: false,

            header.PaymentMethod == (int)PaymentMethod.Cash ? "دفع نقدي للمشتريات" : "ذمة المورد", null, baseCurrency: false);

        // Document-level discounts (received) and additions: each row is its own balanced pair, so currency conversion never unbalances the journal.
        // A row with a counter account is booked directly between the two accounts, outside the supplier's balance.
        if (!isReturn)
            foreach (var adjustment in header.Adjustments.OrderBy(a => a.DisplayOrder))
            {
                var counter = adjustment.CounterAccountId ?? validation.SettlementAccountId;
                var viaCounter = adjustment.CounterAccountId is not null;
                if (adjustment.Kind == (int)PetraERP.Domain.Sales.SalesInvoiceAdjustmentKind.Discount)
                {
                    Add(counter, adjustment.Amount, debit: true, adjustment.Notes ?? "حسم مكتسب على الفاتورة", null, baseCurrency: false);
                    Add(adjustment.AccountId, adjustment.Amount, debit: false, adjustment.Notes ?? "حسم مكتسب على الفاتورة", adjustment.CostCenterId, baseCurrency: false);
                }
                else
                {
                    Add(adjustment.AccountId, adjustment.Amount, debit: true, adjustment.Notes ?? "إضافة على الفاتورة", adjustment.CostCenterId, baseCurrency: false);
                    Add(counter, adjustment.Amount, debit: false, adjustment.Notes ?? (viaCounter ? "إضافة على الفاتورة (حساب مقابل)" : "زيادة ذمة المورد بإضافة"), null, baseCurrency: false);
                }
            }

        return journal;

    }



    private StockMovementRow Movement(PurchaseInvoiceRow header, PurchaseInvoiceLineRow line, StockMovementKind kind, string documentType,

        Guid documentId, string? number, decimal baseQuantity, decimal cost, Guid userId, DateTimeOffset date) => new()

    {

        Id = Guid.NewGuid(),

        CompanyId = header.CompanyId,

        BranchId = header.BranchId,

        WarehouseId = header.WarehouseId,

        ItemId = line.ItemId,

        UnitId = line.UnitId,

        MovementKind = (int)kind,

        Quantity = line.Quantity,

        BaseQuantity = kind == StockMovementKind.PurchaseReturn ? -baseQuantity : baseQuantity,

        UnitCost = decimal.Round(cost / baseQuantity, 4, MidpointRounding.ToEven),

        TotalCost = cost,

        MovementDateUtc = date,

        DocumentType = documentType,

        DocumentId = documentId,

        DocumentLineId = line.Id,

        DocumentNumber = number,

        CostCenterId = line.CostCenterId,

        Notes = header.ReturnOfInvoiceId.HasValue ? header.Notes : line.Notes,

        CreatedByUserId = userId,

        CreatedAtUtc = timeProvider.GetUtcNow()

    };



    private async Task<string> NextNumberAsync(Guid companyId, Guid branchId, Guid userId, PurchaseValidation validation, Guid fiscalYear, Guid documentId, DateOnly date, string? manualNumber,

        CancellationToken cancellationToken) =>

        (await patternService.GenerateNextNumberAsync(PatternScope.Create(companyId, branchId, userId),

            new GetNextPatternNumberCommand { PatternId = validation.PatternId, FiscalYearId = fiscalYear, RevisionId = validation.RevisionId, AccountingDate = date, AllocationKey = documentId, ManualNumber = manualNumber }, cancellationToken)

            .ConfigureAwait(false)).FormattedNumber;



    private async Task<JournalDetailsDto> CreateJournalAsync(Guid companyId, Guid branchId, Guid userId, DateOnly date, string description,

        List<JournalDraftLineInput> lines, string reason, CancellationToken cancellationToken)

    {

        var service = new SqlJournalEntryService(context, timeProvider);

        var scope = JournalEntryScope.Create(companyId, branchId, userId);

        var journal = await service.CreateDraftAsync(scope, new CreateJournalDraftCommand { AccountingDate = date, Description = description, Lines = lines },

            cancellationToken).ConfigureAwait(false);

        var validated = await service.ValidateDraftAsync(scope, journal.Id, new JournalDraftActionCommand

        { ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(journal.VersionBase64)), Reason = reason }, cancellationToken).ConfigureAwait(false);

        return await service.ApproveDraftAsync(scope, journal.Id, new JournalDraftActionCommand

        { ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(validated.VersionBase64)), Reason = reason }, cancellationToken).ConfigureAwait(false);

    }



    private async Task PostJournalAsync(Guid companyId, Guid branchId, Guid userId, Guid documentId, JournalDetailsDto journal, string reason,

        CancellationToken cancellationToken)

    {

        var key = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"PetraERP.Purchases.post:{documentId:N}"))[..16]);

        var request = PostingRequest.Post(PostingRequestScope.Create(companyId, branchId, userId), key, journal.Id,

            ExpectedJournalVersion.FromBase64(journal.VersionBase64), AuditReason.Create(reason));

        await new SqlPostingService(context).PostPurchaseAsync(documentId, request, cancellationToken).ConfigureAwait(false);

    }



    private static SavePurchaseInvoiceCommand ToCommand(PurchaseInvoiceRow r) => new(r.PatternRevisionId, r.SupplierId, r.WarehouseId, r.InvoiceDateUtc,

        (PaymentMethod)r.PaymentMethod, r.CurrencyCode, r.ExchangeRate, r.SupplierReference, r.Notes,

        r.Lines.OrderBy(l => l.DisplayOrder).Select(l => new PurchaseLineCommand(l.ItemId, l.UnitId, l.Quantity, l.UnitCost, l.DiscountAmount,

            l.TaxAmount, l.CostCenterId, l.Notes)).ToList())
    {
        Adjustments = r.Adjustments.OrderBy(a => a.DisplayOrder).Select(a => new PurchaseAdjustmentCommand((PetraERP.Domain.Sales.SalesInvoiceAdjustmentKind)a.Kind, a.AccountId, a.Amount,
            a.Percentage, a.Notes, a.CostCenterId, a.DisplayOrder, a.CounterAccountId)).ToList(),
    };



    private static void RequireKey(string key)

    {

        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)

            throw new RequestValidationException("purchases.idempotency_key_invalid", "A request key of at most 128 characters is required.", "idempotencyKey");

    }



    private async Task LockScopeAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken)

    {

        context.ChangeTracker.Clear();

        await InventoryWriteScope.LockAsync(context, companyId, branchId, cancellationToken).ConfigureAwait(false);

        var company = await context.Companies.FromSqlInterpolated($"SELECT * FROM [system].[Companies] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={companyId}")

            .AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var branch = await context.Branches.FromSqlInterpolated($"SELECT * FROM [system].[Branches] WITH (UPDLOCK,HOLDLOCK) WHERE [CompanyId]={companyId} AND [Id]={branchId}")

            .AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (company is null || branch is null) throw new ResourceNotFoundException("Company/Branch");

        if (!company.IsActive || !branch.IsActive) throw new BusinessRuleException("purchases.scope_inactive", "The company and branch must be active.");

    }



    private async Task<PurchaseInvoiceRow> LoadForUpdateAsync(Guid companyId, Guid branchId, Guid invoiceId, CancellationToken cancellationToken)

    {

        context.ChangeTracker.Clear();

        return await context.PurchaseInvoices.FromSqlInterpolated($"SELECT * FROM [purchases].[Invoices] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={invoiceId} AND [CompanyId]={companyId} AND [BranchId]={branchId}")

            .Include(i => i.Lines).Include(i => i.Adjustments).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)

            ?? throw new ResourceNotFoundException("PurchaseInvoice");

    }



    private async Task<Guid> RequireOpenPeriodAsync(Guid companyId, DateOnly date, CancellationToken cancellationToken)

    {

        var year = await context.FiscalYears.AsNoTracking().SingleOrDefaultAsync(y => y.CompanyId == companyId && y.RangeStart <= date && y.RangeEnd >= date, cancellationToken).ConfigureAwait(false)

            ?? throw new BusinessRuleException("purchases.no_fiscal_year", "No fiscal year covers the document date.");

        var period = await context.AccountingPeriods.AsNoTracking().SingleOrDefaultAsync(p => p.FiscalYearId == year.Id && p.RangeStart <= date && p.RangeEnd >= date, cancellationToken).ConfigureAwait(false);

        if (period is null || period.State != (byte)AccountingPeriodState.Open) throw new BusinessRuleException("purchases.period_closed", "The accounting period is not open.");

        return year.Id;

    }

}

