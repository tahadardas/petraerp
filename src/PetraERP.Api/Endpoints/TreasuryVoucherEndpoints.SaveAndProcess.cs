using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.DocumentProcessing;
using PetraERP.Application.Security;
using PetraERP.Contracts.DocumentProcessing;

namespace PetraERP.Api.Endpoints;

internal static partial class TreasuryVoucherEndpoints
{
    private static void MapVoucherSaveAndProcessEndpoints(RouteGroupBuilder vouchers, string manage)
    {
        vouchers.MapPost("/save-and-process", SaveAndProcessCreateAsync)
            .RetryOnDeadlock().WithName("SaveAndProcessVoucher")
            .WithSummary("Saves a voucher and applies the company posting policy: draft, keep-draft (approve-only) or post.")
            .RequireAuthorization(manage).Validate<SaveAndProcessVoucherRequest>()
            .Produces<DocumentProcessingResultDto>(StatusCodes.Status201Created).ProducesStandardProblems();
        vouchers.MapPut("/{voucherId:guid}/save-and-process", SaveAndProcessUpdateAsync)
            .RetryOnDeadlock().WithName("SaveAndProcessUpdateVoucher")
            .WithSummary("Updates a draft voucher and applies the company posting policy.")
            .RequireAuthorization(manage).Validate<SaveAndProcessVoucherRequest>()
            .Produces<DocumentProcessingResultDto>().ProducesStandardProblems();
    }

    private static async Task<IResult> SaveAndProcessCreateAsync(Guid companyId, Guid branchId, [FromBody] SaveAndProcessVoucherRequest request,
        IDocumentSaveAndProcessService processing, IAccessEvaluator access, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (request.VoucherId is not null) return BadRequest("Use PUT /{voucherId}/save-and-process to update an existing voucher.");
        var (command, error) = Parse(request.Document);
        if (error is not null) return error;
        var session = httpContext.RequireSession();
        var permissions = await VoucherPermissionsAsync(access, session.UserId, companyId, branchId, cancellationToken).ConfigureAwait(false);
        var result = await processing.CreateVoucherAsync(companyId, branchId, session.UserId, permissions, command!, request.SaveAsDraft,
            new DocumentPostingIntent(request.IdempotencyKey, request.ManualNumber), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/companies/{companyId}/branches/{branchId}/treasury/vouchers/{result.DocumentId}", MapVoucherProcessing(result));
    }

    private static async Task<IResult> SaveAndProcessUpdateAsync(Guid companyId, Guid branchId, Guid voucherId, [FromBody] SaveAndProcessVoucherRequest request,
        IDocumentSaveAndProcessService processing, IAccessEvaluator access, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (request.VoucherId is not null && request.VoucherId != voucherId) return BadRequest("VoucherId in the body must match the route.");
        if (!ConcurrencyToken.TryParse(request.ExpectedVersion ?? request.Document.ExpectedVersion, out var version))
            return BadRequest("ExpectedVersion must be the Version returned by the server.");
        var (command, error) = Parse(request.Document);
        if (error is not null) return error;
        var session = httpContext.RequireSession();
        var permissions = await VoucherPermissionsAsync(access, session.UserId, companyId, branchId, cancellationToken).ConfigureAwait(false);
        var result = await processing.UpdateVoucherAsync(companyId, branchId, session.UserId, permissions, voucherId, command! with { ExpectedVersion = version },
            request.SaveAsDraft, new DocumentPostingIntent(request.IdempotencyKey, request.ManualNumber), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(MapVoucherProcessing(result));
    }

    // Vouchers have no approval step, so only the post grant matters; both flags carry the same server-evaluated post decision.
    private static async Task<DocumentProcessingPermissions> VoucherPermissionsAsync(IAccessEvaluator access, Guid userId, Guid companyId, Guid branchId,
        CancellationToken cancellationToken)
    {
        var canPost = await access.EvaluatePermissionAsync(userId, PermissionCatalog.Get(PermissionCatalog.TreasuryVouchersPost), companyId, branchId, cancellationToken)
            .ConfigureAwait(false) == AccessDecision.Granted;
        return new DocumentProcessingPermissions(canPost, canPost);
    }

    private static DocumentProcessingResultDto MapVoucherProcessing(DocumentProcessingResult r) => new()
    {
        DocumentType = r.DocumentType, DocumentId = r.DocumentId, DocumentNumber = r.DocumentNumber, Status = r.Status, Outcome = r.Outcome.ToString(),
        ApprovalApplied = r.ApprovalApplied, PostingApplied = r.PostingApplied, JournalEntryId = r.JournalEntryId,
        EffectivePostingMode = r.EffectivePostingMode, Version = r.Version, Message = r.Message, FailureCode = r.FailureCode,
    };
}
