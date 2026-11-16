using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.Security;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Endpoints;

/// <summary>Cash-book voucher marker of a journal: read with the journal view permission, set with the journal manage permission.</summary>
internal static class JournalVoucherHeaderEndpoints
{
    internal static void MapJournalVoucherHeader(this RouteGroupBuilder ledger)
    {
        ledger.MapGet("/journals/{journalId:guid}/voucher-header", GetAsync).WithName("GetJournalVoucherHeader")
            .WithSummary("Reads the cash-book voucher marker of a journal (404-free: null body when the journal is an ordinary one).")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.JournalsView))
            .Produces<JournalVoucherHeaderDto>().ProducesStandardProblems();
        ledger.MapPut("/journals/drafts/{journalId:guid}/voucher-header", SetAsync).WithName("SetJournalVoucherHeader")
            .WithSummary("Marks a draft journal as a cash-book voucher and names its cash account.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.JournalsManage))
            .Produces<JournalVoucherHeaderDto>().ProducesStandardProblems();
    }

    private static async Task<IResult> GetAsync(Guid companyId, Guid branchId, Guid journalId, IJournalVoucherHeaderService service, HttpContext http, CancellationToken token)
    {
        var scope = JournalEntryScope.Create(companyId, branchId, http.RequireSession().UserId);
        var header = await service.GetAsync(scope, journalId, token).ConfigureAwait(false);
        return TypedResults.Ok(header is null ? new JournalVoucherHeaderDto { Kind = string.Empty, CashAccountId = Guid.Empty } : ToDto(header));
    }

    private static async Task<IResult> SetAsync(Guid companyId, Guid branchId, Guid journalId, JournalVoucherHeaderDto request, IJournalVoucherHeaderService service,
        HttpContext http, CancellationToken token)
    {
        var scope = JournalEntryScope.Create(companyId, branchId, http.RequireSession().UserId);
        return TypedResults.Ok(ToDto(await service.SetAsync(scope, journalId, new JournalVoucherHeaderData(request.Kind, request.CashAccountId), token).ConfigureAwait(false)));
    }

    private static JournalVoucherHeaderDto ToDto(JournalVoucherHeaderData data) => new() { Kind = data.Kind, CashAccountId = data.CashAccountId };
}
