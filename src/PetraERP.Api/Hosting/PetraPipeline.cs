using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PetraERP.Api.Configuration;
using PetraERP.Api.Endpoints;

namespace PetraERP.Api.Hosting;

internal static class PetraPipeline
{
    internal static void UsePetraPipeline(this WebApplication app)
    {
        var api = app.Services.GetRequiredService<IOptions<PetraApiOptions>>().Value;

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<RequestTimingMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        if (api.RequireHttps)
        {
            if (!app.Environment.IsDevelopment())
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();
        }

        app.UseResponseCompression();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<PetraERP.Api.Security.FinancialWorkspaceMiddleware>();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = static _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = static check => check.Tags.Contains("ready"),
        }).AllowAnonymous();

        if (api.ExposeOpenApi)
        {
            app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
        }

        var v1 = app.MapGroup("/api/v1");
        v1.MapPlatformEndpoints();
        v1.MapWorkspaceEndpoints();

        v1.MapWorkspaceSelectionEndpoints();
        v1.MapAuthenticationEndpoints();
        v1.MapContextEndpoints();
        v1.MapAdministrationEndpoints();
        v1.MapAuditEndpoints();
        v1.MapFiscalYearRolloverEndpoints();
        v1.MapBackupRestoreEndpoints();
        v1.MapAlAmeenMigrationEndpoints();
        v1.MapModuleEndpoints();

        var financial = v1.MapGroup("").WithMetadata(PetraERP.Api.Security.FinancialWorkspaceMetadata.Instance);
        financial.MapLedgerPostingEndpoints();
        financial.MapLedgerJournalEndpoints();
        financial.MapLedgerMasterDataEndpoints();
        financial.MapChartTemplateEndpoints();
        financial.MapCompanyOptionsEndpoints();
        financial.MapBankReconciliationEndpoints();
        financial.MapPostingRequestStatusEndpoints();
        financial.MapLedgerReportEndpoints();
        financial.MapPatternEndpoints();
        financial.MapPartyEndpoints();
        financial.MapInventoryEndpoints();
        financial.MapInventoryDocumentEndpoints();
        financial.MapSalesInvoiceEndpoints();
        financial.MapSalesDocumentLinkEndpoints();
        financial.MapTreasuryVoucherEndpoints();
        financial.MapCommercialPaperEndpoints();
        financial.MapPurchaseInvoiceEndpoints();
        financial.MapCommercialReportEndpoints();
        financial.MapProfessionalReportEndpoints();
        financial.MapPostingBatchEndpoints();
        financial.MapCurrencyEndpoints();
        financial.MapBranchAdminEndpoints();
        financial.MapDocumentNumberEndpoints();
        financial.MapBranchConsolidationEndpoints();
        financial.MapInterCompanyEndpoints();
        financial.MapReportMasterCatalogEndpoints();
        financial.MapAnalyticalReportEndpoints();
        financial.MapWarehouseAccountingEndpoints();
        financial.MapFxRevaluationEndpoints();
        financial.MapOpeningBalanceEndpoints();
        financial.MapOpenItemEndpoints();
        financial.MapAccountingReconciliationEndpoints();
    }
}
