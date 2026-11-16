using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Ledger;

/// <summary>Al-Ameen account-card attributes (see the application record of the same name).</summary>
public sealed record AccountProfileDto
{
    [MaxLength(500)]
    public string? Notes { get; init; }

    [Range(1, 3)]
    public int Nature { get; init; } = 3;

    public Guid? ClosingAccountId { get; init; }
    public Guid? DefaultCostCenterId { get; init; }

    [Range(0, 2)]
    public int BudgetKind { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? BudgetAmount { get; init; }

    [StringLength(3, MinimumLength = 3)]
    public string? BudgetCurrencyCode { get; init; }

    [RegularExpression(FinancialDecimalText.RatePattern)]
    public string? BudgetRate { get; init; }

    public bool IncludeCommercialPapers { get; init; }

    [MaxLength(100)]
    public string? IncomeStatementCategory { get; init; }

    [MaxLength(100)]
    public string? EquityCategory { get; init; }

    [MaxLength(100)]
    public string? FinancialPositionCategory { get; init; }

    [MaxLength(100)]
    public string? CashFlowCategory { get; init; }
}
