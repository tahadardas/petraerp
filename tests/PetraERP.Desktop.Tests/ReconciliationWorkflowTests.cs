using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

[Collection(WpfApplicationGroup.Name)]
public sealed class ReconciliationWorkflowTests
{
    [Theory]
    [InlineData("year")]
    [InlineData("branch")]
    [InlineData("company")]
    public async Task ChangingScopeWhileRequestIsPendingLoadsOnlyTheNewReportOnUiThread(string changed)
    {
        await WpfApplicationHost.RunAsync(async () =>
        {
            using var auth = new AuthTransport();
            using var resources = new DelayedReports();
            using var session = new DesktopSession(new Uri("https://localhost/"), auth, resources);
            await session.LoginAsync("user", "not-recorded", TestContext.Current.CancellationToken).ConfigureAwait(true);
            var company = Guid.NewGuid();
            var branch = Guid.NewGuid();
            var firstYear = Guid.NewGuid();
            var secondYear = changed == "year" ? Guid.NewGuid() : firstYear;
            var secondCompany = changed == "company" ? Guid.NewGuid() : company;
            var secondBranch = changed == "branch" ? Guid.NewGuid() : branch;
            var firstWorkspace = Guid.NewGuid();
            var secondWorkspace = Guid.NewGuid();
            resources.YearForWorkspace.Add(firstWorkspace.ToString("D"), firstYear);
            resources.YearForWorkspace.Add(secondWorkspace.ToString("D"), secondYear);
            var localization = new LocalizationService("ar");
            using var page = new ReconciliationViewModel(session, new DesktopOperation(localization), localization);
            var dispatcher = Dispatcher.CurrentDispatcher;
            var changesOnUi = true;
            page.Findings.CollectionChanged += (_, _) => changesOnUi &= dispatcher.CheckAccess();

            session.SelectWorkspace(firstWorkspace, company, firstYear);
            var first = page.ActivateAsync(Context(company, branch, firstYear), null, TestContext.Current.CancellationToken);
            await resources.FirstEntered.Task.WaitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            session.SelectWorkspace(secondWorkspace, secondCompany, secondYear);
            var second = page.ActivateAsync(Context(secondCompany, secondBranch, secondYear), null, TestContext.Current.CancellationToken);
            resources.ReleaseFirst.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.True(changesOnUi);
            Assert.Null(page.Operation.ErrorKey);
            Assert.Equal(secondYear, page.Report?.FiscalYearId);
            Assert.Equal("NEW", Assert.Single(page.Findings).CheckCode);
            Assert.Equal([firstWorkspace.ToString("D"), secondWorkspace.ToString("D")], resources.Workspaces);
            Assert.Contains(secondCompany.ToString("D"), resources.Paths[1], StringComparison.OrdinalIgnoreCase);
            Assert.Contains(secondBranch.ToString("D"), resources.Paths[1], StringComparison.OrdinalIgnoreCase);
        }, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static ShellContext Context(Guid company, Guid branch, Guid year) => new(
        new CompanyContextResponse { Id = company, Code = "A", NameArabic = "شركة", BaseCurrencyCode = "USD" },
        new BranchContextResponse { Id = branch, Code = "MAIN", NameArabic = "رئيسي" },
        new HashSet<string>(StringComparer.Ordinal) { "Ledger.Reports.View" }) { FiscalYearId = year };

    private sealed class AuthTransport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = new TokenResponse
            {
                AccessToken = "access", RefreshToken = "refresh", TokenType = "Bearer", SessionId = Guid.NewGuid(),
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10), RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                SessionExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(token)) });
        }
    }

    private sealed class DelayedReports : HttpMessageHandler
    {
        public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Workspaces { get; } = [];
        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var workspace = request.Headers.GetValues(DesktopSession.WorkspaceHeaderName).Single();
            Workspaces.Add(workspace);
            Paths.Add(request.RequestUri!.AbsolutePath);
            var first = Workspaces.Count == 1;
            if (first)
            {
                FirstEntered.TrySetResult();
                await ReleaseFirst.Task.ConfigureAwait(false);
            }
            var result = new ReconciliationReportResponse
            {
                FiscalYearId = Guid.Empty,
                Findings = [new ReconciliationFindingResponse { CheckCode = first ? "OLD" : "NEW", Severity = "PASS", Explanation = "مطابقة" }],
            };
            // The test maps workspace to its expected year below, without relying on a real API or SQL server.
            result.FiscalYearId = YearForWorkspace[workspace];
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result)) };
        }

        public Dictionary<string, Guid> YearForWorkspace { get; } = new(StringComparer.Ordinal);
    }
}
