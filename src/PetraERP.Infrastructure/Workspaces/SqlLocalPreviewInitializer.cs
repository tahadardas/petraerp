using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Inventory;
using PetraERP.Application.Parties;
using PetraERP.Application.Patterns;
using PetraERP.Application.Security;
using PetraERP.Application.Workspaces;
using PetraERP.Domain.GeneralLedger.Templates;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Parties;
using PetraERP.Domain.Patterns;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Workspaces;

/// <summary>First-run local preview setup. Uses the same scoped services and actor checks as HTTP operations.
/// Existing master data and financial documents are never replaced or reset.</summary>
public sealed class SqlLocalPreviewInitializer(SystemDbContext central, ICompanyProvisioningService provisioning,
    IFiscalDatabaseResolver resolver, IAccountingDbContextFactory factory, IAdministratorConfirmation confirmation)
{
    public async Task<LocalPreviewWorkspace> InitializeAsync(Guid installationId, string userName, string password,
        DateOnly localDate, CancellationToken token = default)
    {
        await VerifyOwnerAsync(installationId, token).ConfigureAwait(false);
        var normalizedName = userName.Trim().ToUpperInvariant();
        var actor = await central.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedUserName == normalizedName, token).ConfigureAwait(false)
            ?? throw new BusinessRuleException("preview.administrator_missing", "The existing preview administrator was not found.");
        await confirmation.ConfirmAsync(actor.Id, password, token).ConfigureAwait(false);
        var company = await central.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Code == "PREVIEW", token).ConfigureAwait(false)
            ?? throw new BusinessRuleException("preview.company_missing", "The preview bootstrap company was not found.");
        // Historical central transactions are evidence, never an annual runtime fallback or an empty opening state.
        // A distinct new demo company has a legitimate first year without pretending that historical balances rolled.
        if (!await central.FiscalDatabases.AnyAsync(y => y.CompanyId == company.Id, token).ConfigureAwait(false)
            && await HasLegacyFinancialHistoryAsync(company.Id, token).ConfigureAwait(false))
        {
            var demo = await central.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Code == SeparateDemoCode, token).ConfigureAwait(false);
            if (demo is null || !demo.IsActive)
            {
                var result = await provisioning.ProvisionCompanyAsync(new ProvisionCompanyInput(SeparateDemoCode,
                    "شركة بترا التجريبية المستقلة", "Petra independent preview company", company.BaseCurrencyCode, localDate.Year,
                    new DateOnly(localDate.Year, 1, 1), new DateOnly(localDate.Year, 12, 31), ChartTemplateCode: ChartOfAccountsTemplates.GenericCode),
                    actor.Id, token).ConfigureAwait(false);
                if (!result.Succeeded || result.ReadinessState != "Active")
                    throw new BusinessRuleException("preview.provisioning_failed", "Separate preview company provisioning failed. Historical PREVIEW data was preserved; review the recorded operation before retrying.");
                demo = await central.Companies.AsNoTracking().SingleAsync(c => c.Id == result.CompanyId, token).ConfigureAwait(false);
            }
            company = demo;
        }
        var branchCode = company.Code == "PREVIEW" ? "MAIN" : "HQ";
        var branch = await central.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.CompanyId == company.Id && b.Code == branchCode && b.IsActive, token).ConfigureAwait(false)
            ?? throw new BusinessRuleException("preview.branch_missing", "The selected preview company's registered branch was not found or is inactive.");
        var registered = await central.FiscalDatabases.AsNoTracking().Where(y => y.CompanyId == company.Id).ToListAsync(token).ConfigureAwait(false);
        var current = registered.SingleOrDefault(y => y.YearNumber == localDate.Year);
        if (current is null || current.State == "Failed")
        {
            if (current is null && registered.Count != 0)
                throw new BusinessRuleException("preview.existing_year_requires_selection", "Preview already has fiscal data in another year. Select that year or complete the normal year rollover; startup cannot bypass opening balances.");
            var provisioned = await provisioning.ProvisionFiscalYearDatabaseAsync(new ProvisionFiscalYearInput(company.Id,
                localDate.Year, "FY" + localDate.Year.ToString(CultureInfo.InvariantCulture), new DateOnly(localDate.Year, 1, 1),
                new DateOnly(localDate.Year, 12, 31), false, false, false), actor.Id, token).ConfigureAwait(false);
            if (!provisioned.Succeeded || provisioned.ReadinessState != "Active")
                throw new BusinessRuleException("preview.provisioning_failed", "Initial fiscal provisioning failed. Review the recorded provisioning operation and retry safely.");
            current = await central.FiscalDatabases.AsNoTracking().SingleAsync(y => y.Id == provisioned.WorkspaceId, token).ConfigureAwait(false);
        }
        if (current.State != "Active")
            throw new BusinessRuleException("preview.workspace_not_active", "The preview workspace is incomplete or awaiting opening balances. Startup cannot mark it ready.");
        var dbsToMigrate = registered.Concat([current]).DistinctBy(y => y.Id)
            .Where(y => y.State is "Active" or "OpeningBalancesPending");
        foreach (var db in dbsToMigrate)
        {
            var targetCs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(central.Database.GetConnectionString()) { InitialCatalog = db.DatabaseName }.ConnectionString;
            using var accContext = factory.CreateDbContext(targetCs);
            await AccountingDatabaseReadiness.ValidateMigrationTargetAsync(accContext, token).ConfigureAwait(false);
            var pending = (await accContext.Database.GetPendingMigrationsAsync(token).ConfigureAwait(false)).ToList();
            if (pending.Count > 0)
            {
                await accContext.Database.MigrateAsync(token).ConfigureAwait(false);
                SqlFiscalDatabaseResolver.InvalidateReadiness();
            }
        }
        var workspace = await resolver.ResolveAsync(current.Id, token).ConfigureAwait(false);
        await EnsureChartAsync(workspace, branch.Id, actor.Id, token).ConfigureAwait(false);
        await EnsureInventoryAsync(workspace, branch.Id, actor.Id, token).ConfigureAwait(false);
        await EnsurePartiesAsync(workspace, branch.Id, actor.Id, token).ConfigureAwait(false);
        await EnsurePatternsAsync(workspace, branch.Id, actor.Id, token).ConfigureAwait(false);
        // Resolve again after initialization: readiness must be actual, not inferred from process startup.
        _ = await resolver.ResolveAsync(current.Id, token).ConfigureAwait(false);
        return new LocalPreviewWorkspace(company.Id, company.Code, company.NameArabic, branch.Id, branch.Code,
            branch.NameArabic, current.Id, current.FiscalYearId, current.YearNumber, workspace.DatabaseName, "Ready");
    }

    private async Task VerifyOwnerAsync(Guid installationId, CancellationToken token)
    {
        var connection = central.Database.GetDbConnection();
        var expected = "PetraERP_LocalPreview_" + installationId.ToString("N")[..16];
        if (installationId == Guid.Empty || !string.Equals(connection.Database, expected, StringComparison.Ordinal))
            throw new BusinessRuleException("preview.database_not_owned", "Preview initialization requires this installation's isolated local preview database.");
        await central.Database.OpenConnectionAsync(token).ConfigureAwait(false);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'PetraERP.LocalPreview.Owner';";
            var marker = Convert.ToString(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (!string.Equals(marker, installationId.ToString("N"), StringComparison.Ordinal))
                throw new BusinessRuleException("preview.database_not_owned", "The preview database ownership marker does not match. No setup was attempted.");
        }
        finally { await central.Database.CloseConnectionAsync().ConfigureAwait(false); }
    }

    private async Task<bool> HasLegacyFinancialHistoryAsync(Guid companyId, CancellationToken token)
    {
        await central.Database.OpenConnectionAsync(token).ConfigureAwait(false);
        try
        {
            using var command = central.Database.GetDbConnection().CreateCommand();
            foreach (var table in LegacyFinancialTables)
            {
                command.Parameters.Clear();
                command.CommandText = "SELECT OBJECT_ID(@table,N'U');";
                var tableParameter = command.CreateParameter(); tableParameter.ParameterName = "@table"; tableParameter.Value = table;
                command.Parameters.Add(tableParameter);
                if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is null or DBNull) continue;
                command.Parameters.Clear();
                // Table identifiers are exclusively the fixed internal allowlist, never client input.
                command.CommandText = "SELECT TOP(1) 1 FROM " + table + " WHERE [CompanyId]=@company;";
                var companyParameter = command.CreateParameter(); companyParameter.ParameterName = "@company"; companyParameter.Value = companyId;
                command.Parameters.Add(companyParameter);
                if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not null and not DBNull) return true;
            }
            return false;
        }
        finally { await central.Database.CloseConnectionAsync().ConfigureAwait(false); }
    }

    private async Task<AccountingDbContext> ContextAsync(ResolvedFiscalDatabase workspace, Guid branch, Guid actor,
        AccountingOperation operation, CancellationToken token)
    {
        var context = await factory.CreateDbContextAsync(workspace.WorkspaceId, token).ConfigureAwait(false);
        context.BindWriteScope(new AccountingWriteScope(workspace.CompanyId, workspace.FiscalYearId, actor, operation, branch));
        return context;
    }

    private async Task EnsureChartAsync(ResolvedFiscalDatabase workspace, Guid branch, Guid actor, CancellationToken token)
    {
        using var context = await ContextAsync(workspace, branch, actor, AccountingOperation.ManageAccounts, token).ConfigureAwait(false);
        var template = ChartOfAccountsTemplates.Generic;
        var codes = await context.Accounts.Where(a => a.CompanyId == workspace.CompanyId).Select(a => a.Code).ToListAsync(token).ConfigureAwait(false);
        if (codes.Count == 0)
            await new SqlChartOfAccountsService(context, TimeProvider.System).ImportAsync(LedgerMasterScope.Create(workspace.CompanyId, actor, branch), template, token).ConfigureAwait(false);
        // On subsequent startups, owners may have added, renamed or deactivated records. Never overwrite their chart.
        foreach (var role in template.DefaultAccountRoles)
            if (!await context.Accounts.AnyAsync(a => a.CompanyId == workspace.CompanyId && a.Code == role.Value && a.IsActive && a.PostingAllowed, token).ConfigureAwait(false))
                throw new BusinessRuleException("preview.chart_mapping_missing", "The existing preview chart lacks an active starter posting account. Restore the required mapping through the chart screen; startup will not overwrite accounts.");
    }

    private async Task EnsureInventoryAsync(ResolvedFiscalDatabase workspace, Guid branch, Guid actor, CancellationToken token)
    {
        Guid warehouse;
        using (var context = await ContextAsync(workspace, branch, actor, AccountingOperation.ManageWarehouses, token).ConfigureAwait(false))
        {
            var service = new SqlInventoryService(context);
            var existing = await context.Warehouses.AsNoTracking().SingleOrDefaultAsync(w => w.CompanyId == workspace.CompanyId && w.Code == "WH01", token).ConfigureAwait(false);
            warehouse = existing?.Id ?? (await service.CreateWarehouseAsync(workspace.CompanyId,
                new CreateWarehouseCommand(branch, "WH01", "المستودع التجريبي", "Preview warehouse", null), token).ConfigureAwait(false)).Id;
            if (existing is not null && (!existing.IsActive || existing.BranchId != branch))
                throw new BusinessRuleException("preview.warehouse_unavailable", "The existing preview warehouse is inactive or belongs to another branch.");
        }
        using (var context = await ContextAsync(workspace, branch, actor, AccountingOperation.ManageItems, token).ConfigureAwait(false))
        {
            var service = new SqlInventoryService(context);
            var unit = await context.UnitsOfMeasure.AsNoTracking().SingleOrDefaultAsync(u => u.CompanyId == workspace.CompanyId && u.Code == "PCS", token).ConfigureAwait(false);
            var unitId = unit?.Id ?? (await service.CreateUnitAsync(workspace.CompanyId,
                new CreateUnitCommand("PCS", "قطعة", "Piece", "pc", 0, UnitDimension.Count), token).ConfigureAwait(false)).Id;
            if (!await context.Items.AnyAsync(i => i.CompanyId == workspace.CompanyId && i.Code == "DEMO-ITEM", token).ConfigureAwait(false))
                await service.CreateItemInBranchAsync(workspace.CompanyId, new CreateItemCommand("DEMO-ITEM", "صنف تجريبي", "Preview item",
                    null, unitId, warehouse, CostMethod.WeightedAverage, null, null, null), branch, token).ConfigureAwait(false);
        }
    }

    private async Task EnsurePartiesAsync(ResolvedFiscalDatabase workspace, Guid branch, Guid actor, CancellationToken token)
    {
        using var context = await ContextAsync(workspace, branch, actor, AccountingOperation.ManageParties, token).ConfigureAwait(false);
        var accounts = await context.Accounts.Where(a => a.CompanyId == workspace.CompanyId).ToDictionaryAsync(a => a.Code, a => a.Id, token).ConfigureAwait(false);
        var service = new SqlPartyService(context);
        foreach (var (code, name, role) in new[] { ("DEMO-CUSTOMER", "عميل تجريبي", PartyRole.Customer), ("DEMO-SUPPLIER", "مورد تجريبي", PartyRole.Supplier) })
            if (await service.FindPartyByCodeAsync(workspace.CompanyId, code, token).ConfigureAwait(false) is null)
                await service.CreatePartyAsync(workspace.CompanyId, new CreatePartyCommand(code, name, null, role,
                    DefaultCurrencyCode: workspace.BaseCurrencyCode, ReceivableAccountId: role == PartyRole.Customer ? accounts["1103"] : null,
                    PayableAccountId: role == PartyRole.Supplier ? accounts["2101"] : null)
                    { ChangedByUserId = actor, ChangedInBranchId = branch }, token).ConfigureAwait(false);
    }

    private async Task EnsurePatternsAsync(ResolvedFiscalDatabase workspace, Guid branch, Guid actor, CancellationToken token)
    {
        foreach (var (kind, code, name, prefix) in new[] {
            (BusinessOperationKind.SalesInvoice, "DEMO-SALES", "فاتورة مبيعات تجريبية", "S-"),
            (BusinessOperationKind.PurchaseInvoice, "DEMO-PURCHASE", "فاتورة مشتريات تجريبية", "P-"),
            (BusinessOperationKind.CashReceipt, "DEMO-RECEIPT", "سند قبض تجريبي", "R-"),
            (BusinessOperationKind.CashPayment, "DEMO-PAYMENT", "سند دفع تجريبي", "V-") })
        {
            using var context = await ContextAsync(workspace, branch, actor, AccountingOperation.ManagePatterns, token).ConfigureAwait(false);
            var existing = await context.DocumentPatterns.AsNoTracking().SingleOrDefaultAsync(p => p.CompanyId == workspace.CompanyId && p.OperationKind == (int)kind && p.Code == code, token).ConfigureAwait(false);
            var scope = PatternScope.Create(workspace.CompanyId, branch, actor);
            var service = new SqlPatternService(context);
            DocumentPatternDetailsDto detail;
            if (existing is null)
            {
                var accounts = await context.Accounts.Where(a => a.CompanyId == workspace.CompanyId).ToDictionaryAsync(a => a.Code, a => a.Id, token).ConfigureAwait(false);
                var warehouse = await context.Warehouses.Where(w => w.CompanyId == workspace.CompanyId && w.BranchId == branch && w.Code == "WH01").Select(w => w.Id).SingleAsync(token).ConfigureAwait(false);
                var roles = PatternAccountRoles.AllowedFor(kind).Where(r => RoleAccountCodes.ContainsKey(r));
                detail = await service.CreatePatternAsync(scope, new CreateDocumentPatternCommand { OperationKind = kind, Code = code,
                    NameArabic = name, ShortCode = prefix.TrimEnd('-'), Group = PreviewPatternGroup,
                    IsDefault = !await context.DocumentPatterns.AnyAsync(p => p.CompanyId == workspace.CompanyId && p.OperationKind == (int)kind && p.IsDefault, token).ConfigureAwait(false),
                    BranchIds = [branch], InitialConfiguration = new PatternConfiguration { DefaultWarehouseId = warehouse,
                        DefaultCurrencyCode = workspace.BaseCurrencyCode, AllowedWarehouseIds = [warehouse], AllowedPaymentMethods = ["Cash", "Credit", "Bank"],
                        NumberingPolicy = new PatternNumberingPolicy { Prefix = prefix },
                        AccountMappings = roles.Select(r => new PatternAccountMapping { Role = r, AccountId = accounts[RoleAccountCodes[r]] }).ToList() } }, token).ConfigureAwait(false);
            }
            else detail = await service.GetPatternAsync(scope, existing.Id, token).ConfigureAwait(false);
            if (!detail.IsActive) throw new BusinessRuleException("preview.pattern_inactive", "An existing preview pattern is retired. Startup will not reactivate user records.");
            // CreatePatternAsync exposes its initial draft as ActiveRevision in the response, although no revision
            // has been published in storage. Only a real Published revision makes the preview pattern usable.
            if (detail.ActiveRevision?.State != PatternRevisionState.Published)
            {
                var revision = detail.Revisions.SingleOrDefault(r => r.State == PatternRevisionState.Draft)
                    ?? throw new BusinessRuleException("preview.pattern_incomplete", "The preview pattern has no publishable draft.");
                if (existing is not null && (existing.Group != PreviewPatternGroup || detail.Revisions.Count != 1
                    || revision.RevisionNumber != 1 || revision.CreatedByUserId != actor || revision.Reason != "Initial revision"))
                    throw new BusinessRuleException("preview.pattern_requires_publication", "An existing preview pattern needs explicit publication. Startup will not publish user revisions.");
                using var publishContext = await ContextAsync(workspace, branch, actor, AccountingOperation.PublishPatterns, token).ConfigureAwait(false);
                var published = await new SqlPatternService(publishContext).PublishRevisionAsync(scope, detail.Id, new PublishPatternRevisionCommand {
                    ExpectedVersion = detail.Version, RevisionId = revision.Id, Reason = "Local preview initial pattern publication" }, token).ConfigureAwait(false);
                if (published.ActiveRevision?.State != PatternRevisionState.Published)
                    throw new BusinessRuleException("preview.pattern_incomplete", "Initial preview pattern publication did not complete.");
            }
        }
    }

    private const string PreviewPatternGroup = "LocalPreview.Initial.v1";
    private const string SeparateDemoCode = "PREVIEW-DEMO";
    private static readonly string[] LegacyFinancialTables = ["[ledger].[JournalEntries]", "[inventory].[StockMovements]"];
    private static readonly Dictionary<string, string> RoleAccountCodes = new Dictionary<string, string>(StringComparer.Ordinal)
    { ["Cash"] = "1101", ["Bank"] = "1102", ["Receivable"] = "1103", ["Inventory"] = "1104", ["Payable"] = "2101", ["Revenue"] = "4101", ["COGS"] = "5101", ["FxGain"] = "4102", ["FxLoss"] = "5102" };
}

public sealed record LocalPreviewWorkspace(Guid CompanyId, string CompanyCode, string CompanyName, Guid BranchId, string BranchCode,
    string BranchName, Guid WorkspaceId, Guid FiscalYearId, int YearNumber, string AnnualDatabase, string Status);
