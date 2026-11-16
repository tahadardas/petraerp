using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Application.Treasury;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Treasury;
using DomainKind = PetraERP.Domain.Treasury.VoucherKind;
using AppFilter = PetraERP.Application.Treasury.SettlementStateFilter;
using ContractFilter = PetraERP.Contracts.Treasury.SettlementStateFilter;

namespace PetraERP.Api.Endpoints;

internal static partial class TreasuryVoucherEndpoints
{
    private static void MapSettlementEndpoints(RouteGroupBuilder root, RouteGroupBuilder vouchers)
    {
        var view = PetraPolicies.Permission(PermissionCatalog.TreasuryVouchersView);
        root.MapGet("/parties/{partyId:guid}/settlement-invoices", SettlementInvoicesAsync).WithName("ListPartySettlementInvoices")
            .WithSummary("Lists posted customer or supplier invoices with derived open, partial or settled balances.")
            .RequireAuthorization(view).RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.PartiesView))
            .Produces<SettlementInvoicePageResponse>().ProducesStandardProblems();
        vouchers.MapPost("/{voucherId:guid}/approve", ApproveVoucherAsync).WithName("ApproveVoucher")
            .WithSummary("Approves a versioned draft voucher after reference, allocation and open-period checks.")
            .RequireAuthorization(view).RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.TreasuryVouchersApprove))
            .Validate<VoucherActionRequest>().Produces<VoucherDetailDto>().ProducesStandardProblems();
        vouchers.MapPost("/{voucherId:guid}/apply-advance", ApplyVoucherAdvanceAsync).WithName("ApplyVoucherAdvance")
            .WithSummary("Applies a posted party advance to invoices at frozen rates; a request-key replay preserves the same outcome.")
            .RequireAuthorization(view).RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.TreasuryVouchersAllocate))
            .Validate<ApplyAdvanceRequest>().Produces<VoucherDetailDto>().ProducesStandardProblems();
        vouchers.MapPost("/{voucherId:guid}/settlements/{batchId:guid}/reverse", ReverseVoucherSettlementAsync).WithName("ReverseVoucherSettlement")
            .WithSummary("Reverses a later settlement batch and releases its allocations while retaining immutable history.")
            .RequireAuthorization(view).RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.TreasuryVouchersAllocate))
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.TreasuryVouchersReverse))
            .Validate<VoucherActionRequest>().Produces<VoucherDetailDto>().ProducesStandardProblems();
    }

    private static async Task<IResult> SettlementInvoicesAsync(Guid companyId, Guid branchId, Guid partyId,
        [FromQuery] VoucherKind kind, [FromQuery] string? currencyCode, [FromQuery] ContractFilter? state,
        [FromQuery] int? page, [FromQuery] int? pageSize, ISettlementService service, HttpContext httpContext,
        IAuthorizationService authorization, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(kind) || state.HasValue && !Enum.IsDefined(state.Value))
            return BadRequest("النوع أو حالة التسديد غير صالحة / Invalid voucher kind or settlement state.");
        var permission = kind == VoucherKind.Receipt ? PermissionCatalog.SalesInvoicesView : PermissionCatalog.PurchaseInvoicesView;
        if (!(await authorization.AuthorizeAsync(httpContext.User, httpContext, PetraPolicies.Permission(permission)).ConfigureAwait(false)).Succeeded)
            return TypedResults.Forbid();
        var result = await service.InvoicesAsync(companyId, branchId, partyId, (DomainKind)kind, currencyCode,
            (AppFilter)(state ?? ContractFilter.All), page ?? 1, pageSize ?? 50, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new SettlementInvoicePageResponse
        {
            Page = result.Page, PageSize = result.PageSize, TotalCount = result.TotalCount,
            Items = result.Items.Select(i => new SettlementInvoiceDto
            {
                InvoiceId = i.InvoiceId, PartyId = i.PartyId, Number = i.Number, DateUtc = i.DateUtc, CurrencyCode = i.CurrencyCode,
                ExchangeRate = FinancialDecimalText.Format(i.ExchangeRate,FinancialDecimalText.RateScale), Total = Money(i.Total),
                Returned = Money(i.Returned), Paid = Money(i.Paid), Outstanding = Money(i.Outstanding), OutstandingBase = Money(i.OutstandingBase),
                State = (ContractFilter)i.State,
            }).ToArray(),
        });
    }

    private static async Task<IResult> ApproveVoucherAsync(Guid companyId, Guid branchId, Guid voucherId,
        [FromBody] VoucherActionRequest request, ISettlementService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(request.ExpectedVersion,out var version))
            return BadRequest("أعد تحميل السند لإرسال نسخته الحالية / Reload the voucher and send its current version.");
        return TypedResults.Ok(Map(await service.ApproveAsync(companyId,branchId,voucherId,httpContext.RequireSession().UserId,
            new VoucherActionCommand(version,request.Reason),cancellationToken).ConfigureAwait(false)));
    }

    private static async Task<IResult> ApplyVoucherAdvanceAsync(Guid companyId, Guid branchId, Guid voucherId,
        [FromBody] ApplyAdvanceRequest request, ISettlementService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(request.ExpectedVersion,out var version))
            return BadRequest("أعد تحميل السند لإرسال نسخته الحالية / Reload the voucher and send its current version.");
        if (request.DateUtc == default) return BadRequest("تاريخ التسوية مطلوب / Settlement date is required.");
        var (lines,error) = ParseSettlementLines(request.Lines);
        if (error is not null) return error;
        if (lines!.Count == 0) return BadRequest("أضف تخصيص فاتورة واحداً على الأقل / At least one invoice application is required.");
        return TypedResults.Ok(Map(await service.ApplyAdvanceAsync(companyId,branchId,voucherId,httpContext.RequireSession().UserId,
            new ApplyAdvanceCommand(version,request.IdempotencyKey,request.Reason,request.DateUtc,lines),cancellationToken).ConfigureAwait(false)));
    }

    private static async Task<IResult> ReverseVoucherSettlementAsync(Guid companyId, Guid branchId, Guid voucherId, Guid batchId,
        [FromBody] VoucherActionRequest request, ISettlementService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryParse(request.ExpectedVersion,out var version))
            return BadRequest("أعد تحميل التسوية لإرسال نسختها الحالية / Reload the settlement and send its current version.");
        return TypedResults.Ok(Map(await service.ReverseSettlementAsync(companyId,branchId,voucherId,batchId,httpContext.RequireSession().UserId,
            new VoucherActionCommand(version,request.Reason),cancellationToken).ConfigureAwait(false)));
    }

    private static (IReadOnlyList<InvoiceSettlementCommand>? Lines,IResult? Error) ParseSettlementLines(IReadOnlyList<InvoiceSettlementInputDto>? inputs)
    {
        if (inputs is null || inputs.Count > 200)
            return (null,BadRequest("أرسل حتى 200 تخصيص / Supply up to 200 invoice applications."));
        var lines = new List<InvoiceSettlementCommand>();
        foreach (var input in inputs)
        {
            if (input is null || input.InvoiceId == Guid.Empty || !TryMoney(input.InvoiceAmount,out var invoiceAmount) || invoiceAmount<=0
                || !TryMoney(input.SettlementAmount,out var settlementAmount) || settlementAmount<=0
                || !FinancialDecimalText.TryParse(input.CrossRate,FinancialDecimalText.RateScale,FinancialDecimalText.RateIntegerDigits,out var rate) || rate.Value<=0)
                return (null,BadRequest("اختر فاتورة وأدخل مبلغين موجبين وسعر صرف مثبتاً / Select an invoice and supply positive invoice amount, settlement amount and frozen cross rate."));
            lines.Add(new(input.InvoiceId,invoiceAmount,settlementAmount,rate.Value));
        }
        return (lines,null);
    }

    private static InvoiceSettlementDto MapSettlement(InvoiceSettlementResult i) => new()
    {
        Id = i.Id, InvoiceId = i.InvoiceId, InvoiceNumber = i.InvoiceNumber, InvoiceDateUtc = i.InvoiceDateUtc,
        InvoiceCurrency = i.InvoiceCurrency, InvoiceAmount = Money(i.InvoiceAmount), SettlementAmount = Money(i.SettlementAmount),
        CrossRate = FinancialDecimalText.Format(i.CrossRate,FinancialDecimalText.RateScale),
    };
}
