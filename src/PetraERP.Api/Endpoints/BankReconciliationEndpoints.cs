using System.Globalization;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Application.Treasury;
using PetraERP.Contracts.Treasury;

namespace PetraERP.Api.Endpoints;

internal static class BankReconciliationEndpoints
{
    internal static void MapBankReconciliationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/treasury/bank-reconciliations")
            .WithTags("Bank reconciliation").WithMetadata(FinancialWorkspaceMetadata.Instance).RequireAuthorization(PetraPolicies.CompanyScope);
        var view = PetraPolicies.Permission(PermissionCatalog.TreasuryVouchersView);
        var manage = PetraPolicies.Permission(PermissionCatalog.BankReconciliationsManage);

        group.MapGet(string.Empty, async (Guid companyId, Guid branchId, Guid? accountId, IBankReconciliationService service, CancellationToken token) =>
                TypedResults.Ok((await service.ListAsync(companyId, branchId, accountId, token).ConfigureAwait(false)).Select(Map).ToArray()))
            .WithName("ListBankReconciliations").WithSummary("Lists bank reconciliations, newest statement first.")
            .RequireAuthorization(view).Produces<BankReconciliationSummaryResponse[]>().ProducesStandardProblems();
        group.MapGet("/{reconciliationId:guid}", async (Guid companyId, Guid branchId, Guid reconciliationId, IBankReconciliationService service, CancellationToken token) =>
                TypedResults.Ok(Map(await service.GetAsync(companyId, branchId, reconciliationId, token).ConfigureAwait(false))))
            .WithName("GetBankReconciliation").WithSummary("Open lines up to the statement date, cleared state and the live difference.")
            .RequireAuthorization(view).Produces<BankReconciliationResponse>().ProducesStandardProblems();
        group.MapPost(string.Empty, async (Guid companyId, Guid branchId, StartBankReconciliationRequest request, IBankReconciliationService service,
                FinancialRequestScope scope, HttpContext http, CancellationToken token) =>
                TypedResults.Ok(Map(await service.StartAsync(companyId, scope.FiscalYearId, branchId, http.RequireSession().UserId, request.AccountId,
                    request.StatementDate, Parse(request.StatementBalance), token).ConfigureAwait(false))))
            .WithName("StartBankReconciliation").WithSummary("Starts the single open reconciliation of a book account.")
            .RequireAuthorization(manage).Produces<BankReconciliationResponse>().ProducesStandardProblems();
        group.MapPost("/{reconciliationId:guid}/lines", async (Guid companyId, Guid branchId, Guid reconciliationId, SetBankReconciliationLinesRequest request,
                IBankReconciliationService service, FinancialRequestScope scope, HttpContext http, CancellationToken token) =>
                TypedResults.Ok(Map(await service.SetClearedAsync(companyId, scope.FiscalYearId, branchId, http.RequireSession().UserId, reconciliationId,
                    request.JournalLineIds, request.Cleared, request.ExpectedVersion, token).ConfigureAwait(false))))
            .WithName("SetBankReconciliationLines").WithSummary("Clears or unclears posted lines; all-or-nothing.")
            .RequireAuthorization(manage).Produces<BankReconciliationResponse>().ProducesStandardProblems();
        group.MapPost("/{reconciliationId:guid}/complete", async (Guid companyId, Guid branchId, Guid reconciliationId, BankReconciliationVersionRequest request,
                IBankReconciliationService service, FinancialRequestScope scope, HttpContext http, CancellationToken token) =>
                TypedResults.Ok(Map(await service.CompleteAsync(companyId, scope.FiscalYearId, branchId, http.RequireSession().UserId, reconciliationId,
                    request.ExpectedVersion, token).ConfigureAwait(false))))
            .WithName("CompleteBankReconciliation").WithSummary("Completes a reconciliation whose difference is exactly zero.")
            .RequireAuthorization(manage).Produces<BankReconciliationResponse>().ProducesStandardProblems();
        group.MapPost("/{reconciliationId:guid}/discard", async (Guid companyId, Guid branchId, Guid reconciliationId, BankReconciliationVersionRequest request,
                IBankReconciliationService service, FinancialRequestScope scope, HttpContext http, CancellationToken token) =>
            {
                await service.DiscardAsync(companyId, scope.FiscalYearId, branchId, http.RequireSession().UserId, reconciliationId, request.ExpectedVersion, token).ConfigureAwait(false);
                return TypedResults.NoContent();
            })
            .WithName("DiscardBankReconciliation").WithSummary("Discards a draft reconciliation and releases its cleared lines.")
            .RequireAuthorization(manage).Produces(StatusCodes.Status204NoContent).ProducesStandardProblems();
    }

    private static decimal Parse(string text) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value : throw new RequestValidationException("bank_reconciliation.balance_invalid", "Use an invariant decimal number such as -1250.5000.", "statementBalance");

    private static string M(decimal value) => LedgerMasterMapping.Money(value);

    private static BankReconciliationSummaryResponse Map(BankReconciliationSummaryDto dto) => new()
    {
        Id = dto.Id, AccountId = dto.AccountId, AccountCode = dto.AccountCode, StatementDate = dto.StatementDate,
        StatementBalance = M(dto.StatementBalance), Difference = M(dto.Difference), Status = dto.Status,
    };

    private static BankReconciliationResponse Map(BankReconciliationDto dto) => new()
    {
        Id = dto.Id, BranchId = dto.BranchId, AccountId = dto.AccountId, AccountCode = dto.AccountCode, StatementDate = dto.StatementDate,
        StatementBalance = M(dto.StatementBalance), PriorClearedBalance = M(dto.PriorClearedBalance),
        ClearedInThisReconciliation = M(dto.ClearedInThisReconciliation), Difference = M(dto.Difference),
        BookBalanceAtStatementDate = M(dto.BookBalanceAtStatementDate), Status = dto.Status, Version = dto.VersionBase64,
        Lines = dto.Lines.Select(l => new BankReconciliationLineResponse
        {
            JournalLineId = l.JournalLineId, JournalId = l.JournalId, AccountingDate = l.AccountingDate, PostingSequence = l.PostingSequence,
            Description = l.Description, Amount = M(l.Amount), Cleared = l.Cleared,
        }).ToArray(),
    };
}
