using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Inventory;

/// <summary>
/// Al-Ameen item-card attributes: units 2 and 3 (unit 1 is the item's base unit), gift rule, specification lists and the
/// item options. Decimals travel as strings like every other financial member of the contract.
/// </summary>
public sealed record ItemProfileDto
{
    public Guid? Unit2Id { get; init; }

    [RegularExpression(FinancialDecimalText.RatePattern)]
    public string? Unit2Factor { get; init; }

    public bool Unit2FactorVariable { get; init; }
    public Guid? Unit3Id { get; init; }

    [RegularExpression(FinancialDecimalText.RatePattern)]
    public string? Unit3Factor { get; init; }

    public bool Unit3FactorVariable { get; init; }

    [Range(1, 3)]
    public int DefaultUnitIndex { get; init; } = 1;

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? GiftQuantity { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? GiftPerQuantity { get; init; }

    [MaxLength(2000)]
    public string? Specifications { get; init; }

    [MaxLength(100)]
    public string? Measure { get; init; }

    [MaxLength(100)]
    public string? Source { get; init; }

    [MaxLength(100)]
    public string? Location { get; init; }

    [MaxLength(100)]
    public string? Manufacturer { get; init; }

    [MaxLength(100)]
    public string? Color { get; init; }

    [MaxLength(100)]
    public string? OriginCountry { get; init; }

    [MaxLength(100)]
    public string? Quality { get; init; }

    [MaxLength(100)]
    public string? Model { get; init; }

    public int ItemKind { get; init; } = 1;
    public bool IsAssembly { get; init; }
    public bool ComponentPricing { get; init; }
    public bool TracksSerialNumber { get; init; }
    public bool EnforceSerialOnReceipt { get; init; }
    public bool EnforceSerialOnIssue { get; init; }
    public bool TracksProductionDate { get; init; }
    public bool TracksExpiryDate { get; init; }
    public bool EnforceDatesOnReceipt { get; init; }
    public bool EnforceDatesOnIssue { get; init; }
    public bool HideInSearch { get; init; }
    public bool IntegerQuantityOnInvoice { get; init; }

    [MaxLength(500)]
    public string? ImagePath { get; init; }

    public int PricingPolicy { get; init; } = 1;

    [RegularExpression(FinancialDecimalText.RatePattern)]
    public string? LastPurchaseRate { get; init; }
}
