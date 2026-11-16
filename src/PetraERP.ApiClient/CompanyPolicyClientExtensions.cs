using System.Net.Http.Json;
using System.Text.Json;

namespace PetraERP.ApiClient;

public sealed record CompanyFinancialPolicyData
{
    public string DocumentPostingMode {get;init;}="ApproveOnly";
    public bool AllowSaveAsDraft {get;init;}=true;
    public string NegativeIssuePolicy {get;init;}="Block";
    public bool RequireDocumentApproval {get;init;}
    public bool RequireProfitLossCostCenter {get;init;}
    public bool AllowCreditSales {get;init;}=true;
    public bool AllowCreditPurchases {get;init;}=true;
    public bool AllowManualNumbering {get;init;}
    public bool AllowPatternNumberingOverrides {get;init;}=true;
    public int DefaultNumberingScope {get;init;}=4;
    public long InitialNumberSequence {get;init;}=1;
    public Guid? DefaultSalesPatternId {get;init;}
    public Guid? DefaultPurchasePatternId {get;init;}
    public Guid? DefaultReceiptPatternId {get;init;}
    public Guid? DefaultPaymentPatternId {get;init;}
    /// <summary>Heading new customer accounts are created under; null keeps the chart convention.</summary>
    public Guid? CustomersParentAccountId {get;init;}
    /// <summary>Heading new supplier accounts are created under; null keeps the chart convention.</summary>
    public Guid? SuppliersParentAccountId {get;init;}
}
public sealed record CompanyFinancialPolicyState(CompanyFinancialPolicyData Policy,string Version,bool NegativePostingAvailable,string PolicyPrecedence);
public sealed record NegativeIssueApprovalState(Guid Id,Guid BranchId,Guid ItemId,Guid WarehouseId,string Quantity,string Reason,string Status,Guid RequestedBy,Guid? ReviewedBy,string Version,bool NegativePostingAvailable);
public partial interface IPetraApiClient
{
    Task<CompanyFinancialPolicyState> ReadCompanyPolicyAsync(Guid company,Guid branch,CancellationToken token=default)=>throw new NotSupportedException();
    Task<CompanyFinancialPolicyState> WriteCompanyPolicyAsync(Guid company,Guid branch,CompanyFinancialPolicyData policy,string version,string reason,CancellationToken token=default)=>throw new NotSupportedException();
    Task<IReadOnlyList<NegativeIssueApprovalState>> ReadNegativeIssueRequestsAsync(Guid company,Guid branch,CancellationToken token=default)=>throw new NotSupportedException();
    Task<NegativeIssueApprovalState> SubmitNegativeIssueRequestAsync(Guid company,Guid branch,Guid requestId,Guid item,Guid warehouse,string quantity,string reason,CancellationToken token=default)=>throw new NotSupportedException();
    Task<NegativeIssueApprovalState> DecideNegativeIssueRequestAsync(Guid company,Guid branch,Guid requestId,bool approve,string version,string reason,CancellationToken token=default)=>throw new NotSupportedException();
}
public partial class PetraApiClient
{
    private static string CompanyPolicyPath(Guid company,Guid branch)=>$"api/v1/companies/{company}/branches/{branch}/company-options/";
    public async Task<CompanyFinancialPolicyState> ReadCompanyPolicyAsync(Guid company,Guid branch,CancellationToken token=default)
    {
        using var response=await _httpClient.GetAsync(CompanyPolicyPath(company,branch),token).ConfigureAwait(false);
        return await ReadPhaseTwoResponseAsync<CompanyFinancialPolicyState>(response,token).ConfigureAwait(false);
    }
    public async Task<CompanyFinancialPolicyState> WriteCompanyPolicyAsync(Guid company,Guid branch,CompanyFinancialPolicyData policy,string version,string reason,CancellationToken token=default)
    {
        using var response=await _httpClient.PutAsJsonAsync(CompanyPolicyPath(company,branch),new {Policy=policy,ExpectedVersion=version,Reason=reason},token).ConfigureAwait(false);
        return await ReadPhaseTwoResponseAsync<CompanyFinancialPolicyState>(response,token).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<NegativeIssueApprovalState>> ReadNegativeIssueRequestsAsync(Guid company,Guid branch,CancellationToken token=default)
    {
        using var response=await _httpClient.GetAsync(CompanyPolicyPath(company,branch)+"negative-issue-requests",token).ConfigureAwait(false);
        return await ReadPhaseTwoResponseAsync<NegativeIssueApprovalState[]>(response,token).ConfigureAwait(false);
    }
    public async Task<NegativeIssueApprovalState> SubmitNegativeIssueRequestAsync(Guid company,Guid branch,Guid requestId,Guid item,Guid warehouse,string quantity,string reason,CancellationToken token=default)
    {
        using var response=await _httpClient.PostAsJsonAsync(CompanyPolicyPath(company,branch)+"negative-issue-requests",new {RequestId=requestId,ItemId=item,WarehouseId=warehouse,Quantity=quantity,Reason=reason},token).ConfigureAwait(false);
        return await ReadPhaseTwoResponseAsync<NegativeIssueApprovalState>(response,token).ConfigureAwait(false);
    }
    public async Task<NegativeIssueApprovalState> DecideNegativeIssueRequestAsync(Guid company,Guid branch,Guid requestId,bool approve,string version,string reason,CancellationToken token=default)
    {
        using var response=await _httpClient.PostAsJsonAsync(CompanyPolicyPath(company,branch)+$"negative-issue-requests/{requestId}/review",new {Approve=approve,ExpectedVersion=version,Reason=reason},token).ConfigureAwait(false);
        return await ReadPhaseTwoResponseAsync<NegativeIssueApprovalState>(response,token).ConfigureAwait(false);
    }
}
