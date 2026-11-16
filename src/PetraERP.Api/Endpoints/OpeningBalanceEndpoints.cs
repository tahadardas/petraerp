using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.OpeningBalances;
using PetraERP.Application.Security;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Endpoints;

internal static class OpeningBalanceEndpoints
{
    internal static void MapOpeningBalanceEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/opening-balances")
            .WithTags("Opening balances");

        var view = PetraPolicies.Permission(PermissionCatalog.JournalsView);
        var manage = PetraPolicies.Permission(PermissionCatalog.JournalsManage);
        var approve = PetraPolicies.Permission(PermissionCatalog.JournalsApprove);
        var post = PetraPolicies.Permission(PermissionCatalog.JournalsPost);
        var reverse = PetraPolicies.Permission(PermissionCatalog.JournalsReverse);

        group.MapPost("", CreateDraftAsync)
            .WithName("CreateOpeningBalanceDraft")
            .WithSummary("Creates a new draft opening balance batch.")
            .RequireAuthorization(manage)
            .Validate<CreateOpeningBalanceBatchRequest>()
            .Produces<OpeningBalanceBatchResponse>(StatusCodes.Status201Created)
            .ProducesStandardProblems();

        group.MapPost("/{batchId:guid}/validate", ValidateAsync)
            .WithName("ValidateOpeningBalanceBatch")
            .WithSummary("Validates an opening balance batch for balance, rates, and accounts.")
            .RequireAuthorization(view)
            .Produces<OpeningBalanceBatchResponse>()
            .ProducesStandardProblems();

        group.MapPost("/{batchId:guid}/approve", ApproveAsync)
            .WithName("ApproveOpeningBalanceBatch")
            .WithSummary("Approves an opening balance batch for posting.")
            .RequireAuthorization(approve)
            .Validate<ApproveOpeningBalanceRequest>()
            .Produces<OpeningBalanceBatchResponse>()
            .ProducesStandardProblems();

        group.MapPost("/{batchId:guid}/post", PostAsync)
            .WithName("PostOpeningBalanceBatch")
            .WithSummary("Posts an approved opening balance batch to the general ledger.")
            .RequireAuthorization(post)
            .Validate<PostOpeningBalanceRequest>()
            .Produces<OpeningBalanceBatchResponse>()
            .ProducesStandardProblems();

        group.MapPost("/{batchId:guid}/reverse", ReverseAsync)
            .WithName("ReverseOpeningBalanceBatch")
            .WithSummary("Reverses a posted opening balance batch.")
            .RequireAuthorization(reverse)
            .Validate<ReverseOpeningBalanceRequest>()
            .Produces<OpeningBalanceBatchResponse>()
            .ProducesStandardProblems();

        group.MapGet("/{batchId:guid}", GetAsync)
            .WithName("GetOpeningBalanceBatch")
            .WithSummary("Gets an opening balance batch by id.")
            .RequireAuthorization(view)
            .Produces<OpeningBalanceBatchResponse>()
            .ProducesStandardProblems();

        group.MapGet("", ListAsync)
            .WithName("ListOpeningBalanceBatches")
            .WithSummary("Lists opening balance batches for the current fiscal workspace.")
            .RequireAuthorization(view)
            .Produces<IReadOnlyList<OpeningBalanceBatchResponse>>()
            .ProducesStandardProblems();
    }

    private static decimal ParseDecimal(string raw, string field)
    {
        if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new RequestValidationException("opening_balances.invalid_decimal", $"The value '{raw}' is not a valid decimal.", field);
        return value;
    }

    private static OpeningBalanceBatchResponse ToResponse(this OpeningBalanceBatchDto dto)
    {
        return new OpeningBalanceBatchResponse
        {
            Id = dto.Id,
            CompanyId = dto.CompanyId,
            FiscalYearId = dto.FiscalYearId,
            BranchId = dto.BranchId,
            OpeningDate = dto.OpeningDate,
            SourceType = dto.SourceType,
            SourceReference = dto.SourceReference,
            SourceFingerprint = dto.SourceFingerprint,
            OriginalCurrency = dto.OriginalCurrency,
            BaseCurrency = dto.BaseCurrency,
            ApprovalState = dto.ApprovalState,
            ActorId = dto.ActorId,
            ApprovedByActorId = dto.ApprovedByActorId,
            CreatedAtUtc = dto.CreatedAtUtc,
            UpdatedAtUtc = dto.UpdatedAtUtc,
            ApprovedAtUtc = dto.ApprovedAtUtc,
            PostedJournalId = dto.PostedJournalId,
            ReversalJournalId = dto.ReversalJournalId,
            IdempotencyKey = dto.IdempotencyKey,
            TotalDebitBase = FinancialDecimalText.Format(dto.TotalDebitBase, FinancialDecimalText.MoneyScale),
            TotalCreditBase = FinancialDecimalText.Format(dto.TotalCreditBase, FinancialDecimalText.MoneyScale),
            IsBalanced = dto.IsBalanced,
            VersionBase64 = dto.VersionBase64,
            Lines = dto.Lines.Select(l => new OpeningBalanceLineResponse
            {
                Id = l.Id,
                BatchId = l.BatchId,
                AccountId = l.AccountId,
                AccountCode = l.AccountCode,
                AccountNameArabic = l.AccountNameArabic,
                CostCenterId = l.CostCenterId,
                CostCenterCode = l.CostCenterCode,
                CurrencyCode = l.CurrencyCode,
                DebitOriginal = FinancialDecimalText.Format(l.DebitOriginal, FinancialDecimalText.MoneyScale),
                CreditOriginal = FinancialDecimalText.Format(l.CreditOriginal, FinancialDecimalText.MoneyScale),
                ExchangeRate = FinancialDecimalText.Format(l.ExchangeRate, FinancialDecimalText.RateScale),
                DebitBase = FinancialDecimalText.Format(l.DebitBase, FinancialDecimalText.MoneyScale),
                CreditBase = FinancialDecimalText.Format(l.CreditBase, FinancialDecimalText.MoneyScale),
                Description = l.Description,
                LineOrder = l.LineOrder
            }).ToList()
        };
    }

    private static async Task<IResult> CreateDraftAsync(
        Guid companyId,
        Guid branchId,
        [FromBody] CreateOpeningBalanceBatchRequest request,
        IOpeningBalanceService service,
        FinancialRequestScope scope,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var lines = request.Lines.Select(l => new OpeningBalanceLineInput(
            l.AccountId,
            l.CostCenterId,
            l.CurrencyCode,
            ParseDecimal(l.DebitOriginal, nameof(l.DebitOriginal)),
            ParseDecimal(l.CreditOriginal, nameof(l.CreditOriginal)),
            ParseDecimal(l.ExchangeRate, nameof(l.ExchangeRate)),
            ParseDecimal(l.DebitBase, nameof(l.DebitBase)),
            ParseDecimal(l.CreditBase, nameof(l.CreditBase)),
            l.Description,
            l.LineOrder)).ToList();

        var command = new CreateOpeningBalanceBatchCommand(
            request.OpeningDate,
            request.SourceType,
            request.SourceReference,
            request.SourceFingerprint,
            request.OriginalCurrency,
            request.BaseCurrency,
            request.BranchId ?? branchId,
            request.IdempotencyKey,
            lines);

        var result = await service.CreateDraftAsync(companyId, scope.FiscalYearId, http.RequireSession().UserId, command, cancellationToken).ConfigureAwait(false);
        var location = $"/api/v1/companies/{companyId:D}/branches/{branchId:D}/opening-balances/{result.Id:D}";
        return TypedResults.Created(location, result.ToResponse());
    }

    private static async Task<IResult> ValidateAsync(
        Guid companyId,
        Guid branchId,
        Guid batchId,
        IOpeningBalanceService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ValidateAsync(companyId, batchId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }

    private static async Task<IResult> ApproveAsync(
        Guid companyId,
        Guid branchId,
        Guid batchId,
        [FromBody] ApproveOpeningBalanceRequest request,
        IOpeningBalanceService service,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await service.ApproveAsync(companyId, batchId, http.RequireSession().UserId, request.Reason, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }

    private static async Task<IResult> PostAsync(
        Guid companyId,
        Guid branchId,
        Guid batchId,
        [FromBody] PostOpeningBalanceRequest request,
        IOpeningBalanceService service,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await service.PostAsync(companyId, batchId, http.RequireSession().UserId, request.Reason, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }

    private static async Task<IResult> ReverseAsync(
        Guid companyId,
        Guid branchId,
        Guid batchId,
        [FromBody] ReverseOpeningBalanceRequest request,
        IOpeningBalanceService service,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await service.ReverseAsync(companyId, batchId, http.RequireSession().UserId, request.Reason, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }

    private static async Task<IResult> GetAsync(
        Guid companyId,
        Guid branchId,
        Guid batchId,
        IOpeningBalanceService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(companyId, batchId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }

    private static async Task<IResult> ListAsync(
        Guid companyId,
        Guid branchId,
        IOpeningBalanceService service,
        FinancialRequestScope scope,
        CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(companyId, scope.FiscalYearId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.Select(ToResponse).ToList());
    }
}
