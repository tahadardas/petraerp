using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Workspaces;

namespace PetraERP.Desktop.Tests;

public sealed class WorkspaceWizardTests
{
    private static readonly SupportedCurrencyResponse Syp = new() { Code = "SYP", NameArabic = "الليرة السورية", NameEnglish = "Syrian Pound" };
    private static readonly SupportedCurrencyResponse Usd = new() { Code = "USD", NameArabic = "الدولار الأمريكي", NameEnglish = "US Dollar" };

    [Fact]
    public async Task CompanyWizardWalksAllSevenStepsValidatesEachAndSendsConfiguredParameters()
    {
        var client = new Client();
        var model = Model(client, out _);
        await model.LoadWorkspacesAsync().ConfigureAwait(true);
        await model.StartCreateCompanyCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(model.IsWizardOpen);
        Assert.Equal(7, model.Steps.Count);

        // Step 0: Company identity
        Assert.True(model.ShowCompanyInfo);
        Assert.False(model.NextCommand.CanExecute(null)); // empty code and name
        model.NewCompanyCode = "DARDAS"; // already used by an existing file
        model.NewCompanyNameArabic = "دردس";
        Assert.False(model.NextCommand.CanExecute(null));
        Assert.NotEmpty(model.StepError);
        model.NewCompanyCode = "petra2";
        Assert.True(model.NextCommand.CanExecute(null));
        model.NextCommand.Execute(null);

        // Step 1: SQL database
        Assert.True(model.ShowCompanyDatabase);
        Assert.NotEmpty(model.NewDatabaseName);
        model.NewDatabaseName = "Invalid Database Name with spaces!";
        Assert.False(model.NextCommand.CanExecute(null));
        model.NewDatabaseName = "Petra_Petra2_2027";
        Assert.True(model.NextCommand.CanExecute(null));
        model.NextCommand.Execute(null);

        // Step 2: Base currency
        Assert.True(model.ShowCompanyCurrency);
        Assert.Equal([Syp.Code, Usd.Code], model.Currencies.Select(c => c.Code));
        Assert.Null(model.NewCompanyCurrency); // never pre-selected
        Assert.False(model.NextCommand.CanExecute(null));
        model.NewCompanyCurrency = model.Currencies[1];
        model.NextCommand.Execute(null);

        // Step 3: Administrator confirmation
        Assert.True(model.ShowAdministratorConfirmation);
        Assert.False(model.NextCommand.CanExecute(null)); // empty password
        model.AdministratorPassword = "AdminSecurePassword123!";
        Assert.True(model.NextCommand.CanExecute(null));
        model.NextCommand.Execute(null);

        // Step 4: Account template
        Assert.True(model.ShowAccountTemplate);
        Assert.NotEmpty(model.AccountTemplates);
        Assert.NotNull(model.SelectedAccountTemplate);
        Assert.True(model.NextCommand.CanExecute(null));
        model.NextCommand.Execute(null);

        // Step 5: First fiscal year
        Assert.True(model.ShowCompanyYear);
        model.NewCompanyYear = 2027;
        Assert.Equal(new DateTime(2027, 1, 1), model.NewCompanyStart);
        model.NewCompanyEnd = new DateTime(2026, 12, 31);
        Assert.False(model.NextCommand.CanExecute(null));
        model.NewCompanyEnd = new DateTime(2027, 12, 31);
        model.NextCommand.Execute(null);

        // Step 6: Review and create
        Assert.True(model.ShowReview);
        Assert.Contains("USD", model.ReviewText, StringComparison.Ordinal);
        model.BackCommand.Execute(null);
        Assert.True(model.ShowCompanyYear);
        Assert.Equal(2027, model.NewCompanyYear); // going back keeps entries
        model.NextCommand.Execute(null);
        await model.FinishCommand.ExecuteAsync(null).ConfigureAwait(true);

        var request = Assert.Single(client.Companies);
        Assert.Equal("PETRA2", request.Code);
        Assert.Equal("USD", request.BaseCurrencyCode);
        Assert.Equal("Petra_Petra2_2027", request.DatabaseName);
        Assert.Equal("PETRA-GENERIC", request.ChartTemplateCode);
        Assert.Equal(new DateOnly(2027, 1, 1), request.StartDate);
        Assert.False(model.IsWizardOpen);
    }

    [Fact]
    public async Task DoubleClickingAFileOpensItAndCancelKeepsTheList()
    {
        var client = new Client();
        var model = Model(client, out var opened);
        await model.LoadWorkspacesAsync().ConfigureAwait(true);
        var file = Assert.Single(model.FilteredWorkspaces);
        model.OpenWorkspaceItemCommand.Execute(file);
        Assert.Same(file, Assert.Single(opened));

        model.StartCreateFiscalYearCommand.Execute(null);
        Assert.True(model.ShowYearInfo);
        Assert.Equal(2027, model.NewYearNumber);
        model.OpenWorkspaceItemCommand.Execute(file); // ignored while a wizard is open
        Assert.Single(opened);
        model.CancelWizardCommand.Execute(null);
        Assert.False(model.IsWizardOpen);
        Assert.Single(model.FilteredWorkspaces);
    }

    internal static WorkspaceSelectionViewModel Model(Client client, out List<WorkspaceSummaryResponse> opened)
    {
        var localization = new LocalizationService("ar");
        var list = new List<WorkspaceSummaryResponse>();
        opened = list;
        return new WorkspaceSelectionViewModel(client, localization, new DesktopOperation(localization), list.Add);
    }

    internal sealed class Client : IDesktopClient
    {
        public List<CreateCompanyWorkspaceRequest> Companies { get; } = [];
        public Task<bool> LoginAsync(string userName, string password, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> ResumeAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task ChangePasswordAsync(string current, string replacement, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LogoutAsync(bool all, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CurrentUserResponse> ProfileAsync(CancellationToken cancellationToken) => Task.FromResult(new CurrentUserResponse());
        public Task<CompanyContextListResponse> CompaniesAsync(CancellationToken cancellationToken) => Task.FromResult(new CompanyContextListResponse());
        public Task<BranchContextListResponse> BranchesAsync(Guid companyId, CancellationToken cancellationToken) => Task.FromResult(new BranchContextListResponse());
        public Task<ServiceStatusResponse> StatusAsync(CancellationToken cancellationToken) => Task.FromResult(new ServiceStatusResponse());
        public Task<WorkspaceListResponse> WorkspacesAsync(CancellationToken cancellationToken) => Task.FromResult(new WorkspaceListResponse
        {
            Items =
            [
                new WorkspaceSummaryResponse
                {
                    WorkspaceId = Guid.NewGuid(), CompanyId = Guid.NewGuid(), CompanyCode = "DARDAS", CompanyNameArabic = "دردس 2026", BaseCurrencyCode = "SYP",
                    FiscalYearId = Guid.NewGuid(), YearNumber = 2026, FiscalYearCode = "FY2026", State = "Active", IsDefault = true,
                },
            ],
        });
        public Task<SupportedCurrencyListResponse> SupportedCurrenciesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SupportedCurrencyListResponse { Items = [Syp, Usd] });
        public Task<IReadOnlyList<RegisteredChartTemplate>> AccountTemplatesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RegisteredChartTemplate>>([
                new RegisteredChartTemplate(Guid.NewGuid(), "PETRA-GENERIC", "دليل عام للمباشرة", "Generic starter", "ZZ", 1, "Generic source", [
                    new RegisteredTemplateAccount("1", "الأصول", null, false),
                    new RegisteredTemplateAccount("11", "الأصول المتداولة", "1", false),
                    new RegisteredTemplateAccount("1101", "الصندوق", "11", true)
                ])
            ]);
        public Task<ProvisioningOperationResponse> CreateCompanyWorkspaceAsync(CreateCompanyWorkspaceRequest request, CancellationToken cancellationToken)
        {
            Companies.Add(request);
            return Task.FromResult(new ProvisioningOperationResponse { CompanyId = Guid.NewGuid(), FiscalYearId = Guid.NewGuid() });
        }
    }
}
