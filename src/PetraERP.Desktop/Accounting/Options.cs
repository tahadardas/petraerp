using PetraERP.Desktop.Localization;

namespace PetraERP.Desktop.Accounting;

/// <summary>A localized choice for a typed enum value.</summary>
public sealed record ChoiceOption<T>(T Value, string Label);

public static class Options
{
    /// <summary>Builds localized options from resource keys "{prefix}{Value}" for every enum member.</summary>
    public static IReadOnlyList<ChoiceOption<T>> For<T>(ILocalizationService localization, string prefix) where T : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(localization);
        return Enum.GetValues<T>().Select(value => new ChoiceOption<T>(value, localization[prefix + value])).ToArray();
    }
}
