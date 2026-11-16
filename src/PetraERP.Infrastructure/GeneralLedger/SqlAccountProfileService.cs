using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

public sealed class SqlAccountProfileService(FinancialDbContext context) : IAccountProfileService
{
    public async Task<AccountProfileData> GetAsync(LedgerMasterScope scope, Guid accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await RequireAccountAsync(scope.CompanyId, accountId, cancellationToken).ConfigureAwait(false);
        var row = await context.AccountProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.AccountId == accountId && p.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false);
        return row is null ? new AccountProfileData() : ToData(row);
    }

    public async Task<AccountProfileData> SetAsync(LedgerMasterScope scope, Guid accountId, AccountProfileData profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate(accountId);
        await RequireAccountAsync(scope.CompanyId, accountId, cancellationToken).ConfigureAwait(false);
        if (profile.ClosingAccountId is { } closing
            && !await context.Accounts.AsNoTracking().AnyAsync(a => a.CompanyId == scope.CompanyId && a.Id == closing && a.IsActive, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("accounts.profile_closing_invalid", "The closing account must be an active account of this company.", "profile.closingAccountId");
        if (profile.DefaultCostCenterId is { } center
            && !await context.CostCenters.AsNoTracking().AnyAsync(c => c.CompanyId == scope.CompanyId && c.Id == center && c.IsActive, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("accounts.profile_cost_center_invalid", "The default cost center must be an active cost center of this company.", "profile.defaultCostCenterId");

        var row = await context.AccountProfiles.FirstOrDefaultAsync(p => p.AccountId == accountId && p.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new AccountProfileRow { AccountId = accountId, CompanyId = scope.CompanyId, CreatedAtUtc = now };
            context.AccountProfiles.Add(row);
        }

        row.Notes = Clean(profile.Notes);
        row.Nature = profile.Nature;
        row.ClosingAccountId = profile.ClosingAccountId;
        row.DefaultCostCenterId = profile.DefaultCostCenterId;
        row.BudgetKind = profile.BudgetKind;
        row.BudgetAmount = profile.BudgetKind == AccountProfileData.NoBudget ? null : profile.BudgetAmount;
        row.BudgetCurrencyCode = profile.BudgetKind == AccountProfileData.NoBudget ? null : profile.BudgetCurrencyCode;
        row.BudgetRate = profile.BudgetKind == AccountProfileData.NoBudget ? null : profile.BudgetRate;
        row.IncludeCommercialPapers = profile.IncludeCommercialPapers;
        row.IncomeStatementCategory = Clean(profile.IncomeStatementCategory);
        row.EquityCategory = Clean(profile.EquityCategory);
        row.FinancialPositionCategory = Clean(profile.FinancialPositionCategory);
        row.CashFlowCategory = Clean(profile.CashFlowCategory);
        row.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToData(row);
    }

    private async Task RequireAccountAsync(Guid companyId, Guid accountId, CancellationToken cancellationToken)
    {
        if (!await context.Accounts.AsNoTracking().AnyAsync(a => a.CompanyId == companyId && a.Id == accountId, cancellationToken).ConfigureAwait(false))
            throw new ResourceNotFoundException("account");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AccountProfileData ToData(AccountProfileRow row) => new(
        row.Notes, row.Nature, row.ClosingAccountId, row.DefaultCostCenterId, row.BudgetKind, row.BudgetAmount, row.BudgetCurrencyCode,
        row.BudgetRate, row.IncludeCommercialPapers, row.IncomeStatementCategory, row.EquityCategory, row.FinancialPositionCategory, row.CashFlowCategory);
}
