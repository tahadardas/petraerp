using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.DocumentNumbers;
using PetraERP.Application.Security;
using PetraERP.Contracts.DocumentNumbers;

namespace PetraERP.Api.Endpoints;

/// <summary>
/// Document-number maintenance: preview (dry run with collisions), request, approval by a different person, rejection,
/// cancellation and the hash-chained audit with its verification. Approval rewrites only the number text of the document.
/// </summary>
internal static class DocumentNumberEndpoints
{
    internal static void MapDocumentNumberEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/document-numbers").WithTags("Document number maintenance");
        var view = PetraPolicies.Permission(PermissionCatalog.DocumentNumbersView);
        var request = PetraPolicies.Permission(PermissionCatalog.DocumentNumbersRequest);
        var approve = PetraPolicies.Permission(PermissionCatalog.DocumentNumbersApprove);

        group.MapGet("/preview", PreviewAsync).WithName("PreviewDocumentNumberChange").RequireAuthorization(view)
            .WithSummary("Dry run: whether a document may take the proposed number, with the documents or pending requests that collide. Writes nothing.")
            .Produces<DocumentNumberPreviewResponse>().ProducesStandardProblems();
        group.MapGet("/requests", ListAsync).WithName("ListDocumentNumberChanges").RequireAuthorization(view)
            .Produces<DocumentNumberChangeListResponse>().ProducesStandardProblems();
        group.MapPost("/requests", RequestAsync).WithName("RequestDocumentNumberChange").RequireAuthorization(request).Validate<RequestDocumentNumberChangeRequest>()
            .Produces<DocumentNumberChangeDto>(StatusCodes.Status201Created).ProducesStandardProblems();
        group.MapPost("/requests/{requestId:guid}/cancel", CancelAsync).WithName("CancelDocumentNumberChange").RequireAuthorization(request).Validate<DocumentNumberDecisionRequest>()
            .WithSummary("The requester withdraws a pending request.").Produces<DocumentNumberChangeDto>().ProducesStandardProblems();
        group.MapPost("/requests/{requestId:guid}/approve", ApproveAsync).WithName("ApproveDocumentNumberChange").RequireAuthorization(approve).Validate<DocumentNumberDecisionRequest>()
            .WithSummary("Applies the change. The approver must differ from the requester; the document keeps its id, type, status and journal link.")
            .Produces<DocumentNumberChangeDto>().ProducesStandardProblems();
        group.MapPost("/requests/{requestId:guid}/reject", RejectAsync).WithName("RejectDocumentNumberChange").RequireAuthorization(approve).Validate<DocumentNumberDecisionRequest>()
            .Produces<DocumentNumberChangeDto>().ProducesStandardProblems();
        group.MapGet("/audit", AuditAsync).WithName("ListDocumentNumberAudit").RequireAuthorization(view)
            .Produces<DocumentNumberAuditResponse>().ProducesStandardProblems();
        group.MapGet("/audit/verify", VerifyAsync).WithName("VerifyDocumentNumberAuditChain").RequireAuthorization(view)
            .WithSummary("Recomputes the audit hash chain of the company-year database.").Produces<DocumentNumberChainVerificationResponse>().ProducesStandardProblems();
    }

    private static async Task<IResult> PreviewAsync(Guid companyId, Guid branchId, string? family, Guid? documentId, string? number, IDocumentNumberMaintenanceService service,
        CancellationToken ct)
    {
        if (documentId is null || string.IsNullOrWhiteSpace(number)) throw new RequestValidationException("docnum.query_invalid", "'family', 'documentId' and 'number' are required.", "documentId");
        var r = await service.PreviewAsync(companyId, branchId, Family(family), documentId.Value, number, ct).ConfigureAwait(false);
        return TypedResults.Ok(new DocumentNumberPreviewResponse
        {
            Family = r.Family.ToString(), DocumentId = r.DocumentId, CurrentNumber = r.CurrentNumber, ProposedNumber = r.ProposedNumber, CanRequest = r.CanRequest,
            BlockingCode = r.BlockingCode, Collisions = r.Collisions.Select(c => new DocumentNumberCollisionDto { Family = c.Family.ToString(), DocumentId = c.DocumentId, Kind = c.Kind }).ToList(),
        });
    }

    private static async Task<IResult> ListAsync(Guid companyId, Guid branchId, string? state, int? take, IDocumentNumberMaintenanceService service, CancellationToken ct)
    {
        DocumentNumberChangeState? parsed = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!Enum.TryParse<DocumentNumberChangeState>(state, ignoreCase: false, out var s) || !Enum.IsDefined(s))
                throw new RequestValidationException("docnum.state_invalid", "Unknown state.", "state");
            parsed = s;
        }
        var rows = await service.ListAsync(companyId, branchId, parsed, take ?? 50, ct).ConfigureAwait(false);
        return TypedResults.Ok(new DocumentNumberChangeListResponse { Items = rows.Select(Map).ToList() });
    }

    private static async Task<IResult> RequestAsync(Guid companyId, Guid branchId, [FromBody] RequestDocumentNumberChangeRequest body, IDocumentNumberMaintenanceService service,
        HttpContext http, CancellationToken ct)
    {
        var r = await service.RequestAsync(companyId, branchId, http.RequireSession().UserId,
            new RequestDocumentNumberChangeCommand(Family(body.Family), body.DocumentId, body.NewNumber, body.Reason), ct).ConfigureAwait(false);
        return TypedResults.Created($"/api/v1/companies/{companyId:D}/branches/{branchId:D}/document-numbers/requests/{r.Id:D}", Map(r));
    }

    private static async Task<IResult> ApproveAsync(Guid companyId, Guid branchId, Guid requestId, [FromBody] DocumentNumberDecisionRequest body,
        IDocumentNumberMaintenanceService service, HttpContext http, CancellationToken ct) =>
        TypedResults.Ok(Map(await service.ApproveAsync(companyId, branchId, http.RequireSession().UserId, requestId, new DocumentNumberDecisionCommand(body.Reason), ct).ConfigureAwait(false)));

    private static async Task<IResult> RejectAsync(Guid companyId, Guid branchId, Guid requestId, [FromBody] DocumentNumberDecisionRequest body,
        IDocumentNumberMaintenanceService service, HttpContext http, CancellationToken ct) =>
        TypedResults.Ok(Map(await service.RejectAsync(companyId, branchId, http.RequireSession().UserId, requestId, new DocumentNumberDecisionCommand(body.Reason), ct).ConfigureAwait(false)));

    private static async Task<IResult> CancelAsync(Guid companyId, Guid branchId, Guid requestId, [FromBody] DocumentNumberDecisionRequest body,
        IDocumentNumberMaintenanceService service, HttpContext http, CancellationToken ct) =>
        TypedResults.Ok(Map(await service.CancelAsync(companyId, branchId, http.RequireSession().UserId, requestId, new DocumentNumberDecisionCommand(body.Reason), ct).ConfigureAwait(false)));

    private static async Task<IResult> AuditAsync(Guid companyId, Guid branchId, int? take, IDocumentNumberMaintenanceService service, CancellationToken ct)
    {
        var rows = await service.AuditAsync(companyId, branchId, take ?? 100, ct).ConfigureAwait(false);
        return TypedResults.Ok(new DocumentNumberAuditResponse
        {
            Items = rows.Select(a => new DocumentNumberAuditDto
            {
                Sequence = a.Sequence, RequestId = a.RequestId, Family = a.Family.ToString(), DocumentId = a.DocumentId, OldNumber = a.OldNumber, NewNumber = a.NewNumber,
                RequestedByUserId = a.RequestedByUserId, ApprovedByUserId = a.ApprovedByUserId, OccurredAtUtc = a.OccurredAtUtc, PreviousHash = a.PreviousHash, Hash = a.Hash,
            }).ToList(),
        });
    }

    private static async Task<IResult> VerifyAsync(Guid companyId, Guid branchId, IDocumentNumberMaintenanceService service, CancellationToken ct)
    {
        var r = await service.VerifyAuditChainAsync(companyId, ct).ConfigureAwait(false);
        return TypedResults.Ok(new DocumentNumberChainVerificationResponse { Intact = r.Intact, EntriesChecked = r.EntriesChecked, FirstBrokenSequence = r.FirstBrokenSequence });
    }

    private static DocumentNumberFamily Family(string? value) =>
        Enum.TryParse<DocumentNumberFamily>(value, ignoreCase: false, out var f) && Enum.IsDefined(f)
            ? f : throw new RequestValidationException("docnum.family_invalid", "Family must be SalesInvoice, PurchaseInvoice or Voucher.", "family");

    private static DocumentNumberChangeDto Map(DocumentNumberChangeResult r) => new()
    {
        Id = r.Id, Family = r.Family.ToString(), DocumentId = r.DocumentId, OldNumber = r.OldNumber, NewNumber = r.NewNumber, Reason = r.Reason, State = r.State.ToString(),
        RequestedByUserId = r.RequestedByUserId, RequestedAtUtc = r.RequestedAtUtc, DecidedByUserId = r.DecidedByUserId, DecidedAtUtc = r.DecidedAtUtc, DecisionReason = r.DecisionReason,
    };
}
