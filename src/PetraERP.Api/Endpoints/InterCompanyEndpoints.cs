using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.InterCompany;
using PetraERP.Application.Security;
using PetraERP.Contracts.InterCompany;

namespace PetraERP.Api.Endpoints;

/// <summary>
/// Inter-company account links and reconciliation. Managing links needs <c>Ledger.Accounts.Manage</c>; reconciling reads the
/// counterparty's ledger and therefore needs the report permission on every branch of both companies.
/// </summary>
internal static class InterCompanyEndpoints
{
    internal static void MapInterCompanyEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/inter-company").WithTags("Inter-company reconciliation");
        group.MapGet("/links", ListAsync).WithName("ListInterCompanyLinks").RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.ReportsView))
            .Produces<InterCompanyLinkListResponse>().ProducesStandardProblems();
        group.MapPut("/links", SaveAsync).WithName("SaveInterCompanyLink").RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.AccountsManage))
            .Validate<SaveInterCompanyLinkRequest>().Produces<InterCompanyLinkDto>().ProducesStandardProblems();
        group.MapPut("/links/{linkId:guid}/active", SetActiveAsync).WithName("SetInterCompanyLinkActive").RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.AccountsManage))
            .Produces<InterCompanyLinkDto>().ProducesStandardProblems();
        group.MapGet("/links/{linkId:guid}/reconciliation", ReconcileAsync).WithName("ReconcileInterCompanyLink")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.ReportsView))
            .WithSummary("Compares the two companies' balances of the linked accounts at a date; Matched when this side + counterparty side = 0.")
            .Produces<InterCompanyReconciliationResponse>().ProducesStandardProblems();
    }

    private static async Task<IResult> ListAsync(Guid companyId, Guid branchId, bool? includeInactive, IInterCompanyReconciliationService service, CancellationToken ct) =>
        TypedResults.Ok(new InterCompanyLinkListResponse { Items = (await service.ListLinksAsync(companyId, includeInactive ?? false, ct).ConfigureAwait(false)).Select(Map).ToList() });

    private static async Task<IResult> SaveAsync(Guid companyId, Guid branchId, [FromBody] SaveInterCompanyLinkRequest body, IInterCompanyReconciliationService service,
        HttpContext http, CancellationToken ct) =>
        TypedResults.Ok(Map(await service.SaveLinkAsync(companyId, http.RequireSession().UserId,
            new SaveInterCompanyLinkCommand(body.CounterpartyCompanyId, body.AccountCode, body.CounterpartyAccountCode, body.CurrencyCode, body.Note), ct).ConfigureAwait(false)));

    private static async Task<IResult> SetActiveAsync(Guid companyId, Guid branchId, Guid linkId, [FromBody] SetInterCompanyLinkActiveRequest body,
        IInterCompanyReconciliationService service, CancellationToken ct) =>
        TypedResults.Ok(Map(await service.SetActiveAsync(companyId, linkId, body.IsActive, ct).ConfigureAwait(false)));

    private static async Task<IResult> ReconcileAsync(Guid companyId, Guid branchId, Guid linkId, DateOnly? asOf, IInterCompanyReconciliationService service,
        HttpContext http, CancellationToken ct)
    {
        if (asOf is null) throw new RequestValidationException("intercompany.date_required", "'asOf' is required.", "asOf");
        var r = await service.ReconcileAsync(companyId, http.RequireSession().UserId, linkId, asOf.Value, ct).ConfigureAwait(false);
        return TypedResults.Ok(new InterCompanyReconciliationResponse
        {
            LinkId = r.LinkId, AsOfDate = r.AsOfDate, CurrencyCode = r.CurrencyCode, ThisSide = Side(r.ThisSide), CounterpartySide = Side(r.CounterpartySide),
            Difference = F(r.Difference), Matched = r.Matched, WarningCode = r.WarningCode, Matching = r.Matching is null ? null : MapMatching(r.Matching),
        });
    }

    private static InterCompanyMovementDto Mv(InterCompanyMovement m) => new() { Date = m.Date, JournalId = m.JournalId, Description = m.Description, Amount = F(m.Amount) };

    private static InterCompanyMatchingDto MapMatching(InterCompanyMatching m) => new()
    {
        Pairs = m.Pairs.Select(p => new InterCompanyMatchPairDto { ThisMovement = Mv(p.ThisMovement), CounterpartyMovement = Mv(p.CounterpartyMovement) }).ToList(),
        UnmatchedThis = m.UnmatchedThis.Select(Mv).ToList(), UnmatchedCounterparty = m.UnmatchedCounterparty.Select(Mv).ToList(), Truncated = m.Truncated,
    };

    private static InterCompanySideDto Side(InterCompanySide s) => new()
    {
        CompanyId = s.CompanyId, DatabaseName = s.DatabaseName, AccountFound = s.AccountFound, Balance = F(s.Balance),
        RecentMovements = s.RecentMovements.Select(m => new InterCompanyMovementDto { Date = m.Date, JournalId = m.JournalId, Description = m.Description, Amount = F(m.Amount) }).ToList(),
    };

    private static InterCompanyLinkDto Map(InterCompanyLinkResult r) => new()
    {
        Id = r.Id, CompanyId = r.CompanyId, CounterpartyCompanyId = r.CounterpartyCompanyId, CounterpartyCompanyName = r.CounterpartyCompanyName, AccountCode = r.AccountCode,
        CounterpartyAccountCode = r.CounterpartyAccountCode, CurrencyCode = r.CurrencyCode, IsActive = r.IsActive, Note = r.Note, UpdatedAtUtc = r.UpdatedAtUtc,
    };

    private static string F(decimal value) => value.ToString("F4", CultureInfo.InvariantCulture);
}
