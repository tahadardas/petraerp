using System.Globalization;
using System.Text;

namespace PetraERP.Desktop.Purchases;

/// <summary>
/// Unsaved-change detection by value. A snapshot of everything the save sends (header, every entered line in order and the
/// discounts-and-additions rows) is taken when editing starts; the draft is dirty exactly while its current snapshot differs.
/// Values the system fills on its own (defaults, the supplier-account currency, the register rate) are folded into the snapshot
/// while the draft is still clean, so opening a document never looks like an edit and typing a value back to what was stored is
/// clean again. Text typed for a return or a reversal (outside the editor) also counts: it would be lost on close.
/// </summary>
public sealed partial class PurchasesViewModel
{
    private string? draftBaseline;

    private void CaptureDraftBaseline() => draftBaseline = IsEditing ? DraftSignature() : null;

    /// <summary>Runs a change the system makes by itself; a draft that was clean before it stays clean after it.</summary>
    private void ApplySystemValue(Action change)
    {
        var wasClean = IsEditing && draftBaseline is not null && !EditorHasChanges;
        change();
        if (wasClean) CaptureDraftBaseline();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private bool EditorHasChanges => IsEditing && (draftBaseline is null || !string.Equals(DraftSignature(), draftBaseline, StringComparison.Ordinal));

    private bool HasPendingActionInput =>
        !string.IsNullOrWhiteSpace(ReturnReason) || !string.IsNullOrWhiteSpace(ReverseReason) || ReturnLines.Any(l => l.ParsedQuantity > 0m);

    public bool HasUnsavedChanges => EditorHasChanges || HasPendingActionInput;

    private void DraftFieldChanged() => OnPropertyChanged(nameof(HasUnsavedChanges));

    partial void OnDraftSupplierReferenceChanged(string? value) => DraftFieldChanged();
    partial void OnDraftNotesChanged(string? value) => DraftFieldChanged();
    partial void OnDraftSellerTextChanged(string value) => DraftFieldChanged();
    partial void OnDraftDistributorTextChanged(string value) => DraftFieldChanged();
    partial void OnDraftCostCenterIdChanged(Guid? value) => DraftFieldChanged();
    partial void OnManualDocumentNumberChanged(string value) => DraftFieldChanged();
    partial void OnReverseReasonChanged(string value) => DraftFieldChanged();

    private string DraftSignature()
    {
        var text = new StringBuilder(512);
        void Add(string? value) => text.Append(value ?? string.Empty).Append('\u001f');
        void AddId(Guid? value) => Add(value?.ToString("N"));
        void AddNumber(string? value) => Add(CanonicalNumber(value));

        AddId(DraftPatternRevisionId); AddId(DraftSupplierId); AddId(DraftWarehouseId); AddId(DraftCostCenterId);
        Add(DraftDate?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(DraftPaymentMethod.ToString());
        Add(DraftCurrency?.Trim().ToUpperInvariant()); AddNumber(DraftRate);
        Add(DraftSupplierReference); Add(DraftNotes); Add(DraftSellerText?.Trim()); Add(DraftDistributorText?.Trim());
        Add(ManualDocumentNumber?.Trim());

        foreach (var line in Lines)
        {
            if (line.IsPristineBlank) continue;
            text.Append('\u001e');
            AddId(line.ItemId); AddId(line.UnitId); AddNumber(line.Quantity); AddNumber(line.UnitCost);
            AddNumber(line.DiscountAmount); AddNumber(line.TaxAmount); AddId(line.CostCenterId); Add(line.Notes);
        }

        foreach (var row in Adjustments)
        {
            if (row.IsBlank) continue;
            text.Append('\u001d');
            Add(row.Kind.ToString()); AddId(row.AccountId); AddId(row.CounterAccountId); AddNumber(row.Amount); AddNumber(row.Percentage);
            Add(row.Notes); AddId(row.CostCenterId);
        }

        return text.ToString();
    }

    /// <summary>"2", "2.0" and "2.0000" are the same entry; text that is not a number is compared as typed.</summary>
    private static string CanonicalNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number.ToString("0.##########", CultureInfo.InvariantCulture)
            : value.Trim();
    }
}
