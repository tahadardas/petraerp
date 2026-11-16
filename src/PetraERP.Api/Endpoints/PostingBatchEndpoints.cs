using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Security;
using PetraERP.Application.Security;
using PetraERP.Contracts.DocumentProcessing;
using PetraERP.Contracts.Financial;
using AppBatchResult = PetraERP.Application.DocumentProcessing.BulkPostingBatchResult;
using AppDocumentReference = PetraERP.Application.DocumentProcessing.BulkPostingDocumentReference;
using AppDocumentType = PetraERP.Application.DocumentProcessing.BulkPostingDocumentType;
using AppFilter = PetraERP.Application.DocumentProcessing.BulkPostingFilter;
using AppItemResult = PetraERP.Application.DocumentProcessing.BulkPostingItemResult;
using AppItemStatus = PetraERP.Application.DocumentProcessing.BulkPostingItemStatus;
using AppService = PetraERP.Application.DocumentProcessing.IBulkDocumentPostingService;

namespace PetraERP.Api.Endpoints;

internal static class PostingBatchEndpoints
{
    internal static void MapPostingBatchEndpoints(this RouteGroupBuilder api)
    {
        var batches = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/posting-batches")
            .WithTags("PostingBatches")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.PostingBatchesManage));

        batches.MapPost("/preview", PreviewAsync)
            .WithName("PreviewPostingBatch")
            .WithSummary("Discovers and classifies unposted documents without changing any state.")
            .Validate<BulkPostingPreviewRequest>()
            .Produces<BulkPostingPreviewResponse>()
            .ProducesStandardProblems();

        batches.MapPost(string.Empty, StartAsync)
            .WithName("StartPostingBatch")
            .WithSummary("Persists a confirmed bulk posting request and posts its eligible documents one by one.")
            .Validate<StartBulkPostingRequest>()
            .Produces<BulkPostingBatchDto>()
            .ProducesStandardProblems();

        batches.MapGet("/{batchId:guid}", GetAsync)
            .WithName("GetPostingBatch")
            .WithSummary("Gets persisted totals and status for one batch.")
            .Produces<BulkPostingBatchDto>()
            .ProducesStandardProblems();

        batches.MapGet("/{batchId:guid}/results", ResultsAsync)
            .WithName("GetPostingBatchResults")
            .WithSummary("Gets per-document results with pagination and status filters.")
            .Produces<BulkPostingResultsResponse>()
            .ProducesStandardProblems();

        batches.MapPost("/{batchId:guid}/resume", ResumeAsync)
            .WithName("ResumePostingBatch")
            .WithSummary("Resumes a batch that was interrupted or report uncertain outcomes, reusing the same per-document keys.")
            .Produces<BulkPostingBatchDto>()
            .ProducesStandardProblems();
    }

    private static async Task<IResult> PreviewAsync(Guid companyId, Guid branchId,
        [FromBody] BulkPostingPreviewRequest request, AppService service, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await service.PreviewAsync(companyId, branchId, http.RequireSession().UserId,
            Filter(request.Filter), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new BulkPostingPreviewResponse
        {
            TotalExamined = result.TotalExamined,
            ReadyToPost = result.ReadyToPost,
            NeedsApproval = result.NeedsApproval,
            Ineligible = result.Ineligible,
            Classifications = result.Classifications
                .Select(c => new BulkPostingClassificationCountDto { Classification = (BulkPostingClassification)c.Classification, Count = c.Count })
                .ToList(),
            Items = result.Items.Select(i => new BulkPostingPreviewItemDto
            {
                DocumentType = (BulkPostingDocumentType)i.DocumentType,
                DocumentId = i.DocumentId,
                DocumentNumber = i.DocumentNumber,
                DocumentDateUtc = i.DocumentDateUtc,
                PartyName = i.PartyName,
                Amount = Money(i.Amount),
                CurrencyCode = i.CurrencyCode,
                CurrentStatus = i.CurrentStatus,
                Classification = (BulkPostingClassification)i.Classification,
                Postable = i.Postable,
                Reason = i.Reason,
                Version = i.Version,
            }).ToList(),
        });
    }

    private static async Task<IResult> StartAsync(Guid companyId, Guid branchId,
        [FromBody] StartBulkPostingRequest request, AppService service, HttpContext http, CancellationToken cancellationToken)
    {
        var command = new PetraERP.Application.DocumentProcessing.StartBulkPostingCommand
        {
            IdempotencyKey = request.IdempotencyKey,
            Filter = Filter(request.Filter),
            Documents = request.Documents is { Count: > 0 } documents
                ? documents.Select(d => new AppDocumentReference((AppDocumentType)d.DocumentType, d.DocumentId)).ToArray()
                : null,
            Confirmed = request.Confirmed,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? "ترحيل جماعي للمستندات" : request.Reason.Trim(),
        };
        var result = await service.StartAsync(companyId, branchId, http.RequireSession().UserId, command, cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(Map(result));
    }

    private static async Task<IResult> GetAsync(Guid companyId, Guid branchId, Guid batchId, AppService service,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(Map(await service.GetAsync(companyId, branchId, batchId, cancellationToken).ConfigureAwait(false)));

    private static async Task<IResult> ResultsAsync(Guid companyId, Guid branchId, Guid batchId, AppService service,
        CancellationToken cancellationToken, BulkPostingItemStatus? status = null, BulkPostingDocumentType? documentType = null,
        int page = 1, int pageSize = 50)
    {
        var result = await service.GetResultsAsync(companyId, branchId, batchId, status is { } s ? (AppItemStatus)s : null,
            documentType is { } t ? (AppDocumentType)t : null, page, pageSize, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new BulkPostingResultsResponse
        {
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            Batch = Map(result.Batch),
            Items = result.Items.Select(MapItem).ToList(),
        });
    }

    private static async Task<IResult> ResumeAsync(Guid companyId, Guid branchId, Guid batchId,
        [FromBody] ResumeBulkPostingRequest? request, AppService service, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await service.ResumeAsync(companyId, branchId, http.RequireSession().UserId, batchId,
            request?.RetryFailed ?? false, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(Map(result));
    }

    private static AppFilter Filter(BulkPostingFilterDto? dto)
    {
        var defaults = new AppFilter();
        return new AppFilter
        {
            DocumentTypes = dto?.DocumentTypes is { Count: > 0 } types
                ? types.Select(t => (AppDocumentType)t).Distinct().ToArray()
                : defaults.DocumentTypes,
            FromDateUtc = dto?.FromDateUtc,
            ToDateUtc = dto?.ToDateUtc,
            Search = dto?.Search,
        };
    }

    private static BulkPostingBatchDto Map(AppBatchResult r) => new()
    {
        BatchId = r.BatchId,
        Status = (BulkPostingBatchStatus)r.Status,
        CreatedAtUtc = r.CreatedAtUtc,
        StartedAtUtc = r.StartedAtUtc,
        CompletedAtUtc = r.CompletedAtUtc,
        TotalExamined = r.TotalExamined,
        AlreadyPosted = r.AlreadyPosted,
        Eligible = r.Eligible,
        Posted = r.Posted,
        Skipped = r.Skipped,
        Failed = r.Failed,
        Unknown = r.Unknown,
        Message = r.Message,
    };

    private static BulkPostingItemDto MapItem(AppItemResult r) => new()
    {
        ItemId = r.ItemId,
        DocumentType = (BulkPostingDocumentType)r.DocumentType,
        DocumentId = r.DocumentId,
        DocumentNumber = r.DocumentNumber,
        DocumentDateUtc = r.DocumentDateUtc,
        PartyName = r.PartyName,
        Amount = Money(r.Amount),
        CurrencyCode = r.CurrencyCode,
        PreviousStatus = r.PreviousStatus,
        NewStatus = r.NewStatus,
        JournalEntryId = r.JournalEntryId,
        PostingNumber = r.PostingNumber,
        Status = (BulkPostingItemStatus)r.Status,
        Classification = (BulkPostingClassification)r.Classification,
        Reason = r.Reason,
        FailureCode = r.FailureCode,
        AttemptCount = r.AttemptCount,
        LastAttemptAtUtc = r.LastAttemptAtUtc,
    };

    private static string? Money(decimal? value) =>
        value is { } amount ? FinancialDecimalText.Format(amount, FinancialDecimalText.MoneyScale) : null;
}
