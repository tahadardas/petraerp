using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.Patterns;
using PetraERP.Application.Security;
using PetraERP.Contracts.Patterns;
using DomainPattern = PetraERP.Domain.Patterns;

namespace PetraERP.Api.Endpoints;

internal static class PatternEndpoints
{
    internal static void MapPatternEndpoints(this RouteGroupBuilder api)
    {
        var patterns = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/patterns")
            .WithTags("Document patterns");

        var view = PetraPolicies.Permission(PermissionCatalog.PatternsView);
        var manage = PetraPolicies.Permission(PermissionCatalog.PatternsManage);
        var publish = PetraPolicies.Permission(PermissionCatalog.PatternsPublish);

        patterns.MapGet(string.Empty, ListPatternsAsync)
            .WithName("ListDocumentPatterns")
            .WithSummary("Lists document patterns for company.")
            .RequireAuthorization(view)
            .Produces<DocumentPatternPageResponse>()
            .ProducesStandardProblems();

        patterns.MapGet("/runnable", GetRunnablePatternsAsync)
            .WithName("GetRunnableDocumentPatterns")
            .WithSummary("Gets active, runnable patterns available for the specified branch.")
            .RequireAuthorization(view)
            .Produces<IReadOnlyList<DocumentPatternSummaryResponse>>()
            .ProducesStandardProblems();

        patterns.MapPost(string.Empty, CreatePatternAsync)
            .WithName("CreateDocumentPattern")
            .WithSummary("Creates a new document pattern with an initial draft revision.")
            .RequireAuthorization(manage)
            .Validate<CreateDocumentPatternRequest>()
            .Produces<DocumentPatternResponse>(StatusCodes.Status201Created)
            .ProducesStandardProblems();

        patterns.MapGet("/{patternId:guid}", GetPatternAsync)
            .WithName("GetDocumentPattern")
            .WithSummary("Gets document pattern details including active revision.")
            .RequireAuthorization(view)
            .Produces<DocumentPatternResponse>()
            .ProducesStandardProblems();

        patterns.MapPut("/{patternId:guid}", UpdatePatternAsync)
            .WithName("UpdateDocumentPattern")
            .WithSummary("Updates pattern metadata and branch grants.")
            .RequireAuthorization(manage)
            .Validate<UpdateDocumentPatternRequest>()
            .Produces<DocumentPatternResponse>()
            .ProducesStandardProblems();

        patterns.MapPost("/{patternId:guid}/revisions", CreateRevisionAsync)
            .WithName("CreatePatternRevision")
            .WithSummary("Creates a new draft revision for a pattern.")
            .RequireAuthorization(manage)
            .Validate<CreatePatternRevisionRequest>()
            .Produces<PatternRevisionResponse>(StatusCodes.Status201Created)
            .ProducesStandardProblems();

        patterns.MapPost("/{patternId:guid}/publish", PublishRevisionAsync)
            .WithName("PublishPatternRevision")
            .WithSummary("Publishes a pattern revision, making it the active immutable configuration.")
            .RequireAuthorization(publish)
            .Validate<PublishPatternRevisionRequest>()
            .Produces<DocumentPatternResponse>()
            .ProducesStandardProblems();

        patterns.MapPost("/{patternId:guid}/retire", RetirePatternAsync)
            .WithName("RetireDocumentPattern")
            .WithSummary("Retires a pattern and marks its active revision retired.")
            .RequireAuthorization(manage)
            .Validate<RetirePatternRequest>()
            .Produces<DocumentPatternResponse>()
            .ProducesStandardProblems();

        patterns.MapPost("/{patternId:guid}/next-number", GenerateNextNumberAsync)
            .WithName("GenerateNextPatternNumber")
            .WithSummary("Allocates and returns the next atomic document number according to pattern policy.")
            .RequireAuthorization(view)
            .Produces<NextNumberResponse>()
            .ProducesStandardProblems();

        patterns.MapGet("/{patternId:guid}/next-number-preview", PreviewNextNumberAsync)
            .WithName("PreviewNextPatternNumber")
            .WithSummary("Reads the number the pattern would issue next without reserving it; an estimate only, allocation stays server-side at save.")
            .RequireAuthorization(view)
            .Produces<NextNumberPreviewResponse>()
            .ProducesStandardProblems();
    }

    private static async Task<IResult> ListPatternsAsync(
        Guid companyId,
        Guid branchId,
        [FromQuery] BusinessOperationKind? kind,
        [FromQuery] bool? activeOnly,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        IPatternService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = PatternScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var pageReq = PageRequest.Create(page <= 0 ? 1 : page, pageSize <= 0 ? 50 : pageSize);
        var domainKind = kind.HasValue ? (DomainPattern.BusinessOperationKind?)kind.Value : null;

        var result = await service.ListPatternsAsync(scope, domainKind, activeOnly, pageReq, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new DocumentPatternPageResponse
        {
            Items = result.Items.Select(PatternMapping.ToResponse).ToList(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
        });
    }

    private static async Task<IResult> GetRunnablePatternsAsync(
        Guid companyId,
        Guid branchId,
        IPatternService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = PatternScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var result = await service.GetRunnablePatternsForBranchAsync(scope, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(result.Select(PatternMapping.ToResponse).ToList());
    }

    private static async Task<IResult> CreatePatternAsync(
        Guid companyId,
        Guid branchId,
        [FromBody] CreateDocumentPatternRequest request,
        IPatternService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = PatternScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var command = new CreateDocumentPatternCommand
        {
            OperationKind = (DomainPattern.BusinessOperationKind)request.OperationKind,
            Code = request.Code,
            NameArabic = request.NameArabic,
            NameEnglish = request.NameEnglish,
            ShortCode = request.ShortCode,
            Icon = request.Icon,
            Group = request.Group,
            IsDefault = request.IsDefault,
            BranchIds = request.BranchIds,
            InitialConfiguration = request.InitialConfiguration?.ToDomain(),
        };

        var details = await service.CreatePatternAsync(scope, command, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/companies/{companyId}/branches/{branchId}/patterns/{details.Id}", details.ToResponse());
    }

    private static async Task<IResult> GetPatternAsync(
        Guid companyId,
        Guid branchId,
        Guid patternId,
        IPatternService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = PatternScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var details = await service.GetPatternAsync(scope, patternId, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(details.ToResponse());
    }

    private static async Task<IResult> UpdatePatternAsync(
        Guid companyId,
        Guid branchId,
        Guid patternId,
        [FromBody] UpdateDocumentPatternRequest request,
        IPatternService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = PatternScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var version = RequireConcurrencyToken(request.ExpectedVersion, nameof(request.ExpectedVersion));

        var command = new UpdateDocumentPatternCommand
        {
            ExpectedVersion = version,
            NameArabic = request.NameArabic,
            NameEnglish = request.NameEnglish,
            ShortCode = request.ShortCode,
            Icon = request.Icon,
            Group = request.Group,
            IsActive = request.IsActive,
            IsDefault = request.IsDefault,
            BranchIds = request.BranchIds,
        };

        var details = await service.UpdatePatternAsync(scope, patternId, command, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(details.ToResponse());
    }

    private static async Task<IResult> CreateRevisionAsync(
        Guid companyId,
        Guid branchId,
        Guid patternId,
        [FromBody] CreatePatternRevisionRequest request,
        IPatternService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = PatternScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var version = RequireConcurrencyToken(request.ExpectedVersion, nameof(request.ExpectedVersion));

        var command = new CreatePatternRevisionCommand
        {
            ExpectedVersion = version,
            Configuration = request.Configuration.ToDomain(),
            Reason = request.Reason,
        };

        var revision = await service.CreateRevisionAsync(scope, patternId, command, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/companies/{companyId}/branches/{branchId}/patterns/{patternId}/revisions/{revision.Id}", revision.ToResponse());
    }

    private static async Task<IResult> PublishRevisionAsync(
        Guid companyId,
        Guid branchId,
        Guid patternId,
        [FromBody] PublishPatternRevisionRequest request,
        IPatternService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = PatternScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var version = RequireConcurrencyToken(request.ExpectedVersion, nameof(request.ExpectedVersion));

        var command = new PublishPatternRevisionCommand
        {
            ExpectedVersion = version,
            RevisionId = request.RevisionId,
            Reason = request.Reason,
        };

        var details = await service.PublishRevisionAsync(scope, patternId, command, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(details.ToResponse());
    }

    private static async Task<IResult> RetirePatternAsync(
        Guid companyId,
        Guid branchId,
        Guid patternId,
        [FromBody] RetirePatternRequest request,
        IPatternService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = PatternScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var version = RequireConcurrencyToken(request.ExpectedVersion, nameof(request.ExpectedVersion));

        var command = new RetirePatternCommand
        {
            ExpectedVersion = version,
            Reason = request.Reason,
        };

        var details = await service.RetirePatternAsync(scope, patternId, command, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(details.ToResponse());
    }

    private static async Task<IResult> GenerateNextNumberAsync(
        Guid companyId,
        Guid branchId,
        Guid patternId,
        [FromQuery] Guid? fiscalYearId,
        [FromQuery] DateOnly? accountingDate,
        [FromQuery] Guid? allocationKey,
        IPatternService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = PatternScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var command = new GetNextPatternNumberCommand
        {
            PatternId = patternId,
            FiscalYearId = fiscalYearId, AccountingDate = accountingDate, AllocationKey = allocationKey,
        };

        var result = await service.GenerateNextNumberAsync(scope, command, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new NextNumberResponse
        {
            SequenceNumber = result.SequenceNumber,
            FormattedNumber = result.FormattedNumber,
        });
    }

    private static async Task<IResult> PreviewNextNumberAsync(
        Guid companyId,
        Guid branchId,
        Guid patternId,
        [FromQuery] Guid? fiscalYearId,
        [FromQuery] DateOnly? accountingDate,
        [FromQuery] Guid? revisionId,
        IPatternService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = PatternScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var command = new GetNextPatternNumberCommand
        {
            PatternId = patternId,
            FiscalYearId = fiscalYearId,
            AccountingDate = accountingDate,
            RevisionId = revisionId,
        };

        var result = await service.PreviewNextNumberAsync(scope, command, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new NextNumberPreviewResponse
        {
            IsEstimate = result.IsAvailable,
            SequenceNumber = result.SequenceNumber,
            FormattedNumber = result.FormattedNumber,
            UnavailableReason = result.UnavailableReason,
        });
    }

    private static ConcurrencyToken RequireConcurrencyToken(string value, string field) =>
        ConcurrencyToken.TryParse(value, out var token)
            ? token
            : throw new RequestValidationException(ApiErrorCodes.VersionInvalid,
                "The version must be the canonical 12-character Base64 value read from the server.", field);
}
