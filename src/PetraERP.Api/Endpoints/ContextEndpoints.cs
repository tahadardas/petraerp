using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Security;
using PetraERP.Contracts.Context;

namespace PetraERP.Api.Endpoints;

internal static class ContextEndpoints
{
    internal static void MapContextEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/me").WithTags("Current user");

        group.MapGet("", GetCurrentUserAsync)
            .WithName("GetCurrentUser")
            .WithSummary("Returns the authenticated user's profile, session and granted permission codes.")
            .RequireAuthorization(PetraPolicies.CredentialMaintenance)
            .Produces<CurrentUserResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/companies", ListMyCompaniesAsync)
            .WithName("ListMyCompanies")
            .WithSummary("Lists active companies the user is explicitly scoped to.")
            .RequireAuthorization()
            .Produces<CompanyContextListResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/companies/{companyId:guid}/branches", ListMyBranchesAsync)
            .WithName("ListMyBranches")
            .WithSummary("Lists active branches the user is explicitly scoped to inside an authorized company.")
            .RequireAuthorization(PetraPolicies.CompanyScope)
            .Produces<BranchContextListResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> GetCurrentUserAsync(IUserContextQueries queries, HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = httpContext.RequireSession();
        var profile = await queries.GetProfileAsync(session.UserId, session.SessionId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return ApiProblems.Result(httpContext, StatusCodes.Status401Unauthorized, ApiErrorCodes.SessionRevoked);
        }

        return TypedResults.Ok(new CurrentUserResponse
        {
            UserId = profile.UserId,
            UserName = profile.UserName,
            DisplayNameArabic = profile.DisplayNameArabic,
            DisplayNameEnglish = profile.DisplayNameEnglish,
            MustChangePassword = profile.MustChangePassword,
            SessionId = profile.SessionId,
            SessionExpiresAtUtc = profile.SessionExpiresAtUtc,
            Permissions = profile.Permissions,
        });
    }

    private static async Task<IResult> ListMyCompaniesAsync(IUserContextQueries queries, HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = httpContext.RequireSession();
        var companies = await queries.GetCompaniesAsync(session.UserId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new CompanyContextListResponse
        {
            Items = companies.Select(static company => new CompanyContextResponse
            {
                Id = company.Id,
                Code = company.Code,
                NameArabic = company.NameArabic,
                NameEnglish = company.NameEnglish,
                BaseCurrencyCode = company.BaseCurrencyCode,
            }).ToArray(),
        });
    }

    private static async Task<IResult> ListMyBranchesAsync(Guid companyId, IUserContextQueries queries, HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = httpContext.RequireSession();
        var branches = await queries.GetBranchesAsync(session.UserId, companyId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new BranchContextListResponse
        {
            Items = branches.Select(static branch => new BranchContextResponse
            {
                Id = branch.Id,
                CompanyId = branch.CompanyId,
                Code = branch.Code,
                NameArabic = branch.NameArabic,
                NameEnglish = branch.NameEnglish,
            }).ToArray(),
        });
    }
}
