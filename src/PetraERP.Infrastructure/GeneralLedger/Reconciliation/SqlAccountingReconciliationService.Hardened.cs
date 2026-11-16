using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.GeneralLedger.Reconciliation;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Treasury;

namespace PetraERP.Infrastructure.GeneralLedger.Reconciliation;

// P02 (fix_20261028_191422): reconciliation outcomes must be supported by the queries that produced them.
//  PASS  = the compared quantities are exactly equal (or the empty set was actually queried and counted);
//  WARNING = unverifiable, or a residual that is explained/rounded and is reported separately;
//  BLOCKING = a provable inconsistency.
public sealed partial class SqlAccountingReconciliationService
{
    // Amount x rate products are rounded when stored at base scale; only that product comparison keeps a documented tolerance.
    private const decimal ProductRoundingTolerance = 0.1m;

    // Subledger = outstanding posted credit invoices + carried open items without an invoice in this year. Control = signed GL balance of the control
    // accounts over posted journals. Separate, individually reported reconciling items: unapplied advances (posted vouchers not yet absorbed by
    // an invoice) and FX revaluation journals. PASS only when the residual after advances is exactly zero.
    private async Task<ReconciliationFindingDto> SubledgerVsControlAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, string code, bool customers, CancellationToken token)
    {
        var invoices = await SettlementQueries.LoadInvoicesAsync(context, companyId, branchId, customers ? VoucherKind.Receipt : VoucherKind.Payment, null, null, null, token).ConfigureAwait(false);
        var partyType = customers ? (byte)1 : (byte)2;
        var carriedItems = await context.OpenItems.AsNoTracking()
            .Where(o => o.CompanyId == companyId && o.PartyType == partyType && o.OriginType != 1 && o.State != 3 && o.State != 4 && (!branchId.HasValue || o.BranchId == branchId.Value))
            .Select(o => new { o.AccountId, o.CurrentBaseCarryingAmount }).ToListAsync(token).ConfigureAwait(false);
        var carried = carriedItems.Sum(o => o.CurrentBaseCarryingAmount);
        var ownInvoices = invoices.Where(i => !i.IsOpenItem).ToList();
        // Independent of the ledger: the invoice's outstanding document-currency amount at its own stored rate. The loader's journal-derived base
        // (OutstandingBase) is compared per invoice, so drift in a posted journal line cannot hide by moving the subledger and the control together.
        var documentBase = ownInvoices.Sum(i => DocumentCurrencyPolicy.ToBase(i.Outstanding, i.ExchangeRate));
        var foreignInvoices = ownInvoices.Count(i => i.ExchangeRate != 1m);
        var journalDrift = ownInvoices.Where(i => Math.Abs(DocumentCurrencyPolicy.ToBase(i.Outstanding, i.ExchangeRate) - i.OutstandingBase) > (i.ExchangeRate == 1m ? 0m : 0.005m)).ToList();
        var subledgerTotal = documentBase + carried;

        var mapped = customers
            ? await context.Parties.AsNoTracking().Where(p => p.CompanyId == companyId && (p.Role == 1 || p.Role == 3) && p.ReceivableAccountId.HasValue)
                .Select(p => p.ReceivableAccountId!.Value).Distinct().ToListAsync(token).ConfigureAwait(false)
            : await context.Parties.AsNoTracking().Where(p => p.CompanyId == companyId && (p.Role == 2 || p.Role == 3) && p.PayableAccountId.HasValue)
                .Select(p => p.PayableAccountId!.Value).Distinct().ToListAsync(token).ConfigureAwait(false);
        var kind = customers ? (byte)VoucherKind.Receipt : (byte)VoucherKind.Payment;
        // Historical provenance: accounts frozen in the posted documents, in open items AND in posted vouchers' control account.
        var voucherAccounts = await context.Vouchers.AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.Kind == kind && (v.Status == 2 || v.Status == 3) && v.PartyControlAccountId != null && (!branchId.HasValue || v.BranchId == branchId.Value))
            .Select(v => v.PartyControlAccountId!.Value).Distinct().ToListAsync(token).ConfigureAwait(false);
        var accounts = mapped.Concat(ownInvoices.Select(i => i.AccountId)).Concat(carriedItems.Select(o => o.AccountId)).Concat(voucherAccounts).Distinct().ToList();

        var control = accounts.Count == 0 ? 0m : await (
            from l in context.JournalLines.AsNoTracking()
            join j in context.JournalEntries.AsNoTracking() on l.JournalId equals j.Id
            where j.CompanyId == companyId && accounts.Contains(l.AccountId) && (j.State == 4 || j.State == 5) && (!branchId.HasValue || j.BranchId == branchId.Value)
            select customers ? l.BaseDebit - l.BaseCredit : l.BaseCredit - l.BaseDebit).SumAsync(token).ConfigureAwait(false);

        // Reconciling item 1 - unapplied advances: posted (not reversed) vouchers credit/debit the control account in full while the subledger is only reduced by what was applied.
        var advanceRows = accounts.Count == 0 ? [] : await context.Vouchers.AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.Kind == kind && v.Status == 2 && v.PartyControlAccountId != null && accounts.Contains(v.PartyControlAccountId!.Value) && (!branchId.HasValue || v.BranchId == branchId.Value))
            .Select(v => new { v.Id, v.Amount, v.ExchangeRate, Legacy = v.Allocations.Sum(a => (decimal?)a.Amount) ?? 0m })
            .ToListAsync(token).ConfigureAwait(false);
        var advanceIds = advanceRows.Select(v => v.Id).ToList();
        var modernApplied = advanceIds.Count == 0 ? [] : (await context.SettlementApplications.AsNoTracking()
            .Where(a => advanceIds.Contains(a.VoucherId) && a.Batch!.State == 1)
            .Select(a => new { a.VoucherId, a.SettlementAmount }).ToListAsync(token).ConfigureAwait(false))
            .GroupBy(a => a.VoucherId).ToDictionary(g => g.Key, g => g.Sum(x => x.SettlementAmount));
        var advances = advanceRows.Sum(v => Math.Max(0m, v.Amount - v.Legacy - modernApplied.GetValueOrDefault(v.Id)) * v.ExchangeRate);

        // Reconciling item 2 - FX revaluation journals posted on these accounts (base-only adjustments with no subledger counterpart).
        var revaluationJournals = accounts.Count == 0 ? [] : await context.FxRevaluationRequests.AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Status == 2 && r.OutcomeJournalId != null).Select(r => r.OutcomeJournalId!.Value).ToListAsync(token).ConfigureAwait(false);
        var fxReval = revaluationJournals.Count == 0 ? 0m : await (
            from l in context.JournalLines.AsNoTracking()
            join j in context.JournalEntries.AsNoTracking() on l.JournalId equals j.Id
            where j.CompanyId == companyId && revaluationJournals.Contains(j.Id) && accounts.Contains(l.AccountId) && (j.State == 4 || j.State == 5) && (!branchId.HasValue || j.BranchId == branchId.Value)
            select customers ? l.BaseDebit - l.BaseCredit : l.BaseCredit - l.BaseDebit).SumAsync(token).ConfigureAwait(false);

        var expected = subledgerTotal - advances;
        var residual = control - expected;
        var label = customers ? "الزبائن" : "الموردين";
        var components = $"مجمع الفواتير والبنود المفتوحة={subledgerTotal}؛ دفعات مقدمة غير موزعة={advances}؛ تقييم عملات={fxReval}؛ رصيد القيود={control}";
        string severity; string text;
        var allowance = Tolerance + 0.005m * foreignInvoices;
        if (journalDrift.Count > 0) { severity = "BLOCKING"; text = $"قيد/قيمة دفترية لفواتير ({journalDrift.Count}) تخالف قيمة المستند بسعره المخزَّن: INVOICE_JOURNAL_BASE_DIFFERS_FROM_DOCUMENT؛ فارق إجمالي={journalDrift.Sum(i => i.OutstandingBase - DocumentCurrencyPolicy.ToBase(i.Outstanding, i.ExchangeRate))} ({components})"; }
        else if (residual == 0m) { severity = "PASS"; text = $"مجمع ذمم {label} بعد الدفعات المقدمة يطابق حساب المراقبة تماماً ({components}) / Subledger equals the control account exactly."; }
        else if (fxReval != 0m && residual == fxReval) { severity = "WARNING"; text = $"فارق {residual} مفسَّر بالكامل بقيود إعادة تقييم العملات ولا يقابله سجل فرعي ({components}) / difference is explained by FX revaluation journals."; }
        else if (Math.Abs(residual) <= allowance) { severity = "WARNING"; text = $"بقايا تقريب {residual} ضمن السماحية {allowance} وليست تطابقاً تاماً ({components}) / rounding residual, not an exact match."; }
        else { severity = "BLOCKING"; text = $"فارق غير مفسَّر {residual} بين مجمع ذمم {label} ورصيد حساب المراقبة ({components}). تحقق من القيود اليدوية على حساب المراقبة والتخصيصات."; }
        return Finding(companyId, fiscalYearId, branchId, code, expected, control, residual, severity, text,
            severity == "PASS" ? null : "راجع القيود اليدوية على حساب المراقبة والتخصيصات غير المكتملة", journalDrift.FirstOrDefault()?.Id.ToString(), accounts.FirstOrDefault());
    }

    // Stock ledger value (latest ValueAfter per item/warehouse) against the GL balance of EVERY inventory account that ever carried a posting
    // role: all item/group "Inventory" mappings (active AND inactive/historical) and the accounts frozen in inventory documents/adjustments.
    // GL is split into the part posted by stock-bearing documents and the rest (opening batches, reversals, manual journals), reported separately.
    private async Task CheckInventoryVsGlAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, List<ReconciliationFindingDto> findings, CancellationToken token)
    {
        var movements = context.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && (!branchId.HasValue || m.BranchId == branchId.Value));
        var latest = movements.Where(m => m.RecordedSequence == context.StockMovements.Where(x => x.CompanyId == m.CompanyId && x.BranchId == m.BranchId && x.ItemId == m.ItemId && x.WarehouseId == m.WarehouseId)
                .Max(x => x.RecordedSequence));
        var movementCount = await movements.CountAsync(token).ConfigureAwait(false);
        var stockValue = await latest.SumAsync(m => m.ValueAfter ?? 0m, token).ConfigureAwait(false);
        var unaudited = await latest.CountAsync(m => m.ValueAfter == null, token).ConfigureAwait(false);

        var mappings = await context.ItemAccountMappings.AsNoTracking().Where(r => r.CompanyId == companyId && r.Role == "Inventory")
            .Select(r => new { r.AccountId, r.IsActive }).ToListAsync(token).ConfigureAwait(false);
        var fromDocuments = await context.InventoryDocuments.AsNoTracking().Where(d => d.CompanyId == companyId && d.InventoryAccountId != null)
            .Select(d => d.InventoryAccountId!.Value).Distinct().ToListAsync(token).ConfigureAwait(false);
        // Posting patterns are versioned: EVERY revision (historical ones included) that names an "Inventory" account contributes it, so a remapped pattern never hides history.
        var patternAccounts = await InventoryAccountsFromPatternRevisionsAsync(companyId, token).ConfigureAwait(false);
        var accounts = mappings.Select(m => m.AccountId).Concat(fromDocuments).Concat(patternAccounts).Distinct().ToList();
        var historicalOnly = mappings.Where(m => !m.IsActive).Select(m => m.AccountId).Distinct().Count(a => !mappings.Any(m => m.IsActive && m.AccountId == a));

        var docJournals = new HashSet<Guid>();
        docJournals.UnionWith(await context.SalesInvoices.AsNoTracking().Where(s => s.CompanyId == companyId && s.JournalEntryId != null && context.StockMovements.Any(m => m.DocumentId == s.Id))
            .Select(s => s.JournalEntryId!.Value).ToListAsync(token).ConfigureAwait(false));
        docJournals.UnionWith(await context.PurchaseInvoices.AsNoTracking().Where(p => p.CompanyId == companyId && p.JournalEntryId != null && context.StockMovements.Any(m => m.DocumentId == p.Id))
            .Select(p => p.JournalEntryId!.Value).ToListAsync(token).ConfigureAwait(false));
        docJournals.UnionWith(await context.InventoryDocuments.AsNoTracking().Where(d => d.CompanyId == companyId && d.JournalId != null).Select(d => d.JournalId!.Value).ToListAsync(token).ConfigureAwait(false));
        docJournals.UnionWith(await context.InventoryDocuments.AsNoTracking().Where(d => d.CompanyId == companyId && d.ReversalJournalId != null).Select(d => d.ReversalJournalId!.Value).ToListAsync(token).ConfigureAwait(false));

        if (accounts.Count == 0)
        {
            var proven = movementCount == 0 && stockValue == 0m && docJournals.Count == 0;
            findings.Add(Finding(companyId, fiscalYearId, branchId, "CHECK_06_INVENTORY_VS_GL", stockValue, null, null, proven ? "PASS" : "WARNING",
                proven ? "NO_DATA: لم يُعثر على حركات مخزون (0) ولا قيود مستندات مخزون (0) ولا حسابات مخزون؛ تم التحقق بالاستعلام ولا يوجد ما يُطابَق / queried: 0 stock movements, 0 stock-document journals."
                    : $"UNVERIFIED: توجد حركات ({movementCount}) أو قيود مستندات مخزون ({docJournals.Count}) لكن لا يوجد حساب مخزون (حالي أو تاريخي) للمقارنة مع الأستاذ / no inventory account (current or historical) to compare against.",
                proven ? null : "عرّف حساب المخزون على المواد أو مجموعاتها ثم أعد الفحص"));
            return;
        }

        var glLines = await (from l in context.JournalLines.AsNoTracking()
                             join j in context.JournalEntries.AsNoTracking() on l.JournalId equals j.Id
                             where j.CompanyId == companyId && accounts.Contains(l.AccountId) && (j.State == 4 || j.State == 5) && (!branchId.HasValue || j.BranchId == branchId.Value)
                             select new { j.Id, Net = l.BaseDebit - l.BaseCredit }).ToListAsync(token).ConfigureAwait(false);
        var gl = glLines.Sum(l => l.Net);
        var glByDocuments = glLines.Where(l => docJournals.Contains(l.Id)).Sum(l => l.Net);
        var glOther = gl - glByDocuments;
        var diff = Math.Abs(stockValue - gl);
        var detail = $"قيمة المخزون={stockValue}؛ الأستاذ={gl} (من مستندات مخزون={glByDocuments}؛ افتتاح/عكس/يدوي={glOther})؛ حسابات={accounts.Count} منها من أنماط الترحيل={patternAccounts.Count} وتاريخية فقط={historicalOnly}؛ حركات بلا تدقيق قيمة={unaudited}";
        string severity;
        string text;
        if (diff == 0m && unaudited == 0) { severity = "PASS"; text = $"قيمة المخزون تطابق رصيد حسابات المخزون تماماً ({detail}) / exact match."; }
        else if (diff <= ProductRoundingTolerance && unaudited == 0) { severity = "WARNING"; text = $"بقايا تقريب {diff} (متوسط التكلفة المرجح) وليست تطابقاً تاماً ({detail})."; }
        else if (unaudited > 0 && diff <= ProductRoundingTolerance) { severity = "WARNING"; text = $"حركات بلا قيمة بعد الحركة لا تسمح بإثبات المطابقة ({detail})."; }
        else { severity = "BLOCKING"; text = $"فارق {diff} بين المخزون والأستاذ ({detail})."; }
        findings.Add(Finding(companyId, fiscalYearId, branchId, "CHECK_06_INVENTORY_VS_GL", stockValue, gl, diff, severity, text,
            severity == "PASS" ? null : "راجع حركات المخزون غير المرحَّلة محاسبياً وحسابات المخزون الخاصة بالأنماط", null, accounts.FirstOrDefault()));
    }

    private async Task<List<Guid>> InventoryAccountsFromPatternRevisionsAsync(Guid companyId, CancellationToken token)
    {
        var configurations = await context.PatternRevisions.AsNoTracking().Where(r => r.Pattern!.CompanyId == companyId).Select(r => r.ConfigurationJson).ToListAsync(token).ConfigureAwait(false);
        var result = new HashSet<Guid>();
        foreach (var json in configurations)
        {
            JsonDocument document;
            try { document = JsonDocument.Parse(json); }
            catch (JsonException) { continue; }
            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object || !TryProperty(document.RootElement, "accountMappings", out var mappings) || mappings.ValueKind != JsonValueKind.Array) continue;
                foreach (var mapping in mappings.EnumerateArray())
                    if (mapping.ValueKind == JsonValueKind.Object && TryProperty(mapping, "role", out var role) && role.ValueKind == JsonValueKind.String
                        && string.Equals(role.GetString(), "Inventory", StringComparison.OrdinalIgnoreCase)
                        && TryProperty(mapping, "accountId", out var account) && account.TryGetGuid(out var id)) result.Add(id);
            }
        }
        return [.. result];
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        value = default;
        return false;
    }

    // Modern settlement model (VoucherAllocationPlan -> SettlementBatch -> SettlementApplication) and legacy VoucherAllocations, validated separately:
    //  * every batch belongs to an existing voucher of the same company/branch; an active batch owns a posted journal (the initial batch shares the voucher's);
    //    a reversed batch owns BOTH a posted settlement journal and a distinct posted reversal journal; unknown states are rejected;
    //  * every application names the batch's voucher, exactly one existing target of the same company/branch whose kind and party match the voucher;
    //  * totals come from batches only - the plan is a separate instruction that must agree with the initial batch, never added to it;
    //  * legacy allocations are summed separately; no voucher/target is applied beyond what it can absorb (exact, per currency) and the base side is capped too.
    private async Task CheckSettlementAllocationsAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, List<ReconciliationFindingDto> findings, CancellationToken token)
    {
        var problems = new List<(string Kind, string Evidence)>();
        var warnings = new List<(string Kind, string Evidence)>();

        var batches = await context.SettlementBatches.AsNoTracking()
            .Where(b => b.CompanyId == companyId && (!branchId.HasValue || b.BranchId == branchId.Value))
            .Select(b => new { b.Id, b.CompanyId, b.BranchId, b.VoucherId, b.IsInitial, b.State, b.JournalId, b.ReversalJournalId, b.ReversedAtUtc }).ToListAsync(token).ConfigureAwait(false);
        var batchById = batches.ToDictionary(b => b.Id);
        var batchVoucherIds = batches.Select(b => b.VoucherId).Distinct().ToList();

        var vouchers = await context.Vouchers.AsNoTracking()
            .Where(v => (v.CompanyId == companyId && (v.Status == 2 || v.Status == 3) && (!branchId.HasValue || v.BranchId == branchId.Value)) || batchVoucherIds.Contains(v.Id))
            .Select(v => new
            {
                v.Id, v.CompanyId, v.BranchId, v.Kind, v.PartyId, v.Status, v.Amount, v.ExchangeRate, v.CurrencyCode, v.JournalEntryId,
                Legacy = v.Allocations.Sum(a => (decimal?)a.Amount) ?? 0m,
            }).ToListAsync(token).ConfigureAwait(false);
        var voucherById = vouchers.ToDictionary(v => v.Id);

        var journalIds = batches.SelectMany(b => new[] { b.JournalId, b.ReversalJournalId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var journals = await context.JournalEntries.AsNoTracking().Where(j => journalIds.Contains(j.Id))
            .Select(j => new { j.Id, j.CompanyId, j.BranchId, j.State }).ToDictionaryAsync(j => j.Id, token).ConfigureAwait(false);
        bool PostedIn(Guid? journal, Guid company, Guid branch) =>
            journal is { } id && journals.TryGetValue(id, out var e) && (e.State == 4 || e.State == 5) && e.CompanyId == company && e.BranchId == branch;

        foreach (var b in batches)
        {
            var id = b.Id.ToString();
            if (!voucherById.TryGetValue(b.VoucherId, out var owner)) { problems.Add(("BATCH_VOUCHER_MISSING", id)); continue; }
            if (owner.CompanyId != b.CompanyId || owner.BranchId != b.BranchId) problems.Add(("BATCH_VOUCHER_SCOPE_MISMATCH", id));
            if (owner.Status is not (2 or 3)) problems.Add(("BATCH_ON_UNPOSTED_VOUCHER", id));
            if (b.State is not (1 or 2)) { problems.Add(("BATCH_UNKNOWN_STATE", id)); continue; }
            if (b.State == 1)
            {
                if (!PostedIn(b.JournalId, b.CompanyId, b.BranchId)) problems.Add(("ACTIVE_BATCH_WITHOUT_POSTED_JOURNAL", id));
                if (b.ReversalJournalId is not null || b.ReversedAtUtc is not null) problems.Add(("ACTIVE_BATCH_HAS_REVERSAL_DATA", id));
                if (owner.Status == 3) problems.Add(("ACTIVE_BATCH_ON_REVERSED_VOUCHER", id));
                if (b.IsInitial && b.JournalId != owner.JournalEntryId) problems.Add(("INITIAL_BATCH_JOURNAL_NOT_VOUCHER_JOURNAL", id));
            }
            else
            {
                if (!PostedIn(b.JournalId, b.CompanyId, b.BranchId)) problems.Add(("REVERSED_BATCH_WITHOUT_POSTED_SETTLEMENT_JOURNAL", id));
                if (!PostedIn(b.ReversalJournalId, b.CompanyId, b.BranchId) || b.ReversedAtUtc is null) problems.Add(("REVERSED_BATCH_WITHOUT_POSTED_REVERSAL_JOURNAL", id));
                if (b.ReversalJournalId is not null && b.ReversalJournalId == b.JournalId) problems.Add(("REVERSAL_JOURNAL_EQUALS_SETTLEMENT_JOURNAL", id));
            }
        }
        foreach (var g in batches.Where(b => b.IsInitial).GroupBy(b => b.VoucherId).Where(g => g.Count() > 1)) problems.Add(("DUPLICATE_INITIAL_BATCH", g.Key.ToString()));

        var applications = await context.SettlementApplications.AsNoTracking()
            .Where(a => a.Batch!.CompanyId == companyId && (!branchId.HasValue || a.Batch.BranchId == branchId.Value))
            .Select(a => new
            {
                a.Id, a.BatchId, a.VoucherId, a.SalesInvoiceId, a.PurchaseInvoiceId, a.OpenItemId, a.InvoiceCurrency, a.SettlementCurrency,
                a.InvoiceAmount, a.SettlementAmount, a.SettlementRate, a.SettlementBaseAmount,
            }).ToListAsync(token).ConfigureAwait(false);
        var active = applications.Where(a => batchById.TryGetValue(a.BatchId, out var b) && b.State == 1).ToList();

        var legacyAllocations = await context.Vouchers.AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.Status == 2 && (!branchId.HasValue || v.BranchId == branchId.Value))
            .SelectMany(v => v.Allocations).Select(a => new { a.VoucherId, a.SalesInvoiceId, a.Amount }).ToListAsync(token).ConfigureAwait(false);
        var legacyPerSales = legacyAllocations.GroupBy(a => a.SalesInvoiceId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var allSalesIds = applications.Where(a => a.SalesInvoiceId.HasValue).Select(a => a.SalesInvoiceId!.Value).Concat(legacyPerSales.Keys).Distinct().ToList();
        var allPurchaseIds = applications.Where(a => a.PurchaseInvoiceId.HasValue).Select(a => a.PurchaseInvoiceId!.Value).Distinct().ToList();
        var allOpenItemIds = applications.Where(a => a.OpenItemId.HasValue).Select(a => a.OpenItemId!.Value).Distinct().ToList();
        var sales = await context.SalesInvoices.AsNoTracking().Where(i => allSalesIds.Contains(i.Id))
            .Select(i => new { i.Id, i.CompanyId, i.BranchId, Party = i.CustomerId, i.GrandTotal, i.CurrencyCode }).ToDictionaryAsync(i => i.Id, token).ConfigureAwait(false);
        var purchases = await context.PurchaseInvoices.AsNoTracking().Where(i => allPurchaseIds.Contains(i.Id))
            .Select(i => new { i.Id, i.CompanyId, i.BranchId, Party = i.SupplierId, i.GrandTotal, i.CurrencyCode }).ToDictionaryAsync(i => i.Id, token).ConfigureAwait(false);
        var openItems = await context.OpenItems.AsNoTracking().Where(o => allOpenItemIds.Contains(o.Id))
            .Select(o => new { o.Id, o.CompanyId, o.BranchId, o.PartyId, o.PartyType, o.OriginalAmount, o.CurrencyCode }).ToDictionaryAsync(o => o.Id, token).ConfigureAwait(false);

        foreach (var a in applications)
        {
            var id = a.Id.ToString();
            var targets = (a.SalesInvoiceId.HasValue ? 1 : 0) + (a.PurchaseInvoiceId.HasValue ? 1 : 0) + (a.OpenItemId.HasValue ? 1 : 0);
            if (targets != 1) problems.Add(("APPLICATION_TARGET_COUNT", id));
            if (a.InvoiceAmount <= 0 || a.SettlementAmount <= 0) problems.Add(("APPLICATION_NON_POSITIVE_AMOUNT", id));
            if (Math.Abs(a.SettlementAmount * a.SettlementRate - a.SettlementBaseAmount) > ProductRoundingTolerance) problems.Add(("APPLICATION_BASE_AMOUNT_MISMATCH", id));
            if (!batchById.TryGetValue(a.BatchId, out var batch)) { problems.Add(("APPLICATION_BATCH_MISSING", id)); continue; }
            if (a.VoucherId != batch.VoucherId) problems.Add(("APPLICATION_VOUCHER_DIFFERS_FROM_BATCH", id));
            voucherById.TryGetValue(batch.VoucherId, out var owner);
            if (owner is not null && !string.Equals(owner.CurrencyCode, a.SettlementCurrency, StringComparison.OrdinalIgnoreCase)) problems.Add(("APPLICATION_CURRENCY_DIFFERS_FROM_VOUCHER", id));

            string? targetCurrency = null; Guid? targetParty = null; bool targetFound = true; bool targetInScope = true; bool kindMatches = true;
            if (a.SalesInvoiceId is { } sid)
            {
                if (sales.TryGetValue(sid, out var s)) { targetCurrency = s.CurrencyCode; targetParty = s.Party; targetInScope = s.CompanyId == batch.CompanyId && s.BranchId == batch.BranchId; }
                else { targetFound = false; problems.Add(("ALLOCATION_TARGETS_MISSING_SALES_INVOICE", sid.ToString())); }
                kindMatches = owner is null || owner.Kind == (byte)VoucherKind.Receipt;
            }
            else if (a.PurchaseInvoiceId is { } pid)
            {
                if (purchases.TryGetValue(pid, out var p)) { targetCurrency = p.CurrencyCode; targetParty = p.Party; targetInScope = p.CompanyId == batch.CompanyId && p.BranchId == batch.BranchId; }
                else { targetFound = false; problems.Add(("ALLOCATION_TARGETS_MISSING_PURCHASE_INVOICE", pid.ToString())); }
                kindMatches = owner is null || owner.Kind == (byte)VoucherKind.Payment;
            }
            else if (a.OpenItemId is { } oid)
            {
                if (openItems.TryGetValue(oid, out var o))
                {
                    targetCurrency = o.CurrencyCode; targetParty = o.PartyId; targetInScope = o.CompanyId == batch.CompanyId && o.BranchId == batch.BranchId;
                    kindMatches = owner is null || (owner.Kind == (byte)VoucherKind.Receipt ? o.PartyType == 1 : owner.Kind == (byte)VoucherKind.Payment && o.PartyType == 2);
                }
                else { targetFound = false; problems.Add(("ALLOCATION_TARGETS_MISSING_OPEN_ITEM", oid.ToString())); }
            }
            if (!targetFound) continue;
            if (!targetInScope) problems.Add(("APPLICATION_TARGET_SCOPE_MISMATCH", id));
            if (!kindMatches) problems.Add(("APPLICATION_TARGET_KIND_DIFFERS_FROM_VOUCHER", id));
            if (owner?.PartyId is { } voucherParty && targetParty is { } party && voucherParty != party) problems.Add(("APPLICATION_TARGET_PARTY_DIFFERS_FROM_VOUCHER", id));
            if (targetCurrency is not null && !string.Equals(targetCurrency, a.InvoiceCurrency, StringComparison.OrdinalIgnoreCase)) problems.Add(("APPLICATION_CURRENCY_DIFFERS_FROM_INVOICE", id));
        }
        foreach (var g in applications.GroupBy(a => (a.BatchId, a.SalesInvoiceId, a.PurchaseInvoiceId, a.OpenItemId)).Where(g => g.Count() > 1))
            problems.Add(("DUPLICATE_APPLICATION_TARGET_IN_BATCH", g.Key.BatchId.ToString()));

        // Plan vs initial batch: separate representations of the SAME instruction; they must agree, and are never added together.
        var plans = await context.VoucherAllocationPlans.AsNoTracking().Where(p => p.Voucher!.CompanyId == companyId && (!branchId.HasValue || p.Voucher.BranchId == branchId.Value) && (p.Voucher.Status == 2 || p.Voucher.Status == 3))
            .Select(p => new { p.VoucherId, p.SalesInvoiceId, p.PurchaseInvoiceId, p.OpenItemId, p.InvoiceAmount, p.SettlementAmount }).ToListAsync(token).ConfigureAwait(false);
        var initialIds = batches.Where(b => b.IsInitial).GroupBy(b => b.VoucherId).ToDictionary(g => g.Key, g => g.First().Id); // duplicates are already reported as DUPLICATE_INITIAL_BATCH
        foreach (var g in plans.GroupBy(p => p.VoucherId))
        {
            if (!initialIds.TryGetValue(g.Key, out var initialBatch)) { warnings.Add(("PLAN_WITHOUT_INITIAL_BATCH", g.Key.ToString())); continue; }
            var planned = g.GroupBy(p => (p.SalesInvoiceId, p.PurchaseInvoiceId, p.OpenItemId)).ToDictionary(x => x.Key, x => (Invoice: x.Sum(y => y.InvoiceAmount), Settlement: x.Sum(y => y.SettlementAmount)));
            var applied = applications.Where(a => a.BatchId == initialBatch).GroupBy(a => (a.SalesInvoiceId, a.PurchaseInvoiceId, a.OpenItemId))
                .ToDictionary(x => x.Key, x => (Invoice: x.Sum(y => y.InvoiceAmount), Settlement: x.Sum(y => y.SettlementAmount)));
            if (planned.Count != applied.Count || planned.Any(kv => !applied.TryGetValue(kv.Key, out var v) || v != kv.Value)) problems.Add(("INITIAL_BATCH_DIFFERS_FROM_PLAN", g.Key.ToString()));
        }

        // Voucher side. Legacy (VoucherAllocations) and modern (active batch applications) are counted once each, in the voucher currency; the base side is capped separately.
        var activePerVoucher = active.GroupBy(a => a.VoucherId).ToDictionary(g => g.Key, g => (Amount: g.Sum(x => x.SettlementAmount), Base: g.Sum(x => x.SettlementBaseAmount), Count: g.Count()));
        var postedVouchers = vouchers.Where(v => v.Status == 2 && v.CompanyId == companyId && (!branchId.HasValue || v.BranchId == branchId.Value)).ToList();
        foreach (var v in postedVouchers)
        {
            var modern = activePerVoucher.GetValueOrDefault(v.Id);
            if (v.Legacy + modern.Amount > v.Amount) problems.Add(("VOUCHER_OVER_APPLIED", v.Id.ToString()));
            if (modern.Base > v.Amount * v.ExchangeRate + ProductRoundingTolerance) problems.Add(("VOUCHER_BASE_OVER_APPLIED", v.Id.ToString()));
            if (v.Legacy > 0m && modern.Count > 0) warnings.Add(("LEGACY_AND_MODERN_ALLOCATIONS_MIXED", v.Id.ToString()));
        }

        // Target side: sales / purchase invoices (net of posted returns) and open items - exact, in the target's own currency.
        var salesReturns = allSalesIds.Count == 0 ? [] : (await context.SalesInvoices.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.ReturnOfInvoiceId != null && allSalesIds.Contains(i.ReturnOfInvoiceId!.Value) && (i.Status == 2 || i.Status == 3))
            .Select(i => new { Id = i.ReturnOfInvoiceId!.Value, i.GrandTotal }).ToListAsync(token).ConfigureAwait(false))
            .GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.Sum(x => x.GrandTotal));
        var appliedPerSales = active.Where(a => a.SalesInvoiceId.HasValue).GroupBy(a => a.SalesInvoiceId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.InvoiceAmount));
        foreach (var (id, invoice) in sales)
            if (appliedPerSales.GetValueOrDefault(id) + legacyPerSales.GetValueOrDefault(id) > invoice.GrandTotal - salesReturns.GetValueOrDefault(id)) problems.Add(("SALES_INVOICE_OVER_APPLIED", id.ToString()));
        foreach (var id in legacyPerSales.Keys.Where(k => !sales.ContainsKey(k))) problems.Add(("ALLOCATION_TARGETS_MISSING_SALES_INVOICE", id.ToString()));

        var purchaseReturns = allPurchaseIds.Count == 0 ? [] : (await context.PurchaseInvoices.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.ReturnOfInvoiceId != null && allPurchaseIds.Contains(i.ReturnOfInvoiceId!.Value) && i.Status == 2)
            .Select(i => new { Id = i.ReturnOfInvoiceId!.Value, i.GrandTotal }).ToListAsync(token).ConfigureAwait(false))
            .GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.Sum(x => x.GrandTotal));
        foreach (var g in active.Where(a => a.PurchaseInvoiceId.HasValue).GroupBy(a => a.PurchaseInvoiceId!.Value))
            if (purchases.TryGetValue(g.Key, out var invoice) && g.Sum(x => x.InvoiceAmount) > invoice.GrandTotal - purchaseReturns.GetValueOrDefault(g.Key)) problems.Add(("PURCHASE_INVOICE_OVER_APPLIED", g.Key.ToString()));
        foreach (var g in active.Where(a => a.OpenItemId.HasValue).GroupBy(a => a.OpenItemId!.Value))
            if (openItems.TryGetValue(g.Key, out var item) && g.Sum(x => x.InvoiceAmount) > item.OriginalAmount) problems.Add(("OPEN_ITEM_OVER_APPLIED", g.Key.ToString()));

        // Separate transaction (per voucher currency) and base values in the report.
        var perCurrency = string.Join("، ", postedVouchers.GroupBy(v => v.CurrencyCode).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
            $"{g.Key}: سندات={g.Sum(v => v.Amount)} موزع(قديم)={g.Sum(v => v.Legacy)} موزع(تسويات)={g.Sum(v => activePerVoucher.GetValueOrDefault(v.Id).Amount)}"));
        var voucherBase = postedVouchers.Sum(v => v.Amount * v.ExchangeRate);
        var appliedBase = postedVouchers.Sum(v => activePerVoucher.GetValueOrDefault(v.Id).Base + v.Legacy * v.ExchangeRate);
        var examined = batches.Count + applications.Count + legacyAllocations.Count + plans.Count;
        var severity = problems.Count > 0 ? "BLOCKING" : warnings.Count > 0 ? "WARNING" : "PASS";
        var text = problems.Count > 0 ? "مخالفات تخصيص: " + string.Join("؛ ", problems.GroupBy(p => p.Kind).Select(g => $"{g.Key}={g.Count()}"))
            : warnings.Count > 0 ? "ملاحظات تخصيص (ليست مخالفة قاطعة): " + string.Join("؛ ", warnings.GroupBy(p => p.Kind).Select(g => $"{g.Key}={g.Count()}"))
            : examined == 0 ? "NO_DATA: لا توجد دفعات تسوية ولا تطبيقات ولا تخصيصات قديمة ولا خطط (0) — تم التحقق بالاستعلام ولا يوجد ما يُطابَق / queried: nothing to reconcile."
            : $"التخصيصات القديمة والتسويات الحديثة متسقة: دفعات={batches.Count}، تطبيقات={applications.Count}، قديمة={legacyAllocations.Count}، خطط={plans.Count}؛ {perCurrency} / Settlement applications are consistent.";
        findings.Add(Finding(companyId, fiscalYearId, branchId, "CHECK_09_SETTLEMENT_ALLOCATIONS", voucherBase, appliedBase, problems.Count, severity, text,
            severity == "PASS" ? null : "راجع التخصيصات المذكورة وأعد توزيعها أو اعكس الدفعة", (problems.Count > 0 ? problems : warnings).FirstOrDefault().Evidence));
    }

    // Opening/rollover continuity. (1) in-database integrity of every posted opening batch (balanced, owns a posted journal equal to its lines, fingerprinted);
    // (2) cross-year: a Rollover batch must equal the previous year's closing balance sheet (net income carried to retained earnings), read from the previous
    // year's own database. If that read cannot run the outcome is UNVERIFIED (WARNING), never PASS. No batch at all is PASS only when the registry proves a first year.
    private async Task CheckRolloverContinuityAsync(Guid companyId, Guid fiscalYearId, List<ReconciliationFindingDto> findings, CancellationToken token)
    {
        var batches = await context.OpeningBalanceBatches.AsNoTracking()
            .Where(b => b.CompanyId == companyId && b.FiscalYearId == fiscalYearId && b.ApprovalState == (byte)PetraERP.Infrastructure.GeneralLedger.OpeningBalances.OpeningBalanceBatchState.Posted)
            .Select(b => new
            {
                b.Id, b.SourceType, b.SourceReference, b.SourceFingerprint, b.PostedJournalId,
                Debit = b.Lines.Sum(l => (decimal?)l.DebitBase) ?? 0m, Credit = b.Lines.Sum(l => (decimal?)l.CreditBase) ?? 0m,
                JournalDebit = context.JournalLines.Where(l => l.JournalId == b.PostedJournalId).Sum(l => (decimal?)l.BaseDebit) ?? 0m,
                JournalCredit = context.JournalLines.Where(l => l.JournalId == b.PostedJournalId).Sum(l => (decimal?)l.BaseCredit) ?? 0m,
                JournalPosted = context.JournalEntries.Any(j => j.Id == b.PostedJournalId && (j.State == 4 || j.State == 5)),
                Lines = b.Lines.Select(l => new { l.AccountId, l.DebitBase, l.CreditBase }).ToList(),
            }).ToListAsync(token).ConfigureAwait(false);
        var problems = new List<string>();
        var unverified = new List<string>();
        foreach (var b in batches)
        {
            if (b.Debit != b.Credit) problems.Add($"{b.Id}: الدفعة غير متوازنة");
            if (b.PostedJournalId is null || !b.JournalPosted) problems.Add($"{b.Id}: لا قيد مرحَّل");
            else if (b.Debit != b.JournalDebit || b.Credit != b.JournalCredit) problems.Add($"{b.Id}: مجموع الأسطر يخالف القيد");
            if (b.SourceType == "Rollover" && string.IsNullOrWhiteSpace(b.SourceFingerprint)) problems.Add($"{b.Id}: بصمة المصدر مفقودة");
        }

        var rollovers = batches.Where(b => b.SourceType == "Rollover").GroupBy(b => b.SourceReference ?? string.Empty).ToList();
        foreach (var group in rollovers)
        {
            var result = priorYear is null ? null : await priorYear.LoadAsync(companyId, fiscalYearId, group.Key, token).ConfigureAwait(false);
            if (result is null) { unverified.Add($"السنة المصدر '{group.Key}': لا يوجد مصدر موثوق لقراءة إقفال السنة السابقة / no trusted previous-year source"); continue; }
            if (result.Status != PriorYearStatus.Loaded || result.Closing is null) { unverified.Add($"السنة المصدر '{group.Key}': {result.Reason ?? "غير متاحة"}"); continue; }
            var closing = result.Closing;
            var actual = group.SelectMany(b => b.Lines).GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => g.Sum(l => l.DebitBase - l.CreditBase));
            var differences = actual.Keys.Concat(closing.BalanceSheetNet.Keys).Distinct()
                .Select(account => (Account: account, Delta: actual.GetValueOrDefault(account) - closing.BalanceSheetNet.GetValueOrDefault(account)))
                .Where(x => x.Delta != 0m).ToList();
            var carriedIncome = -closing.NetIncome;
            if (carriedIncome != 0m)
            {
                var index = differences.FindIndex(x => x.Delta == carriedIncome);
                if (index >= 0) differences.RemoveAt(index); else problems.Add($"صافي ربح/خسارة السنة {group.Key} ({closing.NetIncome}) غير مرحَّل إلى الأرباح المدورة");
            }
            foreach (var d in differences.Take(5)) problems.Add($"رصيد افتتاحي للحساب {d.Account} يخالف إقفال السنة {group.Key} بفارق {d.Delta}");
        }

        if (batches.Count == 0)
        {
            var result = priorYear is null ? null : await priorYear.LoadAsync(companyId, fiscalYearId, null, token).ConfigureAwait(false);
            if (result is null) unverified.Add("لا توجد دفعة افتتاح مرحَّلة ولا مصدر موثوق لإثبات أن هذه أول سنة / no posted opening batch and no trusted source proving a first year");
            else if (result.Status == PriorYearStatus.Unavailable) unverified.Add(result.Reason ?? "تعذر التحقق من السنة السابقة");
            else if (result.Status == PriorYearStatus.Loaded) problems.Add($"توجد سنة سابقة ({result.Closing?.YearCode}) لكن لا توجد دفعة افتتاح مرحَّلة للسنة الحالية");
        }

        var severity = problems.Count > 0 ? "BLOCKING" : unverified.Count > 0 ? "WARNING" : "PASS";
        var text = problems.Count > 0 ? "مخالفات في دفعات الافتتاح/الترحيل: " + string.Join("؛ ", problems.Take(5))
            : unverified.Count > 0 ? "UNVERIFIED: تعذر إثبات استمرارية الإقفال/الافتتاح بين السنتين: " + string.Join("؛ ", unverified.Take(3))
            : batches.Count == 0 ? "NO_DATA: السجل يثبت أنها أول سنة ولا سنة سابقة؛ لا يوجد ما يُطابَق / registry proves a first year; nothing to carry."
            : $"دفعات الافتتاح/المدورة المرحَّلة ({batches.Count}) متوازنة وتطابق قيودها وإقفال السنة السابقة / Opening and rollover batches match their journals and the previous year's closing.";
        findings.Add(Finding(companyId, fiscalYearId, null, "CHECK_11_YEAR_ROLLOVER_CONTINUITY", batches.Sum(b => b.Debit), batches.Sum(b => b.JournalDebit), problems.Count, severity, text,
            severity == "PASS" ? null : problems.Count > 0 ? "راجع دفعة الافتتاح وقيدها قبل الاعتماد" : "فعّل قراءة السنة السابقة (تسجيل قاعدتها) ثم أعد الفحص"));
    }
}
