using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.Security;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Endpoints;

/// <summary>
/// HTTP API surface for General Ledger journals: draft creation, revision, validation,
/// approval, reversal draft generation, deletion, and query operations.
/// </summary>
internal static class LedgerJournalEndpoints
{
    internal static void MapLedgerJournalEndpoints(this RouteGroupBuilder api)
    {
        var ledger = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/ledger")
            .WithTags("General ledger: journals");

        ledger.MapJournalVoucherHeader();

        var view = PetraPolicies.Permission(PermissionCatalog.JournalsView);
        var manage = PetraPolicies.Permission(PermissionCatalog.JournalsManage);
        var approve = PetraPolicies.Permission(PermissionCatalog.JournalsApprove);
        var accountsView = PetraPolicies.Permission(PermissionCatalog.AccountsView);

        ledger.MapPost("/journals/drafts", CreateJournalDraftAsync)
            .WithName("CreateJournalDraft")
            .WithSummary("Creates a new draft journal entry with lines.")
            .RequireAuthorization(manage)
            .Validate<CreateJournalDraftRequest>()
            .Produces<JournalDetailsResponse>(StatusCodes.Status201Created)
            .ProducesStandardProblems();

        ledger.MapGet("/journals/{journalId:guid}", GetJournalAsync)
            .WithName("GetJournal")
            .WithSummary("Gets journal details including lines and full audit trail.")
            .RequireAuthorization(view)
            .Produces<JournalDetailsResponse>()
            .ProducesStandardProblems();

        ledger.MapPut("/journals/drafts/{journalId:guid}", ReviseJournalDraftAsync)
            .WithName("ReviseJournalDraft")
            .WithSummary("Revises an existing journal draft, replacing its lines.")
            .RequireAuthorization(manage)
            .Validate<ReviseJournalDraftRequest>()
            .Produces<JournalDetailsResponse>()
            .ProducesStandardProblems();

        ledger.MapPost("/journals/drafts/{journalId:guid}/validate", ValidateJournalDraftAsync)
            .WithName("ValidateJournalDraft")
            .WithSummary("Validates a journal draft against chart of accounts, currency policies and balancing.")
            .RequireAuthorization(manage)
            .Validate<JournalActionRequest>()
            .Produces<JournalDetailsResponse>()
            .ProducesStandardProblems();

        ledger.MapPost("/journals/drafts/{journalId:guid}/approve", ApproveJournalDraftAsync)
            .WithName("ApproveJournalDraft")
            .WithSummary("Approves a draft journal, preparing it for posting.")
            .RequireAuthorization(approve)
            .Validate<JournalActionRequest>()
            .Produces<JournalDetailsResponse>()
            .ProducesStandardProblems();

        ledger.MapPost("/journals/drafts/{journalId:guid}/return-to-draft", ReturnJournalToDraftAsync)
            .WithName("ReturnJournalToDraft")
            .WithSummary("Returns an approved journal back to draft state for modifications.")
            .RequireAuthorization(manage)
            .Validate<JournalActionRequest>()
            .Produces<JournalDetailsResponse>()
            .ProducesStandardProblems();

        ledger.MapPost("/journals/{journalId:guid}/create-reversal-draft", CreateReversalDraftAsync)
            .WithName("CreateReversalDraft")
            .WithSummary("Creates an unapproved reversal draft referencing a posted journal.")
            .RequireAuthorization(manage)
            .Validate<CreateReversalDraftRequest>()
            .Produces<JournalDetailsResponse>(StatusCodes.Status201Created)
            .ProducesStandardProblems();

        ledger.MapDelete("/journals/drafts/{journalId:guid}", DeleteJournalDraftAsync)
            .WithName("DeleteJournalDraft")
            .WithSummary("Deletes an unvalidated draft journal.")
            .RequireAuthorization(manage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesStandardProblems();

        ledger.MapGet("/journals", SearchJournalsAsync)
            .WithName("SearchJournals")
            .WithSummary("Searches journals with state, date range and keyword filters.")
            .RequireAuthorization(view)
            .Produces<JournalPagedResponse>()
            .ProducesStandardProblems();

        ledger.MapGet("/accounts", GetAccountsAsync)
            .WithName("GetAccounts")
            .WithSummary("Retrieves chart of accounts for line entry and lookup.")
            .RequireAuthorization(accountsView)
            .Produces<IReadOnlyList<AccountLookupResponse>>()
            .ProducesStandardProblems();

        ledger.MapGet("/cost-centers", GetCostCentersAsync)
            .WithName("GetCostCenters")
            .WithSummary("Retrieves cost centers for line allocation.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.CostCentersView))
            .Produces<IReadOnlyList<CostCenterLookupResponse>>()
            .ProducesStandardProblems();

        ledger.MapPost("/journals/validate-lines", ValidateJournalLinesAsync)
            .WithName("ValidateJournalLines")
            .WithSummary("Performs dry-run validation on draft journal lines before submitting.")
            .RequireAuthorization(view)
            .Validate<ValidateJournalLinesRequest>()
            .Produces<JournalValidationResponse>()
            .ProducesStandardProblems();
    }

    private static async Task<IResult> CreateJournalDraftAsync(
        Guid companyId,
        Guid branchId,
        CreateJournalDraftRequest request,
        IJournalEntryService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = JournalEntryScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var command = new CreateJournalDraftCommand
        {
            AccountingDate = request.AccountingDate,
            Description = request.Description,
            Lines = request.Lines.Select(static l => l.ToInput()).ToArray(),
        };

        try
        {
            var result = await service.CreateDraftAsync(scope, command, cancellationToken).ConfigureAwait(false);
            var location = $"/api/v1/companies/{companyId:D}/branches/{branchId:D}/ledger/journals/{result.Id:D}";
            return TypedResults.Created(location, result.ToResponse());
        }
        catch (InvalidOperationException ex)
        {
            return LedgerProblems.Rejected(httpContext, ex, ApiErrorCodes.RequestInvalid);
        }
    }

    private static async Task<IResult> GetJournalAsync(
        Guid companyId,
        Guid branchId,
        Guid journalId,
        IJournalEntryService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = JournalEntryScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var result = await service.GetJournalAsync(scope, journalId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }

    private static async Task<IResult> ReviseJournalDraftAsync(
        Guid companyId,
        Guid branchId,
        Guid journalId,
        ReviseJournalDraftRequest request,
        IJournalEntryService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = JournalEntryScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var version = RequireConcurrencyToken(request.ExpectedVersion, "expectedVersion");
        var command = new ReviseJournalDraftCommand
        {
            ExpectedVersion = version,
            AccountingDate = request.AccountingDate,
            Description = request.Description,
            Lines = request.Lines.Select(static l => l.ToInput()).ToArray(),
            Reason = request.Reason,
        };

        try
        {
            var result = await service.ReviseDraftAsync(scope, journalId, command, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(result.ToResponse());
        }
        catch (InvalidOperationException ex)
        {
            return LedgerProblems.Rejected(httpContext, ex, ApiErrorCodes.RequestInvalid);
        }
    }

    private static async Task<IResult> ValidateJournalDraftAsync(
        Guid companyId,
        Guid branchId,
        Guid journalId,
        JournalActionRequest request,
        IJournalEntryService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = JournalEntryScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var version = RequireConcurrencyToken(request.ExpectedVersion, "expectedVersion");
        var command = new JournalDraftActionCommand
        {
            ExpectedVersion = version,
            Reason = request.Reason,
        };

        try
        {
            var result = await service.ValidateDraftAsync(scope, journalId, command, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(result.ToResponse());
        }
        catch (InvalidOperationException ex)
        {
            return LedgerProblems.Rejected(httpContext, ex, ApiErrorCodes.RequestInvalid);
        }
    }

    private static async Task<IResult> ApproveJournalDraftAsync(
        Guid companyId,
        Guid branchId,
        Guid journalId,
        JournalActionRequest request,
        IJournalEntryService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = JournalEntryScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var version = RequireConcurrencyToken(request.ExpectedVersion, "expectedVersion");
        var command = new JournalDraftActionCommand
        {
            ExpectedVersion = version,
            Reason = request.Reason,
        };

        try
        {
            var result = await service.ApproveDraftAsync(scope, journalId, command, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(result.ToResponse());
        }
        catch (InvalidOperationException ex)
        {
            return LedgerProblems.Rejected(httpContext, ex, ApiErrorCodes.RequestInvalid);
        }
    }

    private static async Task<IResult> ReturnJournalToDraftAsync(
        Guid companyId,
        Guid branchId,
        Guid journalId,
        JournalActionRequest request,
        IJournalEntryService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = JournalEntryScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var version = RequireConcurrencyToken(request.ExpectedVersion, "expectedVersion");
        var command = new JournalDraftActionCommand
        {
            ExpectedVersion = version,
            Reason = request.Reason,
        };

        try
        {
            var result = await service.ReturnToDraftAsync(scope, journalId, command, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(result.ToResponse());
        }
        catch (InvalidOperationException ex)
        {
            return LedgerProblems.Rejected(httpContext, ex, ApiErrorCodes.RequestInvalid);
        }
    }

    private static async Task<IResult> CreateReversalDraftAsync(
        Guid companyId,
        Guid branchId,
        Guid journalId,
        CreateReversalDraftRequest request,
        IJournalEntryService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.OriginalJournalId == Guid.Empty || request.OriginalJournalId != journalId)
        {
            throw new RequestValidationException("ledger.original_journal_invalid",
                "The original journal ID in body must match the route journal ID.", "originalJournalId");
        }

        var scope = JournalEntryScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var version = RequireConcurrencyToken(request.OriginalExpectedVersion, "originalExpectedVersion");
        var command = new CreateReversalDraftCommand
        {
            OriginalJournalId = request.OriginalJournalId,
            OriginalExpectedVersion = version,
            AccountingDate = request.AccountingDate,
            Reason = request.Reason,
        };

        try
        {
            var result = await service.CreateReversalDraftAsync(scope, command, cancellationToken).ConfigureAwait(false);
            var location = $"/api/v1/companies/{companyId:D}/branches/{branchId:D}/ledger/journals/{result.Id:D}";
            return TypedResults.Created(location, result.ToResponse());
        }
        catch (InvalidOperationException ex)
        {
            return LedgerProblems.Rejected(httpContext, ex, ApiErrorCodes.RequestInvalid);
        }
    }

    private static async Task<IResult> DeleteJournalDraftAsync(
        Guid companyId,
        Guid branchId,
        Guid journalId,
        [FromQuery] string expectedVersion,
        IJournalEntryService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = JournalEntryScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var version = RequireConcurrencyToken(expectedVersion, "expectedVersion");

        try
        {
            await service.DeleteDraftAsync(scope, journalId, version, cancellationToken).ConfigureAwait(false);
            return TypedResults.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return LedgerProblems.Rejected(httpContext, ex, ApiErrorCodes.RequestInvalid);
        }
    }

    private static async Task<IResult> SearchJournalsAsync(
        Guid companyId,
        Guid branchId,
        JournalState? state,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? search,
        int? page,
        int? pageSize,
        IJournalEntryService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = JournalEntryScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var pageReq = PageRequest.Create(page, pageSize);

        var result = await service.SearchJournalsAsync(scope, fromDate, toDate,
            state is { } filter ? LedgerJournalMapping.ToPersistedState(filter) : null, search, pageReq, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }

    private static async Task<IResult> GetAccountsAsync(
        Guid companyId,
        Guid branchId,
        IJournalEntryService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAccountsAsync(companyId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok((IReadOnlyList<AccountLookupResponse>)result.Select(static a => a.ToResponse()).ToArray());
    }

    private static async Task<IResult> GetCostCentersAsync(
        Guid companyId,
        Guid branchId,
        IJournalEntryService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetCostCentersAsync(companyId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok((IReadOnlyList<CostCenterLookupResponse>)result.Select(static c => c.ToResponse()).ToArray());
    }

    private static async Task<IResult> ValidateJournalLinesAsync(
        Guid companyId,
        Guid branchId,
        ValidateJournalLinesRequest request,
        IJournalEntryService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var scope = JournalEntryScope.Create(companyId, branchId, httpContext.RequireSession().UserId);
        var lineInputs = request.Lines.Select(static l => l.ToInput()).ToArray();

        try
        {
            var result = await service.ValidateJournalLinesAsync(scope, request.AccountingDate, lineInputs, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(result.ToResponse());
        }
        catch (InvalidOperationException ex)
        {
            return LedgerProblems.Rejected(httpContext, ex, ApiErrorCodes.RequestInvalid);
        }
    }

    private static ConcurrencyToken RequireConcurrencyToken(string value, string field) =>
        ConcurrencyToken.TryParse(value, out var token)
            ? token
            : throw new RequestValidationException(ApiErrorCodes.VersionInvalid,
                "The version must be the canonical 12-character Base64 value read from the server.", field);
}
