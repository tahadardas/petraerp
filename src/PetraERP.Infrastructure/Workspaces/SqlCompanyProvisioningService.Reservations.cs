using System.Data;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Workspaces;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence.Registry;

namespace PetraERP.Infrastructure.Workspaces;

public sealed partial class SqlCompanyProvisioningService
{
    private async Task<(CompanyRow Company, FiscalDatabaseRegistryRow Registry, ProvisioningOperationRow Operation)> ReserveYearAsync(
        ProvisionFiscalYearInput input, bool initialCompanySeed, CancellationToken cancellationToken)
    {
        if (input.DatabaseName is not null) DatabaseIdentifier(input.DatabaseName);
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            input.CompanyId, input.YearNumber, Code=input.YearCode.Trim(), input.StartDate, input.EndDate,
            input.CopyChartOfAccounts, input.CopyWarehouses, input.CopyPatterns, input.SourceFiscalYearId, input.DatabaseName, input.ChartTemplateCode,
        })));
        var transaction = await systemContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var company = await systemContext.Companies.FromSqlInterpolated(
                $"SELECT * FROM [system].[Companies] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={input.CompanyId}")
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException($"Company '{input.CompanyId}' not found.");
            if (!company.IsActive && !initialCompanySeed)
                throw new BusinessRuleException("company.inactive", "An active company is required.");
            var years = await systemContext.FiscalDatabases.Where(r => r.CompanyId == company.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var existing = years.SingleOrDefault(r => r.YearNumber == input.YearNumber);
            ProvisioningOperationRow operation;
            FiscalDatabaseRegistryRow registry;
            if (existing is not null)
            {
                operation = await systemContext.ProvisioningOperations.SingleAsync(o => o.FiscalYearId == existing.FiscalYearId
                    && o.CompanyId == company.Id && o.OperationType == "CreateFiscalDatabase", cancellationToken).ConfigureAwait(false);
                if (existing.State != "Failed" || operation.Status != "Failed" || existing.YearCode != input.YearCode.Trim()
                    || existing.RangeStart != input.StartDate || existing.RangeEnd != input.EndDate
                    || input.DatabaseName is not null && !string.Equals(existing.DatabaseName, input.DatabaseName, StringComparison.OrdinalIgnoreCase))
                    throw new BusinessRuleException("fiscal_year.duplicate", "The year already exists, differs from the failed request, or has an operation in progress.");
                if (existing.ProvisioningFingerprint is not null && existing.ProvisioningFingerprint != fingerprint
                    || existing.ProvisioningFingerprint is null && input.SourceFiscalYearId is not null)
                    throw new BusinessRuleException("fiscal_year.retry_conflict", "Retry must preserve the original master-data source and provisioning choices.");
                registry = existing;
            }
            else
            {
                if (years.Any(r => r.RangeStart is null || r.RangeEnd is null))
                    throw new BusinessRuleException("fiscal_year.range_unverified", "Existing registered year ranges must be verified before adding a year.");
                if (years.Any(r => r.YearCode == input.YearCode.Trim()
                    || (r.RangeStart <= input.EndDate && r.RangeEnd >= input.StartDate)))
                    throw new BusinessRuleException("fiscal_year.overlap", "The fiscal year overlaps an existing range or duplicates its code.");
                var fiscalYearId = Guid.NewGuid();
                registry = new FiscalDatabaseRegistryRow
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id, FiscalYearId = fiscalYearId,
                    YearNumber = input.YearNumber, YearCode = input.YearCode.Trim(), RangeStart = input.StartDate, RangeEnd = input.EndDate,
                    SafeDatabaseKey = $"PDB_{company.Id:N}_{input.YearNumber}",
                    DatabaseName = input.DatabaseName ?? $"{databaseOptions.DatabaseNamePrefix}_{company.Id:N}_{input.YearNumber}", SchemaVersion = 0,
                };
                if (await systemContext.FiscalDatabases.AnyAsync(r => r.DatabaseName == registry.DatabaseName, cancellationToken).ConfigureAwait(false))
                    throw new BusinessRuleException("workspace.database_name_conflict", "The requested database name is already reserved.");
                operation = new ProvisioningOperationRow
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id, FiscalYearId = fiscalYearId,
                    OperationType = "CreateFiscalDatabase", TargetDatabaseName = registry.DatabaseName,
                };
                systemContext.FiscalDatabases.Add(registry);
                systemContext.ProvisioningOperations.Add(operation);
            }
            registry.ProvisioningFingerprint = fingerprint;
            registry.SourceFiscalYearId = input.SourceFiscalYearId;
            registry.State = "Provisioning";
            operation.Status = "InProgress";
            operation.CurrentStep = "ValidatingSchema";
            operation.ErrorDetails = null;
            if (initialCompanySeed)
            {
                var companyRegistry = await systemContext.CompanyDatabases.SingleAsync(r => r.CompanyId == company.Id, cancellationToken).ConfigureAwait(false);
                companyRegistry.Status = "Provisioning";
            }
            await systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return (company, registry, operation);
        }
    }
}
