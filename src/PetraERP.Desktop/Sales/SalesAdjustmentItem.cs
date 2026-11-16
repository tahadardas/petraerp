using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Sales;

/// <summary>
/// One row of the discounts-and-additions table (Al-Ameen). The amount and the percentage of the lines' subtotal follow each other
/// as the operator types in either; the amount (invoice currency) is what is sent and booked to the row's account.
/// </summary>
public sealed partial class SalesAdjustmentItem : ObservableObject
{
    private bool syncing;

    /// <summary>The lines' subtotal the percentage is taken from.</summary>
    public Func<decimal> Basis { get; set; } = static () => 0m;

    public Action? Changed { get; set; }

    [ObservableProperty] private SalesInvoiceAdjustmentKind kind = SalesInvoiceAdjustmentKind.Discount;
    [ObservableProperty] private Guid? accountId;
    [ObservableProperty] private Guid? counterAccountId;
    [ObservableProperty] private string amount = string.Empty;
    [ObservableProperty] private string percentage = string.Empty;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private Guid? costCenterId;

    public decimal AmountValue => Parse(Amount);

    /// <summary>True when the row counts toward the invoice totals; a row with a counter account is booked between two accounts only.</summary>
    public bool CountsInTotals => CounterAccountId is null;

    public bool IsBlank => AccountId is null && CounterAccountId is null && string.IsNullOrWhiteSpace(Amount) && string.IsNullOrWhiteSpace(Percentage) && string.IsNullOrWhiteSpace(Notes);

    public bool IsComplete => AccountId is not null && AmountValue > 0m;

    partial void OnKindChanged(SalesInvoiceAdjustmentKind value) => Changed?.Invoke();

    partial void OnAccountIdChanged(Guid? value) => Changed?.Invoke();

    partial void OnCounterAccountIdChanged(Guid? value) => Changed?.Invoke();

    partial void OnNotesChanged(string? value) => Changed?.Invoke();

    partial void OnCostCenterIdChanged(Guid? value) => Changed?.Invoke();

    partial void OnAmountChanged(string value)
    {
        if (!syncing)
        {
            syncing = true;
            var basis = Basis();
            Percentage = basis > 0m && Parse(value) > 0m ? Math.Round(Parse(value) / basis * 100m, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;
            syncing = false;
        }

        Changed?.Invoke();
    }

    partial void OnPercentageChanged(string value)
    {
        if (!syncing)
        {
            syncing = true;
            var basis = Basis();
            Amount = basis > 0m && Parse(value) > 0m ? Math.Round(basis * Parse(value) / 100m, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;
            syncing = false;
        }

        Changed?.Invoke();
    }

    /// <summary>Fills the row from a stored document without raising change notifications or recomputing the other value.</summary>
    public void LoadValues(SalesInvoiceAdjustmentKind kind, Guid? accountId, string amount, string percentage, string? notes, Guid? costCenterId, Guid? counterAccountId = null)
    {
        var previous = Changed;
        Changed = null;
        syncing = true;
        Kind = kind; AccountId = accountId; CounterAccountId = counterAccountId; Amount = amount; Percentage = percentage; Notes = notes; CostCenterId = costCenterId;
        syncing = false;
        Changed = previous;
    }

    /// <summary>Recomputes the amount from the percentage after the lines changed (a row entered as a percentage tracks the subtotal).</summary>
    public void Rebase()
    {
        if (syncing || Parse(Percentage) <= 0m) return;
        var basis = Basis();
        var expected = basis > 0m ? Math.Round(basis * Parse(Percentage) / 100m, 4, MidpointRounding.AwayFromZero) : 0m;
        if (expected == AmountValue) return;
        syncing = true;
        Amount = expected > 0m ? expected.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;
        syncing = false;
    }

    private static decimal Parse(string? text) => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;
}

/// <summary>A kind choice for the table's combo box.</summary>
public sealed record AdjustmentKindOption(SalesInvoiceAdjustmentKind Value, string Label);
