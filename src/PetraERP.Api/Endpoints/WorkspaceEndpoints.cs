using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.Productivity;
using PetraERP.Contracts.Productivity;
using Category = PetraERP.Application.Productivity.SearchCategory;
using Order = PetraERP.Application.Productivity.SearchOrder;

namespace PetraERP.Api.Endpoints;

internal static class WorkspaceEndpoints
{
    internal static void MapWorkspaceEndpoints(this RouteGroupBuilder api)
    {
        var workspace = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/workspace")
            .WithTags("Accountant workspace").RequireAuthorization(PetraPolicies.CompanyScope);
        workspace.MapGet("/commands", CatalogAsync).WithName("ListWorkspaceCommands").Produces<IReadOnlyList<WorkspaceCommandResponse>>().ProducesStandardProblems();
        workspace.MapGet("/search", SearchAsync).WithName("SearchWorkspace").WithMetadata(FinancialWorkspaceMetadata.Instance).Produces<SearchPageResponse>().ProducesStandardProblems();
        workspace.MapGet("/entities/{category}/{entityId:guid}", PreviewAsync).WithName("GetWorkspaceEntity").WithMetadata(FinancialWorkspaceMetadata.Instance).Produces<EntityPreviewResponse>().ProducesStandardProblems();
        workspace.MapGet("/preferences", LoadAsync).WithName("GetWorkspacePreferences").WithMetadata(FinancialWorkspaceMetadata.Instance).Produces<WorkspaceStateResponse>().ProducesStandardProblems();
        workspace.MapPut("/preferences", SaveAsync).WithName("SaveWorkspacePreferences").WithMetadata(FinancialWorkspaceMetadata.Instance).Produces<WorkspaceStateResponse>().ProducesStandardProblems();
        workspace.MapPost("/recent", RecordRecentAsync).WithName("RecordWorkspaceRecent").WithMetadata(FinancialWorkspaceMetadata.Instance).Produces(StatusCodes.Status204NoContent).ProducesStandardProblems();
    }
    private static WorkspaceScope Scope(HttpContext context, Guid companyId, Guid branchId)
    {
        // The selected annual workspace (empty on central-only endpoints that carry no workspace metadata).
        _ = Guid.TryParse(context.Request.Headers["X-Petra-Workspace"], out var workspace);
        return new(context.RequireSession().UserId, companyId, branchId, workspace);
    }
    private static async Task<IResult> CatalogAsync(Guid companyId, Guid branchId, IWorkspaceService service, HttpContext context, CancellationToken cancellationToken) =>
        TypedResults.Ok((await service.CatalogAsync(Scope(context, companyId, branchId), cancellationToken).ConfigureAwait(false))
            .Select(command => new WorkspaceCommandResponse(command.Id, command.LabelArabic, command.LabelEnglish, (Contracts.Productivity.SearchCategory)(command.Category ?? Category.All))).ToArray());
    private static async Task<IResult> SearchAsync(Guid companyId, Guid branchId, string? search, Contracts.Productivity.SearchCategory? category,
        Contracts.Productivity.SearchOrder? order, bool? descending, int? page, int? pageSize,
        IWorkspaceService service, HttpContext context, CancellationToken cancellationToken)
    {
        var result = await service.SearchAsync(Scope(context, companyId, branchId), new SearchQuery(search, (Category?)category,
            (Order)(order ?? Contracts.Productivity.SearchOrder.Code), descending ?? false, PageRequest.Create(page, pageSize)), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new SearchPageResponse(result.Items.Select(Hit).ToArray(), result.Page, result.PageSize, result.TotalCount));
    }
    private static async Task<IResult> PreviewAsync(Guid companyId, Guid branchId, Contracts.Productivity.SearchCategory category, Guid entityId, int? linePage,
        IWorkspaceService service, HttpContext context, CancellationToken cancellationToken)
    {
        var result = await service.PreviewAsync(Scope(context, companyId, branchId), (Category)category, entityId, linePage ?? 1, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new EntityPreviewResponse(Hit(result.Entity), result.Fields.Select(field => new PreviewFieldResponse(field.LabelArabic, field.LabelEnglish, field.Value)).ToArray(),
            result.Lines.Select(line => new PreviewLineResponse(line.Account, line.Description, line.Currency, line.Debit, line.Credit)).ToArray(),
            result.LinePage, result.LineCount, result.BaseDebit, result.BaseCredit, result.BaseCurrency));
    }
    private static async Task<IResult> LoadAsync(Guid companyId, Guid branchId, IWorkspaceService service, HttpContext context, CancellationToken cancellationToken)
    {
        var result = await service.LoadAsync(Scope(context, companyId, branchId), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(State(result));
    }
    private static async Task<IResult> SaveAsync(Guid companyId, Guid branchId, SaveWorkspaceRequest request,
        IWorkspaceService service, HttpContext context, CancellationToken cancellationToken)
    {
        if (request.Preferences is not { FavoriteCommands: not null, FavoriteReports: not null, Grids: not null } preferences
            || request.Version is null || preferences.Grids.Any(grid => grid is null || grid.Columns is null || grid.Columns.Any(column => column is null)))
            throw new RequestValidationException("workspace.preferences_invalid", "Preferences are required.", null);
        var model = new WorkspacePreferences(preferences.Language, preferences.Theme, preferences.FavoriteCommands, preferences.FavoriteReports,
            preferences.Grids.Select(grid => new GridPreference(grid.Key, grid.Columns.Select(column => new GridColumnPreference(column.Key, column.Width, column.Order, column.Visible)).ToArray())).ToArray());
        _ = await service.SaveAsync(Scope(context, companyId, branchId), model, request.Version, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(State(await service.LoadAsync(Scope(context, companyId, branchId), cancellationToken).ConfigureAwait(false)));
    }
    private static async Task<IResult> RecordRecentAsync(Guid companyId, Guid branchId, RecordRecentRequest request,
        IWorkspaceService service, HttpContext context, CancellationToken cancellationToken)
    {
        await service.RecordRecentAsync(Scope(context, companyId, branchId), (Category)request.Category, request.EntityId, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
    private static SearchHitResponse Hit(SearchHit hit) => new(hit.Id, (Contracts.Productivity.SearchCategory)hit.Category, hit.Code, hit.Name, hit.Status, hit.Date);
    private static WorkspaceStateResponse State(WorkspaceState state) => new(new WorkspacePreferencesRequest(state.Preferences.Language, state.Preferences.Theme,
        state.Preferences.FavoriteCommands, state.Preferences.FavoriteReports, state.Preferences.Grids.Select(grid => new GridPreferenceRequest(grid.Key,
            grid.Columns.Select(column => new GridColumnPreferenceRequest(column.Key, column.Width, column.Order, column.Visible)).ToArray())).ToArray()), state.Recent.Select(Hit).ToArray(), state.Version);
}
