using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports;
using PetraERP.Application.Reports.Professional;

namespace PetraERP.Infrastructure.Reports.Professional;

public sealed partial class SqlProfessionalReportService
{
    private sealed record PartyEffect
    {
        public Guid EffectId { get; init; }
        public bool IsReversal { get; init; }
        public Guid DocumentId { get; init; }
        public Guid? JournalId { get; init; }
        public ProfessionalReportSource Source { get; init; }
        public Guid? PatternRevisionId { get; init; }
        public string? Number { get; init; }
        public DateTimeOffset DateUtc { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
        public string Description { get; init; } = string.Empty;
        public decimal Debit { get; init; }
        public decimal Credit { get; init; }
    }

    private async Task<ProfessionalReportExecutionResponse> PartyAsync(LedgerReportScope scope, ProfessionalReportRequest request, string baseCurrency, ProfessionalReportSource[] patternSources,
        CancellationToken cancellationToken)
    {
        var combined = request.Kind == ProfessionalReportKind.CombinedPartyStatement;
        // A combined statement is expressed in the customer (receivable) direction: balance = debit - credit, so a positive
        // closing means the party owes us net and a negative one means we owe the party net (receivable minus payable).
        var supplier = request.Kind == ProfessionalReportKind.SupplierStatement;
        var partyId = request.PartyId!.Value;
        var currency = request.CurrencyCode!;
        // Display currency (Al-Ameen rule): each document effect is valued at the register rates effective on its own date.
        var valuation = request.DisplayCurrencyCode is { } wanted && wanted != currency
            ? await DisplayValuation.CreateAsync(context, scope.CompanyId, baseCurrency, wanted, [currency], request.To, cancellationToken).ConfigureAwait(false) : null;
        var outputCurrency = valuation?.Code ?? currency;
        (decimal Debit, decimal Credit) Valued(DateTimeOffset dateUtc, decimal debit, decimal credit) => valuation is null ? (debit, credit)
            : (valuation.Convert(debit, currency, valuation.Code, DateOnly.FromDateTime(dateUtc.UtcDateTime)), valuation.Convert(credit, currency, valuation.Code, DateOnly.FromDateTime(dateUtc.UtcDateTime)));
        var party = await context.Parties.AsNoTracking().SingleOrDefaultAsync(p => p.Id == partyId && p.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("party");
        if (!combined && party.Role != 3 && party.Role != (supplier ? 2 : 1))
            throw new RequestValidationException("reporting.party_role_invalid", "الطرف المحدد لا يحمل صفة هذا الكشف (زبون أو مورد). / The selected party does not have this statement role.", "partyId");
        var accountId = supplier ? party.PayableAccountId : party.ReceivableAccountId ?? (combined ? party.PayableAccountId : null);
        var account = await context.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId && a.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("reporting.party_control_account_missing",
                $"لا يملك الطرف حساب الذمة المطلوب؛ عيّن {(supplier ? "حساب دفع الموردين" : "حساب قبض الزبائن")} قبل إصدار الكشف / The party has no mapped control account; assign the {(supplier ? "payable" : "receivable")} account before issuing this statement.");
        var allEffects = combined
            ? PartyEffects(scope, partyId, false, currency).Concat(PartyEffects(scope, partyId, true, currency))
            : PartyEffects(scope, partyId, supplier, currency);
        var effects = allEffects.Where(e => e.DateUtc < End(request.To));
        var start = Start(request.From);
        decimal opening;
        if (valuation is null)
            opening = await effects.Where(e => e.DateUtc < start).SumAsync(e => supplier ? e.Credit - e.Debit : e.Debit - e.Credit, cancellationToken).ConfigureAwait(false);
        else
            opening = (await effects.Where(e => e.DateUtc < start).Select(e => new { e.DateUtc, e.Debit, e.Credit }).ToListAsync(cancellationToken).ConfigureAwait(false))
                .Sum(e => { var v = Valued(e.DateUtc, e.Debit, e.Credit); return supplier ? v.Credit - v.Debit : v.Debit - v.Credit; });
        var actualPeriod = effects.Where(e => e.DateUtc >= start);
        if (await actualPeriod.LongCountAsync(cancellationToken).ConfigureAwait(false) > ProfessionalReportCatalog.MaximumRows) throw TooLarge();
        var filtered = FilterParty(actualPeriod, request, patternSources);
        var actualRows = await actualPeriod.OrderBy(e => e.DateUtc)
            .ThenBy(e => context.JournalEntries.Where(j => j.Id == e.JournalId).Select(j => j.PostingSequence).FirstOrDefault())
            .ThenBy(e => e.JournalId).ThenBy(e => e.EffectId).ThenBy(e => e.IsReversal).ThenBy(e => e.Source)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var selectedRows = await filtered.OrderBy(e => e.DateUtc).ThenBy(e => e.JournalId).ThenBy(e => e.DocumentId).ThenBy(e => e.Source)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        // A multiset retains distinct settlement effects which legitimately share one journal/document.
        var selected = selectedRows.GroupBy(e => e).ToDictionary(g => g.Key, g => g.Count());
        var actual = opening;
        var display = request.BalancePresentation == ProfessionalBalancePresentation.Actual ? opening : 0m;
        var sign = supplier ? -1m : 1m;
        var result = new List<ProfessionalReportRowDto>(selectedRows.Count);
        var journalIds = actualPeriod.Where(e => e.JournalId != null).Select(e => e.JournalId!.Value);
        var sequences = await context.JournalEntries.AsNoTracking().Where(j => j.CompanyId == scope.CompanyId && j.BranchId == scope.BranchId && journalIds.Contains(j.Id))
            .Select(j => new { j.Id, j.PostingSequence }).ToDictionaryAsync(j => j.Id, j => j.PostingSequence, cancellationToken).ConfigureAwait(false);
        decimal periodDebit = 0m, periodCredit = 0m;
        foreach (var effect in actualRows)
        {
            var (debit, credit) = Valued(effect.DateUtc, effect.Debit, effect.Credit);
            periodDebit += debit; periodCredit += credit;
            actual += sign * (debit - credit);
            if (!selected.TryGetValue(effect, out var remaining) || remaining == 0) continue;
            selected[effect] = remaining - 1;
            display += sign * (debit - credit);
            result.Add(new ProfessionalReportRowDto
            {
                RowId = EffectRowId(effect), JournalId = effect.JournalId, DocumentId = effect.DocumentId, Source = effect.Source,
                DocumentNumber = effect.Number, Date = DateOnly.FromDateTime(effect.DateUtc.UtcDateTime),
                PostingSequence = effect.JournalId is { } journal ? sequences.GetValueOrDefault(journal) : null,
                AccountId = account.Id, AccountCode = account.Code, AccountName = account.NameArabic,
                PartyCode = party.Code, PartyName = party.NameArabic, Description = effect.Description, CurrencyCode = outputCurrency,
                Debit = debit, Credit = credit, ActualBalance = actual, DisplayBalance = display,
                Opening = 0m, Closing = 0m, OpeningDebit = 0m, OpeningCredit = 0m, ClosingDebit = 0m, ClosingCredit = 0m,
                TransactionCurrencyCode = currency, TransactionDebit = effect.Debit, TransactionCredit = effect.Credit,
                GroupKey = string.Empty, IsAggregate = false, CanDrillThrough = true,
            });
        }
        var summary = Summary(opening, periodDebit, periodCredit, result.Sum(r => r.Debit), result.Sum(r => r.Credit),
            result.Count, supplier, request.BalancePresentation);
        var response = Empty(request, outputCurrency, party.Code, party.NameArabic, supplier ? "CreditMinusDebit" : "DebitMinusCredit", OrderRows(request, result, true), summary,
            ["كشف مستندي بعملة واحدة: فواتير آجلة ومرتجعات وسندات وتخصيصات فعلية وأرصدة مرحّلة أو افتتاحية، دون ربط أسطر المواد بالأرقام المالية.",
                "الرصيد المرحّل من سنة سابقة أو الرصيد الافتتاحي المنتقل يظهر بقيمة الأصل الكاملة تحت مصدر التسوية، وتظهر تسوياته الجارية عبر سندات الطرف نفسها.",
                "القيود اليدوية على حساب مراقبة مشترك لا تحمل معرف الطرف ولا تنسب إليه افتراضاً؛ المطابقة معها تتم عبر دفتر الأستاذ.",
                "البحث في ملاحظات المستند ووصف توزيعات الدفع وسبب التخصيص المخزنة فعلياً، وفق Collation قاعدة البيانات.",
                "أعمار الذمم، إن طلبت، تعرض المبالغ المفتوحة الفعلية عند تاريخ النهاية مستقلة عن تصفية البيان والمصدر.",
                "الرصيد الجاري الفعلي مرتب بتاريخ المستند الكامل ثم رقم القيد ومعرف الأثر؛ الترتيب المخصص لا يعيد حساب هذا الرصيد."]);
        if (valuation is not null)
            response = response with { Notes = [.. response.Notes, $"العرض بعملة {valuation.Code}: كل حركة مقيَّمة بأسعار النشرة بتاريخ حركتها؛ أعمدة عملة الحركة تبقى بعملة المستند {currency}، وأعمار الذمم وتفاصيل الفواتير تبقى بعملة الكشف. / Each movement is valued at the register rates of its own date; transaction columns, aging and invoice details stay in {currency}."] };
        if (combined)
            response = response with { Notes = [.. response.Notes, "كشف موحد: يجمع حركات الطرف كزبون (مبيعات ومرتجعات وقبض) وكمورد (مشتريات ومرتجعات ودفع) والتسويات والأرصدة المنقولة بعملة واحدة؛ الرصيد = مدين − دائن، فالموجب يعني أن الطرف مدين لنا صافياً والسالب يعني أننا مدينون له صافياً. أعمار الذمم غير متاحة في الكشف الموحد."] };
        if (request.IncludeInvoiceDetails)
            response = response with { InvoiceDetails = await DetailsAsync(scope, request, filtered, cancellationToken).ConfigureAwait(false) };
        if (request.IncludeAging)
        {
            try
            {
                var aging = await commercialReports.GetPartyAgingAsync(scope, partyId, supplier ? PartyStatementKind.Supplier : PartyStatementKind.Customer,
                    currency, request.To, cancellationToken).ConfigureAwait(false);
                var row = aging.Rows.SingleOrDefault(r => r.PartyId == partyId);
                response = response with
                {
                    Aging = new ProfessionalAgingDto(request.To, currency, row?.NotDue ?? 0m, row?.Days1To30 ?? 0m, row?.Days31To60 ?? 0m,
                        row?.Days61To90 ?? 0m, row?.Over90 ?? 0m, row?.Advance ?? 0m, row?.Net ?? 0m),
                };
            }
            catch (BusinessRuleException ex)
            {
                response = response with { Notes = [.. response.Notes, $"تعذر احتساب أعمار الذمم مع بقاء البيان صحيحاً: {ex.Message}"] };
            }
        }
        if (result.Count == 0 && actualRows.Count == 0)
            response = response with { Notes = [.. response.Notes, await ZeroMovementNoteAsync(scope, request, cancellationToken).ConfigureAwait(false)] };
        return response;
    }

    private async Task<string> ZeroMovementNoteAsync(LedgerReportScope scope, ProfessionalReportRequest request, CancellationToken cancellationToken)
    {
        var activity = await GetPartyActivityCurrenciesAsync(scope, request.FiscalYearId, request.PartyId!.Value, request.Kind, request.To, cancellationToken).ConfigureAwait(false);
        if (activity.Contains(request.CurrencyCode!, StringComparer.Ordinal))
            return "لا توجد حركات في الفترة المحددة بهذه العملة؛ قد يتضمن الرصيد الافتتاحي حركات سابقة. / No movements in this period for the selected currency; opening balance may include earlier activity.";
        return activity.Count == 0
            ? "لم تُسجَّل أي حركة آجلة على هذا الطرف في هذا الفرع بأي عملة حتى تاريخ النهاية؛ الرصيد صفري لعدم وجود حركات وليس لفشل التقرير."
            : $"لا حركات لهذا الطرف بالعملة {request.CurrencyCode} المختارة حتى تاريخ النهاية؛ عملات نشاطه في هذا الفرع حتى ذلك التاريخ: {string.Join("، ", activity)} — اختر إحدى عملات النشاط بدل تفسير الصف كصفر مضلل.";
    }

    private static Guid EffectRowId(PartyEffect effect)
    {
        if (!effect.IsReversal) return effect.EffectId;
        Span<byte> source = stackalloc byte[17];
        effect.EffectId.TryWriteBytes(source);
        source[16] = 1;
        Span<byte> digest = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(source, digest);
        return new Guid(digest[..16]);
    }

    private IQueryable<PartyEffect> FilterParty(IQueryable<PartyEffect> query, ProfessionalReportRequest request,
        ProfessionalReportSource[] patternSources)
    {
        query = query.Where(e => request.Sources.Contains(e.Source));
        if (request.DescriptionContains is { } contains) query = query.Where(e => e.Description.Contains(contains));
        if (request.DescriptionNotContains is { } excludes) query = query.Where(e => !e.Description.Contains(excludes));
        if (request.CreatedFrom is { } createdFrom) { var begin = Start(createdFrom); query = query.Where(e => e.CreatedUtc >= begin); }
        if (request.CreatedTo is { } createdTo) { var finish = End(createdTo); query = query.Where(e => e.CreatedUtc < finish); }
        // One merged predicate: a journal posted inside [from, to) matches a single audit row, and the
        // two-Any form previously allowed the window bounds to be satisfied by different audit rows.
        if (request.PostingFrom is not null || request.PostingTo is not null)
        {
            var begin = request.PostingFrom is { } postingFrom ? Start(postingFrom) : DateTimeOffset.MinValue;
            var finish = request.PostingTo is { } postingTo ? End(postingTo) : DateTimeOffset.MaxValue;
            query = query.Where(e => context.JournalAudit.Any(a => a.JournalId == e.JournalId
                && a.Action == (byte)PetraERP.Domain.GeneralLedger.JournalAuditAction.Posted && a.OccurredAtUtc >= begin && a.OccurredAtUtc < finish));
        }
        if (request.FromSequence is { } first) query = query.Where(e => context.JournalEntries.Any(j => j.Id == e.JournalId && j.PostingSequence >= first));
        if (request.ToSequence is { } last) query = query.Where(e => context.JournalEntries.Any(j => j.Id == e.JournalId && j.PostingSequence <= last));
        if (patternSources.Length > 0)
            query = query.Where(e => !patternSources.Contains(e.Source) || e.PatternRevisionId != null && request.PatternRevisionIds.Contains(e.PatternRevisionId.Value));
        return query;
    }
}
