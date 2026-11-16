namespace PetraERP.Application.CompanyOptions;

public enum NegativeIssuePolicy { Block=0, RequestApproval=1, AuthorizedException=2 }
public enum DocumentPostingMode { ApproveOnly=0, ApproveAndPost=1 }
public sealed record CompanyFinancialPolicy
{
    public DocumentPostingMode DocumentPostingMode { get; init; } = DocumentPostingMode.ApproveOnly;
    public bool AllowSaveAsDraft { get; init; } = true;
    public NegativeIssuePolicy NegativeIssuePolicy { get; init; }
    public bool RequireDocumentApproval { get; init; }
    public bool RequireProfitLossCostCenter { get; init; }
    public bool AllowCreditSales { get; init; } = true;
    public bool AllowCreditPurchases { get; init; } = true;
    public bool AllowManualNumbering { get; init; }
    public bool AllowPatternNumberingOverrides { get; init; } = true;
    public int DefaultNumberingScope { get; init; } = 4;
    public long InitialNumberSequence { get; init; } = 1;
    public Guid? DefaultSalesPatternId { get; init; }
    public Guid? DefaultPurchasePatternId { get; init; }
    public Guid? DefaultReceiptPatternId { get; init; }
    public Guid? DefaultPaymentPatternId { get; init; }
    /// <summary>The non-posting account new customer accounts are created under; null keeps the chart convention (code 121).</summary>
    public Guid? CustomersParentAccountId { get; init; }
    /// <summary>The non-posting account new supplier accounts are created under; null keeps the chart convention (code 221).</summary>
    public Guid? SuppliersParentAccountId { get; init; }
}
public sealed record CompanyPolicySnapshot(CompanyFinancialPolicy Policy,string Version)
{
    public bool NegativePostingAvailable { get; init; }
    public string PolicyPrecedence { get; init; } = "Installation security > company policy > permitted branch/pattern override > user display preference";
}
public sealed record NegativeIssueRequest(Guid Id,Guid BranchId,Guid ItemId,Guid WarehouseId,string Quantity,string Reason,
    string Status,Guid RequestedBy,Guid? ReviewedBy,string Version)
{
    public bool NegativePostingAvailable { get; init; }
}
public interface ICompanyOptionsService
{
    Task<CompanyPolicySnapshot> GetAsync(Guid companyId,CancellationToken cancellationToken);
    Task<CompanyPolicySnapshot> SaveAsync(Guid companyId,Guid fiscalYearId,Guid actorId,CompanyFinancialPolicy policy,string expectedVersion,string reason,CancellationToken cancellationToken);
    Task<IReadOnlyList<NegativeIssueRequest>> ListRequestsAsync(Guid companyId,Guid branchId,CancellationToken cancellationToken);
    Task<NegativeIssueRequest> RequestAsync(Guid companyId,Guid fiscalYearId,Guid branchId,Guid actorId,Guid itemId,Guid warehouseId,decimal quantity,string reason,Guid requestId,CancellationToken cancellationToken);
    Task<NegativeIssueRequest> ReviewAsync(Guid companyId,Guid fiscalYearId,Guid branchId,Guid actorId,Guid requestId,bool approve,string expectedVersion,string reason,CancellationToken cancellationToken);
}
