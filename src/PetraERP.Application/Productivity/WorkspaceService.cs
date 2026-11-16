using PetraERP.Application.Common;
using PetraERP.Application.Security;

namespace PetraERP.Application.Productivity;

public sealed class WorkspaceService(IWorkspaceRepository repository, IAccessEvaluator access, IUserContextQueries contexts) : IWorkspaceService
{
    private async Task RequireScopeAsync(WorkspaceScope scope, CancellationToken cancellationToken)
    {
        if (await access.EvaluateCompanyScopeAsync(scope.UserId, scope.CompanyId, cancellationToken).ConfigureAwait(false) != AccessDecision.Granted
            || !(await contexts.GetBranchesAsync(scope.UserId, scope.CompanyId, cancellationToken).ConfigureAwait(false)).Any(branch => branch.Id == scope.BranchId))
            throw new ResourceNotFoundException("workspace");
    }
    public async Task<IReadOnlyList<WorkspaceCommand>> CatalogAsync(WorkspaceScope scope, CancellationToken cancellationToken)
    {
        await RequireScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var commands = new List<WorkspaceCommand>();
        foreach (var command in WorkspaceRules.Commands.Where(command => WorkspaceRules.CommandPermission(command) is not null))
        {
            var permission = PermissionCatalog.Get(WorkspaceRules.CommandPermission(command)!);
            if (await access.EvaluatePermissionAsync(scope.UserId, permission, scope.CompanyId, scope.BranchId, cancellationToken).ConfigureAwait(false) == AccessDecision.Granted)
                commands.Add(command);
        }
        if (commands.Count > 0) commands.Insert(0, WorkspaceRules.Commands[0]);
        return commands.AsReadOnly();
    }
    public async Task<PagedResult<SearchHit>> SearchAsync(WorkspaceScope scope, SearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await RequireScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        if (!Enum.IsDefined(query.Order) || (query.Category.HasValue && !Enum.IsDefined(query.Category.Value)) || query.Page.Page > 1_000_000)
            throw new RequestValidationException("workspace.query_invalid", "Unknown ordering/category or page above 1000000.", null);
        return await repository.SearchAsync(scope, query with { Text = SecurityInput.OptionalSearch(query.Text), Category = query.Category == SearchCategory.All ? null : query.Category }, cancellationToken).ConfigureAwait(false);
    }
    public async Task<EntityPreview> PreviewAsync(WorkspaceScope scope, SearchCategory category, Guid id, int linePage, CancellationToken cancellationToken)
    {
        await RequireScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        if (!Enum.IsDefined(category) || id == Guid.Empty || linePage is < 1 or > 1_000_000)
            throw new RequestValidationException("workspace.entity_invalid", "Invalid entity or page.", null);
        return await repository.PreviewAsync(scope, category, id, linePage, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("entity");
    }
    public async Task<WorkspaceState> LoadAsync(WorkspaceScope scope, CancellationToken cancellationToken)
    {
        var commands = await CatalogAsync(scope, cancellationToken).ConfigureAwait(false);
        var stored = await repository.LoadAsync(scope.UserId, cancellationToken).ConfigureAwait(false);
        var recent = await repository.ResolveRecentAsync(scope, stored.Recent, cancellationToken).ConfigureAwait(false);
        return new WorkspaceState(stored.Preferences with
        {
            FavoriteCommands = stored.Preferences.FavoriteCommands.Where(id => commands.Any(command => command.Id == id)).ToArray(),
            FavoriteReports = stored.Preferences.FavoriteReports.Where(id => commands.Any(command => command.Id == id)).ToArray(),
        }, recent, stored.Version);
    }
    public async Task<StoredWorkspace> SaveAsync(WorkspaceScope scope, WorkspacePreferences preferences, string version, CancellationToken cancellationToken)
    {
        WorkspaceRules.Validate(preferences);
        var catalog = await CatalogAsync(scope, cancellationToken).ConfigureAwait(false);
        if (preferences.FavoriteCommands.Concat(preferences.FavoriteReports).Any(id => !catalog.Any(command => command.Id == id)))
            throw new RequestValidationException("workspace.command_unavailable", "A favorite command is not authorized in this context.", null);
        var stored = await repository.LoadAsync(scope.UserId, cancellationToken).ConfigureAwait(false);
        if (stored.Version != version) throw new ConcurrencyConflictException("Workspace preferences changed on another device.");
        return await repository.SaveAsync(scope.UserId, new StoredWorkspace(preferences, stored.Recent, version), cancellationToken).ConfigureAwait(false);
    }
    public async Task RecordRecentAsync(WorkspaceScope scope, SearchCategory category, Guid id, CancellationToken cancellationToken)
    {
        await RequireScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        if ((await repository.ResolveRecentAsync(scope, [new RecentReference(scope.CompanyId, scope.BranchId, scope.WorkspaceId, category, id)], cancellationToken).ConfigureAwait(false)).Count == 0)
            throw new ResourceNotFoundException("entity");
        var stored = await repository.LoadAsync(scope.UserId, cancellationToken).ConfigureAwait(false);
        var reference = new RecentReference(scope.CompanyId, scope.BranchId, scope.WorkspaceId, category, id);
        var recent = stored.Recent.Where(item => item != reference).Prepend(reference).Take(WorkspaceRules.MaximumRecent).ToArray();
        _ = await repository.SaveAsync(scope.UserId, stored with { Recent = recent }, cancellationToken).ConfigureAwait(false);
    }
}
