using PetraERP.ApiClient;

namespace PetraERP.Desktop.Management;

/// <summary>Desktop view of the bank-reconciliation routes of one branch.</summary>
public interface IBankReconciliationClient
{
    Task<IReadOnlyList<BankReconciliationSummaryResponse>> ListAsync(Guid companyId, Guid branchId, Guid? accountId, CancellationToken cancellationToken);
    Task<BankReconciliationResponse> StartAsync(Guid companyId, Guid branchId, Guid accountId, DateOnly statementDate, string statementBalance, CancellationToken cancellationToken);
    Task<BankReconciliationResponse> GetAsync(Guid companyId, Guid branchId, Guid reconciliationId, CancellationToken cancellationToken);
    Task<BankReconciliationResponse> SetClearedAsync(Guid companyId, Guid branchId, Guid reconciliationId, IReadOnlyList<Guid> lineIds, bool cleared, string version, CancellationToken cancellationToken);
    Task<BankReconciliationResponse> CompleteAsync(Guid companyId, Guid branchId, Guid reconciliationId, string version, CancellationToken cancellationToken);
    Task DiscardAsync(Guid companyId, Guid branchId, Guid reconciliationId, string version, CancellationToken cancellationToken);
}
