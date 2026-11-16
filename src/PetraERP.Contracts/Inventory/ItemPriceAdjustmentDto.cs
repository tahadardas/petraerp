using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Inventory;

/// <summary>Raise or lower the general prices of active items by a percentage (optionally one group and one price type); preview first.</summary>
public sealed record AdjustItemPricesRequest
{
    /// <summary>Only the items of this group and its sub-groups; empty means every item.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>Only this price type (for example WHOLESALE); empty means all price types.</summary>
    [MaxLength(32)]
    public string? PriceListCode { get; init; }

    /// <summary>Signed percentage, for example "10" or "-5.5"; never zero.</summary>
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Percent { get; init; } = "0";

    /// <summary>Digits kept after the change (0 to 4).</summary>
    [Range(0, 4)]
    public int RoundingScale { get; init; } = 2;

    /// <summary>When true nothing is stored; the response says how many items and rows would change.</summary>
    public bool PreviewOnly { get; init; } = true;
}

public sealed record AdjustItemPricesResponse
{
    public required int ItemCount { get; init; }
    public required int RowCount { get; init; }
    public required bool Applied { get; init; }
}
