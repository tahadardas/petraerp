using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Markup;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PetraERP.Desktop.Localization;

/// <summary>
/// Centralized resources (Strings.resx / Strings.ar.resx). XAML binds through the indexer, so switching the
/// language raises "Item[]" and every bound text, the flow direction and the XML language update in place.
/// </summary>
public sealed class LocalizationService : ObservableObject, ILocalizationService
{
    public const string Arabic = "ar";
    public const string English = "en";

    private static readonly ResourceManager Resources =
        new("PetraERP.Desktop.Localization.Strings", typeof(LocalizationService).Assembly);

    // ar-SY keeps the Gregorian calendar (some Arabic cultures default to Hijri calendars).
    private static readonly CultureInfo ArabicCulture = CultureInfo.GetCultureInfo("ar-SY");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");

    private CultureInfo culture;

    public LocalizationService(string language)
    {
        culture = Resolve(language);
    }

    public event EventHandler? LanguageChanged;

    public string this[string key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            return Resources.GetString(key, culture) ?? key;
        }
    }

    public string Language => IsArabic(culture) ? Arabic : English;

    public bool IsRightToLeft => IsArabic(culture);

    public FlowDirection FlowDirection => IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public XmlLanguage XmlLanguage => XmlLanguage.GetLanguage(culture.IetfLanguageTag);

    public CultureInfo Culture => culture;

    public static string Normalize(string? language) =>
        string.Equals(language, English, StringComparison.OrdinalIgnoreCase) ? English : Arabic;

    public void SetLanguage(string language)
    {
        var next = Resolve(language);
        if (next.Equals(culture))
        {
            return;
        }

        culture = next;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        OnPropertyChanged("Item[]");
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(IsRightToLeft));
        OnPropertyChanged(nameof(FlowDirection));
        OnPropertyChanged(nameof(XmlLanguage));
        OnPropertyChanged(nameof(Culture));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static CultureInfo Resolve(string? language) =>
        Normalize(language) == English ? EnglishCulture : ArabicCulture;

    private static bool IsArabic(CultureInfo value) =>
        string.Equals(value.TwoLetterISOLanguageName, Arabic, StringComparison.Ordinal);
}
