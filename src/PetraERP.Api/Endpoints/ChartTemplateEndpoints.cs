using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Security;
using PetraERP.Domain.GeneralLedger.Templates;

namespace PetraERP.Api.Endpoints;

internal static class ChartTemplateEndpoints
{
    internal static void MapChartTemplateEndpoints(this RouteGroupBuilder api)
    {
        var templates=api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/ledger/chart/templates")
            .WithTags("Chart templates").WithMetadata(FinancialWorkspaceMetadata.Instance);
        templates.MapPost("/preview", async (Guid companyId,Guid branchId,ChartOfAccountsTemplate template,[FromServices] IChartTemplateImportService service,HttpContext http,CancellationToken token) =>
            TypedResults.Ok(await service.PreviewAsync(LedgerMasterScope.Create(companyId,http.RequireSession().UserId,branchId),template,token).ConfigureAwait(false)))
            .WithName("PreviewAccountTemplateImport").RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.AccountsManage));
        templates.MapPost("/import", async (Guid companyId,Guid branchId,ChartOfAccountsTemplate template,[FromServices] IChartTemplateImportService service,HttpContext http,CancellationToken token) =>
            TypedResults.Ok(await service.ImportAsync(LedgerMasterScope.Create(companyId,http.RequireSession().UserId,branchId),template,token).ConfigureAwait(false)))
            .WithName("ImportAccountTemplate").RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.AccountsManage));
        templates.MapPost("/file-preview", ReadFileAsync).WithName("PreviewAccountTemplateFile")
            .WithMetadata(new RequestSizeLimitAttribute(5*1024*1024)).DisableAntiforgery()
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.AccountsManage));
    }

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter());
        return json;
    }

    private static async Task<IResult> ReadFileAsync(Guid companyId,Guid branchId,IFormFile file,[FromForm] string metadata,
        [FromServices] IChartTemplateImportService service,HttpContext http,CancellationToken token)
    {
        ChartOfAccountsTemplate template;
        try { template=JsonSerializer.Deserialize<ChartOfAccountsTemplate>(metadata,JsonOptions) ?? throw new JsonException(); }
        catch(JsonException) { throw new RequestValidationException("template.metadata_invalid","Provide valid versioned template metadata.","metadata"); }
        using var stream=file.OpenReadStream();
        return TypedResults.Ok(await service.ReadFileAsync(LedgerMasterScope.Create(companyId,http.RequireSession().UserId,branchId),file.FileName,stream,template,token).ConfigureAwait(false));
    }
}
