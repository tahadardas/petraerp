using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Security;
using PetraERP.Application.Workspaces.Rollover;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Workspaces;

namespace PetraERP.Api.Endpoints;

internal static class FiscalYearRolloverEndpoints
{
    internal static void MapFiscalYearRolloverEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/workspaces/rollover")
            .WithTags("Workspaces: Fiscal Year Rollover");

        var manage = PetraPolicies.Permission(PermissionCatalog.DatabaseManage);

        group.MapPost("/preview", PreviewAsync)
            .WithName("PreviewFiscalYearRollover")
            .WithSummary("Previews financial-year rollover including net income calculation, open items, and stock carryforward.")
            .RequireAuthorization(manage)
            .Validate<RolloverPreviewRequest>()
            .Produces<RolloverPreviewResponse>()
            .ProducesStandardProblems();

        group.MapPost("/execute", ExecuteAsync)
            .WithName("ExecuteFiscalYearRollover")
            .WithSummary("Executes multi-database fiscal year rollover, creates opening balances, carries forward subledgers, and activates target database.")
            .RequireAuthorization(manage)
            .Validate<ExecuteRolloverRequest>()
            .Produces<RolloverResultResponse>()
            .ProducesStandardProblems();
    }

    private static async Task<IResult> PreviewAsync(
        [FromBody] RolloverPreviewRequest request,
        IFiscalYearRolloverService service,
        CancellationToken cancellationToken)
    {
        var query = new RolloverPreviewQuery(
            request.CompanyId,
            request.SourceFiscalYearId,
            request.TargetFiscalYearId,
            request.RetainedEarningsAccountId);

        var preview = await service.PreviewRolloverAsync(query, cancellationToken).ConfigureAwait(false);

        var response = new RolloverPreviewResponse
        {
            CompanyId = preview.CompanyId,
            SourceFiscalYearId = preview.SourceFiscalYearId,
            SourceYearCode = preview.SourceYearCode,
            TargetFiscalYearId = preview.TargetFiscalYearId,
            TargetYearCode = preview.TargetYearCode,
            RetainedEarningsAccountId = preview.RetainedEarningsAccountId,
            RetainedEarningsAccountCode = preview.RetainedEarningsAccountCode,
            TotalRevenues = FinancialDecimalText.Format(preview.TotalRevenues, FinancialDecimalText.MoneyScale),
            TotalExpenses = FinancialDecimalText.Format(preview.TotalExpenses, FinancialDecimalText.MoneyScale),
            NetIncome = FinancialDecimalText.Format(preview.NetIncome, FinancialDecimalText.MoneyScale),
            TotalBalanceSheetDebit = FinancialDecimalText.Format(preview.TotalBalanceSheetDebit, FinancialDecimalText.MoneyScale),
            TotalBalanceSheetCredit = FinancialDecimalText.Format(preview.TotalBalanceSheetCredit, FinancialDecimalText.MoneyScale),
            IsTrialBalanceBalanced = preview.IsTrialBalanceBalanced,
            OpenItemsCount = preview.OpenItemsCount,
            OpenItemsTotalAmount = FinancialDecimalText.Format(preview.OpenItemsTotalAmount, FinancialDecimalText.MoneyScale),
            StockItemsCount = preview.StockItemsCount,
            StockTotalValue = FinancialDecimalText.Format(preview.StockTotalValue, FinancialDecimalText.MoneyScale),
            SourceFingerprint = preview.SourceFingerprint,
            AccountLines = preview.AccountLines.Select(a => new RolloverAccountLineResponse
            {
                AccountId = a.AccountId,
                AccountCode = a.AccountCode,
                AccountNameArabic = a.AccountNameArabic,
                AccountType = a.AccountType,
                ClosingDebit = FinancialDecimalText.Format(a.ClosingDebit, FinancialDecimalText.MoneyScale),
                ClosingCredit = FinancialDecimalText.Format(a.ClosingCredit, FinancialDecimalText.MoneyScale),
                CurrencyCode = a.CurrencyCode
            }).ToList()
        };

        return TypedResults.Ok(response);
    }

    private static async Task<IResult> ExecuteAsync(
        [FromBody] ExecuteRolloverRequest request,
        IFiscalYearRolloverService service,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var command = new ExecuteRolloverCommand(
            request.CompanyId,
            request.SourceFiscalYearId,
            request.TargetFiscalYearId,
            request.RetainedEarningsAccountId,
            request.SourceFingerprint,
            request.Reason,
            request.IdempotencyKey);

        var result = await service.ExecuteRolloverAsync(http.RequireSession().UserId, command, cancellationToken).ConfigureAwait(false);

        var response = new RolloverResultResponse
        {
            RolloverBatchId = result.RolloverBatchId,
            CompanyId = result.CompanyId,
            SourceFiscalYearId = result.SourceFiscalYearId,
            TargetFiscalYearId = result.TargetFiscalYearId,
            Status = result.Status,
            OpeningBalanceBatchId = result.OpeningBalanceBatchId,
            OpeningJournalId = result.OpeningJournalId,
            OpenItemsCreated = result.OpenItemsCreated,
            StockMovementsCreated = result.StockMovementsCreated,
            TargetDatabaseActivated = result.TargetDatabaseActivated,
            Message = result.Message
        };

        return TypedResults.Ok(response);
    }
}
