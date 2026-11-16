using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Security;
using PetraERP.Contracts.Ledger;
using PetraERP.Domain.Primitives;
using ContractOperation = PetraERP.Contracts.Ledger.PostingOperation;

namespace PetraERP.Api.Endpoints;

/// <summary>
/// HTTP surface of the existing durable posting engine (SqlPostingService). The Idempotency-Key header is the
/// posting request's key; route company/branch, the authenticated user, journal ids, versions and reason are
/// bound into the existing fingerprint. No second idempotency mechanism exists.
/// </summary>
internal static partial class LedgerPostingEndpoints
{
    internal const string IdempotencyHeader = "Idempotency-Key";

    internal static void MapLedgerPostingEndpoints(this RouteGroupBuilder api)
    {
        var journals = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/ledger/journals")
            .WithTags("General ledger: posting");

        journals.MapPost("/{journalId:guid}/post", PostJournalAsync)
            .RetryOnDeadlock()
            .WithName("PostJournal")
            .WithSummary("Posts an approved journal atomically. Replaying the same Idempotency-Key and body returns the same receipt.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.JournalsPost))
            .Validate<PostJournalRequest>()
            .Produces<PostingReceiptResponse>()
            .ProducesStandardProblems()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        journals.MapPost("/{journalId:guid}/post-reversal", PostJournalReversalAsync)
            .RetryOnDeadlock()
            .WithName("PostJournalReversal")
            .WithSummary("Posts an approved reversal journal and marks the original as reversed in one transaction.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.JournalsReverse))
            .Validate<PostJournalReversalRequest>()
            .Produces<PostingReceiptResponse>()
            .ProducesStandardProblems()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> PostJournalAsync(Guid companyId, Guid branchId, Guid journalId,
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey, PostJournalRequest request, IPostingService posting,
        HttpContext httpContext, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        var key = RequireKey(idempotencyKey);
        var scope = PostingRequestScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var postingRequest = PostingRequest.Post(scope, key, journalId, RequireJournalVersion(request.ExpectedVersion, "expectedVersion"),
            RequireReason(request.Reason));
        return await ExecuteAsync(posting, postingRequest, httpContext, loggers, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> PostJournalReversalAsync(Guid companyId, Guid branchId, Guid journalId,
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey, PostJournalReversalRequest request, IPostingService posting,
        HttpContext httpContext, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        var key = RequireKey(idempotencyKey);
        if (request.OriginalJournalId == Guid.Empty || request.OriginalJournalId == journalId)
        {
            throw new RequestValidationException("ledger.original_journal_invalid",
                "A different, nonempty original journal is required.", "originalJournalId");
        }

        var scope = PostingRequestScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var postingRequest = PostingRequest.Reverse(scope, key, journalId,
            RequireJournalVersion(request.ExpectedVersion, "expectedVersion"), request.OriginalJournalId,
            RequireJournalVersion(request.OriginalExpectedVersion, "originalExpectedVersion"), RequireReason(request.Reason));
        return await ExecuteAsync(posting, postingRequest, httpContext, loggers, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> ExecuteAsync(IPostingService posting, PostingRequest request, HttpContext httpContext,
        ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        PostingReceipt receipt;
        try
        {
            receipt = await posting.PostAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (PostingAuthorizationException)
        {
            return ApiProblems.Result(httpContext, StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden);
        }
        catch (InvalidOperationException exception)
            when (exception is not PostingConcurrencyException and not PostingIdempotencyConflictException and not PostingAuthorizationException)
        {
            // Accounting rejections carry a stable RuleViolation code (closed period, state, account, conflict).
            return Rejected(httpContext, loggers, request, exception);
        }
        catch (ArgumentException exception)
        {
            // Raised by the engine when a journal is outside the requested company/branch scope.
            return Rejected(httpContext, loggers, request, exception);
        }

        return TypedResults.Ok(new PostingReceiptResponse
        {
            IdempotencyKey = receipt.IdempotencyKey,
            Operation = receipt.Operation == PetraERP.Application.GeneralLedger.Posting.PostingOperation.PostJournal
                ? ContractOperation.PostJournal
                : ContractOperation.PostJournalReversal,
            CompanyId = receipt.Scope.CompanyId,
            BranchId = receipt.Scope.BranchId,
            JournalId = receipt.JournalId,
            OriginalJournalId = receipt.OriginalJournalId,
            FiscalYearId = receipt.PostingNumber.FiscalYearId,
            PostingSequence = receipt.PostingNumber.Sequence,
        });
    }

    private static IResult Rejected(HttpContext httpContext, ILoggerFactory loggers, PostingRequest request, Exception exception)
    {
        LogPostingRejected(loggers.CreateLogger(typeof(LedgerPostingEndpoints).FullName!), request.JournalId, exception);
        return LedgerProblems.Rejected(httpContext, exception, ApiErrorCodes.PostingRejected);
    }

    private static Guid RequireKey(string? value) =>
        Guid.TryParseExact(value, "D", out var key) && key != Guid.Empty
            ? key
            : throw new RequestValidationException(ApiErrorCodes.IdempotencyKeyInvalid,
                "The Idempotency-Key header must be a nonempty UUID in 8-4-4-4-12 format.", IdempotencyHeader);

    private static ExpectedJournalVersion RequireJournalVersion(string value, string field)
    {
        try
        {
            return ExpectedJournalVersion.FromBase64(value);
        }
        catch (FormatException)
        {
            throw new RequestValidationException(ApiErrorCodes.VersionInvalid,
                "The version must be the canonical 12-character Base64 value read from the server.", field);
        }
    }

    private static AuditReason RequireReason(string value) => AuditReason.Create(SecurityInput.RequireReason(value, "reason"));

    [LoggerMessage(EventId = 4100, Level = LogLevel.Warning, Message = "Posting of journal {JournalId} was rejected by the posting engine.")]
    private static partial void LogPostingRejected(ILogger logger, Guid journalId, Exception exception);
}
