using System.Text.Json;
using PetraERP.Domain.Patterns;

namespace PetraERP.Infrastructure.Currencies;

/// <summary>Reads the default currency a pattern revision pins, tolerating either property casing in stored JSON.</summary>
internal static class PatternCurrencyDefaults
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    internal static string? DefaultCurrency(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson)) return null;
        try { return JsonSerializer.Deserialize<PatternConfiguration>(configurationJson, Options)?.DefaultCurrencyCode; }
        catch (JsonException) { return null; }
    }
}
