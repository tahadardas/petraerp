using PetraERP.ApiClient;

namespace PetraERP.Desktop.Connection;

public interface IDesktopClient
{
    Task<bool> LoginAsync(string userName, string password, CancellationToken cancellationToken);
    Task<bool> ResumeAsync(CancellationToken cancellationToken);
    Task ChangePasswordAsync(string current, string replacement, CancellationToken cancellationToken);
    Task LogoutAsync(bool all, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
    Task<CurrentUserResponse> ProfileAsync(CancellationToken cancellationToken);
    Task<CompanyContextListResponse> CompaniesAsync(CancellationToken cancellationToken);
    Task<BranchContextListResponse> BranchesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<ServiceStatusResponse> StatusAsync(CancellationToken cancellationToken);
    Task<WorkspaceListResponse> WorkspacesAsync(CancellationToken cancellationToken) => Task.FromResult(new WorkspaceListResponse());
    Task<IReadOnlyList<RegisteredChartTemplate>> AccountTemplatesAsync(CancellationToken cancellationToken) => throw new NotSupportedException("The server must supply the registered template catalog.");
    Task<SupportedCurrencyListResponse> SupportedCurrenciesAsync(CancellationToken cancellationToken) => Task.FromResult(new SupportedCurrencyListResponse());
    Task<ProvisioningOperationResponse> CreateCompanyWorkspaceAsync(CreateCompanyWorkspaceRequest request, CancellationToken cancellationToken) => Task.FromResult(new ProvisioningOperationResponse());
    Task<ProvisioningOperationResponse> CreateFiscalYearWorkspaceAsync(CreateFiscalYearWorkspaceRequest request, CancellationToken cancellationToken) => Task.FromResult(new ProvisioningOperationResponse());
}

public sealed partial class DesktopClient(DesktopSession session) : IDesktopClient, IFinancialWorkspaceSelector, ICompanyOptionsClient
{
    public void SelectWorkspace(Guid workspaceId, Guid companyId, Guid fiscalYearId) => session.SelectWorkspace(workspaceId, companyId, fiscalYearId);
    public void ClearWorkspace() => session.ClearWorkspace();
    public Task<bool> LoginAsync(string userName, string password, CancellationToken cancellationToken) => session.LoginAsync(userName, password, cancellationToken);
    public Task<bool> ResumeAsync(CancellationToken cancellationToken) => session.ResumeAsync(cancellationToken);
    public Task ChangePasswordAsync(string current, string replacement, CancellationToken cancellationToken) => session.ChangePasswordAsync(current, replacement, cancellationToken);
    public Task LogoutAsync(bool all, CancellationToken cancellationToken) => session.LogoutAsync(all, cancellationToken);
    public Task ClearAsync(CancellationToken cancellationToken) => session.ClearAsync(cancellationToken);
    public Task<CurrentUserResponse> ProfileAsync(CancellationToken cancellationToken) => session.Api.GetCurrentUserAsync(cancellationToken);
    public Task<CompanyContextListResponse> CompaniesAsync(CancellationToken cancellationToken) => session.Api.ListMyCompaniesAsync(cancellationToken);
    public Task<BranchContextListResponse> BranchesAsync(Guid companyId, CancellationToken cancellationToken) => session.Api.ListMyBranchesAsync(companyId, cancellationToken);
    public Task<ServiceStatusResponse> StatusAsync(CancellationToken cancellationToken) => session.StatusAsync(cancellationToken);
    public Task<WorkspaceListResponse> WorkspacesAsync(CancellationToken cancellationToken) => session.Api.ListMyWorkspacesAsync(cancellationToken);
    public Task<IReadOnlyList<RegisteredChartTemplate>> AccountTemplatesAsync(CancellationToken cancellationToken) => session.Api.GetRegisteredAccountTemplatesAsync(cancellationToken);
    public Task<SupportedCurrencyListResponse> SupportedCurrenciesAsync(CancellationToken cancellationToken) => session.Api.ListSupportedCompanyCurrenciesAsync(cancellationToken);
    public Task<ProvisioningOperationResponse> CreateCompanyWorkspaceAsync(CreateCompanyWorkspaceRequest request, CancellationToken cancellationToken) => session.Api.ProvisionCompanyWorkspaceAsync(request, cancellationToken);
    public Task<ProvisioningOperationResponse> CreateFiscalYearWorkspaceAsync(CreateFiscalYearWorkspaceRequest request, CancellationToken cancellationToken) => session.Api.ProvisionFiscalYearWorkspaceAsync(request, cancellationToken);
}
