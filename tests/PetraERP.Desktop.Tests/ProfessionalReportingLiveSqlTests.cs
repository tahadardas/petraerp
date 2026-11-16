using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using PetraERP.Api.Tests;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Reporting;
using PetraERP.Desktop.Shell;
using PetraERP.Reporting.OpenSource;
using Wire = PetraERP.Contracts.Reports;

namespace PetraERP.Desktop.Tests;

/// <summary>Live generated-client/API/isolated-SQL acceptance; no substituted finance query or physical printer claim.</summary>
public sealed class ProfessionalReportingLiveSqlTests
{
    private static readonly ProfessionalReportField[] SelectedColumns =
        [ProfessionalReportField.Description, ProfessionalReportField.Debit, ProfessionalReportField.Credit, ProfessionalReportField.ActualBalance];
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task RealDesktopParametersPreviewAndExportsPreserveServerBalancesAndRejectInvalidHttpRequests()
    {
        var host = await ApiSqlHost.CreateBootstrapWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var trace = new RequestTrace(host.Factory.Server.CreateHandler());
            using var session = new DesktopSession(new Uri("https://localhost/"), host.Factory.Server.CreateHandler(), trace);
            await session.LoginAsync(ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword, Cancellation).ConfigureAwait(true);
            var currentUser = await session.Api.GetCurrentUserAsync(Cancellation).ConfigureAwait(true);
            var company = Assert.Single((await session.Api.ListMyCompaniesAsync(Cancellation).ConfigureAwait(true)).Items);
            var branch = Assert.Single((await session.Api.ListMyBranchesAsync(company.Id, Cancellation).ConfigureAwait(true)).Items);
            session.SelectWorkspace(host.WorkspaceId, host.CompanyId, host.FiscalYearId);
            Assert.Equal((host.CompanyId, host.BranchId), (company.Id, branch.Id));
            var year = Assert.Single(await session.Api.ListFiscalYearsAsync(company.Id, branch.Id, Cancellation).ConfigureAwait(true));
            Assert.Equal(host.FiscalYearId, year.Id);
            var cash = await CreateAccountAsync("1100", "الصندوق العربي", AccountType.Assets).ConfigureAwait(true);
            var equity = await CreateAccountAsync("3100", "رأس المال", AccountType.Equity).ConfigureAwait(true);
            await PostAsync(new DateOnly(2026, 9, 30), "رصيد سابق", "10.1111").ConfigureAwait(true);
            var includedJournal = await PostAsync(new DateOnly(2026, 10, 5), "قبض أحمد [%]_ التقرير", "125.4321").ConfigureAwait(true);
            await PostAsync(new DateOnly(2026, 10, 6), "قيد آخر ملغى", "50.1234").ConfigureAwait(true);

            var localization = new LocalizationService("ar");
            var operation = new DesktopOperation(localization);
            var transport = new ProfessionalReportsClient(session);
            for (var index = 0; index < 51; index++)
            {
                await session.Api.CreatePartyAsync(company.Id, branch.Id, new CreatePartyRequest
                {
                    Code = "PG" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
                    NameArabic = "شركة الأمل صفحات", Role = index == 50 ? PartyRole.Both : PartyRole.Customer,
                    CreditLimit = "0", PaymentTermDays = 0, PolicyKind = CreditPolicyKind.NoLimit,
                }, Cancellation).ConfigureAwait(true);
            }
            var partyFirst = await transport.SearchPageAsync(company.Id, branch.Id, ProfessionalLookupKind.Customer, "امل صفحات", 1, Cancellation).ConfigureAwait(true);
            var partySecond = await transport.SearchPageAsync(company.Id, branch.Id, ProfessionalLookupKind.Customer, "امل صفحات", 2, Cancellation).ConfigureAwait(true);
            Assert.Equal(51, partyFirst.Total); Assert.True(partyFirst.HasMore); Assert.Equal(50, partyFirst.Items.Count);
            Assert.Equal("PG050", Assert.Single(partySecond.Items).Code); Assert.False(partySecond.HasMore);
            var supplier = await transport.SearchPageAsync(company.Id, branch.Id, ProfessionalLookupKind.Supplier, "امل صفحات", 1, Cancellation).ConfigureAwait(true);
            Assert.Equal("PG050", Assert.Single(supplier.Items).Code);
            var noActivity = await transport.PartyActivityCurrenciesAsync(company.Id, branch.Id, Assert.Single(partySecond.Items).Id, year.Id,
                ProfessionalReportKind.CustomerStatement, new DateOnly(2026, 10, 31), Cancellation).ConfigureAwait(true);
            Assert.Empty(noActivity);
            var accountPage = await transport.SearchPageAsync(company.Id, branch.Id, ProfessionalLookupKind.Account, "الصندوق", 1, Cancellation).ConfigureAwait(true);
            Assert.Equal(cash, Assert.Single(accountPage.Items).Id);
            Assert.Equal(1, accountPage.Total);
            Assert.False(accountPage.HasMore);
            using var page = new ProfessionalReportPageViewModel(ProfessionalReportKind.AccountLedger, transport, transport,
                new LocalPresetBoundary(), operation, localization);
            var previews = new List<ProfessionalPreviewRequest>();
            page.PreviewRequested += previews.Add;
            var shellContext = new ShellContext(company, branch, currentUser.Permissions.ToHashSet(StringComparer.Ordinal)) { UserId = currentUser.UserId, FiscalYearId = host.FiscalYearId };
            await page.ActivateAsync(shellContext, null, Cancellation).ConfigureAwait(true);
            Assert.Empty(previews); // Loading catalog/criteria does not execute a financial report.
            Assert.Contains(trace.Paths, value => value.EndsWith("/reporting/catalog", StringComparison.Ordinal));
            Assert.DoesNotContain(trace.Paths, value => value.EndsWith("/reporting/execute", StringComparison.Ordinal));
            Assert.Equal(host.FiscalYearId, page.SelectedFiscalYear!.Id);
            Assert.False(page.CanSelectFiscalYear);
            page.Account.Query = "1100";
            await page.Account.SearchCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.Empty(page.Account.Error);
            var lookup = Assert.Single(page.Account.Items, value => value.Id == cash);
            Assert.Equal("الصندوق العربي", lookup.Name);
            page.Account.Selected = lookup;
            page.FromDate = new DateTime(2026, 10, 1); page.ToDate = new DateTime(2026, 10, 31);
            page.DescriptionContains = "أحمد [%]_"; page.DescriptionNotContains = "ملغى";
            page.BalancePresentation = ProfessionalBalancePresentation.ZeroAtPeriodStart;
            foreach (var source in page.Sources) source.IsSelected = source.Value == ProfessionalReportSource.UnclassifiedJournal;
            foreach (var field in page.Fields) field.IsSelected = SelectedColumns.Contains(field.Value);
            var sourceState = page.Sources.Select(value => (value.Value, value.IsSelected)).ToArray();
            var fieldState = page.Fields.Select(value => (value.Value, value.IsSelected)).ToArray();
            var accountSelection = page.Account.Selected;
            await page.PreviewCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.True(string.IsNullOrEmpty(page.Message), page.Message);
            Assert.False(page.IsExecuting);
            Assert.Single(trace.Paths, value => value.EndsWith("/reporting/execute", StringComparison.Ordinal));
            var preview = Assert.Single(previews);
            var snapshot = preview.Snapshot;
            Assert.NotEqual(Guid.Empty, snapshot.ExecutionId);
            Assert.Equal((company.Id, branch.Id, year.Id), (snapshot.CompanyId, snapshot.BranchId, snapshot.FiscalYearId));
            Assert.Equal("10.1111", snapshot.Summary.ActualOpening);
            Assert.Equal("175.5555", snapshot.Summary.ActualPeriodDebit);
            Assert.Equal("185.6666", snapshot.Summary.ActualClosing);
            Assert.Equal("125.4321", snapshot.Summary.FilteredDebit);
            Assert.Equal("125.4321", snapshot.Summary.DisplayClosing);
            var movement = Assert.Single(snapshot.Rows);
            Assert.Equal(includedJournal, movement.JournalId);
            Assert.Equal("135.5432", movement.ActualBalance);
            Assert.Equal("125.4321", movement.DisplayBalance);
            Assert.Contains("أحمد [%]_", movement.Description, StringComparison.Ordinal);
            Assert.Equal(sourceState, page.Sources.Select(value => (value.Value, value.IsSelected)));
            Assert.Equal(fieldState, page.Fields.Select(value => (value.Value, value.IsSelected)));
            Assert.Same(accountSelection, page.Account.Selected);
            Assert.Equal("أحمد [%]_", page.DescriptionContains);
            Assert.Equal("ملغى", page.DescriptionNotContains);

            var document = ProfessionalReportDocumentFactory.Create(preview);
            Assert.Equal(snapshot.ExecutionId, document.ExecutionId);
            Assert.Equal(185.6666m, document.Summaries.Single(value => value.Key == "ActualClosing").Value);
            Assert.Equal(125.4321m, document.Summaries.Single(value => value.Key == "FilteredDebit").Value);
            var csv = Encoding.UTF8.GetString(new CsvReportExporter().Export(document, false));
            Assert.Contains("أحمد [%]_", csv, StringComparison.Ordinal);
            Assert.Contains("\"125.4321\"", csv, StringComparison.Ordinal);
            using (var stream = new MemoryStream(new ExcelReportExporter().Export(document)))
            using (var workbook = new XLWorkbook(stream))
            {
                var debitColumn = document.Columns.Select((value, index) => (value.Key, Index: index + 1)).Single(value => value.Key == "Debit").Index;
                var cell = workbook.Worksheet("Transactions").Cell(2, debitColumn);
                Assert.Equal(XLDataType.Number, cell.DataType);
                Assert.Equal(125.4321m, cell.GetValue<decimal>());
                var summaryIndex = document.Summaries.Select((value, index) => (value.Key, Index: index + 7)).Single(value => value.Key == "ActualClosing").Index;
                Assert.Equal(185.6666m, workbook.Worksheet("Summary").Cell(summaryIndex, 2).GetValue<decimal>());
                Assert.All(workbook.Worksheets.SelectMany(value => value.CellsUsed()), value => Assert.False(value.HasFormula));
            }
            page.DescriptionContains = "لا توجد حركة مطابقة";
            await page.PreviewCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.True(string.IsNullOrEmpty(page.Message), page.Message);
            Assert.Equal(2, previews.Count);
            Assert.Empty(previews[1].Snapshot.Rows);
            Assert.Equal("185.6666", previews[1].Snapshot.Summary.ActualClosing);
            Assert.Equal("0.0000", previews[1].Snapshot.Summary.FilteredDebit);
            Assert.Equal("185.6666", snapshot.Summary.ActualClosing); // First captured execution remains intact.

            var valid = new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = year.Id, From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31),
                AccountId = cash, Sources = [ProfessionalReportSource.UnclassifiedJournal], Fields = [ProfessionalReportField.Debit],
            };
            var denied = await Assert.ThrowsAsync<PetraApiException<ProblemDetails>>(() => session.Api.GetProfessionalReportCatalogAsync(company.Id, Guid.NewGuid(), Cancellation)).ConfigureAwait(true);
            Assert.Equal(403, denied.StatusCode);
            valid.FiscalYearId = Guid.Empty;
            var invalidYear = await Assert.ThrowsAsync<PetraApiException<ProblemDetails>>(() => session.Api.ExecuteProfessionalReportAsync(company.Id, branch.Id, valid, Cancellation)).ConfigureAwait(true);
            Assert.Equal(400, invalidYear.StatusCode);
            valid.FiscalYearId = year.Id; valid.Sources = [ProfessionalReportSource.ManualJournal];
            var unsupported = await Assert.ThrowsAsync<PetraApiException<ProblemDetails>>(() => session.Api.ExecuteProfessionalReportAsync(company.Id, branch.Id, valid, Cancellation)).ConfigureAwait(true);
            Assert.Equal(400, unsupported.StatusCode);

            using var rawClient = host.Factory.CreateClient();
            var tokens = await ApiSqlHost.LoginAsync(rawClient, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            var raw = new Wire.ProfessionalReportRequest
            {
                Kind = Wire.ProfessionalReportKind.AccountLedger, FiscalYearId = year.Id, From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31),
                AccountId = cash, Sources = [Wire.ProfessionalReportSource.UnclassifiedJournal], Fields = [Wire.ProfessionalReportField.Debit],
            };
            var payload = JsonSerializer.SerializeToNode(raw, ApiJson.Options)!.AsObject();
            payload["untrustedCompanyOverride"] = Guid.NewGuid().ToString("D");
            using var unknown = host.WorkspaceRequest(HttpMethod.Post, $"/api/v1/companies/{company.Id}/branches/{branch.Id}/reporting/execute",
                tokens.AccessToken, JsonContent.Create(payload, options: ApiJson.Options));
            using var unknownResponse = await rawClient.SendAsync(unknown, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.BadRequest, unknownResponse.StatusCode);

            async Task<Guid> CreateAccountAsync(string code, string arabic, AccountType type) => (await session.Api.CreateAccountAsync(company.Id, branch.Id,
                new AccountRequest { Code = code, NameArabic = arabic, AccountType = type, PostingAllowed = true,
                    CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly, CostCenterPolicy = CostCenterPolicy.Optional }, Cancellation).ConfigureAwait(true)).Id;

            async Task<Guid> PostAsync(DateOnly date, string description, string amount)
            {
                var journal = await session.Api.CreateJournalDraftAsync(company.Id, branch.Id, new CreateJournalDraftRequest
                {
                    AccountingDate = date, Description = description,
                    Lines = [Line(cash, amount, "0.0000"), Line(equity, "0.0000", amount)],
                }, Cancellation).ConfigureAwait(true);
                journal = await session.Api.ValidateJournalDraftAsync(company.Id, branch.Id, journal.Id,
                    new JournalActionRequest { ExpectedVersion = journal.Version, Reason = "تحقق بيانات صناعية" }, Cancellation).ConfigureAwait(true);
                journal = await session.Api.ApproveJournalDraftAsync(company.Id, branch.Id, journal.Id,
                    new JournalActionRequest { ExpectedVersion = journal.Version, Reason = "اعتماد بيانات صناعية" }, Cancellation).ConfigureAwait(true);
                var receipt = await session.Api.PostJournalAsync(company.Id, branch.Id, journal.Id,
                    new PostJournalRequest { ExpectedVersion = journal.Version, Reason = "ترحيل اختبار التقارير" }, Guid.NewGuid().ToString("D"), Cancellation).ConfigureAwait(true);
                return receipt.JournalId;
            }

            JournalLineDraftDto Line(Guid account, string debit, string credit) => new()
            {
                AccountId = account, TransactionCurrencyCode = company.BaseCurrencyCode, TransactionDebit = debit, TransactionCredit = credit,
                RateValue = "1.00000000", RateSourceCurrencyCode = company.BaseCurrencyCode, RateTargetCurrencyCode = company.BaseCurrencyCode,
                Description = "حركة صناعية عربية", BaseDebit = debit, BaseCredit = credit,
            };
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
#pragma warning disable CA1707 // Name mandated verbatim by the reporting repair order.
    public async Task TrialBalance_FirstOpen_DefaultCriteria_ExecutesSuccessfully()
#pragma warning restore CA1707
    {
        // The owner-visible defect: opening trial balance and pressing preview with untouched defaults
        // must execute through the real client, API validation, catalog rules and SQL without a 400.
        var host = await ApiSqlHost.CreateBootstrapWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var trace = new RequestTrace(host.Factory.Server.CreateHandler());
            using var session = new DesktopSession(new Uri("https://localhost/"), host.Factory.Server.CreateHandler(), trace);
            await session.LoginAsync(ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword, Cancellation).ConfigureAwait(true);
            var currentUser = await session.Api.GetCurrentUserAsync(Cancellation).ConfigureAwait(true);
            var company = Assert.Single((await session.Api.ListMyCompaniesAsync(Cancellation).ConfigureAwait(true)).Items);
            var branch = Assert.Single((await session.Api.ListMyBranchesAsync(company.Id, Cancellation).ConfigureAwait(true)).Items);
            session.SelectWorkspace(host.WorkspaceId, host.CompanyId, host.FiscalYearId);
            var localization = new LocalizationService("ar");
            var operation = new DesktopOperation(localization);
            var transport = new ProfessionalReportsClient(session);

            async Task<Guid> CreateAccountAsync(string code, string arabic, AccountType type) => (await session.Api.CreateAccountAsync(company.Id, branch.Id,
                new AccountRequest { Code = code, NameArabic = arabic, AccountType = type, PostingAllowed = true,
                    CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly, CostCenterPolicy = CostCenterPolicy.Optional }, Cancellation).ConfigureAwait(true)).Id;

            var cash = await CreateAccountAsync("1101", "النقدية", AccountType.Assets).ConfigureAwait(true);
            var capital = await CreateAccountAsync("3101", "رأس المال", AccountType.Equity).ConfigureAwait(true);
            await PostTrialJournalAsync(session, company, branch, cash, capital, new DateOnly(2026, 4, 1), "افتتاح", "1000.0000").ConfigureAwait(true);
            await PostTrialJournalAsync(session, company, branch, cash, capital, new DateOnly(2026, 6, 15), "حركة", "250.5000").ConfigureAwait(true);

            using var page = new ProfessionalReportPageViewModel(ProfessionalReportKind.TrialBalance, transport, transport,
                new LocalPresetBoundary(), operation, localization);
            var previews = new List<ProfessionalPreviewRequest>();
            page.PreviewRequested += previews.Add;
            var shellContext = new ShellContext(company, branch, currentUser.Permissions.ToHashSet(StringComparer.Ordinal)) { UserId = currentUser.UserId, FiscalYearId = host.FiscalYearId };
            await page.ActivateAsync(shellContext, null, Cancellation).ConfigureAwait(true);

            // First-open defaults without any user sorting interaction.
            Assert.Equal(ProfessionalReportOrder.AccountCode, page.Order);
            var captured = page.CaptureParameters();
            Assert.Equal(ProfessionalReportOrder.AccountCode, captured.Order);

            await page.PreviewCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.True(string.IsNullOrEmpty(page.Message), page.Message);
            var preview = Assert.Single(previews);
            Assert.Equal(ProfessionalReportOrder.AccountCode, preview.Snapshot.Parameters.Order);
            var rows = preview.Snapshot.Rows.Where(row => !row.IsAggregate).ToArray();
            Assert.Contains(rows, row => row.AccountCode == "1101");
            Assert.Contains(rows, row => row.AccountCode == "3101");
            Assert.Equal("1250.5000", preview.Snapshot.Summary.ClosingDebit);
            Assert.Equal("1250.5000", preview.Snapshot.Summary.ClosingCredit);

            // A stale or non-WPF client sending the contract default Order=Date is normalized server-side, not rejected.
            using var rawClient = host.Factory.CreateClient();
            var tokens = await ApiSqlHost.LoginAsync(rawClient, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true);
            var raw = new Wire.ProfessionalReportRequest
            {
                Kind = Wire.ProfessionalReportKind.TrialBalance, FiscalYearId = host.FiscalYearId,
                From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31),
                Sources = [Wire.ProfessionalReportSource.UnclassifiedJournal], Fields = [Wire.ProfessionalReportField.AccountCode, Wire.ProfessionalReportField.ClosingDebit, Wire.ProfessionalReportField.ClosingCredit],
                Order = Wire.ProfessionalReportOrder.Date,
            };
            using var rawRequest = host.WorkspaceRequest(HttpMethod.Post, $"/api/v1/companies/{company.Id}/branches/{branch.Id}/reporting/execute",
                tokens.AccessToken, JsonContent.Create(raw, options: ApiJson.Options));
            using var rawResponse = await rawClient.SendAsync(rawRequest, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, rawResponse.StatusCode);
            var body = JsonDocument.Parse(await rawResponse.Content.ReadAsStringAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal("AccountCode", body.RootElement.GetProperty("parameters").GetProperty("order").ToString());
        }
    }

    private static async Task PostTrialJournalAsync(DesktopSession session, CompanyContextResponse company,
        BranchContextResponse branch, Guid debit, Guid credit, DateOnly date, string description, string amount)
    {
        var journal = await session.Api.CreateJournalDraftAsync(company.Id, branch.Id, new CreateJournalDraftRequest
        {
            AccountingDate = date, Description = description,
            Lines = [TrialLine(debit, amount), TrialLine(credit, amount, debitSide: false)],
        }, Cancellation).ConfigureAwait(true);
        journal = await session.Api.ValidateJournalDraftAsync(company.Id, branch.Id, journal.Id,
            new JournalActionRequest { ExpectedVersion = journal.Version, Reason = "تحقق" }, Cancellation).ConfigureAwait(true);
        journal = await session.Api.ApproveJournalDraftAsync(company.Id, branch.Id, journal.Id,
            new JournalActionRequest { ExpectedVersion = journal.Version, Reason = "اعتماد" }, Cancellation).ConfigureAwait(true);
        await session.Api.PostJournalAsync(company.Id, branch.Id, journal.Id,
            new PostJournalRequest { ExpectedVersion = journal.Version, Reason = "ترحيل" }, Guid.NewGuid().ToString("D"), Cancellation).ConfigureAwait(true);

        JournalLineDraftDto TrialLine(Guid account, string value, bool debitSide = true) => new()
        {
            AccountId = account, TransactionCurrencyCode = company.BaseCurrencyCode,
            TransactionDebit = debitSide ? value : "0.0000", TransactionCredit = debitSide ? "0.0000" : value,
            RateValue = "1.00000000", RateSourceCurrencyCode = company.BaseCurrencyCode, RateTargetCurrencyCode = company.BaseCurrencyCode,
            Description = "حركة اختبار", BaseDebit = debitSide ? value : "0.0000", BaseCredit = debitSide ? "0.0000" : value,
        };
    }

    private sealed class RequestTrace(HttpMessageHandler inner) : DelegatingHandler(inner)    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class LocalPresetBoundary : IProfessionalReportPresetStore
    {
        private readonly Dictionary<(Guid User, Guid Company, Guid Branch, Guid? Year, ProfessionalReportKind Kind), List<ProfessionalReportPreset>> values = [];
        public Task<IReadOnlyList<ProfessionalReportPreset>> LoadAsync(ShellContext context, ProfessionalReportKind kind, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ProfessionalReportPreset>>(values.GetValueOrDefault((context.UserId, context.CompanyId, context.BranchId, context.FiscalYearId, kind))?.ToArray() ?? []);
        }
        public Task SaveAsync(ShellContext context, ProfessionalReportPreset preset, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = (context.UserId, context.CompanyId, context.BranchId, context.FiscalYearId, preset.Parameters.Kind);
            if (!values.TryGetValue(key, out var list)) values.Add(key, list = []);
            list.RemoveAll(value => string.Equals(value.Name, preset.Name, StringComparison.Ordinal));
            list.Add(preset);
            return Task.CompletedTask;
        }
    }
}
