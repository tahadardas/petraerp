using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reconciliation;
using PetraERP.Application.Security;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Endpoints;

internal static class AccountingReconciliationEndpoints
{
    internal static void MapAccountingReconciliationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/reconciliation")
            .WithTags("Accounting reconciliation");

        var view = PetraPolicies.Permission(PermissionCatalog.ReportsView);

        group.MapGet("", RunReconciliationAsync)
            .WithName("RunAccountingReconciliation")
            .WithSummary("Runs all 14 multi-layer reconciliation checks across GL, subledgers, inventory, and journals.")
            .RequireAuthorization(view)
            .Produces<ReconciliationReportResponse>()
            .ProducesStandardProblems();

        group.MapGet("/export/json", ExportJsonAsync)
            .WithName("ExportAccountingReconciliationJson")
            .WithSummary("Exports full reconciliation report as structured JSON.")
            .RequireAuthorization(view)
            .Produces<string>(StatusCodes.Status200OK, "application/json")
            .ProducesStandardProblems();

        group.MapGet("/export/csv", ExportCsvAsync)
            .WithName("ExportAccountingReconciliationCsv")
            .WithSummary("Exports full reconciliation findings as CSV.")
            .RequireAuthorization(view)
            .Produces<string>(StatusCodes.Status200OK, "text/csv")
            .ProducesStandardProblems();
    }

    internal static ReconciliationReportResponse ToResponse(this ReconciliationReportDto dto)
    {
        return new ReconciliationReportResponse
        {
            CompanyId = dto.CompanyId,
            FiscalYearId = dto.FiscalYearId,
            GeneratedAtUtc = dto.GeneratedAtUtc,
            TotalChecks = dto.TotalChecks,
            PassedChecks = dto.PassedChecks,
            WarningChecks = dto.WarningChecks,
            BlockingChecks = dto.BlockingChecks,
            HasBlockingDifferences = dto.HasBlockingDifferences,
            Findings = dto.Findings.Select(f => new ReconciliationFindingResponse
            {
                ReconciliationId = f.ReconciliationId,
                CompanyId = f.CompanyId,
                FiscalYearId = f.FiscalYearId,
                BranchId = f.BranchId,
                CheckCode = f.CheckCode,
                AccountId = f.AccountId,
                PartyId = f.PartyId,
                ItemId = f.ItemId,
                WarehouseId = f.WarehouseId,
                DocumentId = f.DocumentId,
                CurrencyCode = f.CurrencyCode,
                SourceAmount = f.SourceAmount.HasValue ? FinancialDecimalText.Format(f.SourceAmount.Value, FinancialDecimalText.MoneyScale) : null,
                CalculatedAmount = f.CalculatedAmount.HasValue ? FinancialDecimalText.Format(f.CalculatedAmount.Value, FinancialDecimalText.MoneyScale) : null,
                Difference = f.Difference.HasValue ? FinancialDecimalText.Format(f.Difference.Value, FinancialDecimalText.MoneyScale) : null,
                Severity = f.Severity,
                Explanation = f.Explanation,
                SuggestedAction = f.SuggestedAction,
                EvidenceReference = f.EvidenceReference
            }).ToList()
        };
    }

    private static async Task<IResult> RunReconciliationAsync(
        Guid companyId,
        Guid branchId,
        IAccountingReconciliationService service,
        FinancialRequestScope scope,
        CancellationToken cancellationToken)
    {
        var result = await service.RunAllReconciliationsAsync(companyId, scope.FiscalYearId, branchId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }

    private static async Task<IResult> ExportJsonAsync(
        Guid companyId,
        Guid branchId,
        IAccountingReconciliationService service,
        FinancialRequestScope scope,
        CancellationToken cancellationToken)
    {
        var report = await service.RunAllReconciliationsAsync(companyId, scope.FiscalYearId, branchId, cancellationToken).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(report.ToResponse(), JsonSerializerOptions.Web);
        return TypedResults.Content(json, "application/json");
    }

    private static async Task<IResult> ExportCsvAsync(
        Guid companyId,
        Guid branchId,
        IAccountingReconciliationService service,
        FinancialRequestScope scope,
        CancellationToken cancellationToken)
    {
        var csv = await service.ExportReportToCsvAsync(companyId, scope.FiscalYearId, branchId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Content(csv, "text/csv");
    }
}
