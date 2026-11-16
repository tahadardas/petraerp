using System.Globalization;
using System.Text;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Treasury;

/// <summary>
/// Unsaved-change detection by value. A snapshot of everything the save sends (header, the entered allocations and every entered
/// distribution line) is taken when editing starts; the voucher is dirty exactly while its current snapshot differs. Values the
/// system fills on its own (pattern defaults, the party-account currency, the register rate) are folded into the snapshot while
/// the draft is still clean, so opening a voucher never looks like an edit and typing a value back to what was stored is clean
/// again. <see cref="EditingSession"/> is the separate question "is an edit or an advance application open?", which gates the
/// document commands (post, reverse, open the journal, ...); a reversal reason typed but not yet used counts as unsaved input.
/// </summary>
public sealed partial class VouchersViewModel
{
    private string? draftBaseline;

    /// <summary>An edit (or an advance application) is open, whether or not anything has been changed in it yet.</summary>
    private bool EditingSession => IsEditing || IsApplyingAdvance;

    private void CaptureDraftBaseline() => draftBaseline = IsEditing ? DraftSignature() : null;

    /// <summary>Runs a change the system makes by itself; a draft that was clean before it stays clean after it.</summary>
    private void ApplySystemValue(Action change)
    {
        var wasClean = IsEditing && draftBaseline is not null && !EditorHasChanges;
        change();
        if (wasClean) CaptureDraftBaseline();
        DraftFieldChanged();
    }

    private bool EditorHasChanges => IsEditing && (draftBaseline is null || !string.Equals(DraftSignature(), draftBaseline, StringComparison.Ordinal));

    private bool AdvanceInputEntered => IsApplyingAdvance && AllocationRows.Any(r => !string.IsNullOrWhiteSpace(r.Amount));

    public bool HasUnsavedChanges => EditorHasChanges || AdvanceInputEntered || !string.IsNullOrWhiteSpace(ReverseReason);

    private void DraftFieldChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanSaveBeforeClose));
    }

    partial void OnDraftNotesChanged(string? value) => DraftFieldChanged();
    partial void OnDraftSourceNumberChanged(string? value) => DraftFieldChanged();
    partial void OnDraftSourceDateChanged(DateTime? value) => DraftFieldChanged();
    partial void OnDraftSettlementChanged(SettlementMethod value) => DraftFieldChanged();
    partial void OnManualDocumentNumberChanged(string value) => DraftFieldChanged();

    private string DraftSignature()
    {
        var text = new StringBuilder(256);
        void Add(string? value) => text.Append(value ?? string.Empty).Append('\u001f');
        void AddId(Guid? value) => Add(value?.ToString("N"));
        void AddNumber(string? value) => Add(CanonicalNumber(value));

        Add(DraftKind.ToString()); AddId(DraftPatternRevisionId); AddId(DraftPartyId); Add(DraftSettlement.ToString());
        Add(DraftDate?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(DraftCurrency?.Trim().ToUpperInvariant()); AddNumber(DraftRate); AddNumber(DraftAmount);
        Add(DraftNotes); Add(DraftSourceNumber?.Trim());
        Add(DraftSourceDate?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(ManualDocumentNumber?.Trim());

        // Only rows the save sends count: an invoice row listed with no amount is the system's offer, not the operator's entry.
        foreach (var row in AllocationRows)
        {
            if (string.IsNullOrWhiteSpace(row.Amount)) continue;
            text.Append('\u001d');
            Add(row.InvoiceId.ToString("N")); AddNumber(row.Amount); AddNumber(row.SettlementAmount); AddNumber(row.CrossRate);
        }

        foreach (var line in LineRows)
        {
            if (line.IsPristineBlank) continue;
            text.Append('\u001e');
            AddId(line.PartyId); AddId(line.AccountId); AddId(line.CostCenterId); AddNumber(line.Amount); Add(line.Description);
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
