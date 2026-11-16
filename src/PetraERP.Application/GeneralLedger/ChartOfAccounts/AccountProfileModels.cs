using PetraERP.Application.Common;

namespace PetraERP.Application.GeneralLedger.ChartOfAccounts;

/// <summary>
/// Al-Ameen account-card attributes beside the core account: notes, nature (debit / credit / both), closing account, default cost
/// center, the estimated budget, and the financial-statement placement. They are stored and reported with the account; the posting
/// engine does not read them, so they never change a balance.
/// </summary>
public sealed record AccountProfileData(
    string? Notes = null,
    int Nature = AccountProfileData.BothNature,
    Guid? ClosingAccountId = null,
    Guid? DefaultCostCenterId = null,
    int BudgetKind = AccountProfileData.NoBudget,
    decimal? BudgetAmount = null,
    string? BudgetCurrencyCode = null,
    decimal? BudgetRate = null,
    bool IncludeCommercialPapers = false,
    string? IncomeStatementCategory = null,
    string? EquityCategory = null,
    string? FinancialPositionCategory = null,
    string? CashFlowCategory = null)
{
    public const int DebitNature = 1;
    public const int CreditNature = 2;
    public const int BothNature = 3;
    public const int NoBudget = 0;
    public const int DebitBudget = 1;
    public const int CreditBudget = 2;

    public void Validate(Guid accountId)
    {
        if (Notes is { Length: > 500 })
            throw new BusinessRuleException("accounts.profile_notes_length", "The notes allow at most 500 characters.", "profile.notes");
        if (Nature is < DebitNature or > BothNature)
            throw new BusinessRuleException("accounts.profile_nature", "Unknown account nature.", "profile.nature");
        if (ClosingAccountId == accountId)
            throw new BusinessRuleException("accounts.profile_closing_self", "An account cannot be its own closing account.", "profile.closingAccountId");
        if (BudgetKind is < NoBudget or > CreditBudget)
            throw new BusinessRuleException("accounts.profile_budget_kind", "Unknown budget kind.", "profile.budgetKind");
        if (BudgetKind == NoBudget)
        {
            if (BudgetAmount is not null || BudgetCurrencyCode is not null || BudgetRate is not null)
                throw new BusinessRuleException("accounts.profile_budget_orphan", "A budget amount needs a budget kind.", "profile.budgetKind");
        }
        else
        {
            if (BudgetAmount is not >= 0m)
                throw new BusinessRuleException("accounts.profile_budget_amount", "The budget amount must be zero or more.", "profile.budgetAmount");
            if (BudgetCurrencyCode is not { Length: 3 })
                throw new BusinessRuleException("accounts.profile_budget_currency", "The budget currency must be a 3-letter code.", "profile.budgetCurrencyCode");
            if (BudgetRate is not > 0m)
                throw new BusinessRuleException("accounts.profile_budget_rate", "The budget exchange rate must be greater than zero.", "profile.budgetRate");
        }

        foreach (var (name, value) in new[] { ("incomeStatementCategory", IncomeStatementCategory), ("equityCategory", EquityCategory),
                     ("financialPositionCategory", FinancialPositionCategory), ("cashFlowCategory", CashFlowCategory) })
            if (value is { Length: > 100 })
                throw new BusinessRuleException("accounts.profile_category_length", "A statement category allows at most 100 characters.", $"profile.{name}");
    }
}

public interface IAccountProfileService
{
    Task<AccountProfileData> GetAsync(LedgerMasterScope scope, Guid accountId, CancellationToken cancellationToken);

    /// <summary>Replaces the profile; the closing account and default cost center must be active records of the same company.</summary>
    Task<AccountProfileData> SetAsync(LedgerMasterScope scope, Guid accountId, AccountProfileData profile, CancellationToken cancellationToken);
}
