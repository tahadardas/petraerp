using System.Globalization;
using System.Text;

namespace PetraERP.Desktop.Sales;

/// <summary>
/// Unsaved-change detection by value. A snapshot of everything the save sends (header, every entered line in order, the
/// discounts-and-additions rows, the manual number and the first payment) is taken when the draft is loaded; the draft is dirty
/// exactly while its current snapshot differs. Values the system fills on its own (defaults, the register rate) are folded into
/// the snapshot, so opening a document never looks like an edit, and typing a value back to what was stored is clean again.
/// </summary>
public sealed partial class SalesInvoiceCardViewModel
{
    private string? draftBaseline;

    private void CaptureDraftBaseline() => draftBaseline = DraftSignature();

    /// <summary>Runs a change the system makes by itself; a draft that was clean before it stays clean after it.</summary>
    private void ApplySystemValue(Action change)
    {
        var wasClean = draftBaseline is not null && !HasDraftChanges;
        change();
        if (wasClean) CaptureDraftBaseline();
    }

    private string DraftSignature()
    {
        var text = new StringBuilder(512);
        void Add(string? value) => text.Append(value ?? string.Empty).Append('\u001f');
        void AddId(Guid? value) => Add(value?.ToString("N"));
        void AddNumber(string? value) => Add(Canonical(value));

        AddId(DraftPatternRevisionId); AddId(DraftCustomerId); AddId(DraftWarehouseId); AddId(DraftCostCenterId);
        Add(DraftInvoiceDate?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(DraftPaymentMethod.ToString());
        Add(DraftCurrencyCode?.Trim().ToUpperInvariant()); AddNumber(DraftExchangeRate);
        Add(DraftNotes); Add(DraftSellerText?.Trim()); Add(DraftDistributorText?.Trim());
        Add(ManualDocumentNumber?.Trim()); AddId(CustomerAccountOverride);
        AddNumber(FirstPaymentAmount); Add(FirstPaymentMethod.ToString());

        foreach (var line in Lines)
        {
            if (line.IsPristineBlank) continue;
            text.Append('\u001e');
            AddId(line.ItemId); AddId(line.UnitId); AddNumber(line.Quantity); AddNumber(line.UnitPrice);
            AddNumber(line.DiscountPercentage); AddNumber(line.DiscountAmount); AddNumber(line.TaxPercentage); AddNumber(line.TaxAmount);
            AddNumber(line.PumpCost); AddNumber(line.TransportCost); AddId(line.CostCenterId); AddId(line.WarehouseId);
            Add(line.Notes); Add(line.ConcreteGrade);
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
    private static string Canonical(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number.ToString("0.##########", CultureInfo.InvariantCulture)
            : value.Trim();
    }
}
