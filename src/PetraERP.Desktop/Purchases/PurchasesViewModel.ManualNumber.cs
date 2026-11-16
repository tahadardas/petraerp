using CommunityToolkit.Mvvm.ComponentModel;
namespace PetraERP.Desktop.Purchases;
public sealed partial class PurchasesViewModel
{
    [ObservableProperty] private string manualDocumentNumber = string.Empty;
    public bool CanUseManualNumber => HasContext && Can("Patterns.Documents.ManualNumbering")
        && PurchasePatterns.Any(p => p.RevisionId == (Current?.PatternRevisionId ?? DraftPatternRevisionId) && p.AllowManualNumbering);
}
