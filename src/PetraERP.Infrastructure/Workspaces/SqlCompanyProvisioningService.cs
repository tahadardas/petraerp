using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetraERP.Application.Common;
using PetraERP.Application.Workspaces;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Registry;
using PetraERP.Infrastructure.Security;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Application.Security;

namespace PetraERP.Infrastructure.Workspaces;

public sealed partial class SqlCompanyProvisioningService : ICompanyProvisioningService
{
    private readonly SystemDbContext systemContext;
    private readonly IAccountingDbContextFactory contextFactory;
    private readonly IConfiguration configuration;
    private readonly string baseConnectionString;
    private readonly ILogger<SqlCompanyProvisioningService> logger;
    private readonly IAccountingActorAuthority actorAuthority;
    private readonly FiscalDatabaseOptions databaseOptions;

    public SqlCompanyProvisioningService(
        SystemDbContext systemContext,
        IAccountingDbContextFactory contextFactory,
        IConfiguration configuration,
        IOptions<FiscalDatabaseOptions> databaseOptions,
        ILogger<SqlCompanyProvisioningService> logger,
        IAccountingActorAuthority actorAuthority)
    {
        ArgumentNullException.ThrowIfNull(systemContext);
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(databaseOptions);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(actorAuthority);

        this.systemContext = systemContext;
        this.contextFactory = contextFactory;
        this.configuration = configuration;
        this.logger = logger;
        this.actorAuthority = actorAuthority;
        this.databaseOptions = databaseOptions.Value;
        if (!System.Text.RegularExpressions.Regex.IsMatch(this.databaseOptions.DatabaseNamePrefix, "^[A-Za-z][A-Za-z0-9_]{0,79}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidOperationException("The server-configured annual database prefix must be a bounded SQL identifier.");
        this.baseConnectionString = configuration.GetConnectionString("PetraERP")
            ?? throw new InvalidOperationException("ConnectionStrings:PetraERP is not configured.");
    }

    public async Task<ProvisioningResult> ProvisionCompanyAsync(ProvisionCompanyInput input, Guid requestedByUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await actorAuthority.VerifyInstallationAdministratorAsync(requestedByUserId, cancellationToken).ConfigureAwait(false);
        if (input.DatabaseName is not null) DatabaseIdentifier(input.DatabaseName);
        var code = EntityCode.Create(input.Code).Value.ToUpperInvariant();
        var name = BilingualName.Create(input.NameArabic, input.NameEnglish);
        var currency = PetraERP.Application.Currencies.StandardCurrencyCatalog.Find(input.BaseCurrencyCode)
            ?? throw new BusinessRuleException("company.currency_unsupported", "Choose the base currency from the supported list (Arab currencies, USD or EUR).");
        var yearInput = new ProvisionFiscalYearInput(Guid.Empty, input.InitialYear, $"FY{input.InitialYear}",
            input.FiscalYearStart, input.FiscalYearEnd, false, false, false, DatabaseName: input.DatabaseName, ChartTemplateCode: input.ChartTemplateCode);
        if (input.ChartTemplateCode is not null && PetraERP.Domain.GeneralLedger.Templates.ChartOfAccountsTemplates.Find(input.ChartTemplateCode) is null)
            throw new BusinessRuleException("template.not_available", "The selected account template is not registered.");
        FiscalYearProvisioningRules.Validate(yearInput, initialCompanySeed: true);
        Guid companyId;
        var transaction = await systemContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var company = await systemContext.Companies.FromSqlInterpolated(
                $"SELECT * FROM [system].[Companies] WITH (UPDLOCK,HOLDLOCK) WHERE [Code]={code}")
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (company is not null)
            {
                var registration = await systemContext.CompanyDatabases.SingleOrDefaultAsync(r => r.CompanyId == company.Id, cancellationToken).ConfigureAwait(false);
                if (company.IsActive || registration?.Status is not ("Failed" or "Pending") || company.NameArabic != name.Arabic
                    || company.NameEnglish != name.English || company.BaseCurrencyCode != currency.Code)
                    throw new BusinessRuleException("company.code_duplicate", $"Company code '{code}' already exists or has an operation in progress.");
                registration.Status = "Pending";
                companyId = company.Id;
            }
            else
            {
                if (!await systemContext.Currencies.AnyAsync(c => c.Code == currency.Code, cancellationToken).ConfigureAwait(false))
                    systemContext.Currencies.Add(new CurrencyRow
                    {
                        Code = currency.Code, NameArabic = currency.NameArabic, NameEnglish = currency.NameEnglish, IsActive = true,
                    });
                companyId = Guid.NewGuid();
                systemContext.Companies.Add(new CompanyRow
                {
                    Id = companyId, Code = code, NameArabic = name.Arabic, NameEnglish = name.English,
                    BaseCurrencyCode = currency.Code, IsActive = false,
                });
                var branchId = Guid.NewGuid();
                systemContext.Branches.Add(new BranchRow
                {
                    Id = branchId, CompanyId = companyId, Code = "HQ", NameArabic = "الإدارة العامة",
                    NameEnglish = "Headquarters", IsActive = true,
                });
                if (requestedByUserId != Guid.Empty)
                {
                    systemContext.UserCompanyScopes.Add(new UserCompanyScopeRow { UserId = requestedByUserId, CompanyId = companyId });
                    systemContext.UserBranchScopes.Add(new UserBranchScopeRow { UserId = requestedByUserId, CompanyId = companyId, BranchId = branchId });
                }
                systemContext.CompanyDatabases.Add(new CompanyDatabaseRegistryRow
                {
                    Id = Guid.NewGuid(), CompanyId = companyId, DatabaseKey = $"COMP_{companyId:N}",
                    // Companies and identities live in the existing central database; no per-company system database is created.
                    DatabaseName = new SqlConnectionStringBuilder(baseConnectionString).InitialCatalog, Status = "Pending",
                });
            }
            await systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        return await ProvisionFiscalYearCoreAsync(yearInput with { CompanyId = companyId }, requestedByUserId,
            initialCompanySeed: true, cancellationToken).ConfigureAwait(false);
    }

    public Task<ProvisioningResult> ProvisionFiscalYearDatabaseAsync(ProvisionFiscalYearInput input, Guid requestedByUserId,
        CancellationToken cancellationToken = default) =>
        ProvisionFiscalYearCoreAsync(input, requestedByUserId, initialCompanySeed: false, cancellationToken);

    private async Task<ProvisioningResult> ProvisionFiscalYearCoreAsync(ProvisionFiscalYearInput input, Guid requestedByUserId,
        bool initialCompanySeed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        FiscalYearProvisioningRules.Validate(input, initialCompanySeed);
        await actorAuthority.VerifyInstallationAdministratorAsync(requestedByUserId, cancellationToken).ConfigureAwait(false);
        var (company, registry, operation) = await ReserveYearAsync(input, initialCompanySeed, cancellationToken).ConfigureAwait(false);
        try
        {
            operation.CurrentStep = "CreatingOwnedDatabase";
            await systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await EnsureOwnedDatabaseAsync(registry, operation, cancellationToken).ConfigureAwait(false);
            var builder = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = registry.DatabaseName };
            using var accContext = contextFactory.CreateDbContext(builder.ConnectionString);
            accContext.BindProvisioningScope(new AccountingWriteScope(company.Id, registry.FiscalYearId, requestedByUserId, PermissionCatalog.DatabaseManage));
            await actorAuthority.VerifyAsync(new AccountingWriteScope(company.Id, registry.FiscalYearId, requestedByUserId, PermissionCatalog.DatabaseManage), true, cancellationToken).ConfigureAwait(false);
            await AccountingDatabaseReadiness.ValidateMigrationTargetAsync(accContext, cancellationToken).ConfigureAwait(false);
            operation.CurrentStep = "ApplyingMigrations";
            await systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await accContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            SqlFiscalDatabaseResolver.InvalidateReadiness();
            operation.CurrentStep = "SeedingMasterData";
            await systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var branches = await systemContext.Branches.AsNoTracking().Where(b => b.CompanyId == company.Id && b.IsActive)
                .OrderBy(b => b.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (branches.Count == 0)
                throw new BusinessRuleException("company.branch_required", "An active, registered company branch is required.");
            var seedTransaction = await accContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            await using (seedTransaction.ConfigureAwait(false))
            {
                if (!await accContext.FiscalYears.AnyAsync(y => y.Id == registry.FiscalYearId, cancellationToken).ConfigureAwait(false))
                {
                    await SeedYearMasterDataAsync(accContext, company, branches, input, registry.FiscalYearId, initialCompanySeed, cancellationToken).ConfigureAwait(false);
                    if (!initialCompanySeed) await CopyPreviousYearMasterDataAsync(accContext, input, requestedByUserId, cancellationToken).ConfigureAwait(false);
                }
                await seedTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            operation.CurrentStep = "ValidatingWorkspace";
            await systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await AccountingDatabaseReadiness.ValidateWorkspaceAsync(accContext, company.Id, registry.FiscalYearId, input.StartDate, input.EndDate, input.YearCode.Trim(), cancellationToken, company.BaseCurrencyCode, branches.Select(b => b.Id).ToArray()).ConfigureAwait(false);
            await ValidateSeededMasterDataAsync(accContext, company, branches, initialCompanySeed && input.ChartTemplateCode is null, cancellationToken).ConfigureAwait(false);
            var activation = await systemContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            await using (activation.ConfigureAwait(false))
            {
                _ = await systemContext.Companies.FromSqlInterpolated(
                    $"SELECT * FROM [system].[Companies] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={company.Id}")
                    .SingleAsync(cancellationToken).ConfigureAwait(false);
                await actorAuthority.VerifyAsync(new AccountingWriteScope(company.Id, registry.FiscalYearId, requestedByUserId, PermissionCatalog.DatabaseManage), true, cancellationToken).ConfigureAwait(false);
                var continuing = await systemContext.FiscalDatabases.AnyAsync(r => r.CompanyId == company.Id && r.Id != registry.Id
                    && (r.State == "Active" || r.State == "OpeningBalancesPending"), cancellationToken).ConfigureAwait(false);
                registry.IsDefault = !continuing && !await systemContext.FiscalDatabases.AnyAsync(r => r.CompanyId == company.Id && r.IsDefault, cancellationToken).ConfigureAwait(false);
                registry.State = continuing ? "OpeningBalancesPending" : "Active";
                registry.SchemaVersion = 1;
                company.IsActive = true;
                var companyRegistry = await systemContext.CompanyDatabases.SingleOrDefaultAsync(r => r.CompanyId == company.Id, cancellationToken).ConfigureAwait(false);
                if (companyRegistry is not null) companyRegistry.Status = "Active";
                operation.Status = "Succeeded";
                operation.CurrentStep = registry.State == "Active" ? "Completed" : "MasterDataCopied/OpeningBalancesNotRolledOver";
                operation.ErrorDetails = null;
                await systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await activation.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            return Result(registry, operation, true, null);
        }
        catch (Exception exception)
        {
            LogProvisioningFailure(logger, exception, operation.Id, operation.CurrentStep);
            // Discard any uncommitted activation changes. Persist failure in the central database only.
            systemContext.ChangeTracker.Clear();
            var failedOperation = await systemContext.ProvisioningOperations.SingleAsync(o => o.Id == operation.Id, CancellationToken.None).ConfigureAwait(false);
            var failedRegistry = await systemContext.FiscalDatabases.SingleAsync(r => r.Id == registry.Id, CancellationToken.None).ConfigureAwait(false);
            if (failedOperation.Status == "Succeeded" && failedRegistry.State == "Active")
                return Result(failedRegistry, failedOperation, true, null);
            failedOperation.Status = "Failed";
            failedOperation.ErrorDetails = exception.Message[..Math.Min(exception.Message.Length, 4000)];
            failedRegistry.State = "Failed";
            failedRegistry.IsDefault = false;
            if (initialCompanySeed)
            {
                var companyRegistry = await systemContext.CompanyDatabases.SingleAsync(r => r.CompanyId == company.Id, CancellationToken.None).ConfigureAwait(false);
                companyRegistry.Status = "Failed";
            }
            await systemContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            return Result(failedRegistry, failedOperation, false, exception is BusinessRuleException ? exception.Message : "Provisioning failed. Review the recorded operation before retrying.");
        }
    }

    private static ProvisioningResult Result(FiscalDatabaseRegistryRow registry, ProvisioningOperationRow operation, bool succeeded, string? error) =>
        new(operation.Id, registry.CompanyId, registry.FiscalYearId, registry.Id, registry.DatabaseName, succeeded, error,
            registry.State, registry.State == "OpeningBalancesPending" ? "تم نقل البيانات المرجعية فقط. لم يتم تدوير الأرصدة المالية والمخزنية." : null);

    [LoggerMessage(EventId = 6100, Level = LogLevel.Error, Message = "Fiscal database provisioning operation {OperationId} failed at {Step}")]
    private static partial void LogProvisioningFailure(ILogger logger, Exception exception, Guid operationId, string? step);

}
