using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Security;
using PetraERP.Application.Workspaces;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Workspaces;

namespace PetraERP.Api.Endpoints;

internal static class WorkspaceSelectionEndpoints
{
    internal static void MapWorkspaceSelectionEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/workspaces").WithTags("Workspaces & Tenant Databases");

        api.MapGet("/me/workspaces", ListMyWorkspacesAsync)
            .WithTags("Current user")
            .WithName("ListMyWorkspaces")
            .WithSummary("Lists active company-year workspaces the authenticated user is authorized to open.")
            .RequireAuthorization()
            .Produces<WorkspaceListResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/currencies", ListSupportedCompanyCurrencies)
            .WithName("ListSupportedCompanyCurrencies")
            .WithSummary("Base currencies accepted for a new company: Arab League currencies plus USD and EUR.")
            .RequireAuthorization(PetraPolicies.CredentialMaintenance)
            .Produces<SupportedCurrencyListResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/companies", ProvisionCompanyAsync)
            .WithName("ProvisionCompanyWorkspace")
            .WithSummary("Creates a new legal company and provisions its initial fiscal year database.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.DatabaseManage))
            .Produces<ProvisioningOperationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/fiscal-years", ProvisionFiscalYearAsync)
            .WithName("ProvisionFiscalYearWorkspace")
            .WithSummary("Provisions a new independent annual database for an existing company.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.DatabaseManage))
            .Produces<ProvisioningOperationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/migration/plan", GetMigrationPlanAsync)
            .WithName("GetMigrationPlan")
            .WithSummary("Analyzes monolithic source and prepares a partitioned annual migration plan.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.DatabaseManage))
            .Produces<IReadOnlyList<MigrationSummaryResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/migration/execute", ExecuteMigrationAsync)
            .WithName("ExecuteMigration")
            .WithSummary("Executes or tests migration for a single company fiscal year into an independent database.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.DatabaseManage))
            .Produces<MigrationSummaryResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> ListMyWorkspacesAsync(
        IWorkspaceAuthorizationService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = httpContext.RequireSession();
        var authorized = await authService.GetAuthorizedWorkspacesAsync(session.UserId, cancellationToken).ConfigureAwait(false);

        var list = authorized.Select(static w => new WorkspaceSummaryResponse
        {
            WorkspaceId = w.WorkspaceId,
            CompanyId = w.CompanyId,
            CompanyCode = w.CompanyCode,
            CompanyNameArabic = w.CompanyNameArabic,
            CompanyNameEnglish = w.CompanyNameEnglish,
            BaseCurrencyCode = w.BaseCurrencyCode,
            FiscalYearId = w.FiscalYearId,
            YearNumber = w.YearNumber,
            FiscalYearCode = $"FY{w.YearNumber}",
            State = w.State,
            IsDefault = w.IsDefault
        }).ToList();

        return TypedResults.Ok(new WorkspaceListResponse
        {
            Items = list.AsReadOnly()
        });
    }

    private static Microsoft.AspNetCore.Http.HttpResults.Ok<SupportedCurrencyListResponse> ListSupportedCompanyCurrencies()
    {
        var items = PetraERP.Application.Currencies.StandardCurrencyCatalog.All.Select(static c => new SupportedCurrencyResponse
        {
            Code = c.Code,
            NameArabic = c.NameArabic,
            NameEnglish = c.NameEnglish,
        }).ToList();

        return TypedResults.Ok(new SupportedCurrencyListResponse
        {
            Items = items.AsReadOnly()
        });
    }

    private static async Task<IResult> ProvisionCompanyAsync(
        CreateCompanyWorkspaceRequest request,
        ICompanyProvisioningService provisioningService,
        IAdministratorConfirmation confirmation,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = httpContext.RequireSession();
        if (string.IsNullOrWhiteSpace(request.AdministratorPassword))
        {
            return ApiProblems.Result(httpContext, StatusCodes.Status400BadRequest, "administrator.password_required", "Administrator password confirmation is mandatory.");
        }

        await confirmation.ConfirmAsync(session.UserId, request.AdministratorPassword, cancellationToken).ConfigureAwait(false);

        var input = new ProvisionCompanyInput(
            Code: request.Code,
            NameArabic: request.NameArabic,
            NameEnglish: request.NameEnglish,
            BaseCurrencyCode: request.BaseCurrencyCode,
            InitialYear: request.InitialYear,
            FiscalYearStart: request.StartDate,
            FiscalYearEnd: request.EndDate,
            DatabaseName: request.DatabaseName,
            ChartTemplateCode: request.ChartTemplateCode);

        var result = await provisioningService.ProvisionCompanyAsync(input, session.UserId, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return ApiProblems.Result(httpContext, StatusCodes.Status400BadRequest, "company.provisioning_failed", result.Error);
        }

        return TypedResults.Created($"/workspaces/{result.WorkspaceId}", new ProvisioningOperationResponse
        {
            OperationId = result.OperationId,
            CompanyId = result.CompanyId,
            FiscalYearId = result.FiscalYearId,
            Status = result.ReadinessState == "Active" ? "Succeeded" : "MasterDataCopied",
            ReadinessState = result.ReadinessState,
            Notice = result.Notice,
            TargetDatabaseName = result.DatabaseName,
            CurrentStep = result.ReadinessState == "Active" ? "Completed" : "OpeningBalancesNotRolledOver",
            ErrorDetails = null
        });
    }

    private static async Task<IResult> ProvisionFiscalYearAsync(
        CreateFiscalYearWorkspaceRequest request,
        ICompanyProvisioningService provisioningService,
        IAccessEvaluator accessEvaluator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = httpContext.RequireSession();
        var decision = await accessEvaluator.EvaluateCompanyScopeAsync(session.UserId, request.CompanyId, cancellationToken).ConfigureAwait(false);
        if (decision != AccessDecision.Granted)
        {
            return ApiProblems.Result(httpContext, StatusCodes.Status403Forbidden, ApiErrorCodes.CompanyScopeDenied, "You are not authorized for this company.");
        }

        var input = new ProvisionFiscalYearInput(
            CompanyId: request.CompanyId,
            YearNumber: request.YearNumber,
            YearCode: request.YearCode,
            StartDate: request.StartDate,
            EndDate: request.EndDate,
            CopyChartOfAccounts: request.CopyChartOfAccounts,
            CopyWarehouses: request.CopyWarehouses,
            CopyPatterns: request.CopyPatterns,
            SourceFiscalYearId: request.SourceFiscalYearId,
            DatabaseName: request.DatabaseName);

        var result = await provisioningService.ProvisionFiscalYearDatabaseAsync(input, session.UserId, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return ApiProblems.Result(httpContext, StatusCodes.Status400BadRequest, "fiscal_year.provisioning_failed", result.Error);
        }

        return TypedResults.Created($"/workspaces/{result.WorkspaceId}", new ProvisioningOperationResponse
        {
            OperationId = result.OperationId,
            CompanyId = result.CompanyId,
            FiscalYearId = result.FiscalYearId,
            Status = result.ReadinessState == "Active" ? "Succeeded" : "MasterDataCopied",
            ReadinessState = result.ReadinessState,
            Notice = result.Notice,
            TargetDatabaseName = result.DatabaseName,
            CurrentStep = result.ReadinessState == "Active" ? "Completed" : "OpeningBalancesNotRolledOver",
            ErrorDetails = null
        });
    }

    private static async Task<IResult> GetMigrationPlanAsync(
        ILegacyToIndependentDbMigrator migrator,
        CancellationToken cancellationToken)
    {
        var plan = await migrator.PlanMigrationAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(plan.Select(ToResponse).ToList());
    }

    private static async Task<IResult> ExecuteMigrationAsync(
        Guid companyId,
        Guid fiscalYearId,
        bool dryRun,
        ILegacyToIndependentDbMigrator migrator,
        IAccessEvaluator accessEvaluator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = httpContext.RequireSession();
        var decision = await accessEvaluator.EvaluateCompanyScopeAsync(session.UserId, companyId, cancellationToken).ConfigureAwait(false);
        if (decision != AccessDecision.Granted)
        {
            return ApiProblems.Result(httpContext, StatusCodes.Status403Forbidden, ApiErrorCodes.CompanyScopeDenied, "You are not authorized for this company.");
        }

        var result = await migrator.MigrateCompanyYearAsync(companyId, fiscalYearId, dryRun, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToResponse(result));
    }

    private static MigrationSummaryResponse ToResponse(MigrationSummary summary) => new()
    {
        CompanyId = summary.CompanyId,
        FiscalYearId = summary.FiscalYearId,
        TargetDatabaseName = summary.TargetDatabaseName,
        AccountsMigrated = summary.AccountsMigrated,
        CostCentersMigrated = summary.CostCentersMigrated,
        JournalEntriesMigrated = summary.JournalEntriesMigrated,
        JournalLinesMigrated = summary.JournalLinesMigrated,
        TotalDebit = FinancialDecimalText.Format(summary.TotalDebit, FinancialDecimalText.MoneyScale),
        TotalCredit = FinancialDecimalText.Format(summary.TotalCredit, FinancialDecimalText.MoneyScale),
        ItemsMigrated = summary.ItemsMigrated,
        WarehousesMigrated = summary.WarehousesMigrated,
        StockMovementsMigrated = summary.StockMovementsMigrated,
        TotalStockQuantity = FinancialDecimalText.Format(summary.TotalStockQuantity, FinancialDecimalText.QuantityScale),
        SalesInvoicesMigrated = summary.SalesInvoicesMigrated,
        PurchaseInvoicesMigrated = summary.PurchaseInvoicesMigrated,
        VouchersMigrated = summary.VouchersMigrated,
        Discrepancies = summary.Discrepancies
    };
}
