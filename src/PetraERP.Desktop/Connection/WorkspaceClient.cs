using PetraERP.ApiClient;
using PetraERP.Desktop.Productivity;

namespace PetraERP.Desktop.Connection;

public sealed class WorkspaceClient(DesktopSession session) : IWorkspaceClient
{
    public Task<IReadOnlyList<WorkspaceCommandResponse>> CommandsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) => session.Api.ListWorkspaceCommandsAsync(companyId, branchId, cancellationToken);
    public Task<SearchPageResponse> SearchAsync(Guid companyId, Guid branchId, string text, SearchCategory? category, SearchOrder order, bool descending, int page, CancellationToken cancellationToken) =>
        session.Api.SearchWorkspaceAsync(companyId, branchId, text, category, order, descending, page, 50, cancellationToken);
    public Task<EntityPreviewResponse> PreviewAsync(Guid companyId, Guid branchId, SearchCategory category, Guid id, int linePage, CancellationToken cancellationToken) => session.Api.GetWorkspaceEntityAsync(companyId, branchId, category, id, linePage, cancellationToken);
    public Task<WorkspaceStateResponse> PreferencesAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) => session.Api.GetWorkspacePreferencesAsync(companyId, branchId, cancellationToken);
    public Task<WorkspaceStateResponse> SavePreferencesAsync(Guid companyId, Guid branchId, SaveWorkspaceRequest request, CancellationToken cancellationToken) => session.Api.SaveWorkspacePreferencesAsync(companyId, branchId, request, cancellationToken);
    public Task RecordRecentAsync(Guid companyId, Guid branchId, RecordRecentRequest request, CancellationToken cancellationToken) => session.Api.RecordWorkspaceRecentAsync(companyId, branchId, request, cancellationToken);
}
