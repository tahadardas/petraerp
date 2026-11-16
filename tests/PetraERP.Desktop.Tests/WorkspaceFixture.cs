using System.Text.Json;
using PetraERP.ApiClient;
using PetraERP.Desktop.Productivity;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Tests;

internal sealed class TestThemes : IThemeService
{
    public ThemePreference Preference { get; private set; }
    public AppTheme EffectiveTheme => ThemeResolver.Resolve(Preference, true);
    public event EventHandler? ThemeChanged;
    public void Apply(ThemePreference preference) { Preference = preference; ThemeChanged?.Invoke(this, EventArgs.Empty); }
}

internal sealed class WorkspaceFixture : IWorkspaceClient
{
    internal CompanyContextResponse A { get; } = new() { Id = Guid.NewGuid(), Code = "A", NameArabic = "شركة ألف", NameEnglish = "Company A", BaseCurrencyCode = "USD" };
    internal CompanyContextResponse B { get; } = new() { Id = Guid.NewGuid(), Code = "B", NameArabic = "شركة باء", NameEnglish = "Company B", BaseCurrencyCode = "SYP" };
    internal IReadOnlyList<BranchContextResponse> Branches { get; }
    internal TaskCompletionSource<EntityPreviewResponse>? PendingPreview { get; set; }
    internal TaskCompletionSource PreviewStarted { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<SearchPageResponse>? PendingSearch { get; set; }
    internal bool SessionUnavailable { get; set; }
    internal bool AllowAccounts { get; set; } = true;
    internal SearchOrder LastSort { get; private set; }
    internal int LastPage { get; private set; }
    internal SaveWorkspaceRequest? LastSaved { get; private set; }
    private WorkspaceStateResponse state = new() { Version = "v0", Preferences = new() { Language = "ar", Theme = "Light", FavoriteCommands = [], FavoriteReports = [], Grids = [] }, Recent = [] };
    public WorkspaceFixture() => Branches = [new() { Id = Guid.NewGuid(), CompanyId = A.Id, Code = "A1", NameArabic = "فرع أول" },
        new() { Id = Guid.NewGuid(), CompanyId = A.Id, Code = "A2", NameArabic = "فرع ثان" }, new() { Id = Guid.NewGuid(), CompanyId = B.Id, Code = "B1", NameArabic = "فرع باء" }];
    public Task<IReadOnlyList<WorkspaceCommandResponse>> CommandsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkspaceCommandResponse>>(AllowAccounts ? [new() { Id = "accounts.browse", LabelArabic = "دليل الحسابات", LabelEnglish = "Chart of accounts", Category = SearchCategory.Accounts }] : []);
    public Task<SearchPageResponse> SearchAsync(Guid companyId, Guid branchId, string text, SearchCategory? category, SearchOrder order, bool descending, int page, CancellationToken cancellationToken)
    {
        if (SessionUnavailable) return Task.FromException<SearchPageResponse>(new PetraERP.Desktop.Connection.SessionUnavailableException());
        LastSort = order; LastPage = page;
        if (PendingSearch is { } pending) { PendingSearch = null; return pending.Task; }
        return Task.FromResult(new SearchPageResponse { Page = page, PageSize = 50, TotalCount = AllowAccounts ? 101 : 0,
            Items = AllowAccounts ? [new() { Id = Guid.NewGuid(), Code = branchId.ToString("N"), Name = "الصندوق", Status = "Active", Category = SearchCategory.Accounts }] : [] });
    }
    public Task<EntityPreviewResponse> PreviewAsync(Guid companyId, Guid branchId, SearchCategory category, Guid id, int linePage, CancellationToken cancellationToken)
    {
        if (PendingPreview is { } pending) { PendingPreview = null; PreviewStarted.TrySetResult(); return pending.Task; }
        return Task.FromResult(new EntityPreviewResponse { Entity = new() { Id = id, Category = category, Code = "1001", Name = "الصندوق", Status = "Active" }, Fields = [], Lines = [], LinePage = 1, LineCount = 0 });
    }
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    public Task<WorkspaceStateResponse> PreferencesAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) => Task.FromResult(Clone(state));
    public Task<WorkspaceStateResponse> SavePreferencesAsync(Guid companyId, Guid branchId, SaveWorkspaceRequest request, CancellationToken cancellationToken)
    {
        LastSaved = Clone(request); state.Preferences = Clone(request.Preferences); state.Version += "1"; return Task.FromResult(Clone(state));
    }
    public Task RecordRecentAsync(Guid companyId, Guid branchId, RecordRecentRequest request, CancellationToken cancellationToken) { state.Version += "1"; return Task.CompletedTask; }
}
