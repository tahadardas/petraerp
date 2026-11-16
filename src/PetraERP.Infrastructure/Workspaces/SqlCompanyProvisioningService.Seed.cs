using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Workspaces;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Workspaces;

public sealed partial class SqlCompanyProvisioningService
{
    private static async Task ValidateSeededMasterDataAsync(AccountingDbContext context, CompanyRow company,
        List<BranchRow> branches, bool initialCompanySeed, CancellationToken cancellationToken)
    {
        var snapshot = await context.Companies.AsNoTracking().SingleAsync(cancellationToken).ConfigureAwait(false);
        var actualBranches = await context.Branches.AsNoTracking().OrderBy(b => b.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Code != company.Code || snapshot.BaseCurrencyCode != company.BaseCurrencyCode
            || snapshot.NameArabic != company.NameArabic || snapshot.NameEnglish != company.NameEnglish
            || !actualBranches.Select(b => b.Id).SequenceEqual(branches.Select(b => b.Id))
            || await context.AccountingPeriods.AnyAsync(p => p.State != (byte)PetraERP.Domain.AccountingCalendars.AccountingPeriodState.Open, cancellationToken).ConfigureAwait(false)
            || await context.JournalEntries.AnyAsync(cancellationToken).ConfigureAwait(false)
            || await context.StockMovements.AnyAsync(cancellationToken).ConfigureAwait(false))
            throw new PetraERP.Application.Common.BusinessRuleException("fiscal_year.seed_invalid", "The annual seed does not match the central organization or contains unexpected financial data.");
        var warehouses = await context.Warehouses.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var roots = await context.Accounts.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        if (initialCompanySeed && (warehouses.Count != 1 || warehouses[0].BranchId != branches[0].Id || warehouses[0].CompanyId != company.Id
            || !warehouses[0].IsActive || roots.Count != 5 || roots.Any(a => a.CompanyId != company.Id || a.PostingAllowed || a.ParentId is not null)
            || !roots.Select(a => a.Code).Order(StringComparer.Ordinal).SequenceEqual(InitialRootCodes, StringComparer.Ordinal)))
            throw new PetraERP.Application.Common.BusinessRuleException("fiscal_year.seed_invalid", "Initial company accounts or warehouse did not pass post-provision validation.");
    }

    private static readonly string[] InitialRootCodes = ["1", "2", "3", "4", "5"];

    private static async Task SeedYearMasterDataAsync(
        AccountingDbContext accContext,
        CompanyRow company,
        List<BranchRow> branches,
        ProvisionFiscalYearInput input,
        Guid fiscalYearId,
        bool initialCompanySeed,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. Snapshot the base currency (the company row references it), then the company
        if (!await accContext.Currencies.AnyAsync(c => c.Code == company.BaseCurrencyCode, cancellationToken).ConfigureAwait(false)
            && PetraERP.Application.Currencies.StandardCurrencyCatalog.Find(company.BaseCurrencyCode) is { } currency)
        {
            accContext.Currencies.Add(new CurrencyRow
            {
                Code = currency.Code, NameArabic = currency.NameArabic, NameEnglish = currency.NameEnglish,
                IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now,
            });
        }

        if (!await accContext.Companies.AnyAsync(c => c.Id == company.Id, cancellationToken).ConfigureAwait(false))
        {
            accContext.Companies.Add(new CompanyRow
            {
                Id = company.Id,
                Code = company.Code,
                NameArabic = company.NameArabic,
                NameEnglish = company.NameEnglish,
                BaseCurrencyCode = company.BaseCurrencyCode,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        foreach (var branch in branches)
        {
            if (!await accContext.Branches.AnyAsync(b => b.Id == branch.Id, cancellationToken).ConfigureAwait(false))
                accContext.Branches.Add(new BranchRow
                {
                    Id = branch.Id, CompanyId = company.Id, Code = branch.Code,
                    NameArabic = branch.NameArabic, NameEnglish = branch.NameEnglish, IsActive = branch.IsActive,
                });
        }

        // 2. Fiscal Year and Periods
        var yearRow = new FiscalYearRow
        {
            Id = fiscalYearId,
            CompanyId = company.Id,
            Code = input.YearCode.Trim(),
            RangeStart = input.StartDate,
            RangeEnd = input.EndDate,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        accContext.FiscalYears.Add(yearRow);

        accContext.AccountingPeriods.AddRange(FiscalYearProvisioningRules.Periods(company.Id, fiscalYearId, input.StartDate, input.EndDate));

        // Existing internal Phase 1 callers retain their historical root-only seed; the company wizard explicitly
        // selects the registered operational template. Never overwrite existing company accounts on a template update.
        if (initialCompanySeed && input.ChartTemplateCode is { } templateCode)
        {
            var template = PetraERP.Domain.GeneralLedger.Templates.ChartOfAccountsTemplates.Find(templateCode)
                ?? throw new PetraERP.Application.Common.BusinessRuleException("template.not_available", "Selected account template is unavailable.");
            var ids = template.Accounts.ToDictionary(a => a.AccountCode, _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);
            foreach (var account in template.OrderedAccounts())
                accContext.Accounts.Add(new AccountRow
                {
                    Id = ids[account.AccountCode], CompanyId = company.Id, Code = account.AccountCode,
                    NameArabic = account.AccountNameArabic, NameEnglish = account.AccountNameEnglish,
                    ParentId = account.ParentCode is null ? null : ids[account.ParentCode], AccountType = (byte)account.AccountType,
                    PostingAllowed = account.PostingAllowed, CurrencyMode = (byte)account.CurrencyMode,
                    CostCenterPolicy = (byte)account.CostCenterPolicy, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now,
                });
        }
        // Compatibility seed for pre-template internal provisioning callers.
        if (initialCompanySeed && input.ChartTemplateCode is null)
        {
            var roots = new (string Code, string Ar, string En, byte Type)[]
            {
                ("1", "الأصول", "Assets", 1),
                ("2", "الخصوم", "Liabilities", 2),
                ("3", "حقوق الملكية", "Equity", 3),
                ("4", "الإيرادات", "Revenue", 4),
                ("5", "المصروفات", "Expenses", 5)
            };

            foreach (var r in roots)
            {
                accContext.Accounts.Add(new AccountRow
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company.Id,
                    Code = r.Code,
                    NameArabic = r.Ar,
                    NameEnglish = r.En,
                    AccountType = r.Type,
                    ParentId = null,
                    PostingAllowed = false,
                    CostCenterPolicy = 1,
                    CurrencyMode = 1,
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
        }

        // 4. Default Warehouse
        if (initialCompanySeed)
        {
            accContext.Warehouses.Add(new WarehouseRow
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                BranchId = branches[0].Id,
                Code = "WH01",
                NameArabic = "المستودع الرئيسي",
                NameEnglish = "Main Warehouse",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        await accContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
