using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.Posting;
using PetraERP.Contracts.Posting;

namespace PetraERP.Api.Endpoints;

internal static class PostingRequestStatusEndpoints
{
    internal static void MapPostingRequestStatusEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/companies/{companyId:guid}/branches/{branchId:guid}/posting-requests/{idempotencyKey}", GetAsync)
            .WithTags("Posting requests").WithMetadata(FinancialWorkspaceMetadata.Instance).RequireAuthorization(PetraPolicies.CompanyScope)
            .WithName("GetPostingRequestStatus")
            .WithSummary("What the server durably recorded for an Idempotency-Key of the caller: use it after a lost response instead of posting again.")
            .Produces<PostingRequestStatusResponse>().ProducesStandardProblems();
    }

    private static async Task<IResult> GetAsync(Guid companyId, Guid branchId, string idempotencyKey, IPostingRequestStatusService service,
        HttpContext http, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw new RequestValidationException("posting_request.key_invalid", "The idempotency key must be 1 to 128 characters.", "idempotencyKey");
        var status = await service.GetAsync(companyId, branchId, http.RequireSession().UserId, idempotencyKey, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("posting request");
        return TypedResults.Ok(new PostingRequestStatusResponse
        {
            IdempotencyKey = status.IdempotencyKey,
            Kind = (PostingRequestKindCode)status.Kind,
            State = (PostingRequestStateCode)status.State,
            RecommendedAction = status.State == PostingRequestState.Completed ? PostingRecommendedAction.DoNotRepost : PostingRecommendedAction.RetryWithSameKey,
            DocumentId = status.DocumentId,
            OutcomeJournalId = status.OutcomeJournalId,
            RegisteredAtUtc = status.RegisteredAtUtc,
            CompletedAtUtc = status.CompletedAtUtc,
        });
    }
}
