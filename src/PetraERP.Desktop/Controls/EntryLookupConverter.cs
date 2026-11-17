using System.Collections;
using System.Globalization;
using System.Windows.Data;
using PetraERP.ApiClient;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.Controls;

/// <summary>Adapts existing lookup responses to the shared code/name keyboard picker. No financial defaults are inferred.</summary>
public sealed class EntryLookupConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IEnumerable items) return Array.Empty<LookupItem>();
        try
        {
            return items.Cast<object>().Select(ToItem).OfType<LookupItem>().ToList();
        }
        catch (Exception)
        {
            return Array.Empty<LookupItem>();
        }
    }

    private static LookupItem? ToItem(object value) => value switch
    {
        LookupItem item => item,
        ItemLookupDto item when item.Id is { } id => new(id, item.Code ?? string.Empty, item.NameArabic ?? string.Empty, item.NameEnglish),
        PartyLookupDto item when item.Id is { } id => new(id, item.Code ?? string.Empty, item.NameArabic ?? string.Empty, item.NameEnglish),
        WarehouseDto item when item.Id is { } id => new(id, item.Code ?? string.Empty, item.NameArabic ?? string.Empty, item.NameEnglish),
        UnitOfMeasureDto item when item.Id is { } id => new(id, item.Code ?? string.Empty, item.NameArabic ?? string.Empty, item.NameEnglish),
        ItemGroupDto item when item.Id is { } id => new(id, item.Code ?? string.Empty, item.NameArabic ?? string.Empty, item.NameEnglish),
        AccountResponse item when item.Id is { } id => new(id, item.Code ?? string.Empty, item.NameArabic ?? string.Empty, item.NameEnglish),
        CostCenterResponse item when item.Id is { } id => new(id, item.Code ?? string.Empty, item.NameArabic ?? string.Empty, item.NameEnglish),
        PatternLookupItem item => new(item.RevisionId, string.Empty, item.Name, item.Name),
        _ => null,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
