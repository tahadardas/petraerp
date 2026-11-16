using PetraERP.Domain.Primitives;
using PetraERP.Domain.FinancialValues;

namespace PetraERP.Domain.Inventory;

/// <summary>One stored price row as the resolver sees it.</summary>
public sealed record PriceCandidate(
    Guid Id,
    string PriceListCode,
    Guid UnitId,
    string CurrencyCode,
    decimal Price,
    decimal MinQuantity,
    Guid? PartyId,
    Guid? PatternId,
    DateOnly? ValidFrom,
    DateOnly? ValidTo);

/// <summary>What a document line needs a price for.</summary>
public sealed record PriceRequest(
    Guid UnitId,
    string CurrencyCode,
    decimal Quantity,
    DateOnly Date,
    Guid? PartyId,
    Guid? PatternId,
    string? PriceListCode);

/// <summary>
/// The one price priority the server enforces for every channel (WPF, web, Android, integrations). Only rows in the
/// document unit and currency, valid on the document date and whose minimum quantity is reached are eligible; no currency
/// or unit conversion is applied to a price. Among eligible rows the most specific wins:
/// customer + pattern, then customer, then pattern, then general; then the requested list (or DEFAULT), the highest
/// quantity break, the latest validity start, and finally the row id so the answer is deterministic.
/// </summary>
public static class PriceListPolicy
{
    public const string DefaultList = "DEFAULT";
    public const string ValidityInvalid = "inventory.price_validity_invalid";
    public const string PriceInvalid = "inventory.price_invalid";

    public static void RequireValid(decimal price, decimal minQuantity, DateOnly? validFrom, DateOnly? validTo)
    {
        if (price < 0 || price > FinancialPrecision.MaximumMoney || decimal.Round(price, 4) != price
            || minQuantity < 0 || minQuantity > FinancialPrecision.MaximumMoney || decimal.Round(minQuantity, 4) != minQuantity)
            throw RuleViolation.Create(PriceInvalid, "Price and minimum quantity must be non-negative with at most four decimals.");
        if (validFrom is { } from && validTo is { } to && to < from)
            throw RuleViolation.Create(ValidityInvalid, "The price validity end must not be before its start.");
    }

    public static int Specificity(PriceCandidate candidate) =>
        (candidate.PartyId.HasValue ? 2 : 0) + (candidate.PatternId.HasValue ? 1 : 0);

    public static PriceCandidate? Select(IEnumerable<PriceCandidate> candidates, PriceRequest request)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(request);
        var list = string.IsNullOrWhiteSpace(request.PriceListCode) ? null : request.PriceListCode.Trim().ToUpperInvariant();
        return candidates
            .Where(c => c.UnitId == request.UnitId
                && string.Equals(c.CurrencyCode, request.CurrencyCode, StringComparison.Ordinal)
                && c.MinQuantity <= request.Quantity
                && (c.ValidFrom is null || c.ValidFrom <= request.Date)
                && (c.ValidTo is null || c.ValidTo >= request.Date)
                && (c.PartyId is null || c.PartyId == request.PartyId)
                && (c.PatternId is null || c.PatternId == request.PatternId)
                && (list is null || string.Equals(c.PriceListCode, list, StringComparison.Ordinal)))
            .OrderByDescending(Specificity)
            .ThenByDescending(c => string.Equals(c.PriceListCode, list ?? DefaultList, StringComparison.Ordinal))
            .ThenBy(c => c.PriceListCode, StringComparer.Ordinal)
            .ThenByDescending(c => c.MinQuantity)
            .ThenByDescending(c => c.ValidFrom ?? DateOnly.MinValue)
            .ThenBy(c => c.Id)
            .FirstOrDefault();
    }
}
