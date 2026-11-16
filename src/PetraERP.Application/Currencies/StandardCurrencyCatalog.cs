namespace PetraERP.Application.Currencies;

public sealed record StandardCurrency(string Code, string NameArabic, string NameEnglish, int DecimalPlaces = 2);

/// <summary>
/// ISO 4217 currencies offered when a company is created: the currencies of the Arab League states plus USD and EUR.
/// Choosing from this list (instead of typing a code) prevents base currencies that do not exist.
/// </summary>
public static class StandardCurrencyCatalog
{
    public static IReadOnlyList<StandardCurrency> All { get; } =
    [
        new("SYP", "الليرة السورية", "Syrian Pound"),
        new("USD", "الدولار الأمريكي", "US Dollar"),
        new("EUR", "اليورو", "Euro"),
        new("JOD", "الدينار الأردني", "Jordanian Dinar", 3),
        new("LBP", "الليرة اللبنانية", "Lebanese Pound"),
        new("IQD", "الدينار العراقي", "Iraqi Dinar", 3),
        new("SAR", "الريال السعودي", "Saudi Riyal"),
        new("AED", "الدرهم الإماراتي", "UAE Dirham"),
        new("KWD", "الدينار الكويتي", "Kuwaiti Dinar", 3),
        new("QAR", "الريال القطري", "Qatari Riyal"),
        new("BHD", "الدينار البحريني", "Bahraini Dinar", 3),
        new("OMR", "الريال العماني", "Omani Rial", 3),
        new("YER", "الريال اليمني", "Yemeni Rial"),
        new("EGP", "الجنيه المصري", "Egyptian Pound"),
        new("SDG", "الجنيه السوداني", "Sudanese Pound"),
        new("LYD", "الدينار الليبي", "Libyan Dinar", 3),
        new("TND", "الدينار التونسي", "Tunisian Dinar", 3),
        new("DZD", "الدينار الجزائري", "Algerian Dinar"),
        new("MAD", "الدرهم المغربي", "Moroccan Dirham"),
        new("MRU", "الأوقية الموريتانية", "Mauritanian Ouguiya"),
        new("SOS", "الشلن الصومالي", "Somali Shilling"),
        new("DJF", "الفرنك الجيبوتي", "Djiboutian Franc", 0),
        new("KMF", "الفرنك القمري", "Comorian Franc", 0),
    ];

    public static StandardCurrency? Find(string? code) =>
        code is null ? null : All.FirstOrDefault(c => string.Equals(c.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
}
