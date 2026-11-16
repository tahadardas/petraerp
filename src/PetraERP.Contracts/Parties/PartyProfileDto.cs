using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Parties;

/// <summary>Al-Ameen customer / supplier card attributes (see the application record of the same name).</summary>
public sealed record PartyProfileDto
{
    [MaxLength(100)]
    public string? Title { get; init; }

    [MaxLength(100)]
    public string? Suffix { get; init; }

    [MaxLength(64)]
    public string? Barcode { get; init; }

    public bool HideInSearch { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? DiscountPercent { get; init; }

    [MaxLength(32)]
    public string? SalesPriceType { get; init; }

    public Guid? ConditionalDiscountAccountId { get; init; }
    public Guid? VatAccountId { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }
}
