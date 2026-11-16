using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.DocumentProcessing;
using PetraERP.Application.Sales;
using PetraERP.Application.Security;
using PetraERP.Contracts.DocumentProcessing;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Sales;
using AppStatus = PetraERP.Domain.Sales.SalesInvoiceStatus;
using AppPaymentMethod = PetraERP.Domain.Sales.PaymentMethod;
using ContractStatus = PetraERP.Contracts.Sales.SalesInvoiceStatus;
using ContractPaymentMethod = PetraERP.Contracts.Sales.PaymentMethod;

namespace PetraERP.Api.Endpoints;

internal static class SalesInvoiceEndpoints
{
    internal static void MapSalesInvoiceEndpoints(this RouteGroupBuilder api)
    {
        var invoices = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/sales/invoices")
            .WithTags("SalesInvoices");

        var view = PetraPolicies.Permission(PermissionCatalog.SalesInvoicesView);
        var manage = PetraPolicies.Permission(PermissionCatalog.SalesInvoicesManage);
        var approve = PetraPolicies.Permission(PermissionCatalog.SalesInvoicesApprove);
        var post = PetraPolicies.Permission(PermissionCatalog.SalesInvoicesPost);
        var reverse = PetraPolicies.Permission(PermissionCatalog.SalesInvoicesReverse);

        invoices.MapGet(string.Empty, ListSalesInvoicesAsync)
            .WithName("ListSalesInvoices")
            .WithSummary("Lists sales invoices for the company and branch with pagination and filters.")
            .RequireAuthorization(view)
            .Produces<SalesInvoicePageResponse>()
            .ProducesStandardProblems();

        invoices.MapGet("/navigate", NavigateSalesInvoiceAsync)
            .WithName("NavigateSalesInvoice")
            .WithSummary("Returns the id of the first/previous/next/last invoice over the branch, optionally restricted by the list filters (customer, warehouse, status, dates, search). Without sortBy the order is ascending (date, created-at, id); with sortBy/sortDescending it is exactly the order the list shows (requested column, then date, created-at, id in the same direction).")
            .RequireAuthorization(view)
            .Produces<SalesInvoiceNavigationResponse>()
            .ProducesStandardProblems();

        invoices.MapGet("/{invoiceId:guid}", GetSalesInvoiceAsync)
            .WithName("GetSalesInvoice")
            .WithSummary("Gets detailed sales invoice by ID including line items.")
            .RequireAuthorization(view)
            .Produces<SalesInvoiceDetailDto>()
            .ProducesStandardProblems();

        invoices.MapPost(string.Empty, CreateSalesInvoiceAsync)
            .WithName("CreateSalesInvoice")
            .WithSummary("Creates a new sales invoice draft.")
            .RequireAuthorization(manage)
            .Validate<CreateSalesInvoiceRequest>()
            .Produces<SalesInvoiceDetailDto>(StatusCodes.Status201Created)
            .ProducesStandardProblems();

        invoices.MapPut("/{invoiceId:guid}", UpdateSalesInvoiceAsync)
            .WithName("UpdateSalesInvoice")
            .WithSummary("Updates a draft sales invoice.")
            .RequireAuthorization(manage)
            .Validate<UpdateSalesInvoiceRequest>()
            .Produces<SalesInvoiceDetailDto>()
            .ProducesStandardProblems();

        invoices.MapDelete("/{invoiceId:guid}", DeleteSalesInvoiceAsync)
            .WithName("DeleteSalesInvoice")
            .WithSummary("Deletes a never-approved draft sales invoice; posted invoices must be reversed instead.")
            .RequireAuthorization(manage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesStandardProblems();

        invoices.MapPost("/save-and-process", SaveAndProcessSalesInvoiceAsync)
            .RetryOnDeadlock()
            .WithName("SaveAndProcessSalesInvoice")
            .WithSummary("Saves a sales invoice and applies the company posting policy: draft, approve-only or approve-and-post.")
            .RequireAuthorization(manage)
            .Validate<SaveAndProcessSalesInvoiceRequest>()
            .Produces<DocumentProcessingResultDto>(StatusCodes.Status201Created)
            .ProducesStandardProblems();

        invoices.MapPut("/{invoiceId:guid}/save-and-process", SaveAndProcessUpdateSalesInvoiceAsync)
            .RetryOnDeadlock()
            .WithName("SaveAndProcessUpdateSalesInvoice")
            .WithSummary("Updates a draft sales invoice and applies the company posting policy.")
            .RequireAuthorization(manage)
            .Validate<SaveAndProcessSalesInvoiceRequest>()
            .Produces<DocumentProcessingResultDto>()
            .ProducesStandardProblems();

        invoices.MapPost("/{invoiceId:guid}/approve", ApproveSalesInvoiceAsync)
            .WithName("ApproveSalesInvoice")
            .WithSummary("Approves a draft sales invoice.")
            .RequireAuthorization(approve)
            .Validate<ApproveSalesInvoiceRequest>()
            .Produces<SalesInvoiceDetailDto>()
            .ProducesStandardProblems();

        invoices.MapPost("/{invoiceId:guid}/post", PostSalesInvoiceAsync)
            .RetryOnDeadlock()
            .WithName("PostSalesInvoice")
            .WithSummary("Atomically posts a sales invoice, generating sequence number, stock movements, and journal entry.")
            .RequireAuthorization(post)
            .Validate<PostSalesInvoiceRequest>()
            .Produces<SalesInvoiceDetailDto>()
            .ProducesStandardProblems();

        invoices.MapPost("/{invoiceId:guid}/reverse", ReverseSalesInvoiceAsync)
            .WithName("ReverseSalesInvoice")
            .WithSummary("Reverses a posted sales invoice with linked reversal journal entry and stock return movements.")
            .RequireAuthorization(reverse)
            .Validate<ReverseSalesInvoiceRequest>()
            .Produces<SalesInvoiceDetailDto>()
            .ProducesStandardProblems();

        // A return is a correction (Reverse) that posts a new journal (Post); both grants are required.
        invoices.MapPost("/{invoiceId:guid}/returns", ReturnSalesInvoiceAsync)
            .WithName("ReturnSalesInvoice")
            .WithSummary("Posts a partial or full return of a posted invoice at original price and cost; returns the new return document.")
            .RequireAuthorization(reverse)
            .RequireAuthorization(post)
            .Validate<CreateSalesReturnRequest>()
            .Produces<SalesInvoiceDetailDto>(StatusCodes.Status201Created)
            .ProducesStandardProblems();
    }

    private static async Task<IResult> ListSalesInvoicesAsync(
        Guid companyId,
        Guid branchId,
        [FromQuery] Guid? customerId,
        [FromQuery] Guid? warehouseId,
        [FromQuery] Guid? patternRevisionId,
        [FromQuery] ContractStatus? status,
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        [FromQuery] Guid? returnOfInvoiceId,
        [FromQuery] string? search,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] bool? sortDescending,
        ISalesInvoiceService service,
        CancellationToken cancellationToken)
    {
        if (!TryParseSort(sortBy, sortDescending, out var sort))
            return TypedResults.Problem(title: "sales.sort_invalid", statusCode: StatusCodes.Status400BadRequest);
        var query = new SalesInvoiceSearchQuery(
            CustomerId: customerId,
            WarehouseId: warehouseId,
            PatternRevisionId: patternRevisionId,
            Status: status.HasValue ? (AppStatus)status.Value : null,
            FromDateUtc: fromDate,
            ToDateUtc: toDate,
            Page: page <= 0 ? 1 : page,
            PageSize: pageSize <= 0 ? 50 : pageSize)
        {
            ReturnOfInvoiceId = returnOfInvoiceId,
            Search = search is { Length: > 100 } ? search[..100] : search,
            Sort = sort,
        };

        var result = await service.GetInvoicesAsync(companyId, branchId, query, cancellationToken).ConfigureAwait(false);

        var response = new SalesInvoicePageResponse
        {
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            Items = result.Items.Select(MapSummary).ToList(),
        };

        return TypedResults.Ok(response);
    }

    /// <summary>Absent sortBy keeps the historical order; an unknown column or numeric value is a client error, never a silent default.</summary>
    internal static bool TryParseSort(string? sortBy, bool? descending, out SalesInvoiceSort? sort)
    {
        sort = null;
        if (string.IsNullOrWhiteSpace(sortBy)) return descending is null;
        if (!Enum.TryParse<SalesInvoiceSortField>(sortBy.Trim(), ignoreCase: true, out var field) || !Enum.IsDefined(field)
            || sortBy.Trim().All(char.IsAsciiDigit)) return false;
        sort = new SalesInvoiceSort(field, descending ?? false);
        return true;
    }

    private static async Task<IResult> NavigateSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        [FromQuery] string direction,
        [FromQuery] Guid? from,
        [FromQuery] Guid? customerId,
        [FromQuery] Guid? warehouseId,
        [FromQuery] ContractStatus? status,
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] bool? sortDescending,
        ISalesInvoiceService service,
        CancellationToken cancellationToken)
    {
        if (!TryParseSort(sortBy, sortDescending, out var sort))
            return TypedResults.Problem(title: "sales.sort_invalid", statusCode: StatusCodes.Status400BadRequest);
        if (!Enum.TryParse<SalesInvoiceNavigationDirection>(direction, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            return TypedResults.Problem(title: "sales.navigation_direction_invalid", statusCode: StatusCodes.Status400BadRequest);
        if (parsed is SalesInvoiceNavigationDirection.Previous or SalesInvoiceNavigationDirection.Next && from is null)
            return TypedResults.Problem(title: "sales.navigation_origin_required", statusCode: StatusCodes.Status400BadRequest);
        // The same filter set as the list endpoint, so previous/next walk exactly the rows the user is looking at.
        var filter = new SalesInvoiceSearchQuery(
            CustomerId: customerId, WarehouseId: warehouseId, Status: status.HasValue ? (AppStatus)status.Value : null,
            FromDateUtc: fromDate, ToDateUtc: toDate)
        {
            Search = search is { Length: > 100 } ? search[..100] : search,
            Sort = sort,
        };
        var id = await service.GetNeighborInvoiceIdAsync(companyId, branchId, from, parsed, filter, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new SalesInvoiceNavigationResponse { InvoiceId = id });
    }

    private static async Task<IResult> GetSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        ISalesInvoiceService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetInvoiceAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(MapDetail(result));
    }

    private static async Task<IResult> CreateSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        [FromBody] CreateSalesInvoiceRequest request,
        ISalesInvoiceService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.RequireSession().UserId;

        var parsed = ParseDocument(request);
        if (parsed.Error is not null) return parsed.Error;

        var result = await service.CreateInvoiceAsync(companyId, branchId, userId, ToCreateCommand(request, parsed.Document!), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/companies/{companyId}/branches/{branchId}/sales/invoices/{result.Id}", MapDetail(result));
    }

    private static async Task<IResult> DeleteSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        [FromQuery] string? expectedVersion,
        ISalesInvoiceService service,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(expectedVersion, out var version))
        {
            return TypedResults.Problem("expectedVersion must be the Version returned by the server.", statusCode: StatusCodes.Status400BadRequest);
        }

        await service.DeleteDraftAsync(companyId, branchId, invoiceId, version, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> UpdateSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        [FromBody] UpdateSalesInvoiceRequest request,
        ISalesInvoiceService service,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(request.ExpectedVersion, out var expectedVersion))
        {
            return TypedResults.Problem("ExpectedVersion must be the Version returned by the server.", statusCode: StatusCodes.Status400BadRequest);
        }

        var source = new CreateSalesInvoiceRequest
        {
            CustomerId = request.CustomerId,
            WarehouseId = request.WarehouseId,
            InvoiceDateUtc = request.InvoiceDateUtc,
            PaymentMethod = request.PaymentMethod,
            CurrencyCode = request.CurrencyCode,
            ExchangeRate = request.ExchangeRate,
            CostCenterId = request.CostCenterId,
            Notes = request.Notes,
            Lines = request.Lines,
            Adjustments = request.Adjustments,
        };
        var parsed = ParseDocument(source);
        if (parsed.Error is not null) return parsed.Error;

        var command = ToUpdateCommand(source, parsed.Document!, expectedVersion);
        var result = await service.UpdateInvoiceAsync(companyId, branchId, invoiceId, command, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(MapDetail(result));
    }

    private static async Task<IResult> SaveAndProcessSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        [FromBody] SaveAndProcessSalesInvoiceRequest request,
        IDocumentSaveAndProcessService processing,
        IAccessEvaluator access,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.InvoiceId is not null) return TypedResults.Problem("Use PUT /{invoiceId}/save-and-process to update an existing invoice.", statusCode: StatusCodes.Status400BadRequest);
        if (!Enum.IsDefined(request.Document.PaymentMethod)) return TypedResults.Problem("PaymentMethod must be Credit or Cash.", statusCode: StatusCodes.Status400BadRequest);
        var parsed = ParseDocument(request.Document);
        if (parsed.Error is not null) return parsed.Error;
        var session = httpContext.RequireSession();
        var permissions = await ProcessingPermissionsAsync(access, session.UserId, companyId, branchId,
            PermissionCatalog.SalesInvoicesApprove, PermissionCatalog.SalesInvoicesPost, cancellationToken).ConfigureAwait(false);
        var result = await processing.CreateSalesInvoiceAsync(companyId, branchId, session.UserId, permissions,
            ToCreateCommand(request.Document, parsed.Document!), request.SaveAsDraft, PostingIntent(request), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/companies/{companyId}/branches/{branchId}/sales/invoices/{result.DocumentId}", MapProcessing(result));
    }

    private static async Task<IResult> SaveAndProcessUpdateSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        [FromBody] SaveAndProcessSalesInvoiceRequest request,
        IDocumentSaveAndProcessService processing,
        IAccessEvaluator access,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.InvoiceId is not null && request.InvoiceId != invoiceId)
            return TypedResults.Problem("InvoiceId in the body must match the route.", statusCode: StatusCodes.Status400BadRequest);
        if (!ConcurrencyToken.TryParse(request.ExpectedVersion, out var expectedVersion))
            return TypedResults.Problem("ExpectedVersion must be the Version returned by the server.", statusCode: StatusCodes.Status400BadRequest);
        if (!Enum.IsDefined(request.Document.PaymentMethod)) return TypedResults.Problem("PaymentMethod must be Credit or Cash.", statusCode: StatusCodes.Status400BadRequest);
        var parsed = ParseDocument(request.Document);
        if (parsed.Error is not null) return parsed.Error;
        var session = httpContext.RequireSession();
        var permissions = await ProcessingPermissionsAsync(access, session.UserId, companyId, branchId,
            PermissionCatalog.SalesInvoicesApprove, PermissionCatalog.SalesInvoicesPost, cancellationToken).ConfigureAwait(false);
        var result = await processing.UpdateSalesInvoiceAsync(companyId, branchId, session.UserId, permissions, invoiceId,
            ToUpdateCommand(request.Document, parsed.Document!, expectedVersion), request.SaveAsDraft, PostingIntent(request), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(MapProcessing(result));
    }

    private static DocumentPostingIntent? PostingIntent(SaveAndProcessSalesInvoiceRequest request) =>
        new(request.IdempotencyKey, request.ManualNumber, request.PostingNotes)
        {
            ConfirmCreditWarning = request.ConfirmCreditWarning,
            OverrideCreditPolicy = request.OverrideCreditPolicy,
            CreditOverrideReason = request.CreditOverrideReason,
            NegativeStockReason = request.NegativeStockReason,
        };

    private static async Task<DocumentProcessingPermissions> ProcessingPermissionsAsync(IAccessEvaluator access, Guid userId,
        Guid companyId, Guid branchId, string approveCode, string postCode, CancellationToken cancellationToken)
    {
        var canApprove = await access.EvaluatePermissionAsync(userId, PermissionCatalog.Get(approveCode), companyId, branchId, cancellationToken)
            .ConfigureAwait(false) == AccessDecision.Granted;
        var canPost = await access.EvaluatePermissionAsync(userId, PermissionCatalog.Get(postCode), companyId, branchId, cancellationToken)
            .ConfigureAwait(false) == AccessDecision.Granted;
        return new DocumentProcessingPermissions(canApprove, canPost);
    }

    private static DocumentProcessingResultDto MapProcessing(DocumentProcessingResult r) => new()
    {
        DocumentType = r.DocumentType,
        DocumentId = r.DocumentId,
        DocumentNumber = r.DocumentNumber,
        Status = r.Status,
        Outcome = r.Outcome.ToString(),
        ApprovalApplied = r.ApprovalApplied,
        PostingApplied = r.PostingApplied,
        JournalEntryId = r.JournalEntryId,
        EffectivePostingMode = r.EffectivePostingMode,
        Version = r.Version,
        Message = r.Message,
        FailureCode = r.FailureCode,
    };

    private static async Task<IResult> ApproveSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        [FromBody] ApproveSalesInvoiceRequest request,
        ISalesInvoiceService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(request.ExpectedVersion, out var expectedVersion))
        {
            return TypedResults.Problem("ExpectedVersion must be the Version returned by the server.", statusCode: StatusCodes.Status400BadRequest);
        }

        var userId = httpContext.RequireSession().UserId;
        var result = await service.ApproveInvoiceAsync(companyId, branchId, invoiceId, userId, expectedVersion, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(MapDetail(result));
    }

    private static async Task<IResult> PostSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        [FromBody] PostSalesInvoiceRequest request,
        ISalesInvoiceService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.RequireSession().UserId;
        var command = new PostSalesInvoiceCommand(request.IdempotencyKey, request.Notes)
        {
            ManualNumber = request.ManualNumber,
            ConfirmCreditWarning = request.ConfirmCreditWarning,
            OverrideCreditPolicy = request.OverrideCreditPolicy,
            CreditOverrideReason = request.CreditOverrideReason,
            NegativeStockReason = request.NegativeStockReason,
        };
        var result = await service.PostInvoiceAsync(companyId, branchId, invoiceId, userId, command, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(MapDetail(result));
    }

    private static async Task<IResult> ReverseSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        [FromBody] ReverseSalesInvoiceRequest request,
        ISalesInvoiceService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.RequireSession().UserId;
        var command = new ReverseSalesInvoiceCommand(request.Reason);
        var result = await service.ReverseInvoiceAsync(companyId, branchId, invoiceId, userId, command, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(MapDetail(result));
    }

    private static async Task<IResult> ReturnSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        [FromBody] CreateSalesReturnRequest request,
        ISalesInvoiceService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var lines = new List<SalesReturnLineCommand>();
        foreach (var line in request.Lines)
        {
            if (!FinancialDecimalText.TryParse(line.Quantity, FinancialDecimalText.MoneyScale, 15, out var quantity) || quantity.Value <= 0)
            {
                return TypedResults.Problem("Return quantity must be a positive decimal string.", statusCode: StatusCodes.Status400BadRequest);
            }
            lines.Add(new SalesReturnLineCommand(line.SourceLineId, quantity.Value));
        }

        var userId = httpContext.RequireSession().UserId;
        var result = await service.ReturnInvoiceAsync(companyId, branchId, invoiceId, userId,
            new CreateSalesReturnCommand(request.IdempotencyKey, request.Reason, lines), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/companies/{companyId}/branches/{branchId}/sales/invoices/{result.Id}", MapDetail(result));
    }

    private sealed record ParsedSalesDocument(decimal? ExchangeRate, List<CreateSalesInvoiceLineCommand> Lines, List<CreateSalesInvoiceAdjustmentCommand> Adjustments);

    private static (ParsedSalesDocument? Document, IResult? Error) ParseDocument(CreateSalesInvoiceRequest request)
    {
        decimal? exchangeRate = null;
        if (!string.IsNullOrEmpty(request.ExchangeRate))
        {
            if (!FinancialDecimalText.TryParse(request.ExchangeRate, FinancialDecimalText.RateScale, FinancialDecimalText.RateIntegerDigits, out var parsedRate) || parsedRate.Value <= 0)
            {
                return (null, TypedResults.Problem("ExchangeRate must be a positive decimal string.", statusCode: StatusCodes.Status400BadRequest));
            }
            exchangeRate = parsedRate.Value;
        }

        var lines = new List<CreateSalesInvoiceLineCommand>();
        foreach (var l in request.Lines)
        {
            var parsed = ParseLine(l);
            if (parsed.Error is not null)
            {
                return (null, TypedResults.Problem(parsed.Error, statusCode: StatusCodes.Status400BadRequest));
            }
            lines.Add(parsed.Command!);
        }

        var adjustments = new List<CreateSalesInvoiceAdjustmentCommand>();
        foreach (var a in request.Adjustments)
        {
            if (!FinancialDecimalText.TryParse(a.Amount, FinancialDecimalText.MoneyScale, 15, out var amount) || amount.Value <= 0)
                return (null, TypedResults.Problem("Adjustment Amount must be a positive decimal.", statusCode: StatusCodes.Status400BadRequest));
            var percentage = 0m;
            if (!string.IsNullOrEmpty(a.Percentage))
            {
                if (!FinancialDecimalText.TryParse(a.Percentage, FinancialDecimalText.MoneyScale, 15, out var parsedPercentage) || parsedPercentage.Value < 0 || parsedPercentage.Value > 100)
                    return (null, TypedResults.Problem("Adjustment Percentage must be between 0 and 100.", statusCode: StatusCodes.Status400BadRequest));
                percentage = parsedPercentage.Value;
            }
            adjustments.Add(new CreateSalesInvoiceAdjustmentCommand((PetraERP.Domain.Sales.SalesInvoiceAdjustmentKind)a.Kind, a.AccountId, amount.Value, percentage,
                a.Notes, a.CostCenterId, a.DisplayOrder, a.CounterAccountId));
        }

        return (new ParsedSalesDocument(exchangeRate, lines, adjustments), null);
    }

    private static CreateSalesInvoiceCommand ToCreateCommand(CreateSalesInvoiceRequest request, ParsedSalesDocument parsed) =>
        new(
            PatternRevisionId: request.PatternRevisionId,
            CustomerId: request.CustomerId ?? Guid.Empty,
            WarehouseId: request.WarehouseId,
            InvoiceDateUtc: request.InvoiceDateUtc,
            PaymentMethod: (AppPaymentMethod)request.PaymentMethod,
            CurrencyCode: request.CurrencyCode,
            ExchangeRate: parsed.ExchangeRate,
            CostCenterId: request.CostCenterId,
            Notes: request.Notes,
            Lines: parsed.Lines)
        {
            Adjustments = parsed.Adjustments,
            CustomerAccountId = request.CustomerAccountId,
            SellerCode = request.SellerCode,
            DistributorCode = request.DistributorCode,
        };

    private static UpdateSalesInvoiceCommand ToUpdateCommand(CreateSalesInvoiceRequest request, ParsedSalesDocument parsed,
        ConcurrencyToken expectedVersion) =>
        new(
            CustomerId: request.CustomerId ?? Guid.Empty,
            WarehouseId: request.WarehouseId,
            InvoiceDateUtc: request.InvoiceDateUtc,
            PaymentMethod: (AppPaymentMethod)request.PaymentMethod,
            CurrencyCode: request.CurrencyCode,
            ExchangeRate: parsed.ExchangeRate,
            CostCenterId: request.CostCenterId,
            Notes: request.Notes,
            Lines: parsed.Lines)
        {
            ExpectedVersion = expectedVersion,
            Adjustments = parsed.Adjustments,
            CustomerAccountId = request.CustomerAccountId,
            SellerCode = request.SellerCode,
            DistributorCode = request.DistributorCode,
        };

    private static (CreateSalesInvoiceLineCommand? Command, string? Field, string? Error) ParseLine(CreateSalesInvoiceLineRequest l)
    {
        if (!FinancialDecimalText.TryParse(l.Quantity, FinancialDecimalText.MoneyScale, 15, out var qty) || qty.Value <= 0)
        {
            return (null, "Quantity", "Quantity must be a positive decimal.");
        }

        if (!FinancialDecimalText.TryParse(l.UnitPrice, FinancialDecimalText.MoneyScale, 15, out var price) || price.Value < 0)
        {
            return (null, "UnitPrice", "UnitPrice must be non-negative.");
        }

        decimal discountPct = 0m;
        if (!string.IsNullOrEmpty(l.DiscountPercentage))
        {
            if (!FinancialDecimalText.TryParse(l.DiscountPercentage, FinancialDecimalText.MoneyScale, 15, out var dp) || dp.Value < 0)
            {
                return (null, "DiscountPercentage", "DiscountPercentage must be non-negative.");
            }
            discountPct = dp.Value;
        }

        decimal discountAmt = 0m;
        if (!string.IsNullOrEmpty(l.DiscountAmount))
        {
            if (!FinancialDecimalText.TryParse(l.DiscountAmount, FinancialDecimalText.MoneyScale, 15, out var da) || da.Value < 0)
            {
                return (null, "DiscountAmount", "DiscountAmount must be non-negative.");
            }
            discountAmt = da.Value;
        }

        decimal taxPct = 0m;
        if (!string.IsNullOrEmpty(l.TaxPercentage))
        {
            if (!FinancialDecimalText.TryParse(l.TaxPercentage, FinancialDecimalText.MoneyScale, 15, out var tp) || tp.Value < 0)
            {
                return (null, "TaxPercentage", "TaxPercentage must be non-negative.");
            }
            taxPct = tp.Value;
        }

        decimal taxAmt = 0m;
        if (!string.IsNullOrEmpty(l.TaxAmount))
        {
            if (!FinancialDecimalText.TryParse(l.TaxAmount, FinancialDecimalText.MoneyScale, 15, out var ta) || ta.Value < 0)
            {
                return (null, "TaxAmount", "TaxAmount must be non-negative.");
            }
            taxAmt = ta.Value;
        }

        decimal pumpCost = 0m;
        if (!string.IsNullOrEmpty(l.PumpCost))
        {
            if (!FinancialDecimalText.TryParse(l.PumpCost, FinancialDecimalText.MoneyScale, 15, out var pc) || pc.Value < 0)
            {
                return (null, "PumpCost", "PumpCost must be non-negative.");
            }
            pumpCost = pc.Value;
        }

        decimal transportCost = 0m;
        if (!string.IsNullOrEmpty(l.TransportCost))
        {
            if (!FinancialDecimalText.TryParse(l.TransportCost, FinancialDecimalText.MoneyScale, 15, out var tc) || tc.Value < 0)
            {
                return (null, "TransportCost", "TransportCost must be non-negative.");
            }
            transportCost = tc.Value;
        }

        var cmd = new CreateSalesInvoiceLineCommand(
            ItemId: l.ItemId,
            UnitId: l.UnitId,
            Quantity: qty.Value,
            UnitPrice: price.Value,
            DiscountPercentage: discountPct,
            DiscountAmount: discountAmt,
            TaxPercentage: taxPct,
            TaxAmount: taxAmt,
            PumpCost: pumpCost,
            TransportCost: transportCost,
            CostCenterId: l.CostCenterId,
            WarehouseId: l.WarehouseId,
            Notes: l.Notes,
            ConcreteGrade: l.ConcreteGrade,
            DisplayOrder: l.DisplayOrder);

        return (cmd, null, null);
    }

    private static SalesInvoiceSummaryDto MapSummary(SalesInvoiceSummaryResult r) =>
        new()
        {
            Id = r.Id,
            CompanyId = r.CompanyId,
            BranchId = r.BranchId,
            PatternRevisionId = r.PatternRevisionId,
            PatternCode = r.PatternCode,
            PatternName = r.PatternName,
            CustomerId = r.CustomerId,
            CustomerCode = r.CustomerCode,
            CustomerName = r.CustomerName,
            WarehouseId = r.WarehouseId,
            WarehouseCode = r.WarehouseCode,
            InvoiceNumber = r.InvoiceNumber,
            InvoiceDateUtc = r.InvoiceDateUtc,
            PaymentMethod = (ContractPaymentMethod)r.PaymentMethod,
            CurrencyCode = r.CurrencyCode,
            ExchangeRate = FinancialDecimalText.Format(r.ExchangeRate, FinancialDecimalText.RateScale),
            Status = (ContractStatus)r.Status,
            SubTotal = FinancialDecimalText.Format(r.SubTotal, FinancialDecimalText.MoneyScale),
            DiscountTotal = FinancialDecimalText.Format(r.DiscountTotal, FinancialDecimalText.MoneyScale),
            TaxTotal = FinancialDecimalText.Format(r.TaxTotal, FinancialDecimalText.MoneyScale),
            AdditionsTotal = FinancialDecimalText.Format(r.AdditionsTotal, FinancialDecimalText.MoneyScale),
            GrandTotal = FinancialDecimalText.Format(r.GrandTotal, FinancialDecimalText.MoneyScale),
            JournalEntryId = r.JournalEntryId,
            CreatedAtUtc = r.CreatedAtUtc,
            PostedAtUtc = r.PostedAtUtc,
            ReturnOfInvoiceId = r.ReturnOfInvoiceId,
            SettledAmount = r.SettledAmount.HasValue ? FinancialDecimalText.Format(r.SettledAmount.Value, FinancialDecimalText.MoneyScale) : null,
            ReturnedAmount = r.ReturnedAmount.HasValue ? FinancialDecimalText.Format(r.ReturnedAmount.Value, FinancialDecimalText.MoneyScale) : null,
            OutstandingAmount = r.OutstandingAmount.HasValue ? FinancialDecimalText.Format(r.OutstandingAmount.Value, FinancialDecimalText.MoneyScale) : null,
            Version = ConcurrencyToken.FromBytes(r.RowVersion).ToBase64(),
        };

    private static SalesInvoiceDetailDto MapDetail(SalesInvoiceDetailResult r) =>
        new()
        {
            Id = r.Id,
            CompanyId = r.CompanyId,
            BranchId = r.BranchId,
            PatternRevisionId = r.PatternRevisionId,
            PatternCode = r.PatternCode,
            PatternName = r.PatternName,
            CustomerId = r.CustomerId,
            CustomerCode = r.CustomerCode,
            CustomerName = r.CustomerName,
            WarehouseId = r.WarehouseId,
            WarehouseCode = r.WarehouseCode,
            InvoiceNumber = r.InvoiceNumber,
            InvoiceDateUtc = r.InvoiceDateUtc,
            PaymentMethod = (ContractPaymentMethod)r.PaymentMethod,
            CurrencyCode = r.CurrencyCode,
            ExchangeRate = FinancialDecimalText.Format(r.ExchangeRate, FinancialDecimalText.RateScale),
            Status = (ContractStatus)r.Status,
            CostCenterId = r.CostCenterId,
            CostCenterCode = r.CostCenterCode,
            Notes = r.Notes,
            CustomerAccountId = r.CustomerAccountId,
            SellerCode = r.SellerCode,
            DistributorCode = r.DistributorCode,
            SubTotal = FinancialDecimalText.Format(r.SubTotal, FinancialDecimalText.MoneyScale),
            DiscountTotal = FinancialDecimalText.Format(r.DiscountTotal, FinancialDecimalText.MoneyScale),
            TaxTotal = FinancialDecimalText.Format(r.TaxTotal, FinancialDecimalText.MoneyScale),
            AdditionsTotal = FinancialDecimalText.Format(r.AdditionsTotal, FinancialDecimalText.MoneyScale),
            GrandTotal = FinancialDecimalText.Format(r.GrandTotal, FinancialDecimalText.MoneyScale),
            JournalEntryId = r.JournalEntryId,
            ReversedByInvoiceId = r.ReversedByInvoiceId,
            ReversesInvoiceId = r.ReversesInvoiceId,
            ReturnOfInvoiceId = r.ReturnOfInvoiceId,
            CustomerIsCashCustomer = r.CustomerIsCashCustomer,
            CreditBlocked = r.CreditBlocked,
            CreditPolicyKind = r.CreditPolicyKind,
            CreditExposureBase = r.CreditExposureBase is { } exposure ? FinancialDecimalText.Format(exposure, FinancialDecimalText.MoneyScale) : null,
            CreditLimitBase = r.CreditLimitBase is { } limit ? FinancialDecimalText.Format(limit, FinancialDecimalText.MoneyScale) : null,
            CreditProjectedExposureBase = r.CreditProjectedExposureBase is { } projected ? FinancialDecimalText.Format(projected, FinancialDecimalText.MoneyScale) : null,
            CreditPolicyRequiresAction = r.CreditPolicyRequiresAction,
            CreditOverrideApplied = r.CreditOverrideApplied,
            CreditOverrideReason = r.CreditOverrideReason,
            CreditPolicyActorUserId = r.CreditPolicyActorUserId,
            CreditPolicyRecordedAtUtc = r.CreditPolicyRecordedAtUtc,
            SettledAmount = r.SettledAmount is { } settled ? FinancialDecimalText.Format(settled, FinancialDecimalText.MoneyScale) : null,
            ReturnedAmount = r.ReturnedAmount is { } returned ? FinancialDecimalText.Format(returned, FinancialDecimalText.MoneyScale) : null,
            OutstandingAmount = r.OutstandingAmount is { } outstanding ? FinancialDecimalText.Format(outstanding, FinancialDecimalText.MoneyScale) : null,
            CreatedByUserId = r.CreatedByUserId,
            CreatedAtUtc = r.CreatedAtUtc,
            ApprovedByUserId = r.ApprovedByUserId,
            ApprovedAtUtc = r.ApprovedAtUtc,
            PostedByUserId = r.PostedByUserId,
            PostedAtUtc = r.PostedAtUtc,
            Version = ConcurrencyToken.FromBytes(r.RowVersion).ToBase64(),
            Lines = r.Lines.Select(MapLine).ToList(),
            Adjustments = r.Adjustments.Select(a => new SalesInvoiceAdjustmentDto
            {
                Id = a.Id, Kind = (SalesInvoiceAdjustmentKind)a.Kind, AccountId = a.AccountId, AccountCode = a.AccountCode, AccountNameArabic = a.AccountNameArabic, CounterAccountId = a.CounterAccountId,
                Amount = FinancialDecimalText.Format(a.Amount, FinancialDecimalText.MoneyScale), Percentage = FinancialDecimalText.Format(a.Percentage, FinancialDecimalText.MoneyScale),
                Notes = a.Notes, CostCenterId = a.CostCenterId, DisplayOrder = a.DisplayOrder,
            }).ToList(),
        };

    private static SalesInvoiceLineDto MapLine(SalesInvoiceLineResult l) =>
        new()
        {
            Id = l.Id,
            SalesInvoiceId = l.SalesInvoiceId,
            ItemId = l.ItemId,
            ItemCode = l.ItemCode,
            ItemNameArabic = l.ItemNameArabic,
            UnitId = l.UnitId,
            UnitCode = l.UnitCode,
            Quantity = FinancialDecimalText.Format(l.Quantity, FinancialDecimalText.MoneyScale),
            UnitPrice = FinancialDecimalText.Format(l.UnitPrice, FinancialDecimalText.MoneyScale),
            DiscountPercentage = FinancialDecimalText.Format(l.DiscountPercentage, FinancialDecimalText.MoneyScale),
            DiscountAmount = FinancialDecimalText.Format(l.DiscountAmount, FinancialDecimalText.MoneyScale),
            TaxPercentage = FinancialDecimalText.Format(l.TaxPercentage, FinancialDecimalText.MoneyScale),
            TaxAmount = FinancialDecimalText.Format(l.TaxAmount, FinancialDecimalText.MoneyScale),
            PumpCost = FinancialDecimalText.Format(l.PumpCost, FinancialDecimalText.MoneyScale),
            TransportCost = FinancialDecimalText.Format(l.TransportCost, FinancialDecimalText.MoneyScale),
            LineTotal = FinancialDecimalText.Format(l.LineTotal, FinancialDecimalText.MoneyScale),
            CostCenterId = l.CostCenterId,
            CostCenterCode = l.CostCenterCode,
            WarehouseId = l.WarehouseId,
            WarehouseCode = l.WarehouseCode,
            Notes = l.Notes,
            DisplayOrder = l.DisplayOrder,
            ConcreteGrade = l.ConcreteGrade,
            SourceLineId = l.SourceLineId,
            ReturnedQuantity = FinancialDecimalText.Format(l.ReturnedQuantity, FinancialDecimalText.MoneyScale),
        };
}
