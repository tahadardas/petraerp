using System.Data;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports;
using PetraERP.Application.Reports.Professional;
using PetraERP.Application.Security;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Reports.Professional;

/// <summary>Read-only reporting. Serializable reads keep all summaries and detail rows in one coherent execution.</summary>
public sealed partial class SqlProfessionalReportService(FinancialDbContext context, IAccessEvaluator access, ICommercialReportService commercialReports)
    : IProfessionalReportService
{
    private static readonly byte[] PostedStates = [(byte)JournalEntryState.Posted, (byte)JournalEntryState.Reversed];

    public async Task<ProfessionalReportCatalogResponse> GetCatalogAsync(LedgerReportScope scope, CancellationToken cancellationToken)
    {
        await RequireAsync(scope, PermissionCatalog.ReportsView, cancellationToken).ConfigureAwait(false);
        var years = await context.FiscalYears.AsNoTracking().Where(y => y.CompanyId == scope.CompanyId).OrderBy(y => y.RangeStart)
            .Select(y => new ProfessionalFiscalYearDto(y.Id, y.Code, y.RangeStart, y.RangeEnd)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var definitions = new List<ProfessionalReportDefinitionDto>();
        foreach (var kind in Enum.GetValues<ProfessionalReportKind>())
        {
            if (!await GrantedAsync(scope, PermissionCatalog.JournalsView, cancellationToken).ConfigureAwait(false)) continue;
            if (ProfessionalReportCatalog.IsPartyKind(kind))
            {
                var combinedKind = kind == ProfessionalReportKind.CombinedPartyStatement;
                if (!await GrantedAsync(scope, kind == ProfessionalReportKind.CustomerStatement || combinedKind ? PermissionCatalog.SalesInvoicesView
                    : PermissionCatalog.PurchaseInvoicesView, cancellationToken).ConfigureAwait(false)
                    || combinedKind && !await GrantedAsync(scope, PermissionCatalog.PurchaseInvoicesView, cancellationToken).ConfigureAwait(false)
                    || !await GrantedAsync(scope, PermissionCatalog.TreasuryVouchersView, cancellationToken).ConfigureAwait(false)) continue;
            }
            var required = ProfessionalReportCatalog.IsPartyKind(kind)
                ? PermissionCatalog.PartiesView : PermissionCatalog.JournalsView;
            if (await GrantedAsync(scope, required, cancellationToken).ConfigureAwait(false))
            {
                var definition = ProfessionalReportCatalog.Define(kind);
                var sources = new List<ProfessionalSourceDefinitionDto>();
                foreach (var source in definition.Sources)
                {
                    var allowed = source.Supported && await GrantedAsync(scope, SourcePermission(source.Source), cancellationToken).ConfigureAwait(false);
                    sources.Add(source with { Supported = allowed, Reason = allowed ? null : source.Reason ?? "لا تملك صلاحية عرض بيانات هذا المصدر." });
                }
                var canViewItems = await GrantedAsync(scope, PermissionCatalog.InventoryItemsView, cancellationToken).ConfigureAwait(false);
                var options = definition.Options.Select(option => option.Option == ProfessionalReportOption.InvoiceLineDetails && option.Supported && !canViewItems
                    ? option with { Supported = false, Reason = "تفاصيل المواد تتطلب صلاحية عرض المواد." } : option).ToArray();
                definitions.Add(definition with { Sources = sources, Options = options });
            }
        }
        var allowedSources = definitions.SelectMany(d => d.Sources).Where(s => s.Supported).Select(s => s.Source).Distinct().ToArray();
        var sourceLinks = Sources(scope);
        var patternSources = await sourceLinks.Where(s => s.PatternRevisionId != null && allowedSources.Contains(s.Source))
            .Select(s => new { RevisionId = s.PatternRevisionId!.Value, s.Source }).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        var revisions = patternSources.Select(p => p.RevisionId).Distinct().ToArray();
        var patterns = await context.PatternRevisions.AsNoTracking().Where(r => r.Pattern!.CompanyId == scope.CompanyId && revisions.Contains(r.Id))
            .Select(r => new { r.Id, r.Pattern!.Code, r.Pattern.NameArabic, r.RevisionNumber }).ToDictionaryAsync(r => r.Id, cancellationToken).ConfigureAwait(false);
        return new ProfessionalReportCatalogResponse
        {
            Reports = definitions, FiscalYears = years, MaximumRows = ProfessionalReportCatalog.MaximumRows,
            Patterns = patternSources.Select(p => new ProfessionalPatternDto(p.RevisionId, patterns[p.RevisionId].Code,
                patterns[p.RevisionId].NameArabic + " / " + patterns[p.RevisionId].RevisionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Source))
                .OrderBy(p => p.Code, StringComparer.Ordinal).ToArray(),
            DescriptionSearchScope = ProfessionalReportCatalog.DescriptionScope,
        };
    }

    public async Task<ProfessionalReportExecutionResponse> ExecuteAsync(LedgerReportScope scope, ProfessionalReportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        request = ProfessionalReportCatalog.Validate(request);
        if (scope.Consolidated) throw new RequestValidationException("reporting.branch_required", "يجب تنفيذ هذا التقرير على فرع محدد ومصرّح به؛ النطاق الموحد غير مقبول هنا. / This report must run against one authorized branch; the consolidated scope is not accepted.", "branchId");
        await RequireAsync(scope, PermissionCatalog.ReportsView, cancellationToken).ConfigureAwait(false);
        await RequireAsync(scope, PermissionCatalog.JournalsView, cancellationToken).ConfigureAwait(false);
        var party = ProfessionalReportCatalog.IsPartyKind(request.Kind);
        if (party)
        {
            // Actual balances include all sources, even when display filters hide some of them.
            await RequireAsync(scope, PermissionCatalog.PartiesView, cancellationToken).ConfigureAwait(false);
            if (request.Kind != ProfessionalReportKind.SupplierStatement)
                await RequireAsync(scope, PermissionCatalog.SalesInvoicesView, cancellationToken).ConfigureAwait(false);
            if (request.Kind != ProfessionalReportKind.CustomerStatement)
                await RequireAsync(scope, PermissionCatalog.PurchaseInvoicesView, cancellationToken).ConfigureAwait(false);
            await RequireAsync(scope, PermissionCatalog.TreasuryVouchersView, cancellationToken).ConfigureAwait(false);
        }
        foreach (var source in request.Sources)
            await RequireAsync(scope, SourcePermission(source), cancellationToken).ConfigureAwait(false);
        if (request.IncludeInvoiceDetails) await RequireAsync(scope, PermissionCatalog.InventoryItemsView, cancellationToken).ConfigureAwait(false);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using var transactionLifetime = transaction.ConfigureAwait(false);
        var company = await context.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == scope.CompanyId && c.IsActive, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("company");
        var branch = await context.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == scope.BranchId && b.CompanyId == scope.CompanyId && b.IsActive, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("branch");
        var year = await context.FiscalYears.AsNoTracking().SingleOrDefaultAsync(y => y.Id == request.FiscalYearId && y.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("fiscal year");
        if (request.From < year.RangeStart || request.To > year.RangeEnd)
            throw new RequestValidationException("reporting.year_range_invalid", $"تاريخا بداية ونهاية التقرير يجب أن يقعَا داخل السنة المالية المختارة ({year.RangeStart:yyyy-MM-dd} إلى {year.RangeEnd:yyyy-MM-dd}). " +
                $"Report dates must fall inside the selected fiscal year {year.RangeStart:yyyy-MM-dd} to {year.RangeEnd:yyyy-MM-dd}.", "fiscalYearId");
        if (request.CurrencyCode is { } currency && currency != company.BaseCurrencyCode
            && !await context.CompanyAdditionalCurrencies.AnyAsync(c => c.CompanyId == scope.CompanyId && c.CurrencyCode == currency, cancellationToken).ConfigureAwait(false))
            throw new RequestValidationException("reporting.currency_invalid", "العملة المختارة ليست هي العملة الأساسية للشركة ولا ضمن عملاتها الإضافية المفعّلة. / The selected currency is neither the company base currency nor one of its enabled additional currencies.", "currencyCode");
        if (request.CostCenterId is { } center && !await context.CostCenters.AnyAsync(c => c.Id == center && c.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false))
            throw new ResourceNotFoundException("cost center");
        if (request.OppositeAccountId is { } opposite && !await context.Accounts.AnyAsync(a => a.Id == opposite && a.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false))
            throw new ResourceNotFoundException("opposite account");
        if (request.PatternRevisionIds.Count > 0)
        {
            var count = await context.PatternRevisions.CountAsync(r => request.PatternRevisionIds.Contains(r.Id) && r.Pattern!.CompanyId == scope.CompanyId,
                cancellationToken).ConfigureAwait(false);
            if (count != request.PatternRevisionIds.Distinct().Count()) throw new ResourceNotFoundException("pattern revision");
        }
        // A pattern selection narrows only the sources it belongs to; other selected sources keep all their documents.
        var patternSources = request.PatternRevisionIds.Count == 0 ? [] : await Sources(scope)
            .Where(s => s.PatternRevisionId != null && request.PatternRevisionIds.Contains(s.PatternRevisionId.Value))
            .Select(s => s.Source).Distinct().ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var report = party
            ? await PartyAsync(scope, request, company.BaseCurrencyCode, patternSources, cancellationToken).ConfigureAwait(false)
            : await LedgerAsync(scope, request, company.BaseCurrencyCode, patternSources, cancellationToken).ConfigureAwait(false);
        var result = report with
        {
            ExecutionId = Guid.NewGuid(), GeneratedAtUtc = DateTimeOffset.UtcNow, CompanyId = scope.CompanyId, BranchId = scope.BranchId,
            FiscalYearId = year.Id, CompanyName = company.NameArabic, BranchName = branch.NameArabic, FiscalYearCode = year.Code,
            Parameters = request, SelectedFields = request.Fields,
        };
        result = ProfessionalReportAdvancedFilter.Apply(result, request);
        result = await ProfessionalReportCurrencyConversion.ApplyAsync(context, scope.CompanyId, company.BaseCurrencyCode, request, result, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task RequireAsync(LedgerReportScope scope, string permission, CancellationToken cancellationToken)
    {
        if (!await GrantedAsync(scope, permission, cancellationToken).ConfigureAwait(false))
            throw new ReportScopeDeniedException(permission,
                $"لا تملك صلاحية {PermissionLabel(permission)} ({permission}) في نطاق الشركة والفرع الحاليين؛ اطلب من الإدارة منحها ثم أعد المحاولة. " +
                $"Missing permission {permission} for the current company and branch scope.");
    }

    private static string PermissionLabel(string permission) => permission switch
    {
        PermissionCatalog.ReportsView => "عرض التقارير",
        PermissionCatalog.JournalsView => "عرض بيانات القيود",
        PermissionCatalog.PartiesView => "عرض دليل الأطراف",
        PermissionCatalog.SalesInvoicesView => "عرض فواتير المبيعات",
        PermissionCatalog.PurchaseInvoicesView => "عرض فواتير المشتريات",
        PermissionCatalog.TreasuryVouchersView => "عرض سندات الخزينة والتسويات",
        PermissionCatalog.InventoryMovementsView => "عرض حركات المخزون",
        PermissionCatalog.InventoryItemsView => "عرض بيانات المواد",
        _ => permission,
    };

    private Task<AccessDecision> DecisionAsync(LedgerReportScope scope, string permission, CancellationToken cancellationToken) =>
        access.EvaluatePermissionAsync(scope.ActorUserId, PermissionCatalog.Get(permission), scope.CompanyId, scope.BranchId, cancellationToken);

    private async Task<bool> GrantedAsync(LedgerReportScope scope, string permission, CancellationToken cancellationToken) =>
        await DecisionAsync(scope, permission, cancellationToken).ConfigureAwait(false) == AccessDecision.Granted;

    private static string SourcePermission(ProfessionalReportSource source) => source switch
    {
        ProfessionalReportSource.SalesInvoice or ProfessionalReportSource.SalesReturn => PermissionCatalog.SalesInvoicesView,
        ProfessionalReportSource.PurchaseInvoice or ProfessionalReportSource.PurchaseReturn => PermissionCatalog.PurchaseInvoicesView,
        ProfessionalReportSource.ReceiptVoucher or ProfessionalReportSource.PaymentVoucher or ProfessionalReportSource.Settlement => PermissionCatalog.TreasuryVouchersView,
        ProfessionalReportSource.InventoryDocument => PermissionCatalog.InventoryMovementsView,
        _ => PermissionCatalog.JournalsView,
    };

    private static DateTimeOffset Start(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    private static DateTimeOffset End(DateOnly date) => date == DateOnly.MaxValue ? DateTimeOffset.MaxValue : Start(date.AddDays(1));

    private static RequestValidationException TooLarge() => new("reporting.result_limit_exceeded",
        "The complete result exceeds 100,000 rows. Narrow the period or account scope; no partial preview/export was generated.", "parameters");

    private static ProfessionalReportExecutionResponse Empty(ProfessionalReportRequest request, string currency, string code, string name,
        string convention, IReadOnlyList<ProfessionalReportRowDto> rows, ProfessionalReportSummaryDto summary, IReadOnlyList<string> notes) => new()
    {
        ExecutionId = Guid.Empty, GeneratedAtUtc = default, CompanyId = Guid.Empty, BranchId = Guid.Empty, FiscalYearId = request.FiscalYearId,
        CompanyName = string.Empty, BranchName = string.Empty, FiscalYearCode = string.Empty, CurrencyCode = currency, SubjectCode = code,
        SubjectName = name, BalanceConvention = convention, Parameters = request, SelectedFields = request.Fields, Rows = rows, Summary = summary,
        Notes = notes, InvoiceDetails = [],
    };

    private static ProfessionalReportSummaryDto Summary(decimal opening, decimal debit, decimal credit, decimal filteredDebit,
        decimal filteredCredit, int count, bool supplier, ProfessionalBalancePresentation presentation)
    {
        var sign = supplier ? -1m : 1m;
        var closing = opening + sign * (debit - credit);
        return new ProfessionalReportSummaryDto
        {
            ActualOpening = opening, ActualPeriodDebit = debit, ActualPeriodCredit = credit, ActualClosing = closing,
            FilteredDebit = filteredDebit, FilteredCredit = filteredCredit, RowCount = count,
            DisplayClosing = (presentation == ProfessionalBalancePresentation.Actual ? opening : 0m) + sign * (filteredDebit - filteredCredit),
            OpeningDebit = Math.Max(opening * sign, 0m), OpeningCredit = Math.Max(-opening * sign, 0m),
            ClosingDebit = Math.Max(closing * sign, 0m), ClosingCredit = Math.Max(-closing * sign, 0m),
        };
    }

    private static string Group(ProfessionalReportRequest request, ProfessionalReportRowDto row) => request.Grouping switch
    {
        ProfessionalReportGrouping.Account => row.AccountCode,
        ProfessionalReportGrouping.Month => row.Date?.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        ProfessionalReportGrouping.Source => row.Source is { } source ? ProfessionalReportCatalog.SourceLabel(source) : string.Empty,
        _ => string.Empty,
    };

    private static ProfessionalReportRowDto[] OrderRows(ProfessionalReportRequest request, IEnumerable<ProfessionalReportRowDto> rows, bool preserveChronology = false)
    {
        var grouped = rows.Select(row => row with { GroupKey = Group(request, row) });
        IEnumerable<ProfessionalReportRowDto> ordered = request.Order switch
        {
            ProfessionalReportOrder.AccountCode => grouped.OrderBy(r => r.AccountCode, StringComparer.Ordinal).ThenBy(r => r.Date).ThenBy(r => r.PostingSequence).ThenBy(r => r.RowId),
            ProfessionalReportOrder.PostingSequence => grouped.OrderBy(r => r.PostingSequence).ThenBy(r => r.Date).ThenBy(r => r.RowId),
            // LINQ ordering is stable: retain the authoritative full timestamp/effect order within a displayed day.
            _ => preserveChronology ? grouped : grouped.OrderBy(r => r.Date),
        };
        return request.Grouping == ProfessionalReportGrouping.None ? ordered.ToArray()
            : ordered.OrderBy(r => r.GroupKey, StringComparer.Ordinal).ToArray();
    }
}
