using CommunityToolkit.Mvvm.ComponentModel;
namespace PetraERP.Desktop.Sales;
public sealed partial class SalesInvoiceCardViewModel
{
    [ObservableProperty] private string manualDocumentNumber = string.Empty;
    partial void OnManualDocumentNumberChanged(string value) => DraftChanged();
    public bool CanUseManualNumber => HasContext && Can("Patterns.Documents.ManualNumbering")
        && PatternLookups.Any(p => p.RevisionId == (Invoice?.PatternRevisionId ?? DraftPatternRevisionId) && p.AllowManualNumbering);
}
