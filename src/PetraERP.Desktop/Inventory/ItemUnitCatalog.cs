using System.Globalization;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Inventory;

/// <summary>
/// The document units one item accepts: its base unit plus every unit with one explicit positive conversion to that base unit,
/// item-specific or company-wide, in either stored direction. <see cref="Resolved"/> is false when the server could not be asked;
/// the list then holds only what is certain and nothing is guessed.
/// </summary>
public sealed record ItemUnitChoices(Guid? BaseUnitId, IReadOnlyList<Guid> UnitIds, bool Resolved)
{
    public static ItemUnitChoices Unknown { get; } = new(null, [], false);

    public bool Allows(Guid unitId) => UnitIds.Contains(unitId);
}

/// <summary>
/// Answers which units a document line may use for an item. It mirrors the server rule in InventoryUnits (one direct conversion to
/// the base unit, no chains, no company-wide conversion across dimensions) so an unusable unit is refused while the line is being
/// typed. The server remains the authority when the document is saved and posted. Answers are cached per item until
/// <see cref="Clear"/>; one request per item, never one per keystroke.
/// </summary>
public sealed class ItemUnitCatalog(IInventoryClient client)
{
    private readonly Dictionary<Guid, Task<ItemUnitChoices>> cache = [];

    /// <summary>Forget every answer (company, branch or fiscal year changed, or unit data was edited).</summary>
    public void Clear() => cache.Clear();

    public Task<ItemUnitChoices> ForAsync(Guid companyId, Guid branchId, Guid itemId, Guid? knownBaseUnitId,
        IReadOnlyCollection<UnitOfMeasureDto> units, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(itemId, out var cached) && !cached.IsFaulted && !cached.IsCanceled) return cached;
        var task = LoadAsync(companyId, branchId, itemId, knownBaseUnitId, units, cancellationToken);
        cache[itemId] = task;
        return task;
    }

    private async Task<ItemUnitChoices> LoadAsync(Guid companyId, Guid branchId, Guid itemId, Guid? knownBaseUnitId,
        IReadOnlyCollection<UnitOfMeasureDto> units, CancellationToken cancellationToken)
    {
        try
        {
            var baseUnitId = knownBaseUnitId
                ?? (await client.GetItemAsync(companyId, branchId, itemId, cancellationToken).ConfigureAwait(true)).BaseUnitId;
            if (baseUnitId is not { } baseId || baseId == Guid.Empty) return new(null, [], true);
            var conversions = await client.ListUnitConversionsAsync(companyId, branchId, itemId, cancellationToken).ConfigureAwait(true);
            return Compute(itemId, baseId, conversions, units);
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or PetraApiException or InvalidOperationException)
        {
            // Not cached as an answer: the next request asks again. Only the certain base unit is offered meanwhile.
            cache.Remove(itemId);
            return knownBaseUnitId is { } known ? new(known, [known], false) : ItemUnitChoices.Unknown;
        }
    }

    /// <summary>Pure rule, shared with the tests: base unit first, then each unit directly convertible to it.</summary>
    public static ItemUnitChoices Compute(Guid itemId, Guid baseUnitId, IEnumerable<UnitConversionDto> conversions, IEnumerable<UnitOfMeasureDto> units)
    {
        var dimensions = units.Where(u => u.Id.HasValue).GroupBy(u => u.Id!.Value)
            .ToDictionary(g => g.Key, g => g.First().Dimension ?? UnitDimension.Unspecified);
        UnitDimension DimensionOf(Guid id) => dimensions.TryGetValue(id, out var d) ? d : UnitDimension.Unspecified;

        var allowed = new List<Guid> { baseUnitId };
        foreach (var conversion in conversions)
        {
            if (conversion.ItemId is { } owner && owner != itemId) continue;
            if (!decimal.TryParse(conversion.Factor, NumberStyles.Number, CultureInfo.InvariantCulture, out var factor) || factor <= 0m) continue;
            if (conversion.FromUnitId is not { } from || conversion.ToUnitId is not { } to || from == to) continue;
            var other = from == baseUnitId ? to : to == baseUnitId ? from : (Guid?)null;
            if (other is not { } unitId || allowed.Contains(unitId)) continue;
            // A company-wide conversion never crosses dimensions (no density is assumed); that needs an item conversion.
            var a = DimensionOf(unitId); var b = DimensionOf(baseUnitId);
            if (conversion.ItemId is null && a != UnitDimension.Unspecified && b != UnitDimension.Unspecified && a != b) continue;
            allowed.Add(unitId);
        }

        return new(baseUnitId, allowed, true);
    }
}
