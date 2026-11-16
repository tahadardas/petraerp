using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Security;
using PetraERP.Application.Treasury;
using PetraERP.Application.Common;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Treasury;
using AppKind = PetraERP.Application.Treasury.CommercialPaperKind;
using AppStatus = PetraERP.Application.Treasury.CommercialPaperStatus;
using AppSeverity = PetraERP.Application.Treasury.CommercialPaperAlertSeverity;

namespace PetraERP.Api.Endpoints;

internal static class CommercialPaperEndpoints
{
    internal static void MapCommercialPaperEndpoints(this RouteGroupBuilder api)
    {
        var root = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/treasury/commercial-papers")
            .WithTags("CommercialPapers");

        var view = PetraPolicies.Permission(PermissionCatalog.TreasuryVouchersView);
        var manage = PetraPolicies.Permission(PermissionCatalog.TreasuryVouchersManage);

        root.MapGet(string.Empty, ListAsync)
            .WithName("ListCommercialPapers")
            .WithSummary("Lists commercial papers (cheques, promissory notes) with status and due date filters.")
            .RequireAuthorization(view)
            .Produces<CommercialPaperListResponse>()
            .ProducesStandardProblems();

        root.MapGet("/{paperId:guid}", GetAsync)
            .WithName("GetCommercialPaper")
            .WithSummary("Gets a commercial paper by its identifier.")
            .RequireAuthorization(view)
            .Produces<CommercialPaperDto>()
            .ProducesStandardProblems();

        root.MapPost(string.Empty, RegisterAsync)
            .WithName("RegisterCommercialPaper")
            .WithSummary("Registers a new cheque or commercial paper.")
            .RequireAuthorization(manage)
            .Validate<RegisterCommercialPaperRequest>()
            .Produces<CommercialPaperDto>(StatusCodes.Status201Created)
            .ProducesStandardProblems();

        root.MapPost("/{paperId:guid}/status", TransitionStatusAsync)
            .WithName("TransitionCommercialPaperStatus")
            .WithSummary("Transitions the commercial paper lifecycle status (UnderCollection, Cleared, Bounced, Endorsed, Cancelled) and posts automated ledger entries.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.TreasuryVouchersPost))
            .Validate<TransitionCommercialPaperStatusRequest>()
            .Produces<CommercialPaperDto>()
            .ProducesStandardProblems();

        root.MapGet("/alerts", GetAlertsAsync)
            .WithName("GetCommercialPaperAlerts")
            .WithSummary("Retrieves overdue, due today, and due soon commercial paper alerts.")
            .RequireAuthorization(view)
            .Produces<CommercialPaperAlertsResponse>()
            .ProducesStandardProblems();
    }

    private static async Task<IResult> ListAsync(
        Guid companyId,
        Guid branchId,
        [FromQuery] Contracts.Treasury.CommercialPaperStatus? status,
        [FromQuery] Contracts.Treasury.CommercialPaperKind? kind,
        [FromQuery] Guid? partyId,
        [FromQuery] DateOnly? fromDueDate,
        [FromQuery] DateOnly? toDueDate,
        [FromQuery] string? query,
        [FromServices] ICommercialPaperService service,
        CancellationToken cancellationToken)
    {
        var searchQuery = new CommercialPaperSearchQuery
        {
            Status = status.HasValue ? (AppStatus)status.Value : null,
            Kind = kind.HasValue ? (AppKind)kind.Value : null,
            PartyId = partyId,
            FromDueDate = fromDueDate,
            ToDueDate = toDueDate,
            Query = query
        };

        var items = await service.ListAsync(companyId, branchId, searchQuery, cancellationToken).ConfigureAwait(false);
        var dtos = items.Select(MapToContract).ToList();
        var totalAmount = items.Sum(i => i.Amount).ToString("F4", System.Globalization.CultureInfo.InvariantCulture);

        return TypedResults.Ok(new CommercialPaperListResponse
        {
            Items = dtos,
            TotalAmount = totalAmount,
            TotalCount = dtos.Count
        });
    }

    private static async Task<IResult> GetAsync(
        Guid companyId,
        Guid branchId,
        Guid paperId,
        [FromServices] ICommercialPaperService service,
        CancellationToken cancellationToken)
    {
        var item = await service.GetByIdAsync(companyId, branchId, paperId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(MapToContract(item));
    }

    private static async Task<IResult> RegisterAsync(
        Guid companyId,
        Guid branchId,
        [FromBody] RegisterCommercialPaperRequest request,
        [FromServices] ICommercialPaperService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new RegisterCommercialPaperCommand
        {
            PaperKind = (AppKind)request.PaperKind,
            PaperNumber = request.PaperNumber,
            BankName = request.BankName,
            BranchName = request.BranchName,
            DrawerName = request.DrawerName,
            PayeeName = request.PayeeName,
            IssueDate = request.IssueDate,
            DueDate = request.DueDate,
            Amount = decimal.Parse(request.Amount, System.Globalization.CultureInfo.InvariantCulture),
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode) ? null : request.CurrencyCode.Trim().ToUpperInvariant(),
            ExchangeRate = string.IsNullOrEmpty(request.ExchangeRate) ? null
                : FinancialDecimalText.TryParse(request.ExchangeRate, FinancialDecimalText.RateScale, FinancialDecimalText.RateIntegerDigits, out var rate) && rate.Value > 0
                    ? rate.Value
                    : throw new RequestValidationException("treasury.rate_invalid", "ExchangeRate must be a positive decimal string.", "exchangeRate"),
            PartyId = request.PartyId,
            PortfolioAccountId = request.PortfolioAccountId,
            Notes = request.Notes
        };

        var item = await service.RegisterAsync(companyId, branchId, httpContext.RequireSession().UserId, command, cancellationToken).ConfigureAwait(false);
        var contract = MapToContract(item);
        return TypedResults.Created($"/companies/{companyId}/branches/{branchId}/treasury/commercial-papers/{item.Id}", contract);
    }

    private static async Task<IResult> TransitionStatusAsync(
        Guid companyId,
        Guid branchId,
        Guid paperId,
        [FromBody] TransitionCommercialPaperStatusRequest request,
        [FromServices] ICommercialPaperService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new TransitionCommercialPaperStatusCommand
        {
            NewStatus = (AppStatus)request.NewStatus,
            TargetAccountId = request.TargetAccountId,
            EndorsedPartyId = request.EndorsedPartyId,
            TransitionDate = request.TransitionDate,
            Reason = request.Reason
        };

        var item = await service.TransitionStatusAsync(companyId, branchId, httpContext.RequireSession().UserId, paperId, command, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(MapToContract(item));
    }

    private static async Task<IResult> GetAlertsAsync(
        Guid companyId,
        Guid branchId,
        [FromQuery] int? daysAhead,
        [FromServices] ICommercialPaperService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAlertsAsync(companyId, branchId, daysAhead ?? 7, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new CommercialPaperAlertsResponse
        {
            OverdueCount = result.OverdueCount,
            DueTodayCount = result.DueTodayCount,
            DueSoonCount = result.DueSoonCount,
            Alerts = result.Alerts.Select(MapToContract).ToList()
        });
    }

    private static CommercialPaperDto MapToContract(CommercialPaperResult item) => new()
    {
        Id = item.Id,
        CompanyId = item.CompanyId,
        BranchId = item.BranchId,
        PaperKind = (Contracts.Treasury.CommercialPaperKind)item.PaperKind,
        PaperKindName = item.PaperKind switch
        {
            AppKind.ChequeReceived => "شيك وارد",
            AppKind.ChequeIssued => "شيك صادر",
            _ => "كمبيالة"
        },
        PaperNumber = item.PaperNumber,
        BankName = item.BankName,
        BranchName = item.BranchName,
        DrawerName = item.DrawerName,
        PayeeName = item.PayeeName,
        IssueDate = item.IssueDate,
        DueDate = item.DueDate,
        Amount = item.Amount.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
        CurrencyCode = item.CurrencyCode,
        ExchangeRate = FinancialDecimalText.Format(item.ExchangeRate, FinancialDecimalText.RateScale),
        BaseAmount = item.BaseAmount.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
        Status = (Contracts.Treasury.CommercialPaperStatus)item.Status,
        StatusName = item.Status switch
        {
            AppStatus.Received => "مستلم",
            AppStatus.UnderCollection => "برسم التحصيل",
            AppStatus.Cleared => "محصل",
            AppStatus.Bounced => "مرتجع",
            AppStatus.Cancelled => "ملغى",
            AppStatus.Endorsed => "مظهر",
            _ => "غير معروف"
        },
        PartyId = item.PartyId,
        PartyName = item.PartyName,
        IntermediaryAccountId = item.IntermediaryAccountId,
        BankAccountId = item.BankAccountId,
        EndorsedPartyId = item.EndorsedPartyId,
        EndorsedPartyName = item.EndorsedPartyName,
        VoucherId = item.VoucherId,
        LastJournalEntryId = item.LastJournalEntryId,
        StatusDate = item.StatusDate,
        Notes = item.Notes,
        Severity = (Contracts.Treasury.CommercialPaperAlertSeverity)item.Severity
    };
}
