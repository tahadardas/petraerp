using System.Globalization;
using System.Text;

namespace PetraERP.Desktop.Inventory;

/// <summary>
/// Unsaved-change detection by value. A snapshot of everything the save sends (kind, warehouses, accounts, cost center, date,
/// reason and every entered line) is taken when editing starts; the document is dirty exactly while its current snapshot differs,
/// so opening a draft for editing, or starting a new one and touching nothing, never asks to discard, and typing a value back to
/// what was stored is clean again.
/// </summary>
public sealed partial class InventoryOperationsViewModel
{
    private string? draftBaseline;

    private void CaptureDraftBaseline() => draftBaseline = IsEditing ? DraftSignature() : null;

    public bool HasUnsavedChanges => IsEditing && (draftBaseline is null || !string.Equals(DraftSignature(), draftBaseline, StringComparison.Ordinal));

    private string DraftSignature()
    {
        var text = new StringBuilder(256);
        void Add(string? value) => text.Append(value ?? string.Empty).Append('\u001f');
        void AddId(Guid? value) => Add(value?.ToString("N"));
        void AddNumber(string? value) => Add(CanonicalNumber(value));

        Add(Kind.ToString()); AddId(WarehouseId); AddId(TargetWarehouseId); AddId(CostCenterId); AddId(InventoryAccountId); AddId(OffsetAccountId);
        Add(Date?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); Add(Reason?.Trim());
        foreach (var line in Lines)
        {
            if (line is { ItemId: null, UnitId: null } && string.IsNullOrWhiteSpace(line.OpeningUnitCost) && CanonicalNumber(line.Quantity) == CanonicalNumber("1.0000")) continue;
            text.Append('\u001e');
            AddId(line.ItemId); AddId(line.UnitId); AddNumber(line.Quantity); AddNumber(line.OpeningUnitCost);
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
