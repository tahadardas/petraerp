using PetraERP.ApiClient;
namespace PetraERP.Desktop.Connection;

public interface ICompanyOptionsClient
{
    Task<CompanyFinancialPolicyState> GetCompanyPolicyAsync(Guid company,Guid branch,CancellationToken token);
    Task<CompanyFinancialPolicyState> SaveCompanyPolicyAsync(Guid company,Guid branch,CompanyFinancialPolicyData policy,string version,string reason,CancellationToken token);
    Task<IReadOnlyList<DocumentPatternSummaryResponse>> CompanyPolicyPatternsAsync(Guid company,Guid branch,CancellationToken token);
    /// <summary>The chart of accounts, for choosing the customers / suppliers headings.</summary>
    Task<IReadOnlyList<AccountResponse>> CompanyPolicyAccountsAsync(Guid company,Guid branch,CancellationToken token);
}
public sealed partial class DesktopClient
{
    public Task<CompanyFinancialPolicyState> GetCompanyPolicyAsync(Guid company,Guid branch,CancellationToken token)=>session.Api.ReadCompanyPolicyAsync(company,branch,token);
    public Task<CompanyFinancialPolicyState> SaveCompanyPolicyAsync(Guid company,Guid branch,CompanyFinancialPolicyData policy,string version,string reason,CancellationToken token)=>session.Api.WriteCompanyPolicyAsync(company,branch,policy,version,reason,token);
    public Task<IReadOnlyList<DocumentPatternSummaryResponse>> CompanyPolicyPatternsAsync(Guid company,Guid branch,CancellationToken token)=>session.Api.GetRunnableDocumentPatternsAsync(company,branch,token);
    public Task<IReadOnlyList<AccountResponse>> CompanyPolicyAccountsAsync(Guid company,Guid branch,CancellationToken token)=>session.Api.GetAccountTreeAsync(company,branch,token);
}
