namespace PetraERP.Contracts.Productivity;

public enum SearchCategory { Accounts, Parties, Items, Invoices, Receipts, Payments, JournalEntries, All }
public enum SearchOrder { Code, Name, Date }
public sealed record WorkspaceCommandResponse(string Id, string LabelArabic, string LabelEnglish, SearchCategory Category);
public sealed record SearchHitResponse(Guid Id, SearchCategory Category, string Code, string Name, string Status, DateOnly? Date);
public sealed record SearchPageResponse(IReadOnlyList<SearchHitResponse> Items, int Page, int PageSize, long TotalCount);
public sealed record PreviewFieldResponse(string LabelArabic, string LabelEnglish, string? Value);
public sealed record PreviewLineResponse(string Account, string Description, string Currency, string Debit, string Credit);
public sealed record EntityPreviewResponse(SearchHitResponse Entity, IReadOnlyList<PreviewFieldResponse> Fields, IReadOnlyList<PreviewLineResponse> Lines,
    int LinePage, long LineCount, string? BaseDebit, string? BaseCredit, string? BaseCurrency);
public sealed record GridColumnPreferenceRequest(string Key, int Width, int Order, bool Visible);
public sealed record GridPreferenceRequest(string Key, IReadOnlyList<GridColumnPreferenceRequest> Columns);
public sealed record WorkspacePreferencesRequest(string Language, string Theme, IReadOnlyList<string> FavoriteCommands,
    IReadOnlyList<string> FavoriteReports, IReadOnlyList<GridPreferenceRequest> Grids);
public sealed record SaveWorkspaceRequest(WorkspacePreferencesRequest Preferences, string Version);
public sealed record WorkspaceStateResponse(WorkspacePreferencesRequest Preferences, IReadOnlyList<SearchHitResponse> Recent, string Version);
public sealed record RecordRecentRequest(SearchCategory Category, Guid EntityId);
