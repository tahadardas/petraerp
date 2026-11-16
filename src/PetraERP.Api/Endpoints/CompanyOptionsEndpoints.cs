using System.Globalization;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.CompanyOptions;
using PetraERP.Application.Security;
using PetraERP.Contracts.CompanyOptions;

namespace PetraERP.Api.Endpoints;

public sealed record UpdateCompanyPolicyRequest(CompanyFinancialPolicy Policy,string ExpectedVersion,string Reason);
internal static class CompanyOptionsEndpoints
{
    internal static void MapCompanyOptionsEndpoints(this RouteGroupBuilder api)
    {
        var options=api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/company-options")
            .WithTags("Company options").WithMetadata(FinancialWorkspaceMetadata.Instance).RequireAuthorization(PetraPolicies.CompanyScope);
        options.MapGet("/",async(Guid companyId,Guid branchId,ICompanyOptionsService service,CancellationToken token)=>TypedResults.Ok(await service.GetAsync(companyId,token).ConfigureAwait(false)))
            .WithName("GetCompanyFinancialOptions");
        options.MapPut("/",async(Guid companyId,Guid branchId,UpdateCompanyPolicyRequest request,ICompanyOptionsService service,FinancialRequestScope scope,HttpContext http,CancellationToken token)=>
            TypedResults.Ok(await service.SaveAsync(companyId,scope.FiscalYearId,http.RequireSession().UserId,request.Policy,request.ExpectedVersion,request.Reason,token).ConfigureAwait(false)))
            .WithName("UpdateCompanyFinancialOptions").RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.CompanyOptionsManage));
        options.MapGet("/negative-issue-requests",async(Guid companyId,Guid branchId,ICompanyOptionsService service,CancellationToken token)=>
            TypedResults.Ok(await service.ListRequestsAsync(companyId,branchId,token).ConfigureAwait(false)))
            .WithName("ListNegativeIssueRequests").RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.InventoryMovementsView));
        options.MapPost("/negative-issue-requests",RequestAsync).WithName("RequestNegativeIssueApproval")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.InventoryNegativeRequest)).Validate<NegativeIssueApprovalRequest>();
        options.MapPost("/negative-issue-requests/{requestId:guid}/review",async(Guid companyId,Guid branchId,Guid requestId,ReviewNegativeIssueRequest request,ICompanyOptionsService service,FinancialRequestScope scope,HttpContext http,CancellationToken token)=>
            TypedResults.Ok(await service.ReviewAsync(companyId,scope.FiscalYearId,branchId,http.RequireSession().UserId,requestId,request.Approve,request.ExpectedVersion,request.Reason,token).ConfigureAwait(false)))
            .WithName("ReviewNegativeIssueRequest").RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.InventoryNegativeApprove)).Validate<ReviewNegativeIssueRequest>();
    }
    private static async Task<IResult> RequestAsync(Guid companyId,Guid branchId,NegativeIssueApprovalRequest request,ICompanyOptionsService service,FinancialRequestScope scope,HttpContext http,CancellationToken token)
    {
        if(!decimal.TryParse(request.Quantity,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var quantity))
            throw new RequestValidationException("inventory.quantity_invalid","Use invariant positive decimal quantity text.","quantity");
        var result=await service.RequestAsync(companyId,scope.FiscalYearId,branchId,http.RequireSession().UserId,request.ItemId,request.WarehouseId,quantity,request.Reason,request.RequestId,token).ConfigureAwait(false);
        return TypedResults.Ok(new {result.Id,result.BranchId,result.ItemId,result.WarehouseId,Quantity=quantity.ToString("0.####",CultureInfo.InvariantCulture),result.Reason,result.Status,result.RequestedBy,result.ReviewedBy,result.Version,result.NegativePostingAvailable});
    }
}
