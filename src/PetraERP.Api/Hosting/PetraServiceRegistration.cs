using PetraERP.Application.Productivity;

using PetraERP.Application.Workspaces;

using PetraERP.Infrastructure.Productivity;

using PetraERP.Infrastructure.Workspaces;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using PetraERP.Api.Configuration;
using PetraERP.Api.Errors;
using PetraERP.Api.OpenApi;
using PetraERP.Api.Security;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Api.Hosting;

internal static class PetraServiceRegistration
{
    internal static void AddPetraServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddPetraCore();
        services.AddHostedService<ModuleStartupService>();

        // Validated options; read lazily so test hosts and environment overrides are honoured.
        services.AddOptions<PetraApiOptions>().BindConfiguration(PetraApiOptions.SectionName)
            .Validate(static options => { options.Validate(); return true; }).ValidateOnStart();
        services.AddOptions<PetraSecurityOptions>().BindConfiguration(PetraSecurityOptions.SectionName)
            .Validate(static options => { options.Validate(); return true; }).ValidateOnStart();

        services.AddOptions<KestrelServerOptions>().Configure<IOptions<PetraApiOptions>>(static (kestrel, api) =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = api.Value.MaxRequestBodyBytes;
        });
        services.Configure<RouteHandlerOptions>(static options => options.ThrowOnBadRequest = true);
        services.Configure<JsonOptions>(static options => ConfigureJson(options.SerializerOptions));

        // Browser/webview clients are cross-origin: answer preflights before authentication. Bearer tokens travel in a header, so no cookies/credentials.
        services.AddCors();
        services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>().Configure<IOptions<PetraApiOptions>>(static (cors, api) =>
        {
            var allowed = new HashSet<string>(PetraApiOptions.BuiltInUiOrigins.Concat(api.Value.UiOrigins), StringComparer.OrdinalIgnoreCase);
            cors.AddPolicy(PetraPipeline.UiCorsPolicy, policy => policy
                .SetIsOriginAllowed(origin => allowed.Contains(origin.TrimEnd('/')))
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                .WithHeaders("Authorization", "Content-Type", "Accept", "Accept-Language", "Idempotency-Key", "If-Match", "X-Petra-Workspace", "X-Correlation-Id")
                .WithExposedHeaders("Server-Timing", "ETag", "Location", "X-Correlation-Id", "X-Petra-Workspace-Readiness", "Retry-After", "Content-Disposition")
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10)));
        });

        services.AddProblemDetails(static options => options.CustomizeProblemDetails = ApiProblems.Customize);
        services.AddExceptionHandler<ApiExceptionHandler>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<PetraSecurityOptions>>(static (jwt, security) =>
            {
                var tokens = security.Value.Tokens;
                jwt.MapInboundClaims = false;
                jwt.SaveToken = false;
                jwt.IncludeErrorDetails = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = tokens.Issuer,
                    ValidateAudience = true,
                    ValidAudience = tokens.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(security.Value.SigningKeyBytes()),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(tokens.ClockSkewSeconds),
                    NameClaimType = PetraClaims.Subject,
                };
                jwt.Events = JwtSessionEvents.Create();
            });

        services.AddAuthorization(PetraPolicies.Register);
        services.AddScoped<IAuthorizationHandler, ActiveCredentialHandler>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddScoped<IAuthorizationHandler, CompanyScopeHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();

        services.AddResponseCompression(static options => { options.EnableForHttps = true; });
        services.AddRateLimiter(RateLimitPolicies.Configure);
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
        services.AddOpenApi("v1", static options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
            options.AddDocumentTransformer<PetraOpenApiDocumentTransformer>();
            options.AddSchemaTransformer<PetraProblemSchemaTransformer>();
        });

        builder.Logging.Configure(static options => options.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
    }

    /// <summary>The external-module host: registry, installer, runtime. Modules start only when the operator has enabled them.</summary>
    private static void AddPetraModuleHost(this IServiceCollection services)
    {
        services.AddOptions<PetraERP.Infrastructure.Modules.ModuleHostOptions>().BindConfiguration(PetraERP.Infrastructure.Modules.ModuleHostOptions.SectionName);
        services.AddScoped<PetraERP.Infrastructure.Modules.ModuleRegistryStore>();
        services.AddSingleton<PetraERP.Modules.Host.IModuleStateStore, PetraERP.Infrastructure.Modules.ScopedModuleStateStore>();
        services.AddSingleton<PetraERP.Modules.Host.IModuleLoader, PetraERP.Modules.Host.AssemblyModuleLoader>();
        services.AddSingleton<PetraERP.Modules.Host.IModulePackageStager>(static p =>
        {
            var options = p.GetRequiredService<IOptions<PetraERP.Infrastructure.Modules.ModuleHostOptions>>().Value;
            var root = string.IsNullOrWhiteSpace(options.Root) ? Path.Combine(p.GetRequiredService<IHostEnvironment>().ContentRootPath, "modules") : options.Root;
            return new PetraERP.Infrastructure.Modules.DirectoryModuleStager(root, p.GetRequiredService<TimeProvider>());
        });
        services.AddSingleton(static p =>
        {
            var options = p.GetRequiredService<IOptions<PetraERP.Infrastructure.Modules.ModuleHostOptions>>().Value;
            var trust = options.RequireSignedPackages
                ? new PetraERP.Modules.Packaging.PublisherTrust(new Dictionary<string, string>(options.PublisherKeys), new HashSet<string>(options.RevokedPublishers))
                : null;
            return new PetraERP.Modules.Host.ModuleManager(p.GetRequiredService<PetraERP.Modules.Host.IModuleStateStore>(),
                p.GetRequiredService<PetraERP.Modules.Host.IModulePackageStager>(), p.GetRequiredService<TimeProvider>(), trust);
        });
        services.AddSingleton<PetraERP.Infrastructure.Modules.ModuleRuntimeLog>();
        services.AddSingleton(static p =>
        {
            var log = p.GetRequiredService<PetraERP.Infrastructure.Modules.ModuleRuntimeLog>();
            return new PetraERP.Modules.Host.ModuleRuntime(
                p.GetRequiredService<PetraERP.Modules.Host.IModuleStateStore>(), p.GetRequiredService<PetraERP.Modules.Host.IModuleLoader>(),
                () => p.GetRequiredService<PetraERP.Modules.Abstractions.ModuleServiceReferences>(), p.GetRequiredService<TimeProvider>(),
                log.Write);
        });
    }

    /// <summary>Registrations shared by the HTTP host and the installation CLI.</summary>
    internal static void AddPetraCore(this IServiceCollection services)
    {
        services.AddOptions<PetraDatabaseOptions>().BindConfiguration(PetraDatabaseOptions.SectionName)
            .Validate(static options => { options.Validate(); return true; });
        services.AddOptions<PetraSecurityOptions>().BindConfiguration(PetraSecurityOptions.SectionName);
        services.AddSingleton(TimeProvider.System);
        services.AddPetraModuleHost();
        services.AddDbContext<PetraDbContext>(static (provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var database = provider.GetRequiredService<IOptions<PetraDatabaseOptions>>().Value;
            var connectionString = configuration.GetConnectionString("PetraERP");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:PetraERP is not configured. Use user-secrets (development) or a protected environment " +
                    "variable (installation). Connection strings are never stored in source control.");
            }

            options.UseSqlServer(connectionString, sql => sql
                .UseCompatibilityLevel(database.CompatibilityLevel)
                .CommandTimeout(database.CommandTimeoutSeconds))
                .AddInterceptors(PetraERP.Infrastructure.Diagnostics.SqlTimingInterceptor.Instance, PetraERP.Infrastructure.Diagnostics.ReadCommittedConnectionInterceptor.Instance);
        });

        services.AddSingleton(static provider =>
        {
            var settings = provider.GetRequiredService<IOptions<PetraSecurityOptions>>().Value.ToAuthenticationSettings();
            settings.Validate();
            return settings;
        });
        services.AddScoped<CentralDataContext>(p => new CentralDataContext(p.GetRequiredService<SystemDbContext>()));
        services.AddScoped<FinancialRequestScope>();
        services.AddScoped<FinancialDbContext>(p => p.GetRequiredService<FinancialRequestScope>().Context);
        // Lazy annual accessor: lets a scoped service (e.g. workspace repository) serve central-only endpoints
        // without forcing a workspace binding, and resolve the annual context only on financial operations.
        services.AddScoped<Func<FinancialDbContext>>(p => p.GetRequiredService<FinancialDbContext>);
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddScoped<IAuthenticationService, SqlAuthenticationService>();
        services.AddScoped<ISessionValidator, SqlSessionValidator>();
        services.AddScoped<IAccessEvaluator, SqlAccessEvaluator>();
        services.AddScoped<IUserContextQueries, SqlUserContextQueries>();
        services.AddScoped<ISecurityAdministrationService, SqlSecurityAdministrationService>();
        services.AddScoped<ISecurityAuditQueries, SqlSecurityAuditQueries>();
        services.AddScoped<ISecurityAuditRecorder, SqlSecurityAuditRecorder>();
        services.AddScoped<ISecurityBootstrapper, SqlSecurityBootstrapper>();
        services.AddScoped<IPostingService, SqlPostingService>();
        services.AddScoped<PetraERP.Modules.Abstractions.IFinancialPostingGateway, PetraERP.Infrastructure.Modules.SqlFinancialPostingGateway>();
        services.AddScoped<PetraERP.Modules.Abstractions.IInventoryMovementGateway, PetraERP.Infrastructure.Modules.SqlInventoryMovementGateway>();
        services.AddScoped<PetraERP.Infrastructure.Modules.IModuleLedgerContexts, PetraERP.Infrastructure.Modules.AccountingModuleLedgerContexts>();
        services.AddScoped<PetraERP.Infrastructure.Modules.SqlFinancialPostingGateway>();
        services.AddScoped<PetraERP.Infrastructure.Modules.SqlInventoryMovementGateway>();
        services.AddScoped<PetraERP.Modules.Abstractions.IReferenceDataQuery, PetraERP.Infrastructure.Modules.SqlReferenceDataQuery>();
        services.AddScoped<PetraERP.Infrastructure.Modules.SqlReferenceDataQuery>();
        services.AddScoped<PetraERP.Modules.Abstractions.IAuditTrail, PetraERP.Infrastructure.Modules.SqlModuleAuditTrail>();
        services.AddScoped<PetraERP.Infrastructure.Modules.SqlModuleAuditTrail>();
        // Modules receive per-call scoped wrappers: they are activated once but must never keep a request's database context.
        services.AddSingleton<PetraERP.Modules.Abstractions.ModuleServiceReferences>(p =>
            PetraERP.Infrastructure.Modules.ModuleServiceComposition.Create(p.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>()));
        services.AddScoped<IJournalEntryService, SqlJournalEntryService>();
        services.AddScoped<PetraERP.Application.GeneralLedger.ChartOfAccounts.IChartOfAccountsService, SqlChartOfAccountsService>();
        services.AddScoped<PetraERP.Application.GeneralLedger.ChartOfAccounts.IChartTemplateImportService, SqlChartOfAccountsService>();
        services.AddScoped<PetraERP.Application.GeneralLedger.Calendars.IAccountingCalendarService, PetraERP.Infrastructure.AccountingCalendars.SqlAccountingCalendarService>();
        services.AddSingleton<PetraERP.Application.GeneralLedger.Reports.ILedgerCursorSigner>(_ => PetraERP.Api.Security.HmacLedgerCursorSigner.FromEnvironment());
        services.AddScoped<PetraERP.Application.GeneralLedger.Reports.ILedgerQueryService, SqlLedgerQueryService>();
        services.AddScoped<PetraERP.Application.GeneralLedger.Reports.IAnalyticalReportService>(sp => (SqlLedgerQueryService)sp.GetRequiredService<PetraERP.Application.GeneralLedger.Reports.ILedgerQueryService>());
        services.AddScoped<IWorkspaceRepository, SqlWorkspaceRepository>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddScoped<PetraERP.Application.Patterns.IPatternService, PetraERP.Infrastructure.Patterns.SqlPatternService>();
        services.AddScoped<PetraERP.Application.Parties.IPartyService, PetraERP.Infrastructure.Parties.SqlPartyService>();
        services.AddScoped<PetraERP.Application.Inventory.IInventoryService, PetraERP.Infrastructure.Inventory.SqlInventoryService>();
        services.AddScoped<PetraERP.Application.Inventory.IInventoryDocumentService, PetraERP.Infrastructure.Inventory.SqlInventoryDocumentService>();
        services.AddScoped<PetraERP.Application.Sales.ISalesInvoiceService, PetraERP.Infrastructure.Sales.SqlSalesInvoiceService>();
        services.AddScoped<PetraERP.Application.DocumentProcessing.IDocumentSaveAndProcessService, PetraERP.Infrastructure.DocumentProcessing.SqlDocumentSaveAndProcessService>();
        services.AddScoped<PetraERP.Application.DocumentProcessing.IBulkDocumentPostingService, PetraERP.Infrastructure.DocumentProcessing.SqlBulkDocumentPostingService>();
        services.AddScoped<PetraERP.Application.Sales.ISalesDocumentLinkService, PetraERP.Infrastructure.Sales.SqlSalesDocumentLinkService>();
        services.AddScoped<PetraERP.Infrastructure.Treasury.SqlTreasuryVoucherService>();
        services.AddScoped<PetraERP.Application.Treasury.ITreasuryVoucherService>(p => p.GetRequiredService<PetraERP.Infrastructure.Treasury.SqlTreasuryVoucherService>());
        services.AddScoped<PetraERP.Application.Treasury.ISettlementService>(p => p.GetRequiredService<PetraERP.Infrastructure.Treasury.SqlTreasuryVoucherService>());
        services.AddScoped<PetraERP.Application.Treasury.ICommercialPaperService, PetraERP.Infrastructure.Treasury.SqlCommercialPaperService>();
        services.AddScoped<PetraERP.Application.Purchases.IPurchaseInvoiceService, PetraERP.Infrastructure.Purchases.SqlPurchaseInvoiceService>();
        services.AddScoped<PetraERP.Application.Reports.ICommercialReportService, PetraERP.Infrastructure.Reports.SqlCommercialReportService>();
        services.AddScoped<PetraERP.Application.Reports.Professional.IProfessionalReportService, PetraERP.Infrastructure.Reports.Professional.SqlProfessionalReportService>();
        services.AddScoped<PetraERP.Application.Currencies.ICurrencyService, PetraERP.Infrastructure.Currencies.SqlCurrencyService>();
        services.AddScoped<PetraERP.Application.Branches.IBranchAdminService, PetraERP.Infrastructure.Organizations.SqlBranchAdminService>();
        services.AddScoped<PetraERP.Application.Branches.IBranchSubAccountService, PetraERP.Infrastructure.Organizations.SqlBranchSubAccountService>();
        services.AddScoped<PetraERP.Application.GeneralLedger.IJournalVoucherHeaderService, PetraERP.Infrastructure.GeneralLedger.SqlJournalVoucherHeaderService>();
        services.AddScoped<PetraERP.Application.GeneralLedger.ChartOfAccounts.IAccountProfileService, PetraERP.Infrastructure.GeneralLedger.SqlAccountProfileService>();
        services.AddScoped<PetraERP.Application.Inventory.IWarehouseAccountingService, PetraERP.Infrastructure.Inventory.SqlWarehouseAccountingService>();
        services.AddScoped<PetraERP.Application.Branches.IBranchConsolidationService, PetraERP.Infrastructure.Organizations.SqlBranchConsolidationService>();
        services.AddScoped<PetraERP.Application.InterCompany.IInterCompanyReconciliationService, PetraERP.Infrastructure.InterCompany.SqlInterCompanyReconciliationService>();
        services.AddScoped<PetraERP.Application.DocumentNumbers.IDocumentNumberMaintenanceService, PetraERP.Infrastructure.DocumentNumbers.SqlDocumentNumberMaintenanceService>();
        services.AddScoped<PetraERP.Application.Currencies.IFxRevaluationService, PetraERP.Infrastructure.Currencies.SqlFxRevaluationService>();

        // Multi-database and Workspace Infrastructure
        services.AddDbContext<SystemDbContext>(static (provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var database = provider.GetRequiredService<IOptions<PetraDatabaseOptions>>().Value;
            var connectionString = configuration.GetConnectionString("PetraERP");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings:PetraERP is not configured.");
            }

            options.UseSqlServer(connectionString, sql => sql
                .UseCompatibilityLevel(database.CompatibilityLevel)
                .CommandTimeout(database.CommandTimeoutSeconds))
                .AddInterceptors(PetraERP.Infrastructure.Diagnostics.SqlTimingInterceptor.Instance, PetraERP.Infrastructure.Diagnostics.ReadCommittedConnectionInterceptor.Instance);
        });
        services.AddOptions<FiscalDatabaseOptions>().BindConfiguration("Petra:Database");
        services.AddScoped<IFiscalDatabaseResolver, SqlFiscalDatabaseResolver>();
        services.AddScoped<IAccountingDbContextFactory, AccountingDbContextFactory>();
        services.AddScoped<PetraERP.Infrastructure.Persistence.Actors.IAccountingActorAuthority, PetraERP.Infrastructure.Persistence.Actors.SqlAccountingActorAuthority>();
        services.AddScoped<IWorkspaceAuthorizationService, SqlWorkspaceAuthorizationService>();
        services.AddScoped<ICompanyProvisioningService, SqlCompanyProvisioningService>();
        services.AddScoped<IAdministratorConfirmation, SqlAdministratorConfirmation>();
        services.AddScoped<PetraERP.Application.Posting.IPostingRequestStatusService, PetraERP.Infrastructure.GeneralLedger.SqlPostingRequestStatusService>();
        services.AddScoped<PetraERP.Application.Treasury.IBankReconciliationService, PetraERP.Infrastructure.Treasury.SqlBankReconciliationService>();
        services.AddScoped<PetraERP.Application.CompanyOptions.ICompanyOptionsService, PetraERP.Infrastructure.CompanyOptions.SqlCompanyOptionsService>();
        services.AddScoped<ILegacyToIndependentDbMigrator, SqlLegacyToIndependentDbMigrator>();
        services.AddScoped<PetraERP.Application.GeneralLedger.OpeningBalances.IOpeningBalanceService, PetraERP.Infrastructure.GeneralLedger.OpeningBalances.SqlOpeningBalanceService>();
        services.AddScoped<PetraERP.Application.Parties.OpenItems.IOpenItemService, PetraERP.Infrastructure.Parties.OpenItems.SqlOpenItemService>();
        services.AddScoped<PetraERP.Application.Inventory.Opening.IInventoryOpeningService, PetraERP.Infrastructure.Inventory.SqlInventoryOpeningService>();
        services.AddScoped<PetraERP.Application.Workspaces.Rollover.IFiscalYearRolloverService, PetraERP.Infrastructure.Workspaces.SqlFiscalYearRolloverService>();
        services.AddScoped<PetraERP.Infrastructure.GeneralLedger.Reconciliation.IPriorYearClosingSource, PetraERP.Infrastructure.GeneralLedger.Reconciliation.FiscalDatabasePriorYearClosingSource>();
        services.AddScoped<PetraERP.Application.GeneralLedger.Reconciliation.IAccountingReconciliationService, PetraERP.Infrastructure.GeneralLedger.Reconciliation.SqlAccountingReconciliationService>();
        services.AddScoped<PetraERP.Application.Administration.IBackupRestoreCoordinator, PetraERP.Infrastructure.Administration.SqlBackupRestoreCoordinator>();
        services.AddSingleton(provider => provider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()
            .GetSection(PetraERP.Infrastructure.AlAmeenMigration.AlAmeenMigrationOptions.SectionName)
            .Get<PetraERP.Infrastructure.AlAmeenMigration.AlAmeenMigrationOptions>() ?? new PetraERP.Infrastructure.AlAmeenMigration.AlAmeenMigrationOptions());
        services.AddScoped<PetraERP.Application.AlAmeenMigration.IMigrationCoordinator, PetraERP.Infrastructure.AlAmeenMigration.AlAmeenMigrationCoordinator>();
    }

    internal static void ConfigureJson(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.RespectNullableAnnotations = true;
        options.AllowTrailingCommas = false;
        options.ReadCommentHandling = JsonCommentHandling.Disallow;
    }
}
