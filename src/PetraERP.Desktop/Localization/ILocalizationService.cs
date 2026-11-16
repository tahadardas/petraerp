using System.Globalization;

namespace PetraERP.Desktop.Localization;

/// <summary>Language state and resource lookup for view models. Arabic is first-class; English is the neutral resource.</summary>
public interface ILocalizationService
{
    string this[string key] { get; }

    /// <summary>"ar" or "en".</summary>
    string Language { get; }

    bool IsRightToLeft { get; }

    CultureInfo Culture { get; }

    event EventHandler? LanguageChanged;

    void SetLanguage(string language);
}
