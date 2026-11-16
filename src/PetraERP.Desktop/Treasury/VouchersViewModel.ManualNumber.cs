using CommunityToolkit.Mvvm.ComponentModel;
namespace PetraERP.Desktop.Treasury;
public sealed partial class VouchersViewModel
{
    [ObservableProperty] private string manualDocumentNumber = string.Empty;
    public bool CanUseManualNumber => HasContext && Can("Patterns.Documents.ManualNumbering")
        && ReceiptPatterns.Concat(PaymentPatterns).Any(p => p.RevisionId == (Current?.PatternRevisionId ?? DraftPatternRevisionId) && p.AllowManualNumbering);
}
