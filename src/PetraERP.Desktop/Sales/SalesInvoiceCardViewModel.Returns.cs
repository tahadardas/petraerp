using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Sales;

/// <summary>One original line in the return panel; the server remains authoritative for returnable quantity.</summary>
public sealed partial class SalesReturnLineItem : ObservableObject
{
    public Guid SourceLineId { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public decimal Sold { get; init; }
    public decimal Returned { get; init; }
    public decimal Returnable => Sold - Returned;
    public string SoldDisplay => Sold.ToString("F4", CultureInfo.InvariantCulture);
    public string ReturnedDisplay => Returned.ToString("F4", CultureInfo.InvariantCulture);
    public string ReturnableDisplay => Returnable.ToString("F4", CultureInfo.InvariantCulture);
    public Action? Changed { get; set; }

    [ObservableProperty] private string returnQuantity = string.Empty;

    partial void OnReturnQuantityChanged(string value) => Changed?.Invoke();

    /// <summary>Null when blank; otherwise the parsed quantity, or -1 when the text is invalid or exceeds the returnable quantity.</summary>
    public decimal? ParsedQuantity
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ReturnQuantity)) return null;
            if (!decimal.TryParse(ReturnQuantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity)
                || quantity <= 0m || decimal.Round(quantity, 4) != quantity || quantity > Returnable) return -1m;
            return quantity;
        }
    }
}

public sealed partial class SalesInvoiceCardViewModel
{
    private AsyncRelayCommand? postReturnCommand;
    private string? pendingReturnKey;
    private string? pendingReturnFingerprint;

    public ObservableCollection<SalesReturnLineItem> ReturnLines { get; } = [];

    [ObservableProperty] private string returnReason = string.Empty;

    public bool IsReturnDocument => Invoice?.ReturnOfInvoiceId is not null;
    public bool HasReturns => Invoice?.Lines?.Any(l => ParseQuantity(l.ReturnedQuantity) > 0m) == true;
    public bool CanReturnPermission => Can(SalesInvoicesViewModel.ReversePermission) && Can(SalesInvoicesViewModel.PostPermission);

    /// <summary>Only an original posted, non-reversed invoice can be returned (cash → cash refund, credit → receivable).</summary>
    public bool IsReturnable => Invoice is { Status: SalesInvoiceStatus.Posted, ReversedByInvoiceId: null, ReversesInvoiceId: null, ReturnOfInvoiceId: null }
        && ReturnLines.Any(l => l.Returnable > 0m);

    public AsyncRelayCommand PostReturnCommand => postReturnCommand ??= new(PostReturnAsync, CanPostReturn);

    partial void OnReturnReasonChanged(string value) => postReturnCommand?.NotifyCanExecuteChanged();

    private bool CanPostReturn()
    {
        if (!HasContext || !Idle || !IsViewMode || !CanReturnPermission || !IsReturnable || string.IsNullOrWhiteSpace(ReturnReason)) return false;
        var quantities = ReturnLines.Select(l => l.ParsedQuantity).ToList();
        return quantities.Any(q => q > 0m) && !quantities.Any(q => q < 0m);
    }

    private void PrepareReturnLines(SalesInvoiceDetailDto detail)
    {
        ReturnLines.Clear();
        ReturnReason = string.Empty;
        if (detail is { Status: SalesInvoiceStatus.Posted, ReversedByInvoiceId: null, ReversesInvoiceId: null, ReturnOfInvoiceId: null })
        {
            foreach (var line in detail.Lines ?? [])
            {
                if (line.Id is not { } id) continue;
                ReturnLines.Add(new SalesReturnLineItem
                {
                    SourceLineId = id,
                    ItemCode = line.ItemCode ?? string.Empty,
                    ItemName = line.ItemNameArabic ?? string.Empty,
                    Sold = ParseQuantity(line.Quantity),
                    Returned = ParseQuantity(line.ReturnedQuantity),
                    Changed = () => postReturnCommand?.NotifyCanExecuteChanged(),
                });
            }
        }
        OnPropertyChanged(nameof(IsReturnDocument));
        OnPropertyChanged(nameof(HasReturns));
        OnPropertyChanged(nameof(IsReturnable));
        postReturnCommand?.NotifyCanExecuteChanged();
    }

    private async Task PostReturnAsync()
    {
        if (!CanPostReturn() || InvoiceId is not { } originalId) return;
        if (!await confirmation.ConfirmAsync("D_ConfirmReturnInvoice").ConfigureAwait(true)) return;

        var lines = ReturnLines.Where(l => l.ParsedQuantity > 0m).Select(l => new SalesReturnLineRequest
        {
            SourceLineId = l.SourceLineId,
            Quantity = l.ParsedQuantity!.Value.ToString("F4", CultureInfo.InvariantCulture),
        }).ToList();
        var reason = ReturnReason.Trim();
        // Same intent after a lost response keeps its key (server replays); any change to lines or reason is a new intent.
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { originalId, reason, lines })));
        if (pendingReturnFingerprint != fingerprint)
        {
            pendingReturnFingerprint = fingerprint;
            pendingReturnKey = $"sales-return:{originalId:N}:{Guid.NewGuid():N}";
        }

        try
        {
            var created = await salesInvoiceClient.ReturnSalesInvoiceAsync(CompanyId, BranchId, originalId,
                new CreateSalesReturnRequest { IdempotencyKey = pendingReturnKey!, Reason = reason, Lines = lines }, CancellationToken.None).ConfigureAwait(true);
            pendingReturnKey = null; pendingReturnFingerprint = null;
            await LoadInvoiceAsync(CancellationToken.None).ConfigureAwait(true);
            if (created.Id is { } returnId)
                await navigator.NavigateAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(returnId)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Operation.SetError(Connection.DesktopErrors.Key(ex));
        }
    }

    private static decimal ParseQuantity(string? text) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : 0m;
}
