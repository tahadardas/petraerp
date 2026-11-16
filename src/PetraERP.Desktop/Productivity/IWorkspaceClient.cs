using PetraERP.ApiClient;

namespace PetraERP.Desktop.Productivity;

/// <summary>Workspace search/preview/preference operations. Authentication and context selection belong to the shell.</summary>
public interface IWorkspaceClient
{
    Task<IReadOnlyList<WorkspaceCommandResponse>> CommandsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<SearchPageResponse> SearchAsync(Guid companyId, Guid branchId, string text, SearchCategory? category, SearchOrder order, bool descending, int page, CancellationToken cancellationToken);
    Task<EntityPreviewResponse> PreviewAsync(Guid companyId, Guid branchId, SearchCategory category, Guid id, int linePage, CancellationToken cancellationToken);
    Task<WorkspaceStateResponse> PreferencesAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<WorkspaceStateResponse> SavePreferencesAsync(Guid companyId, Guid branchId, SaveWorkspaceRequest request, CancellationToken cancellationToken);
    Task RecordRecentAsync(Guid companyId, Guid branchId, RecordRecentRequest request, CancellationToken cancellationToken);
}
