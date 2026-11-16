using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.Sales;
using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.Patterns;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Sales;

public sealed partial class SqlSalesInvoiceService
{
    private static Guid SalesRequestKey(string operation, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            throw new BusinessRuleException("sales.idempotency_key_invalid", "A request key of at most 128 characters is required.");
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"PetraERP.Sales.{operation}:{key}"))[..16]);
    }

    private async Task LockSalesScopeAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken)
    {
        if (context.ChangeTracker.HasChanges()) throw new InvalidOperationException("Save pending changes before posting a document.");
        context.ChangeTracker.Clear();
        await context.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result=sp_getapplock @Resource=N'PetraERP.Security.Administration', @LockMode=N'Shared', @LockOwner=N'Transaction', @LockTimeout=15000;
            IF @result<0 THROW 51010,N'Cannot acquire the security administration lock.',1;
            """, cancellationToken).ConfigureAwait(false);
        var company = await context.Companies.FromSqlInterpolated($"SELECT * FROM [system].[Companies] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={companyId}")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var branch = await context.Branches.FromSqlInterpolated($"SELECT * FROM [system].[Branches] WITH (UPDLOCK,HOLDLOCK) WHERE [CompanyId]={companyId} AND [Id]={branchId}")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (company is null || branch is null) throw new ResourceNotFoundException("Company/Branch");
        if (!company.IsActive || !branch.IsActive) throw new BusinessRuleException("sales.scope_inactive", "The company and branch must be active.");
    }

    private async Task<SalesInvoiceRow> LoadInvoiceForUpdateAsync(Guid companyId, Guid branchId, Guid invoiceId, CancellationToken cancellationToken)
        => await context.SalesInvoices.FromSqlInterpolated($"SELECT * FROM [sales].[Invoices] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={invoiceId} AND [CompanyId]={companyId} AND [BranchId]={branchId}")
            .Include(i => i.Lines).Include(i => i.Adjustments).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? throw new ResourceNotFoundException("SalesInvoice");

    private async Task<Guid> RequireOpenSalesPeriodAsync(Guid companyId, DateOnly date, CancellationToken cancellationToken)
    {
        var year = await context.FiscalYears.AsNoTracking().SingleOrDefaultAsync(y => y.CompanyId == companyId && y.RangeStart <= date && y.RangeEnd >= date, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("sales.no_fiscal_year", "No fiscal year covers this date.");
        var period = await context.AccountingPeriods.AsNoTracking().SingleOrDefaultAsync(p => p.FiscalYearId == year.Id && p.RangeStart <= date && p.RangeEnd >= date, cancellationToken).ConfigureAwait(false);
        if (period is null || period.State != (byte)AccountingPeriodState.Open) throw new BusinessRuleException("sales.period_closed", "The accounting period is not open.");
        return year.Id;
    }

    private Task ValidateStoredReferencesAsync(SalesInvoiceRow invoice, CancellationToken cancellationToken)
        => ValidateDraftReferencesAsync(invoice.CompanyId, invoice.BranchId, invoice.WarehouseId, invoice.CostCenterId,
            invoice.Lines.Select(l => new CreateSalesInvoiceLineCommand(l.ItemId, l.UnitId, l.Quantity, l.UnitPrice, CostCenterId: l.CostCenterId, WarehouseId: l.WarehouseId)).ToArray(), cancellationToken);

    private static async Task<JournalDetailsDto> ApproveSalesJournalAsync(SqlJournalEntryService service, JournalEntryScope scope,
        JournalDetailsDto journal, string reason, CancellationToken cancellationToken)
    {
        var validated = await service.ValidateDraftAsync(scope, journal.Id, new JournalDraftActionCommand
        { ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(journal.VersionBase64)), Reason = reason }, cancellationToken).ConfigureAwait(false);
        return await service.ApproveDraftAsync(scope, journal.Id, new JournalDraftActionCommand
        { ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(validated.VersionBase64)), Reason = reason }, cancellationToken).ConfigureAwait(false);
    }

    private static PatternConfiguration CanonicalSalesRoles(PatternConfiguration configuration)
    {
        var mappings = configuration.AccountMappings.Select(m => m with
        {
            Role = m.Role.ToUpperInvariant() switch
            { "SALES" => "Revenue", "CUSTOMER" => "Receivable", "COSTOFGOODS" => "COGS", _ => m.Role }
        }).ToArray();
        if (mappings.GroupBy(m => m.Role, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new BusinessRuleException("sales.pattern_roles_conflict", "The pattern contains ambiguous account roles.");
        return configuration with { AccountMappings = mappings.ToList() };
    }

    private Task<decimal> SalesBaseQuantityAsync(Guid companyId, Guid itemId, Guid unitId, Guid baseUnitId, decimal quantity, CancellationToken cancellationToken)
        => PetraERP.Infrastructure.Inventory.InventoryUnits.BaseQuantityAsync(context, companyId, itemId, unitId, baseUnitId, quantity, cancellationToken);
}
