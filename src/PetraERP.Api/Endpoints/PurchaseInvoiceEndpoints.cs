using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.DocumentProcessing;
using PetraERP.Application.Purchases;
using PetraERP.Application.Security;
using PetraERP.Contracts.DocumentProcessing;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Purchases;
using ContractPaymentMethod = PetraERP.Contracts.Sales.PaymentMethod;
using DomainPaymentMethod = PetraERP.Domain.Sales.PaymentMethod;
using DomainStatus = PetraERP.Domain.Purchases.PurchaseInvoiceStatus;

namespace PetraERP.Api.Endpoints;

internal static class PurchaseInvoiceEndpoints
{
    internal static void MapPurchaseInvoiceEndpoints(this RouteGroupBuilder api)
    {
        var invoices = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/purchases/invoices").WithTags("PurchaseInvoices");
        var view = PetraPolicies.Permission(PermissionCatalog.PurchaseInvoicesView);
        var manage = PetraPolicies.Permission(PermissionCatalog.PurchaseInvoicesManage);

        invoices.MapGet(string.Empty, ListAsync).WithName("ListPurchaseInvoices").WithSummary("Lists purchase invoices and returns in the branch.")
            .RequireAuthorization(view).Produces<PurchasePageResponse>().ProducesStandardProblems();
        invoices.MapGet("/{invoiceId:guid}", GetAsync).WithName("GetPurchaseInvoice").WithSummary("Gets a purchase invoice or return with lines.")
            .RequireAuthorization(view).Produces<PurchaseDetailDto>().ProducesStandardProblems();
        invoices.MapPost(string.Empty, CreateAsync).WithName("CreatePurchaseInvoice").WithSummary("Creates a draft purchase invoice.")
            .RequireAuthorization(manage).Validate<SavePurchaseInvoiceRequest>().Produces<PurchaseDetailDto>(StatusCodes.Status201Created).ProducesStandardProblems();
        invoices.MapPut("/{invoiceId:guid}", UpdateAsync).WithName("UpdatePurchaseInvoice").WithSummary("Replaces a draft purchase invoice; requires ExpectedVersion.")
            .RequireAuthorization(manage).Validate<SavePurchaseInvoiceRequest>().Produces<PurchaseDetailDto>().ProducesStandardProblems();
        invoices.MapPost("/save-and-process", SaveAndProcessCreateAsync)
            .RetryOnDeadlock().WithName("SaveAndProcessPurchaseInvoice")
            .WithSummary("Saves a purchase invoice and applies the company posting policy: draft, approve-only or approve-and-post.")
            .RequireAuthorization(manage).Validate<SaveAndProcessPurchaseInvoiceRequest>()
            .Produces<DocumentProcessingResultDto>(StatusCodes.Status201Created).ProducesStandardProblems();
        invoices.MapPut("/{invoiceId:guid}/save-and-process", SaveAndProcessUpdateAsync)
            .RetryOnDeadlock().WithName("SaveAndProcessUpdatePurchaseInvoice")
            .WithSummary("Updates a draft purchase invoice and applies the company posting policy.")
            .RequireAuthorization(manage).Validate<SaveAndProcessPurchaseInvoiceRequest>()
            .Produces<DocumentProcessingResultDto>().ProducesStandardProblems();
        invoices.MapDelete("/{invoiceId:guid}", DeleteAsync).WithName("DeletePurchaseInvoice").WithSummary("Deletes a never-posted draft purchase invoice.")
            .RequireAuthorization(manage).Produces(StatusCodes.Status204NoContent).ProducesStandardProblems();
        invoices.MapPost("/{invoiceId:guid}/approve", ApproveAsync).WithName("ApprovePurchaseInvoice")
            .WithSummary("Approves a draft purchase invoice.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.PurchaseInvoicesApprove)).Validate<ApprovePurchaseInvoiceRequest>()
            .Produces<PurchaseDetailDto>().ProducesStandardProblems();
        invoices.MapPost("/{invoiceId:guid}/post", PostAsync)
            .RetryOnDeadlock().WithName("PostPurchaseInvoice")
            .WithSummary("Atomically numbers the invoice, receives stock at cost in base units and posts the supplier payable journal.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.PurchaseInvoicesPost)).Validate<PostPurchaseInvoiceRequest>()
            .Produces<PurchaseDetailDto>().ProducesStandardProblems();
        invoices.MapPost("/{invoiceId:guid}/reverse", ReverseAsync).WithName("ReversePurchaseInvoice")
            .WithSummary("Reverses a posted purchase invoice with a linked reversal document, a mirrored journal and stock movements back at original cost.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.PurchaseInvoicesPost)).Validate<ReversePurchaseInvoiceRequest>()
            .Produces<PurchaseDetailDto>().ProducesStandardProblems();
        invoices.MapPost("/{invoiceId:guid}/returns", ReturnAsync).WithName("ReturnPurchaseInvoice")
            .WithSummary("Posts a return to the supplier at the original price and rate; returns the new return document.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.PurchaseInvoicesReturn)).Validate<CreatePurchaseReturnRequest>()
            .Produces<PurchaseDetailDto>(StatusCodes.Status201Created).ProducesStandardProblems();
    }

    private static async Task<IResult> ListAsync(Guid companyId, Guid branchId, [FromQuery] Guid? supplierId, [FromQuery] PurchaseInvoiceStatus? status,
        [FromQuery] DateTimeOffset? fromDate, [FromQuery] DateTimeOffset? toDate, [FromQuery] Guid? returnOfInvoiceId, IPurchaseInvoiceService service,
        CancellationToken cancellationToken, int page = 1, int pageSize = 50)
    {
        var result = await service.ListAsync(companyId, branchId, new PurchaseSearchQuery(supplierId, (DomainStatus?)status, fromDate, toDate,
            returnOfInvoiceId, page, pageSize), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new PurchasePageResponse
        {
            Page = result.Page, PageSize = result.PageSize, TotalCount = result.TotalCount,
            Items = result.Items.Select(i => new PurchaseSummaryDto
            {
                Id = i.Id, InvoiceNumber = i.InvoiceNumber, SupplierReference = i.SupplierReference, InvoiceDateUtc = i.InvoiceDateUtc,
                SupplierId = i.SupplierId, SupplierName = i.SupplierName, CurrencyCode = i.CurrencyCode, GrandTotal = Money(i.GrandTotal),
                Status = (PurchaseInvoiceStatus)i.Status, ReturnOfInvoiceId = i.ReturnOfInvoiceId,
                SettledAmount = i.SettledAmount.HasValue ? Money(i.SettledAmount.Value) : null,
                ReturnedAmount = i.ReturnedAmount.HasValue ? Money(i.ReturnedAmount.Value) : null,
                OutstandingAmount = i.OutstandingAmount.HasValue ? Money(i.OutstandingAmount.Value) : null,
                Version = ConcurrencyToken.FromBytes(i.RowVersion).ToBase64(),
            }).ToList(),
        });
    }

    private static async Task<IResult> GetAsync(Guid companyId, Guid branchId, Guid invoiceId, IPurchaseInvoiceService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(Map(await service.GetAsync(companyId, branchId, invoiceId, cancellationToken).ConfigureAwait(false)));

    private static async Task<IResult> CreateAsync(Guid companyId, Guid branchId, [FromBody] SavePurchaseInvoiceRequest request,
        IPurchaseInvoiceService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var (command, error) = Parse(request);
        if (error is not null) return error;
        var result = await service.CreateDraftAsync(companyId, branchId, httpContext.RequireSession().UserId, command!, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/companies/{companyId}/branches/{branchId}/purchases/invoices/{result.Id}", Map(result));
    }

    private static async Task<IResult> UpdateAsync(Guid companyId, Guid branchId, Guid invoiceId, [FromBody] SavePurchaseInvoiceRequest request,
        IPurchaseInvoiceService service, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(request.ExpectedVersion, out var version)) return BadRequest("ExpectedVersion must be the Version returned by the server.");
        var (command, error) = Parse(request);
        if (error is not null) return error;
        return TypedResults.Ok(Map(await service.UpdateDraftAsync(companyId, branchId, invoiceId, command! with { ExpectedVersion = version }, cancellationToken)
            .ConfigureAwait(false)));
    }

    private static async Task<IResult> SaveAndProcessCreateAsync(Guid companyId, Guid branchId,
        [FromBody] SaveAndProcessPurchaseInvoiceRequest request, IDocumentSaveAndProcessService processing, IAccessEvaluator access,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (request.InvoiceId is not null) return BadRequest("Use PUT /{invoiceId}/save-and-process to update an existing invoice.");
        var (command, error) = Parse(request.Document);
        if (error is not null) return error;
        var session = httpContext.RequireSession();
        var permissions = await ProcessingPermissionsAsync(access, session.UserId, companyId, branchId, cancellationToken).ConfigureAwait(false);
        var result = await processing.CreatePurchaseInvoiceAsync(companyId, branchId, session.UserId, permissions, command!,
            request.SaveAsDraft, PostingIntent(request), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/companies/{companyId}/branches/{branchId}/purchases/invoices/{result.DocumentId}", MapProcessing(result));
    }

    private static async Task<IResult> SaveAndProcessUpdateAsync(Guid companyId, Guid branchId, Guid invoiceId,
        [FromBody] SaveAndProcessPurchaseInvoiceRequest request, IDocumentSaveAndProcessService processing, IAccessEvaluator access,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (request.InvoiceId is not null && request.InvoiceId != invoiceId) return BadRequest("InvoiceId in the body must match the route.");
        var versionText = request.ExpectedVersion ?? request.Document.ExpectedVersion;
        if (!ConcurrencyToken.TryParse(versionText, out var version)) return BadRequest("ExpectedVersion must be the Version returned by the server.");
        var (command, error) = Parse(request.Document);
        if (error is not null) return error;
        var session = httpContext.RequireSession();
        var permissions = await ProcessingPermissionsAsync(access, session.UserId, companyId, branchId, cancellationToken).ConfigureAwait(false);
        var result = await processing.UpdatePurchaseInvoiceAsync(companyId, branchId, session.UserId, permissions, invoiceId,
            command! with { ExpectedVersion = version }, request.SaveAsDraft, PostingIntent(request), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(MapProcessing(result));
    }

    private static DocumentPostingIntent? PostingIntent(SaveAndProcessPurchaseInvoiceRequest request) =>
        new(request.IdempotencyKey, request.ManualNumber);

    private static async Task<DocumentProcessingPermissions> ProcessingPermissionsAsync(IAccessEvaluator access, Guid userId,
        Guid companyId, Guid branchId, CancellationToken cancellationToken)
    {
        var canApprove = await access.EvaluatePermissionAsync(userId, PermissionCatalog.Get(PermissionCatalog.PurchaseInvoicesApprove), companyId, branchId, cancellationToken)
            .ConfigureAwait(false) == AccessDecision.Granted;
        var canPost = await access.EvaluatePermissionAsync(userId, PermissionCatalog.Get(PermissionCatalog.PurchaseInvoicesPost), companyId, branchId, cancellationToken)
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

    private static async Task<IResult> ApproveAsync(Guid companyId, Guid branchId, Guid invoiceId, [FromBody] ApprovePurchaseInvoiceRequest request,
        IPurchaseInvoiceService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(request.ExpectedVersion, out var version)) return BadRequest("ExpectedVersion must be the Version returned by the server.");
        var result = await service.ApproveAsync(companyId, branchId, invoiceId, httpContext.RequireSession().UserId,
            new ApprovePurchaseInvoiceCommand(version, request.Notes), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(Map(result));
    }

    private static async Task<IResult> DeleteAsync(Guid companyId, Guid branchId, Guid invoiceId, [FromQuery] string? expectedVersion,
        IPurchaseInvoiceService service, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(expectedVersion, out var version)) return BadRequest("expectedVersion must be the Version returned by the server.");
        await service.DeleteDraftAsync(companyId, branchId, invoiceId, version, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> PostAsync(Guid companyId, Guid branchId, Guid invoiceId, [FromBody] PostPurchaseInvoiceRequest request,
        IPurchaseInvoiceService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(request.ExpectedVersion, out var version)) return BadRequest("ExpectedVersion must be the Version returned by the server.");
        return TypedResults.Ok(Map(await service.PostAsync(companyId, branchId, invoiceId, httpContext.RequireSession().UserId,
            new PostPurchaseInvoiceCommand(version, request.IdempotencyKey, request.ManualNumber), cancellationToken).ConfigureAwait(false)));
    }

    private static async Task<IResult> ReverseAsync(Guid companyId, Guid branchId, Guid invoiceId, [FromBody] ReversePurchaseInvoiceRequest request,
        IPurchaseInvoiceService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(request.ExpectedVersion, out var version)) return BadRequest("ExpectedVersion must be the Version returned by the server.");
        return TypedResults.Ok(Map(await service.ReverseInvoiceAsync(companyId, branchId, invoiceId, httpContext.RequireSession().UserId,
            new ReversePurchaseInvoiceCommand(version, request.IdempotencyKey, request.Reason), cancellationToken).ConfigureAwait(false)));
    }

    private static async Task<IResult> ReturnAsync(Guid companyId, Guid branchId, Guid invoiceId, [FromBody] CreatePurchaseReturnRequest request,
        IPurchaseInvoiceService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var lines = new List<PurchaseReturnLineCommand>();
        foreach (var line in request.Lines ?? [])
        {
            if (!TryMoney(line.Quantity, out var quantity) || quantity <= 0) return BadRequest("Return quantity must be a positive decimal string.");
            lines.Add(new(line.SourceLineId, quantity));
        }
        var result = await service.ReturnAsync(companyId, branchId, invoiceId, httpContext.RequireSession().UserId,
            new CreatePurchaseReturnCommand(request.IdempotencyKey, request.Reason, lines) { VarianceAccountId = request.VarianceAccountId }, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/companies/{companyId}/branches/{branchId}/purchases/invoices/{result.Id}", Map(result));
    }

    private static (SavePurchaseInvoiceCommand? Command, IResult? Error) Parse(SavePurchaseInvoiceRequest request)
    {
        if (!Enum.IsDefined(request.PaymentMethod)) return (null, BadRequest("PaymentMethod must be Credit or Cash."));
        decimal? exchangeRate = null;
        if (!string.IsNullOrEmpty(request.ExchangeRate))
        {
            if (!FinancialDecimalText.TryParse(request.ExchangeRate, FinancialDecimalText.RateScale, FinancialDecimalText.RateIntegerDigits, out var rate) || rate.Value <= 0)
                return (null, BadRequest("ExchangeRate must be a positive decimal string."));
            exchangeRate = rate.Value;
        }
        var lines = new List<PurchaseLineCommand>();
        foreach (var l in request.Lines ?? [])
        {
            if (!TryMoney(l.Quantity, out var quantity) || quantity <= 0) return (null, BadRequest("Quantity must be a positive decimal string."));
            if (!TryMoney(l.UnitCost, out var cost) || cost < 0) return (null, BadRequest("UnitCost must be a non-negative decimal string."));
            var discount = 0m; var tax = 0m;
            if (l.DiscountAmount is not null && (!TryMoney(l.DiscountAmount, out discount) || discount < 0)) return (null, BadRequest("DiscountAmount must be non-negative."));
            if (l.TaxAmount is not null && (!TryMoney(l.TaxAmount, out tax) || tax < 0)) return (null, BadRequest("TaxAmount must be non-negative."));
            lines.Add(new(l.ItemId, l.UnitId, quantity, cost, discount, tax, l.CostCenterId, l.Notes));
        }
        var adjustments = new List<PurchaseAdjustmentCommand>();
        foreach (var a in request.Adjustments ?? [])
        {
            if (!TryMoney(a.Amount, out var amount) || amount <= 0) return (null, BadRequest("Adjustment Amount must be a positive decimal string."));
            var percentage = 0m;
            if (!string.IsNullOrEmpty(a.Percentage) && (!TryMoney(a.Percentage, out percentage) || percentage < 0 || percentage > 100))
                return (null, BadRequest("Adjustment Percentage must be between 0 and 100."));
            adjustments.Add(new((PetraERP.Domain.Sales.SalesInvoiceAdjustmentKind)a.Kind, a.AccountId, amount, percentage, a.Notes, a.CostCenterId, a.DisplayOrder, a.CounterAccountId));
        }
        return (new SavePurchaseInvoiceCommand(request.PatternRevisionId, request.SupplierId, request.WarehouseId, request.InvoiceDateUtc,
            (DomainPaymentMethod)request.PaymentMethod, string.IsNullOrWhiteSpace(request.CurrencyCode) ? null : request.CurrencyCode.Trim().ToUpperInvariant(), exchangeRate, request.SupplierReference, request.Notes, lines) { Adjustments = adjustments, SellerCode = request.SellerCode, DistributorCode = request.DistributorCode }, null);
    }

    private static PurchaseDetailDto Map(PurchaseDetailResult r) => new()
    {
        VarianceAccountId = r.VarianceAccountId,
        Id = r.Id, CompanyId = r.CompanyId, BranchId = r.BranchId, PatternRevisionId = r.PatternRevisionId, PatternCode = r.PatternCode,
        PatternName = r.PatternName, SupplierId = r.SupplierId, SupplierCode = r.SupplierCode, SupplierName = r.SupplierName, WarehouseId = r.WarehouseId,
        WarehouseCode = r.WarehouseCode, InvoiceNumber = r.InvoiceNumber, SupplierReference = r.SupplierReference, InvoiceDateUtc = r.InvoiceDateUtc,
        PaymentMethod = (ContractPaymentMethod)r.PaymentMethod, CurrencyCode = r.CurrencyCode,
        ExchangeRate = FinancialDecimalText.Format(r.ExchangeRate, FinancialDecimalText.RateScale), Status = (PurchaseInvoiceStatus)r.Status, Notes = r.Notes, SellerCode = r.SellerCode, DistributorCode = r.DistributorCode,
        SubTotal = Money(r.SubTotal), DiscountTotal = Money(r.DiscountTotal), TaxTotal = Money(r.TaxTotal), GrandTotal = Money(r.GrandTotal),
        AdditionsTotal = Money(r.AdditionsTotal),
        Adjustments = r.Adjustments.Select(a => new PurchaseAdjustmentDto
        {
            Id = a.Id, Kind = (PetraERP.Contracts.Sales.SalesInvoiceAdjustmentKind)a.Kind, AccountId = a.AccountId, CounterAccountId = a.CounterAccountId, AccountCode = a.AccountCode,
            AccountNameArabic = a.AccountNameArabic, Amount = Money(a.Amount), Percentage = Money(a.Percentage), Notes = a.Notes, CostCenterId = a.CostCenterId, DisplayOrder = a.DisplayOrder,
        }).ToList(),
        JournalEntryId = r.JournalEntryId, ReturnOfInvoiceId = r.ReturnOfInvoiceId, PostedAtUtc = r.PostedAtUtc,
        ReversedByInvoiceId = r.ReversedByInvoiceId, ReversesInvoiceId = r.ReversesInvoiceId,
        SettledAmount = r.SettledAmount.HasValue ? Money(r.SettledAmount.Value) : null,
        ReturnedAmount = r.ReturnedAmount.HasValue ? Money(r.ReturnedAmount.Value) : null,
        OutstandingAmount = r.OutstandingAmount.HasValue ? Money(r.OutstandingAmount.Value) : null,
        Version = ConcurrencyToken.FromBytes(r.RowVersion).ToBase64(),
        Lines = r.Lines.Select(l => new PurchaseLineDto
        {
            Id = l.Id, ItemId = l.ItemId, ItemCode = l.ItemCode, ItemName = l.ItemName, UnitId = l.UnitId, UnitCode = l.UnitCode, Quantity = Money(l.Quantity),
            UnitCost = Money(l.UnitCost), DiscountAmount = Money(l.DiscountAmount), TaxAmount = Money(l.TaxAmount), LineTotal = Money(l.LineTotal),
            CostCenterId = l.CostCenterId, Notes = l.Notes, DisplayOrder = l.DisplayOrder, SourceLineId = l.SourceLineId, ReturnedQuantity = Money(l.ReturnedQuantity),
        }).ToList(),
    };

    private static bool TryMoney(string? text, out decimal value)
    {
        value = 0;
        if (!FinancialDecimalText.TryParse(text, FinancialDecimalText.MoneyScale, 15, out var parsed)) return false;
        value = parsed.Value;
        return true;
    }

    private static string Money(decimal value) => FinancialDecimalText.Format(value, FinancialDecimalText.MoneyScale);

    private static ProblemHttpResult BadRequest(string detail) => TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}
