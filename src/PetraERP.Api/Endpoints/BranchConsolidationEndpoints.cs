using System.Globalization;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Branches;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Contracts.Branches;

namespace PetraERP.Api.Endpoints;

/// <summary>
/// Branch-by-branch trial balance with optional elimination of inter-branch clearing accounts. Reading a branch needs the
/// report permission on that branch; omitting <c>branchIds</c> reads all branches and needs it on every one (refused otherwise).
/// </summary>
internal static class BranchConsolidationEndpoints
{
    internal static void MapBranchConsolidationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/branch-reports").WithTags("Branch reports");
        group.MapGet("/trial-balance", TrialBalanceAsync).WithName("GetBranchTrialBalance")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.ReportsView))
            .WithSummary("Posted opening, period and closing balances per account and per branch; eliminate=true removes inter-branch clearing accounts from the consolidated column.")
            .Produces<BranchTrialBalanceResponse>().ProducesStandardProblems();
    }

    private static async Task<IResult> TrialBalanceAsync(Guid companyId, Guid branchId, DateOnly? from, DateOnly? to, string? branchIds, bool? eliminate,
        IBranchConsolidationService service, HttpContext http, CancellationToken ct)
    {
        if (from is null || to is null) throw new RequestValidationException("branches.report_range_required", "'from' and 'to' are required.", "from");
        List<Guid>? ids = null;
        if (!string.IsNullOrWhiteSpace(branchIds))
        {
            ids = [];
            foreach (var part in branchIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Guid.TryParse(part, out var id) || id == Guid.Empty)
                    throw new RequestValidationException("branches.branch_ids_invalid", "branchIds is a comma-separated list of branch ids.", "branchIds");
                ids.Add(id);
            }
        }
        var r = await service.TrialBalanceAsync(companyId, http.RequireSession().UserId, new BranchTrialBalanceQuery(from.Value, to.Value, ids, eliminate ?? false), ct).ConfigureAwait(false);
        return TypedResults.Ok(new BranchTrialBalanceResponse
        {
            From = r.From, To = r.To, BaseCurrencyCode = r.BaseCurrencyCode, Eliminated = r.Eliminated,
            Branches = r.Branches.Select(b => new BranchTrialBalanceBranchDto { BranchId = b.BranchId, Code = b.Code, Name = b.Name }).ToList(),
            Rows = r.Rows.Select(row => new BranchTrialBalanceRowDto
            {
                AccountId = row.AccountId, AccountCode = row.AccountCode, AccountName = row.AccountName, IsInterBranchClearing = row.IsInterBranchClearing,
                ByBranch = row.ByBranch.Select(c => new BranchBalanceColumnDto { BranchId = c.BranchId, Opening = F(c.Opening), Debit = F(c.Debit), Credit = F(c.Credit), Closing = F(c.Closing) }).ToList(),
                TotalOpening = F(row.TotalOpening), TotalDebit = F(row.TotalDebit), TotalCredit = F(row.TotalCredit), TotalClosing = F(row.TotalClosing),
                ConsolidatedClosing = F(row.ConsolidatedClosing),
            }).ToList(),
            TotalDebit = F(r.TotalDebit), TotalCredit = F(r.TotalCredit), ConsolidatedClosingSum = F(r.ConsolidatedClosingSum), InterBranchResidual = F(r.InterBranchResidual),
        });
    }

    private static string F(decimal value) => value.ToString("F4", CultureInfo.InvariantCulture);
}
