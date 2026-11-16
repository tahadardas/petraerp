using System.IO;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Reporting;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class ProfessionalReportingWorkflowTests
{
    [Fact]
    public async Task LookupOpensBoundedFirstPageForEmptyQueryAndLoadsMoreOnDemand()
    {
        var calls = new List<int>();
        var lookup = new ProfessionalLookupViewModel((_, _) => Task.FromResult<IReadOnlyList<ProfessionalLookupItem>>([]),
            () => CancellationToken.None, searchPage: (_, page, _) =>
            {
                calls.Add(page);
                return Task.FromResult(new ProfessionalLookupPage([new(Guid.NewGuid(), page.ToString(System.Globalization.CultureInfo.InvariantCulture), "حساب")], 2, page == 1, false));
            });
        await lookup.SearchCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal([1], calls);
        Assert.Single(lookup.Items);
        Assert.True(lookup.HasMore);
        calls.Clear();
        lookup.Query = "حساب";
        await lookup.SearchCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(lookup.HasMore);
        await lookup.LoadMoreCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Collection(calls, first => Assert.Equal(1, first), second => Assert.Equal(2, second));
        Assert.Equal(2, lookup.Items.Count);
        Assert.False(lookup.HasMore);
        lookup.Query = "آخر";
        Assert.Empty(lookup.Items);
        Assert.False(lookup.HasMore);
    }

    [Theory]
    [InlineData(ProfessionalReportKind.CustomerStatement, ProfessionalLookupKind.Customer)]
    [InlineData(ProfessionalReportKind.SupplierStatement, ProfessionalLookupKind.Supplier)]
    public async Task PartyControlAccountIsContextOnlyAndMissingMappingBlocksPreview(ProfessionalReportKind kind, ProfessionalLookupKind expectedKind)
    {
        var client = new ReportClient { Kind = kind };
        var lookup = new Lookups();
        var localization = new LocalizationService("ar");
        using var page = new ProfessionalReportPageViewModel(kind, client, lookup, new Presets(), new DesktopOperation(localization), localization);
        var context = Context() with { FiscalYearId = client.Year.Id, Permissions = new HashSet<string>(StringComparer.Ordinal) { "Ledger.Reports.View", "Parties.Directory.View" } };
        await page.ActivateAsync(context, null, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var id = Guid.NewGuid();
        page.Account.Selected = new(Guid.NewGuid(), "999", "لا يرسل");
        page.Party.Selected = new(id, "C001", "طرف عربي");
        Assert.Equal(expectedKind, lookup.LastKind);
        Assert.Contains("1100 · ذمم", page.PartyAccountContext, StringComparison.Ordinal);
        Assert.Empty(page.CurrencyCode);
        Assert.Equal("USD", Assert.Single(page.PartyActivityCurrencies));
        page.CurrencyCode = "SYP";
        Assert.Contains("USD", page.PartyCurrencyStatus, StringComparison.Ordinal);
        page.CurrencyCode = "USD";
        Assert.Null(page.CaptureParameters().AccountId);
        Guid? opened = null;
        page.PartyCardRequested += value => opened = value;
        page.OpenPartyCardCommand.Execute(null);
        Assert.Equal(id, opened);

        lookup.ControlAccount = new(PartyControlAccountState.Missing);
        page.Party.Selected = new(Guid.NewGuid(), "C002", "طرف آخر");
        Assert.Contains("بطاقة الطرف", page.PartyAccountContext, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(page.CaptureParameters);
    }

    [Fact]
    public async Task RestoreUsesResolvedLabelAndDoesNotOverwriteLaterInput()
    {
        var id = Guid.NewGuid();
        var pending = new TaskCompletionSource<ProfessionalLookupItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lookup = new ProfessionalLookupViewModel((_, _) => Task.FromResult<IReadOnlyList<ProfessionalLookupItem>>([]),
            () => CancellationToken.None, resolve: (_, _) => pending.Task);
        var restore = lookup.RestoreAsync(id);
        lookup.Query = "اختيار آخر";
        pending.SetResult(new(id, "C001", "زبون عربي"));
        await restore.ConfigureAwait(true);
        Assert.Null(lookup.Selected);
        Assert.Equal("اختيار آخر", lookup.Query);

        var resolved = new ProfessionalLookupViewModel((_, _) => Task.FromResult<IReadOnlyList<ProfessionalLookupItem>>([]),
            () => CancellationToken.None, resolve: (value, _) => Task.FromResult(new ProfessionalLookupItem(value, "C001", "زبون عربي")));
        await resolved.RestoreAsync(id).ConfigureAwait(true);
        Assert.Equal(id, resolved.Selected?.Id);
        Assert.Equal("C001 · زبون عربي", resolved.Query);
    }

    [Fact]
    public async Task PresetRestoresRealAccountLabelBeforeReportExecution()
    {
        var client = new ReportClient();
        using var page = Page(client);
        await page.ActivateAsync(Context() with { FiscalYearId = client.Year.Id }, null, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var account = Guid.NewGuid();
        page.SelectedPreset = new ProfessionalReportPreset("test", new ProfessionalReportRequest
        {
            Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = client.Year.Id,
            From = new(2026, 1, 1), To = new(2026, 1, 31), AccountId = account,
            Sources = [ProfessionalReportSource.ManualJournal], Fields = [ProfessionalReportField.Description],
        });
        await page.RestorePresetCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(account, page.Account.Selected?.Id);
        Assert.Equal("4110 · حساب", page.Account.Query);
        Assert.Equal(account, page.CaptureParameters().AccountId);
    }

    [Fact]
    public async Task EditingSelectionInvalidatesItAndLateSearchCannotRestoreIt()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<ProfessionalLookupItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lookup = new ProfessionalLookupViewModel((_, _) => pending.Task, () => CancellationToken.None);
        lookup.Query = "أ";
        var search = lookup.SearchCommand.ExecuteAsync(null);
        var chosen = new ProfessionalLookupItem(Guid.NewGuid(), "1", "أحمد");
        lookup.Selected = chosen;
        Assert.Equal(chosen.Label, lookup.Query);
        lookup.Query = "محمد";
        Assert.Null(lookup.Selected);
        pending.SetResult([chosen]);
        await search.ConfigureAwait(true);
        Assert.Empty(lookup.Items);
        Assert.False(lookup.IsResultsOpen);
        var replacement = new ProfessionalLookupItem(Guid.NewGuid(), "2", "محمد");
        lookup.Selected = replacement;
        Assert.Equal(replacement.Label, lookup.Query);
        lookup.ClearCommand.Execute(null);
        Assert.Null(lookup.Selected);
        Assert.Empty(lookup.Query);
    }

    [Fact]
    public async Task SavedIdentityIsNotTreatedAsAResolvedSelection()
    {
        var lookup = new ProfessionalLookupViewModel((_, _) => Task.FromResult<IReadOnlyList<ProfessionalLookupItem>>([]), () => CancellationToken.None);
        await lookup.RestoreAsync(Guid.NewGuid()).ConfigureAwait(true);
        Assert.Null(lookup.Selected);
        Assert.Empty(lookup.Items);
        Assert.NotEmpty(lookup.Error);
    }

    [Fact]
    public async Task LookupSearchPublishesResultsAndOpensTheSelectionList()
    {
        var accountId = Guid.NewGuid();
        var lookup = new ProfessionalLookupViewModel((text, _) =>
            Task.FromResult<IReadOnlyList<ProfessionalLookupItem>>(
                [new(accountId, "4110", "حساب تجريبي")]), () => CancellationToken.None)
        {
            Query = "تجريبي",
        };

        await lookup.SearchCommand.ExecuteAsync(null).ConfigureAwait(true);

        var result = Assert.Single(lookup.Items);
        Assert.Equal(accountId, result.Id);
        Assert.True(lookup.IsResultsOpen);
        Assert.Contains("1", lookup.Status, StringComparison.Ordinal);
        lookup.Selected = result;
        Assert.False(lookup.IsResultsOpen);
        Assert.Equal(result.Label, lookup.Status);
    }

    [Fact]
    public async Task ReportOpensOnTheYearToDateInsteadOfTheWholeFiscalYear()
    {
        var client = new ReportClient();
        using var page = Page(client);
        await page.ActivateAsync(Context() with { FiscalYearId = client.Year.Id }, null, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(new DateOnly(2026, 1, 1), DateOnly.FromDateTime(page.FromDate!.Value));
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        Assert.Equal(today > client.Year.To ? client.Year.To : today, DateOnly.FromDateTime(page.ToDate!.Value));
        Assert.True(page.ToDate >= page.FromDate);
    }

    [Fact]
    public async Task FiscalYearIsExplicitAndUnsupportedFieldsCannotBeSelected()
    {
        var client = new ReportClient(); var context = Context() with { FiscalYearId = null };
        using var page = Page(client);
        await page.ActivateAsync(context, null, TestContext.Current.CancellationToken).ConfigureAwait(true);
        page.Account.Selected = new(Guid.NewGuid(), "4110", "حساب");
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        if (client.Year.From <= today && today <= client.Year.To)
        {
            // The year containing today is the only default; it is shown to the operator and can be changed.
            Assert.Equal(client.Year.Id, page.SelectedFiscalYear?.Id);
        }
        else
        {
            Assert.Null(page.SelectedFiscalYear);
            Assert.Throws<InvalidOperationException>(page.CaptureParameters);
            page.SelectedFiscalYear = Assert.Single(page.FiscalYears);
            Assert.Null(page.Account.Selected);
        }
        page.Account.Selected = new(Guid.NewGuid(), "4110", "حساب");
        var unsupported = page.Fields.Single(field => field.Value == ProfessionalReportField.CostCenter);
        unsupported.IsSelected = true;
        Assert.False(unsupported.IsSelected);
        var parameters = page.CaptureParameters();
        Assert.Equal(client.Year.Id, parameters.FiscalYearId);
        Assert.Equal(new DateOnly(2026, 1, 1), parameters.From);
        Assert.DoesNotContain(ProfessionalReportField.CostCenter, parameters.Fields!);
    }

    [Fact]
    public async Task PreviewBoundaryRetainsTheSameParametersAndUsesOnlyTheServerExecution()
    {
        var client = new ReportClient(); using var page = Page(client);
        await page.ActivateAsync(Context() with { FiscalYearId = client.Year.Id }, null, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var account = Guid.NewGuid(); page.Account.Selected = new(account, "4110", "حساب");
        page.DescriptionContains = "دفعة مُسددة"; page.DescriptionNotContains = "ملغى";
        page.SourceSearch = "قيد"; page.BalancePresentation = ProfessionalBalancePresentation.ZeroAtPeriodStart;
        page.Sources.Single(source => source.Value == ProfessionalReportSource.SalesInvoice).IsSelected = false;
        ProfessionalPreviewRequest? preview = null;
        page.PreviewRequested += value => preview = value;
        await page.PreviewCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.NotNull(preview); Assert.Equal(1, client.Executions);
        Assert.Same(client.LastResult, preview.Snapshot);
        Assert.Equal("دفعة مُسددة", client.LastRequest!.DescriptionContains);
        Assert.Equal("ملغى", client.LastRequest.DescriptionNotContains);
        Assert.Equal("دفعة مُسددة", page.DescriptionContains); Assert.Equal("قيد", page.SourceSearch);
        Assert.Equal(account, page.Account.Selected?.Id);
        Assert.False(page.Sources.Single(source => source.Value == ProfessionalReportSource.SalesInvoice).IsSelected);
        Assert.False(page.IsExecuting);
    }

    [Fact]
    public async Task PatternSelectionFollowsSelectedSourcesAndCategoryAppliesOnlyToTrialBalance()
    {
        var client = new ReportClient(); using var page = Page(client); var context = Context() with { FiscalYearId = client.Year.Id };
        await page.ActivateAsync(context, null, TestContext.Current.CancellationToken).ConfigureAwait(true);
        page.Account.Selected = new(Guid.NewGuid(), "4110", "حساب"); page.AccountCategory = ProfessionalAccountCategory.Assets;
        Assert.Equal(2, page.VisiblePatterns.Count());
        Assert.Empty(page.CaptureParameters().PatternRevisionIds!); // No selection = every pattern of the selected sources.
        page.PatternSearch = "SI-A";
        var cash = Assert.Single(page.VisiblePatterns);
        cash.IsSelected = true;
        Assert.Equal("1 / 1", page.PatternCount);
        page.PatternSearch = string.Empty;
        Assert.Equal(new[] { client.CashPattern.RevisionId }, page.CaptureParameters().PatternRevisionIds!);
        Assert.Null(page.CaptureParameters().AccountCategory);
        page.Sources.Single(source => source.Value == ProfessionalReportSource.SalesInvoice).IsSelected = false;
        Assert.Empty(page.VisiblePatterns);
        Assert.Empty(page.CaptureParameters().PatternRevisionIds!); // A pattern of an unselected source is never sent.
        await page.ActivateAsync(context, null, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.True(page.Patterns.Single(pattern => pattern.RevisionId == client.CashPattern.RevisionId).IsSelected); // Reload keeps the choice.

        var trialClient = new ReportClient { Kind = ProfessionalReportKind.TrialBalance };
        using var trial = new ProfessionalReportPageViewModel(ProfessionalReportKind.TrialBalance, trialClient, new Lookups(), new Presets(),
            new DesktopOperation(new LocalizationService("ar")), new LocalizationService("ar"));
        await trial.ActivateAsync(Context() with { FiscalYearId = trialClient.Year.Id }, null, TestContext.Current.CancellationToken).ConfigureAwait(true);
        trial.Order = ProfessionalReportOrder.AccountCode; trial.AccountCategory = ProfessionalAccountCategory.Revenue;
        Assert.Equal(ProfessionalAccountCategory.Revenue, trial.CaptureParameters().AccountCategory);
    }

    [Fact]
    public void DisplayColumnsAndFilteredRowsNeverRecalculateActualServerMeasures()
    {
        var request = new ProfessionalReportRequest { Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = Guid.NewGuid(), From = new(2026, 1, 1), To = new(2026, 12, 31) };
        var snapshot = Result(request);
        snapshot.SelectedFields = [ProfessionalReportField.Description, ProfessionalReportField.Debit];
        var report = ProfessionalReportDocumentFactory.Create(new(snapshot, "دفتر الأستاذ", new Dictionary<ProfessionalReportField, string>
        {
            [ProfessionalReportField.Description] = "البيان", [ProfessionalReportField.Debit] = "مدين",
        }));
        Assert.Equal(2, report.Columns.Count);
        Assert.Equal("دفعة عربية", Assert.Single(report.Rows)["Description"]);
        Assert.Equal(10m, report.Rows[0]["Debit"]);
        Assert.Equal(300m, report.Summaries.Single(value => value.Key == "ActualClosing").Value);
        Assert.Equal(10m, report.Summaries.Single(value => value.Key == "FilteredDebit").Value);
        Assert.Equal(210m, report.Summaries.Single(value => value.Key == "DisplayClosing").Value);
        snapshot.Rows[0].Debit = "999.00";
        Assert.Equal(10m, report.Rows[0]["Debit"]);
        Assert.Equal(snapshot.ExecutionId, report.ExecutionId);
    }

    [Fact]
    public async Task PresetsRemainScopedToUserCompanyBranchAndFiscalYear()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PetraReportingPresetTest_" + Guid.NewGuid().ToString("N"));
        var store = new ProfessionalReportPresetStore(directory); var context = Context();
        var request = new ProfessionalReportRequest { Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = context.FiscalYearId!.Value, From = new(2026, 1, 1), To = new(2026, 12, 31),
            DescriptionContains = "أقساط", Fields = [ProfessionalReportField.Debit], Sources = [ProfessionalReportSource.ManualJournal] };
        try
        {
            await store.SaveAsync(context, new("إعداد عربي", request), TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal("أقساط", Assert.Single(await store.LoadAsync(context, request.Kind, TestContext.Current.CancellationToken).ConfigureAwait(true)).Parameters.DescriptionContains);
            Assert.Empty(await store.LoadAsync(context with { UserId = Guid.NewGuid() }, request.Kind, TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Empty(await store.LoadAsync(context with { FiscalYearId = Guid.NewGuid() }, request.Kind, TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Empty(await store.LoadAsync(context with { Branch = new BranchContextResponse { Id = Guid.NewGuid(), CompanyId = context.CompanyId } }, request.Kind, TestContext.Current.CancellationToken).ConfigureAwait(true));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(context with { FiscalYearId = Guid.NewGuid() }, new("Wrong scope", request), TestContext.Current.CancellationToken)).ConfigureAwait(true);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    internal static ShellContext Context() => new(new CompanyContextResponse { Id = Guid.NewGuid(), NameArabic = "شركة الاختبار", BaseCurrencyCode = "SYP" },
        new BranchContextResponse { Id = Guid.NewGuid(), NameArabic = "الفرع" }, new HashSet<string>(StringComparer.Ordinal) { "Ledger.Reports.View", "Ledger.Journals.View" })
    { UserId = Guid.NewGuid(), FiscalYearId = Guid.NewGuid() };

    internal static ProfessionalReportPageViewModel Page(ReportClient client)
    {
        var localization = new LocalizationService("ar");
        return new(ProfessionalReportKind.AccountLedger, client, new Lookups(), new Presets(), new DesktopOperation(localization), localization);
    }

    internal static ProfessionalReportExecutionResponse Result(ProfessionalReportRequest request) => new()
    {
        ExecutionId = Guid.NewGuid(), GeneratedAtUtc = DateTimeOffset.UtcNow, CompanyId = Guid.NewGuid(), BranchId = Guid.NewGuid(), FiscalYearId = request.FiscalYearId,
        CompanyName = "شركة عربية", BranchName = "الفرع", FiscalYearCode = "2026", CurrencyCode = "SYP", SubjectCode = "100", SubjectName = "الصندوق",
        BalanceConvention = "debit-minus-credit", Parameters = request, SelectedFields = [ProfessionalReportField.Description, ProfessionalReportField.Debit], Notes = [], InvoiceDetails = [],
        Rows = [new ProfessionalReportRowDto { RowId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Source = ProfessionalReportSource.SalesInvoice,
            CanDrillThrough = true, AccountCode = "100", AccountName = "الصندوق", Description = "دفعة عربية", CurrencyCode = "SYP",
            Debit = "10.00", Credit = "0.00", ActualBalance = "210.00", DisplayBalance = "210.00", Opening = "200.00", Closing = "300.00",
            OpeningDebit = "200.00", OpeningCredit = "0.00", ClosingDebit = "300.00", ClosingCredit = "0.00", TransactionDebit = "10.00", TransactionCredit = "0.00", TransactionCurrencyCode = "SYP", GroupKey = string.Empty }],
        Summary = new ProfessionalReportSummaryDto { ActualOpening = "200.00", ActualPeriodDebit = "100.00", ActualPeriodCredit = "0.00", ActualClosing = "300.00",
            FilteredDebit = "10.00", FilteredCredit = "0.00", DisplayClosing = "210.00", RowCount = 1, OpeningDebit = "200.00", OpeningCredit = "0.00", ClosingDebit = "300.00", ClosingCredit = "0.00" },
    };

    internal sealed class ReportClient : IProfessionalReportsClient
    {
        public Task<IReadOnlyList<string>> PartyActivityCurrenciesAsync(Guid companyId, Guid branchId, Guid partyId, Guid fiscalYearId, ProfessionalReportKind kind, DateOnly asOfDate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(["USD"]);
        public ProfessionalFiscalYearDto Year { get; } = new() { Id = Guid.NewGuid(), Code = "2026", From = new(2026, 1, 1), To = new(2026, 12, 31) };
        public int Executions { get; private set; }
        public ProfessionalReportKind Kind { get; init; } = ProfessionalReportKind.AccountLedger;
        public ProfessionalReportRequest? LastRequest { get; private set; }
        public ProfessionalReportExecutionResponse? LastResult { get; private set; }
        public ProfessionalPatternDto CashPattern { get; } = new() { RevisionId = Guid.NewGuid(), Code = "SI-A", NameArabic = "مبيعات نقدية / 1", Source = ProfessionalReportSource.SalesInvoice };
        public ProfessionalPatternDto CreditPattern { get; } = new() { RevisionId = Guid.NewGuid(), Code = "SI-B", NameArabic = "مبيعات آجلة / 1", Source = ProfessionalReportSource.SalesInvoice };
        public Task<ProfessionalReportCatalogResponse> CatalogAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) => Task.FromResult(new ProfessionalReportCatalogResponse
        {
            FiscalYears = [Year], DescriptionSearchScope = "Posted journal descriptions", MaximumRows = 10_000,
            Patterns = [CashPattern, CreditPattern], AccountCategories = Enum.GetValues<ProfessionalAccountCategory>(),
            Reports = [new ProfessionalReportDefinitionDto { Kind = Kind, NameArabic = Kind == ProfessionalReportKind.TrialBalance ? "ميزان المراجعة" : Kind == ProfessionalReportKind.CustomerStatement ? "كشف الزبون" : Kind == ProfessionalReportKind.SupplierStatement ? "كشف المورد" : "دفتر الأستاذ", NameEnglish = Kind.ToString(), Options = [],
                Sources = [new ProfessionalSourceDefinitionDto { Source = ProfessionalReportSource.ManualJournal, LabelArabic = "قيد يدوي", Supported = true }, new ProfessionalSourceDefinitionDto { Source = ProfessionalReportSource.SalesInvoice, LabelArabic = "فاتورة مبيعات", Supported = true }],
                Fields = [new ProfessionalFieldDefinitionDto { Field = ProfessionalReportField.Description, LabelArabic = "البيان", Supported = true, DefaultSelected = true },
                    new ProfessionalFieldDefinitionDto { Field = ProfessionalReportField.Debit, LabelArabic = "مدين", Supported = true, DefaultSelected = true },
                    new ProfessionalFieldDefinitionDto { Field = ProfessionalReportField.CostCenter, LabelArabic = "مركز الكلفة", Supported = false, Reason = "Unavailable" }] }],
        });
        public Task<ProfessionalReportExecutionResponse> ExecuteAsync(Guid companyId, Guid branchId, ProfessionalReportRequest request, CancellationToken cancellationToken)
        {
            Executions++; LastRequest = request; LastResult = Result(request); LastResult.CompanyId = companyId; LastResult.BranchId = branchId; return Task.FromResult(LastResult);
        }
    }
    private sealed class Lookups : IProfessionalReportLookups
    {
        public Task<ProfessionalLookupPage> SearchPageAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind, string text, int page, CancellationToken cancellationToken) => Task.FromResult(new ProfessionalLookupPage([], 0, false, false));
        public PartyControlAccount ControlAccount { get; set; } = new(PartyControlAccountState.Available, "1100", "ذمم");
        public ProfessionalLookupKind? LastKind { get; private set; }
        public Task<PartyControlAccount> GetPartyControlAccountAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind, Guid partyId, CancellationToken cancellationToken)
        {
            LastKind = kind;
            return Task.FromResult(ControlAccount);
        }
        public Task<IReadOnlyList<ProfessionalLookupItem>> SearchAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind, string text, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProfessionalLookupItem>>([]);
        public Task<ProfessionalLookupItem> ResolveAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind, Guid id, CancellationToken cancellationToken) => Task.FromResult(new ProfessionalLookupItem(id, "4110", "حساب"));
    }
    private sealed class Presets : IProfessionalReportPresetStore
    {
        public Task<IReadOnlyList<ProfessionalReportPreset>> LoadAsync(ShellContext context, ProfessionalReportKind kind, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProfessionalReportPreset>>([]);
        public Task SaveAsync(ShellContext context, ProfessionalReportPreset preset, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
