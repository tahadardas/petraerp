namespace PetraERP.Desktop.Connection;

/// <summary>
/// Selects a registered company/year for resource requests in the current signed-in session.
/// The server resolves and authorizes the registration; database names and connection strings are never sent.
/// </summary>
public interface IFinancialWorkspaceSelector
{
    void SelectWorkspace(Guid workspaceId, Guid companyId, Guid fiscalYearId);
    void ClearWorkspace();
}
