using PetraERP.ApiClient;
using PetraERP.Desktop.Management;

namespace PetraERP.Desktop.Connection;

public sealed class BankReconciliationClient(DesktopSession session) : IBankReconciliationClient
{
    public Task<IReadOnlyList<BankReconciliationSummaryResponse>> ListAsync(Guid companyId, Guid branchId, Guid? accountId, CancellationToken cancellationToken) =>
        session.Api.ListBankReconciliationsAsync(companyId, branchId, accountId, cancellationToken);

    public Task<BankReconciliationResponse> StartAsync(Guid companyId, Guid branchId, Guid accountId, DateOnly statementDate, string statementBalance, CancellationToken cancellationToken) =>
        session.Api.StartBankReconciliationAsync(companyId, branchId,
            new StartBankReconciliationRequest { AccountId = accountId, StatementDate = statementDate, StatementBalance = statementBalance }, cancellationToken);

    public Task<BankReconciliationResponse> GetAsync(Guid companyId, Guid branchId, Guid reconciliationId, CancellationToken cancellationToken) =>
        session.Api.GetBankReconciliationAsync(companyId, branchId, reconciliationId, cancellationToken);

    public Task<BankReconciliationResponse> SetClearedAsync(Guid companyId, Guid branchId, Guid reconciliationId, IReadOnlyList<Guid> lineIds, bool cleared, string version, CancellationToken cancellationToken) =>
        session.Api.SetBankReconciliationLinesAsync(companyId, branchId, reconciliationId,
            new SetBankReconciliationLinesRequest { JournalLineIds = lineIds.ToList(), Cleared = cleared, ExpectedVersion = version }, cancellationToken);

    public Task<BankReconciliationResponse> CompleteAsync(Guid companyId, Guid branchId, Guid reconciliationId, string version, CancellationToken cancellationToken) =>
        session.Api.CompleteBankReconciliationAsync(companyId, branchId, reconciliationId, new BankReconciliationVersionRequest { ExpectedVersion = version }, cancellationToken);

    public Task DiscardAsync(Guid companyId, Guid branchId, Guid reconciliationId, string version, CancellationToken cancellationToken) =>
        session.Api.DiscardBankReconciliationAsync(companyId, branchId, reconciliationId, new BankReconciliationVersionRequest { ExpectedVersion = version }, cancellationToken);
}
