using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Application.Treasury;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Treasury;
using DomainKind = PetraERP.Domain.Treasury.VoucherKind;
using DomainSettlement = PetraERP.Domain.Treasury.SettlementMethod;
using DomainStatus = PetraERP.Domain.Treasury.VoucherStatus;

namespace PetraERP.Api.Endpoints;

internal static partial class TreasuryVoucherEndpoints
{
    internal static void MapTreasuryVoucherEndpoints(this RouteGroupBuilder api)
    {
        var root = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/treasury").WithTags("TreasuryVouchers");
        var vouchers = root.MapGroup("/vouchers");
        var view = PetraPolicies.Permission(PermissionCatalog.TreasuryVouchersView);
        var manage = PetraPolicies.Permission(PermissionCatalog.TreasuryVouchersManage);
        MapSettlementEndpoints(root, vouchers);
        MapVoucherSaveAndProcessEndpoints(vouchers, manage);

        vouchers.MapGet(string.Empty, ListAsync).WithName("ListVouchers").WithSummary("Lists receipt and payment vouchers in the branch.")
            .RequireAuthorization(view).Produces<VoucherPageResponse>().ProducesStandardProblems();
        vouchers.MapGet("/{voucherId:guid}", GetAsync).WithName("GetVoucher").WithSummary("Gets a voucher with allocations and lines.")
            .RequireAuthorization(view).Produces<VoucherDetailDto>().ProducesStandardProblems();
        vouchers.MapPost(string.Empty, CreateAsync).WithName("CreateVoucher").WithSummary("Creates a draft receipt or payment voucher.")
            .RequireAuthorization(manage).Validate<SaveVoucherRequest>().Produces<VoucherDetailDto>(StatusCodes.Status201Created).ProducesStandardProblems();
        vouchers.MapPut("/{voucherId:guid}", UpdateAsync).WithName("UpdateVoucher").WithSummary("Replaces a draft voucher; requires ExpectedVersion.")
            .RequireAuthorization(manage).Validate<SaveVoucherRequest>().Produces<VoucherDetailDto>().ProducesStandardProblems();
        vouchers.MapDelete("/{voucherId:guid}", DeleteAsync).WithName("DeleteVoucher").WithSummary("Deletes a never-posted draft voucher.")
            .RequireAuthorization(manage).Produces(StatusCodes.Status204NoContent).ProducesStandardProblems();
        vouchers.MapPost("/{voucherId:guid}/post", PostAsync)
            .RetryOnDeadlock().WithName("PostVoucher")
            .WithSummary("Atomically numbers the voucher, rechecks allocations under lock and posts its journal; retrying the key replays.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.TreasuryVouchersPost)).Validate<PostVoucherRequest>()
            .Produces<VoucherDetailDto>().ProducesStandardProblems();
        vouchers.MapPost("/{voucherId:guid}/reverse", ReverseAsync).WithName("ReverseVoucher")
            .WithSummary("Posts a linked reversal journal and releases the voucher's allocations.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.TreasuryVouchersReverse)).Validate<ReverseVoucherRequest>()
            .Produces<VoucherDetailDto>().ProducesStandardProblems();
        root.MapGet("/customers/{customerId:guid}/outstanding-invoices", OutstandingAsync).WithName("ListOutstandingInvoices")
            .WithSummary("Posted credit invoices of a customer with authoritative paid and outstanding balances.")
            .RequireAuthorization(view).RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.SalesInvoicesView))
            .Produces<IReadOnlyList<OutstandingInvoiceDto>>().ProducesStandardProblems();
    }

    private static async Task<IResult> ListAsync(Guid companyId, Guid branchId, [FromQuery] VoucherKind? kind, [FromQuery] Guid? partyId,
        [FromQuery] VoucherStatus? status, [FromQuery] DateTimeOffset? fromDate, [FromQuery] DateTimeOffset? toDate, [FromQuery] Guid? salesInvoiceId,
        ITreasuryVoucherService service, [FromQuery] Guid? journalEntryId,
        HttpContext httpContext, IAuthorizationService authorization, CancellationToken cancellationToken, int page = 1, int pageSize = 50)
    {
        if (journalEntryId.HasValue && !(await authorization.AuthorizeAsync(httpContext.User, httpContext,
                PetraPolicies.Permission(PermissionCatalog.JournalsView)).ConfigureAwait(false)).Succeeded)
            return TypedResults.Forbid();
        var result = await service.ListAsync(companyId, branchId, new VoucherSearchQuery((DomainKind?)kind, partyId, (DomainStatus?)status,
            fromDate, toDate, salesInvoiceId, page, pageSize, journalEntryId), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new VoucherPageResponse
        {
            Page = result.Page, PageSize = result.PageSize, TotalCount = result.TotalCount,
            Items = result.Items.Select(v => new VoucherSummaryDto
            {
                Id = v.Id, Kind = (VoucherKind)v.Kind, VoucherNumber = v.VoucherNumber, VoucherDateUtc = v.VoucherDateUtc, PartyId = v.PartyId,
                PartyName = v.PartyName, CurrencyCode = v.CurrencyCode, Amount = Money(v.Amount), Status = (VoucherStatus)v.Status,
                JournalEntryId = v.JournalEntryId, Version = ConcurrencyToken.FromBytes(v.RowVersion).ToBase64(),
            }).ToList(),
        });
    }

    private static async Task<IResult> GetAsync(Guid companyId, Guid branchId, Guid voucherId, ITreasuryVoucherService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(Map(await service.GetAsync(companyId, branchId, voucherId, cancellationToken).ConfigureAwait(false)));

    private static async Task<IResult> CreateAsync(Guid companyId, Guid branchId, [FromBody] SaveVoucherRequest request, ITreasuryVoucherService service,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        var (command, error) = Parse(request);
        if (error is not null) return error;
        var result = await service.CreateDraftAsync(companyId, branchId, httpContext.RequireSession().UserId, command! with { ActorUserId = httpContext.RequireSession().UserId }, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/companies/{companyId}/branches/{branchId}/treasury/vouchers/{result.Id}", Map(result));
    }

    private static async Task<IResult> UpdateAsync(Guid companyId, Guid branchId, Guid voucherId, [FromBody] SaveVoucherRequest request,
        ITreasuryVoucherService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(request.ExpectedVersion, out var version)) return BadRequest("ExpectedVersion must be the Version returned by the server.");
        var (command, error) = Parse(request);
        if (error is not null) return error;
        return TypedResults.Ok(Map(await service.UpdateDraftAsync(companyId, branchId, voucherId, command! with { ExpectedVersion = version, ActorUserId = httpContext.RequireSession().UserId },
            cancellationToken).ConfigureAwait(false)));
    }

    private static async Task<IResult> DeleteAsync(Guid companyId, Guid branchId, Guid voucherId, [FromQuery] string? expectedVersion,
        ITreasuryVoucherService service, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(expectedVersion, out var version)) return BadRequest("expectedVersion must be the Version returned by the server.");
        await service.DeleteDraftAsync(companyId, branchId, voucherId, version, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> PostAsync(Guid companyId, Guid branchId, Guid voucherId, [FromBody] PostVoucherRequest request,
        ITreasuryVoucherService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(request.ExpectedVersion, out var version)) return BadRequest("ExpectedVersion must be the Version returned by the server.");
        return TypedResults.Ok(Map(await service.PostAsync(companyId, branchId, voucherId, httpContext.RequireSession().UserId,
            new PostVoucherCommand(version, request.IdempotencyKey, request.ManualNumber), cancellationToken).ConfigureAwait(false)));
    }

    private static async Task<IResult> ReverseAsync(Guid companyId, Guid branchId, Guid voucherId, [FromBody] ReverseVoucherRequest request,
        ITreasuryVoucherService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(request.ExpectedVersion, out var version)) return BadRequest("ExpectedVersion must be the Version returned by the server.");
        return TypedResults.Ok(Map(await service.ReverseAsync(companyId, branchId, voucherId, httpContext.RequireSession().UserId,
            new ReverseVoucherCommand(version, request.Reason), cancellationToken).ConfigureAwait(false)));
    }

    private static async Task<IResult> OutstandingAsync(Guid companyId, Guid branchId, Guid customerId, [FromQuery] string currencyCode,
        ITreasuryVoucherService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Length != 3) return BadRequest("currencyCode must be a three-letter code.");
        var rows = await service.GetOutstandingInvoicesAsync(companyId, branchId, customerId, currencyCode, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(rows.Select(r => new OutstandingInvoiceDto
        {
            InvoiceId = r.InvoiceId, InvoiceNumber = r.InvoiceNumber, InvoiceDateUtc = r.InvoiceDateUtc, CurrencyCode = r.CurrencyCode,
            GrandTotal = Money(r.GrandTotal), ReturnedAmount = Money(r.ReturnedAmount), PaidAmount = Money(r.PaidAmount), Outstanding = Money(r.Outstanding),
        }).ToList());
    }

    private static (SaveVoucherCommand? Command, IResult? Error) Parse(SaveVoucherRequest request)
    {
        if (!Enum.IsDefined(request.Kind) || !Enum.IsDefined(request.SettlementMethod)) return (null, BadRequest("Kind and SettlementMethod must be valid."));
        if (!TryMoney(request.Amount, out var amount) || amount <= 0) return (null, BadRequest("Amount must be a positive decimal string."));
        decimal? exchangeRate = null;
        if (!string.IsNullOrEmpty(request.ExchangeRate))
        {
            if (!FinancialDecimalText.TryParse(request.ExchangeRate, FinancialDecimalText.RateScale, FinancialDecimalText.RateIntegerDigits, out var rate) || rate.Value <= 0)
                return (null, BadRequest("ExchangeRate must be a positive decimal string."));
            exchangeRate = rate.Value;
        }
        var allocations = new List<VoucherAllocationCommand>();
        foreach (var a in request.Allocations ?? [])
        {
            if (!TryMoney(a.Amount, out var value) || value <= 0) return (null, BadRequest("Allocation amounts must be positive decimal strings."));
            allocations.Add(new(a.SalesInvoiceId, value));
        }
        var lines = new List<VoucherLineCommand>();
        foreach (var l in request.Lines ?? [])
        {
            if (!TryMoney(l.Amount, out var value) || value <= 0) return (null, BadRequest("Line amounts must be positive decimal strings."));
            lines.Add(new(l.PartyId, l.AccountId, value, l.CostCenterId, l.Description));
        }
        var (invoiceAllocations, allocationError) = ParseSettlementLines(request.InvoiceAllocations);
        if (allocationError is not null) return (null, allocationError);
        return (new SaveVoucherCommand((DomainKind)request.Kind, request.PatternRevisionId, request.PartyId, (DomainSettlement)request.SettlementMethod,
            request.VoucherDateUtc, string.IsNullOrWhiteSpace(request.CurrencyCode) ? null : request.CurrencyCode.Trim().ToUpperInvariant(), exchangeRate, amount, request.Notes, allocations, lines)
        { InvoiceAllocations = invoiceAllocations!, SourceNumber = request.SourceNumber, SourceDate = request.SourceDate }, null);
    }

    private static VoucherDetailDto Map(VoucherDetailResult v) => new()
    {
        Id = v.Id, CompanyId = v.CompanyId, BranchId = v.BranchId, Kind = (VoucherKind)v.Kind, PatternRevisionId = v.PatternRevisionId,
        PatternCode = v.PatternCode, PatternName = v.PatternName, PartyId = v.PartyId, PartyCode = v.PartyCode, PartyName = v.PartyName,
        SettlementMethod = (SettlementMethod)v.SettlementMethod, VoucherNumber = v.VoucherNumber, VoucherDateUtc = v.VoucherDateUtc,
        CurrencyCode = v.CurrencyCode, ExchangeRate = FinancialDecimalText.Format(v.ExchangeRate, FinancialDecimalText.RateScale),
        Amount = Money(v.Amount), AllocatedAmount = Money(v.AllocatedAmount), AdvanceAmount = Money(v.AdvanceAmount), Status = (VoucherStatus)v.Status,
        Notes = v.Notes, JournalEntryId = v.JournalEntryId, ReversalJournalEntryId = v.ReversalJournalEntryId, ReversalReason = v.ReversalReason,
        PostedAtUtc = v.PostedAtUtc, ReversedAtUtc = v.ReversedAtUtc, Version = ConcurrencyToken.FromBytes(v.RowVersion).ToBase64(),
        SourceNumber = v.SourceNumber, SourceDate = v.SourceDate,
        Audit = v.Audit is { } audit ? new VoucherAuditDto { CreatedAtUtc = audit.CreatedAtUtc, CreatedBy = audit.CreatedBy, UpdatedAtUtc = audit.UpdatedAtUtc,
            PostedBy = audit.PostedBy, PostedAtUtc = audit.PostedAtUtc, ReversedBy = audit.ReversedBy, ReversedAtUtc = audit.ReversedAtUtc } : null,
        InvoiceAllocations = v.InvoiceAllocations.Select(MapSettlement).ToArray(),
        SettlementBatches = v.SettlementBatches.Select(b => new SettlementBatchDto
        {
            Id = b.Id, DateUtc = b.DateUtc, State = b.State, Reason = b.Reason, JournalId = b.JournalId,
            Version = ConcurrencyToken.FromBytes(b.Version).ToBase64(), IsInitial = b.IsInitial,
            ReversalJournalId = b.ReversalJournalId, ReversedAtUtc = b.ReversedAtUtc, ReversedBy = b.ReversedBy, ReversalReason = b.ReversalReason,
            Applications = b.Applications.Select(MapSettlement).ToArray(),
        }).ToArray(),
        Allocations = v.Allocations.Select(a => new VoucherAllocationDto
        { Id = a.Id, SalesInvoiceId = a.SalesInvoiceId, InvoiceNumber = a.InvoiceNumber, InvoiceDateUtc = a.InvoiceDateUtc, Amount = Money(a.Amount) }).ToList(),
        Lines = v.Lines.Select(l => new VoucherLineDto
        {
            Id = l.Id, PartyId = l.PartyId, PartyName = l.PartyName, AccountId = l.AccountId, AccountCode = l.AccountCode, CostCenterId = l.CostCenterId,
            Amount = Money(l.Amount), Description = l.Description, DisplayOrder = l.DisplayOrder,
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

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult BadRequest(string detail) => TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}
