using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Branches;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Contracts.Branches;

namespace PetraERP.Api.Endpoints;

/// <summary>
/// Branch administration of one company-year: card, tree, activation (never deletion) and the account mappings
/// (cash, bank, sales, inventory, ...) that posting uses for the branch. Changes need <c>System.Branches.Manage</c>.
/// </summary>
internal static class BranchAdminEndpoints
{
    internal static void MapBranchAdminEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/branch-admin").WithTags("Branch administration");
        var view = PetraPolicies.Permission(PermissionCatalog.BranchesView);
        var manage = PetraPolicies.Permission(PermissionCatalog.BranchesManage);

        group.MapGet("", ListAsync).WithName("ListCompanyBranches").RequireAuthorization(view)
            .WithSummary("Branches of the company (active only unless includeInactive=true).")
            .Produces<BranchListResponse>().ProducesStandardProblems();
        group.MapGet("/tree", TreeAsync).WithName("GetCompanyBranchTree").RequireAuthorization(view)
            .Produces<BranchTreeResponse>().ProducesStandardProblems();
        group.MapGet("/{target:guid}", GetAsync).WithName("GetBranchCard").RequireAuthorization(view)
            .Produces<BranchCardDto>().ProducesStandardProblems();
        group.MapPost("", CreateAsync).WithName("CreateBranch").RequireAuthorization(manage).Validate<SaveBranchRequest>()
            .Produces<BranchCardDto>(StatusCodes.Status201Created).ProducesStandardProblems();
        group.MapPut("/{target:guid}", UpdateAsync).WithName("UpdateBranch").RequireAuthorization(manage).Validate<UpdateBranchRequest>()
            .Produces<BranchCardDto>().ProducesStandardProblems();
        group.MapPut("/{target:guid}/active", SetActiveAsync).WithName("SetBranchActive").RequireAuthorization(manage).Validate<SetBranchActiveRequest>()
            .WithSummary("Activates or deactivates a branch. A branch with active children or the last active branch cannot be deactivated.")
            .Produces<BranchCardDto>().ProducesStandardProblems();
        group.MapPost("/{target:guid}/sync-central", SyncAsync).WithName("SyncBranchToCentral").RequireAuthorization(manage)
            .WithSummary("Repeats the copy of the branch card to the central database (safe to repeat).")
            .Produces(StatusCodes.Status204NoContent).ProducesStandardProblems();
        group.MapGet("/{target:guid}/account-links", ListLinksAsync).WithName("ListBranchAccountLinks").RequireAuthorization(view)
            .Produces<BranchAccountLinkListResponse>().ProducesStandardProblems();
        group.MapPut("/{target:guid}/account-links", SetLinkAsync).WithName("SetBranchAccountLink").RequireAuthorization(manage).Validate<SetBranchAccountLinkRequest>()
            .WithSummary("Maps a role to an account from a date; the previous mapping of that role ends the day before.")
            .Produces<BranchAccountLinkDto>().ProducesStandardProblems();
        group.MapPost("/{target:guid}/sub-accounts/apply", ApplySubAccountsAsync).WithName("ApplyBranchSubAccounts")
            .RequireAuthorization(manage).RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.AccountsManage)).Validate<ApplyBranchSubAccountsRequest>()
            .WithSummary("Creates the branch's sub-accounts under the model branch's parents and maps them from the given date. Idempotent per (parent, branch code).")
            .Produces<BranchSubAccountApplyResponse>().ProducesStandardProblems();
        group.MapGet("/{target:guid}/sub-account-preview", PreviewAsync).WithName("PreviewBranchSubAccounts").RequireAuthorization(view)
            .WithSummary("Preview of sub-accounts the wizard would create for the branch, modelled on another branch. Creates nothing.")
            .Produces<BranchSubAccountPreviewResponse>().ProducesStandardProblems();
    }

    private static async Task<IResult> ListAsync(Guid companyId, Guid branchId, bool? includeInactive, IBranchAdminService service, CancellationToken ct) =>
        TypedResults.Ok(new BranchListResponse { Items = (await service.ListAsync(companyId, includeInactive ?? false, ct).ConfigureAwait(false)).Select(Map).ToList() });

    private static async Task<IResult> TreeAsync(Guid companyId, Guid branchId, bool? includeInactive, IBranchAdminService service, CancellationToken ct) =>
        TypedResults.Ok(new BranchTreeResponse { Roots = (await service.GetTreeAsync(companyId, includeInactive ?? false, ct).ConfigureAwait(false)).Select(Map).ToList() });

    private static async Task<IResult> GetAsync(Guid companyId, Guid branchId, Guid target, IBranchAdminService service, CancellationToken ct) =>
        TypedResults.Ok(Map(await service.GetAsync(companyId, target, ct).ConfigureAwait(false)));

    private static async Task<IResult> CreateAsync(Guid companyId, Guid branchId, [FromBody] SaveBranchRequest request, IBranchAdminService service, HttpContext http, CancellationToken ct)
    {
        var created = Map(await service.CreateAsync(companyId, http.RequireSession().UserId, ToCommand(request), ct).ConfigureAwait(false));
        return TypedResults.Created($"/api/v1/companies/{companyId:D}/branches/{branchId:D}/branch-admin/{created.Id:D}", created);
    }

    private static async Task<IResult> UpdateAsync(Guid companyId, Guid branchId, Guid target, [FromBody] UpdateBranchRequest request, IBranchAdminService service,
        HttpContext http, CancellationToken ct) =>
        TypedResults.Ok(Map(await service.UpdateAsync(companyId, target, http.RequireSession().UserId,
            new UpdateBranchCommand(ToCommand(request), ApiRequestGuards.RequireVersion(request.Version), request.Reason), ct).ConfigureAwait(false)));

    private static async Task<IResult> SetActiveAsync(Guid companyId, Guid branchId, Guid target, [FromBody] SetBranchActiveRequest request, IBranchAdminService service,
        HttpContext http, CancellationToken ct) =>
        TypedResults.Ok(Map(await service.SetActiveAsync(companyId, target, http.RequireSession().UserId,
            new SetBranchActiveCommand(request.IsActive, ApiRequestGuards.RequireVersion(request.Version), request.Reason), ct).ConfigureAwait(false)));

    private static async Task<IResult> SyncAsync(Guid companyId, Guid branchId, Guid target, IBranchAdminService service, CancellationToken ct)
    {
        await service.SyncToCentralAsync(companyId, target, ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListLinksAsync(Guid companyId, Guid branchId, Guid target, DateOnly? asOf, IBranchAdminService service, CancellationToken ct) =>
        TypedResults.Ok(new BranchAccountLinkListResponse { Items = (await service.ListAccountLinksAsync(companyId, target, asOf, ct).ConfigureAwait(false)).Select(Map).ToList() });

    private static async Task<IResult> SetLinkAsync(Guid companyId, Guid branchId, Guid target, [FromBody] SetBranchAccountLinkRequest request, IBranchAdminService service,
        HttpContext http, CancellationToken ct)
    {
        if (!Enum.TryParse<BranchAccountRole>(request.Role, ignoreCase: true, out var role) || !Enum.IsDefined(role))
            throw new RequestValidationException("branch.link_role_invalid", "Unknown account role.", "role");
        return TypedResults.Ok(Map(await service.SetAccountLinkAsync(companyId, target, http.RequireSession().UserId,
            new SetBranchAccountLinkCommand(role, request.AccountId, request.EffectiveFrom, request.Reason), ct).ConfigureAwait(false)));
    }

    private static async Task<IResult> PreviewAsync(Guid companyId, Guid branchId, Guid target, Guid? modelBranchId, IBranchAdminService service, CancellationToken ct)
    {
        if (modelBranchId is null)
            throw new RequestValidationException("branch.model_required", "'modelBranchId' is required.", "modelBranchId");
        var plans = await service.PreviewSubAccountsAsync(companyId, target, modelBranchId.Value, ct).ConfigureAwait(false);
        return TypedResults.Ok(new BranchSubAccountPreviewResponse
        {
            Items = plans.Select(p => new BranchSubAccountPlanDto
            {
                Role = p.Role.ToString(), ParentAccountId = p.ParentAccountId, ParentCode = p.ParentCode, SuggestedCode = p.SuggestedCode,
                NameArabic = p.NameArabic, NameEnglish = p.NameEnglish,
            }).ToList(),
        });
    }

    private static async Task<IResult> ApplySubAccountsAsync(Guid companyId, Guid branchId, Guid target, [FromBody] ApplyBranchSubAccountsRequest request,
        IBranchSubAccountService service, HttpContext http, CancellationToken ct)
    {
        var items = await service.ApplyAsync(companyId, target, http.RequireSession().UserId,
            new ApplyBranchSubAccountsCommand(request.ModelBranchId, request.EffectiveFrom, request.Reason), ct).ConfigureAwait(false);
        return TypedResults.Ok(new BranchSubAccountApplyResponse
        {
            Items = items.Select(i => new BranchSubAccountApplyItemDto { Role = i.Role.ToString(), AccountId = i.AccountId, AccountCode = i.AccountCode, AccountCreated = i.AccountCreated, LinkChanged = i.LinkChanged }).ToList(),
        });
    }

    private static SaveBranchCommand ToCommand(SaveBranchRequest r) => new(r.Code, r.NameArabic, r.NameEnglish, r.ParentBranchId, r.NumberPrefix, r.OpenedOn,
        r.Address, r.Phone, r.Email, r.Notes, r.DefaultWarehouseId, r.DefaultCashAccountId, r.DefaultBankAccountId, r.DefaultCostCenterId);

    private static BranchCardDto Map(BranchCardResult b) => new()
    {
        Id = b.Id, CompanyId = b.CompanyId, Code = b.Code, NameArabic = b.NameArabic, NameEnglish = b.NameEnglish, IsActive = b.IsActive,
        ParentBranchId = b.ParentBranchId, NumberPrefix = b.NumberPrefix, OpenedOn = b.OpenedOn, Address = b.Address, Phone = b.Phone, Email = b.Email,
        Notes = b.Notes, DefaultWarehouseId = b.DefaultWarehouseId, DefaultCashAccountId = b.DefaultCashAccountId, DefaultBankAccountId = b.DefaultBankAccountId,
        DefaultCostCenterId = b.DefaultCostCenterId, Version = ConcurrencyToken.FromBytes(b.RowVersion).ToBase64(), UpdatedAtUtc = b.UpdatedAtUtc,
    };

    private static BranchTreeNodeDto Map(BranchTreeNode n) => new() { Branch = Map(n.Branch), Depth = n.Depth, Children = n.Children.Select(Map).ToList() };

    private static BranchAccountLinkDto Map(BranchAccountLinkResult l) => new()
    {
        Id = l.Id, BranchId = l.BranchId, Role = l.Role.ToString(), AccountId = l.AccountId, AccountCode = l.AccountCode,
        AccountNameArabic = l.AccountNameArabic, EffectiveFrom = l.EffectiveFrom, EffectiveTo = l.EffectiveTo,
    };
}
