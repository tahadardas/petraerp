using PetraERP.Application.Common;

namespace PetraERP.Application.Productivity;

public enum SearchCategory { Accounts, Parties, Items, Invoices, Receipts, Payments, JournalEntries, All }
public enum SearchOrder { Code, Name, Date }
public sealed record WorkspaceScope(Guid UserId, Guid CompanyId, Guid BranchId, Guid WorkspaceId);
public sealed record WorkspaceCommand(string Id, string LabelArabic, string LabelEnglish, SearchCategory? Category);
public sealed record SearchHit(Guid Id, SearchCategory Category, string Code, string Name, string Status, DateOnly? Date);
public sealed record SearchQuery(string? Text, SearchCategory? Category, SearchOrder Order, bool Descending, PageRequest Page);
public sealed record PreviewField(string LabelArabic, string LabelEnglish, string? Value);
public sealed record PreviewLine(string Account, string Description, string Currency, string Debit, string Credit);
public sealed record EntityPreview(SearchHit Entity, IReadOnlyList<PreviewField> Fields, IReadOnlyList<PreviewLine> Lines,
    int LinePage, long LineCount, string? BaseDebit, string? BaseCredit, string? BaseCurrency);
public sealed record GridColumnPreference(string Key, int Width, int Order, bool Visible);
public sealed record GridPreference(string Key, IReadOnlyList<GridColumnPreference> Columns);
public sealed record WorkspacePreferences(string Language, string Theme, IReadOnlyList<string> FavoriteCommands,
    IReadOnlyList<string> FavoriteReports, IReadOnlyList<GridPreference> Grids)
{
    public static WorkspacePreferences Default { get; } = new("ar", "System", [], [], []);
}
public sealed record RecentReference(Guid CompanyId, Guid BranchId, Guid WorkspaceId, SearchCategory Category, Guid EntityId);
public sealed record StoredWorkspace(WorkspacePreferences Preferences, IReadOnlyList<RecentReference> Recent, string Version);
public sealed record WorkspaceState(WorkspacePreferences Preferences, IReadOnlyList<SearchHit> Recent, string Version);

public interface IWorkspaceRepository
{
    Task<StoredWorkspace> LoadAsync(Guid userId, CancellationToken cancellationToken);
    Task<StoredWorkspace> SaveAsync(Guid userId, StoredWorkspace value, CancellationToken cancellationToken);
    Task<PagedResult<SearchHit>> SearchAsync(WorkspaceScope scope, SearchQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<SearchHit>> ResolveRecentAsync(WorkspaceScope scope, IReadOnlyList<RecentReference> recent, CancellationToken cancellationToken);
    Task<EntityPreview?> PreviewAsync(WorkspaceScope scope, SearchCategory category, Guid id, int linePage, CancellationToken cancellationToken);
}
public interface IWorkspaceService
{
    Task<IReadOnlyList<WorkspaceCommand>> CatalogAsync(WorkspaceScope scope, CancellationToken cancellationToken);
    Task<PagedResult<SearchHit>> SearchAsync(WorkspaceScope scope, SearchQuery query, CancellationToken cancellationToken);
    Task<EntityPreview> PreviewAsync(WorkspaceScope scope, SearchCategory category, Guid id, int linePage, CancellationToken cancellationToken);
    Task<WorkspaceState> LoadAsync(WorkspaceScope scope, CancellationToken cancellationToken);
    Task<StoredWorkspace> SaveAsync(WorkspaceScope scope, WorkspacePreferences preferences, string version, CancellationToken cancellationToken);
    Task RecordRecentAsync(WorkspaceScope scope, SearchCategory category, Guid id, CancellationToken cancellationToken);
}
