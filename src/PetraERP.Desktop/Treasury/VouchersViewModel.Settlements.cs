using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Treasury;

public sealed record SettlementStateOption(SettlementStateFilter State, string Label);
public sealed record VoucherSettlementHistory(SettlementBatchDto Source, ILocalizationService Localization)
{
    public string Date => Source.DateUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
    public string State => Localization[Convert.ToInt32(Source.State, CultureInfo.InvariantCulture) == 2 ? "ARP_ReversedApplication" : "ARP_PostedApplication"];
    public string Reason => Source.Reason ?? string.Empty;
    public string ApplicationKind => Localization[Source.IsInitial == true ? "ARP_InitialApplication" : "ARP_LaterApplication"];
    public string ReversalDate => Source.ReversedAtUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
    public string ReversalReason => Source.ReversalReason ?? string.Empty;
    public string ReversedBy => Convert.ToString(Source.ReversedBy, CultureInfo.InvariantCulture) ?? string.Empty;
    public string Amount => (Source.Applications ?? []).Sum(a => decimal.TryParse(a.SettlementAmount, NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out var value) ? value : 0m).ToString("F4", CultureInfo.InvariantCulture);
}

public sealed partial class VouchersViewModel
{
    private int invoicePage = 1, invoiceTotal;
    private string? advanceKey, advanceFingerprint;
    [ObservableProperty] private bool isApplyingAdvance;
    [ObservableProperty] private DateTime? applicationDate = DateTime.Today;
    [ObservableProperty] private string invoiceCurrencyFilter = string.Empty;
    [ObservableProperty] private SettlementStateFilter invoiceStateFilter = SettlementStateFilter.Open;
    [ObservableProperty] private VoucherSettlementHistory? selectedBatch;
    [ObservableProperty] private InvoiceSettlementDto? selectedHistoryAllocation;

    public ObservableCollection<VoucherAllocationItem> OriginalAllocations { get; } = [];
    public ObservableCollection<VoucherSettlementHistory> SettlementBatches { get; } = [];
    public IReadOnlyList<InvoiceSettlementDto> SelectedBatchApplications => SelectedBatch?.Source.Applications ?? [];
    public string InvoicePageStatus => $"{invoicePage} · {invoiceTotal}";
    public IReadOnlyList<SettlementStateOption> InvoiceStateOptions => [new(SettlementStateFilter.All, Localization["ARP_All"]),
        new(SettlementStateFilter.Open, Localization["ARP_Open"]), new(SettlementStateFilter.Partial, Localization["ARP_Partial"]),
        new(SettlementStateFilter.Settled, Localization["ARP_Settled"])];
    public AsyncRelayCommand StartAdvanceCommand { get; private set; } = null!;
    public AsyncRelayCommand ApplyAdvanceCommand { get; private set; } = null!;
    public AsyncRelayCommand ReverseSettlementCommand { get; private set; } = null!;
    public AsyncRelayCommand PreviousInvoicesCommand { get; private set; } = null!;
    public AsyncRelayCommand NextInvoicesCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenSettlementJournalCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenSettlementReversalJournalCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenHistoryInvoiceCommand { get; private set; } = null!;

    private void InitializeSettlements()
    {
        StartAdvanceCommand = new(() => RunAsync(async t =>
        {
            IsApplyingAdvance = true; AllocationRows.Clear(); SelectedAllocation = null; ApplicationDate = DateTime.Today;
            ReverseReason = string.Empty; invoicePage = 1;
            await LoadOutstandingCoreAsync(null, t).ConfigureAwait(true);
        }), () => HasContext && Idle && !EditingSession && Current is { Status: VoucherStatus.Posted, PartyId: not null }
            && CanAllocate && Can(IsReceipt ? "Sales.Invoices.View" : "Purchases.Invoices.View") && Parse(Current.AdvanceAmount) is > 0m);
        ApplyAdvanceCommand = new(() => RunAsync(ApplyAdvanceAsync), () => HasContext && Idle && IsApplyingAdvance && CanAllocate
            && Current is { Status: VoucherStatus.Posted } && ApplicationDate.HasValue && !string.IsNullOrWhiteSpace(ReverseReason)
            && AllocationRows.Any(r => !string.IsNullOrWhiteSpace(r.Amount)) && AllocationsComplete()
            && Total(AllocationRows.Select(r => r.SettlementAmount)) <= Parse(Current.AdvanceAmount));
        ReverseSettlementCommand = new(() => RunAsync(ReverseSettlementAsync), () => HasContext && Idle && !EditingSession && CanReversePermission
            && Current is { Status: VoucherStatus.Posted } && SelectedBatch?.Source.Id is not null
            && SelectedBatch.Source.IsInitial != true
            && Convert.ToInt32(SelectedBatch.Source.State, CultureInfo.InvariantCulture) == 1 && !string.IsNullOrWhiteSpace(ReverseReason));
        PreviousInvoicesCommand = new(() => InvoicePageAsync(-1), () => CanLoadInvoices() && invoicePage > 1);
        NextInvoicesCommand = new(() => InvoicePageAsync(1), () => CanLoadInvoices() && (long)invoicePage * 50 < invoiceTotal);
        OpenSettlementJournalCommand = new(() => SelectedBatch?.Source.JournalId is { } id
            ? navigator.NavigateAsync("journals", new OpenJournalRequest(id)) : Task.CompletedTask,
            () => HasContext && Idle && !EditingSession && SelectedBatch?.Source.JournalId is not null && Can("Ledger.Journals.View"));
        OpenSettlementReversalJournalCommand = new(() => SelectedBatch?.Source.ReversalJournalId is { } id
            ? navigator.NavigateAsync("journals", new OpenJournalRequest(id)) : Task.CompletedTask,
            () => HasContext && Idle && !EditingSession && SelectedBatch?.Source.ReversalJournalId is not null && Can("Ledger.Journals.View"));
        OpenHistoryInvoiceCommand = new(() => SelectedHistoryAllocation?.InvoiceId is { } id
            ? IsReceipt ? navigator.NavigateAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(id))
                : navigator.NavigateAsync("purchase-invoices", new Purchases.OpenPurchaseRequest(id)) : Task.CompletedTask,
            () => HasContext && Idle && SelectedHistoryAllocation?.InvoiceId is not null && Can(IsReceipt ? "Sales.Invoices.View" : "Purchases.Invoices.View"));
    }
    private bool CanLoadInvoices() => HasContext && Idle && IsPartySettlement && DraftPartyId is not null
        && Can(IsReceipt ? "Sales.Invoices.View" : "Purchases.Invoices.View")
        && (string.IsNullOrWhiteSpace(InvoiceCurrencyFilter) || Controls.RequiredInput.Currency(InvoiceCurrencyFilter));
    private async Task InvoicePageAsync(int change)
    {
        var previous = invoicePage; invoicePage += change;
        if (!await RunAsync(t => LoadOutstandingCoreAsync(null, t)).ConfigureAwait(true)) invoicePage = previous;
        RefreshEditor();
    }
    private async Task LoadOutstandingCoreAsync(Guid? preselect, CancellationToken cancellationToken)
    {
        if (DraftPartyId is not { } partyId) return;
        var companyId = CompanyId; var branchId = BranchId; var kind = DraftKind;
        Dictionary<Guid, VoucherAllocationItem> kept = CanEditAllocations ? AllocationRows.Where(r => !string.IsNullOrWhiteSpace(r.Amount)
            || !string.IsNullOrWhiteSpace(r.SettlementAmount)).ToDictionary(r => r.InvoiceId) : [];
        var page = await client.SettlementInvoicesAsync(companyId, branchId, partyId, kind,
            string.IsNullOrWhiteSpace(InvoiceCurrencyFilter) ? null : InvoiceCurrencyFilter.Trim().ToUpperInvariant(), InvoiceStateFilter,
            invoicePage, 50, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId != CompanyId || branchId != BranchId || DraftPartyId != partyId || DraftKind != kind) return;
        invoiceTotal = page.TotalCount ?? 0;
        if (preselect is { } invoiceId && CanEditAllocations
            && (page.Items ?? []).FirstOrDefault(i => i.InvoiceId == invoiceId) is { } matched)
        {
            if (!string.IsNullOrWhiteSpace(matched.CurrencyCode) && !string.Equals(DraftCurrency, matched.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            {
                // Adopting the invoice currency asks the register for its rate; a foreign invoice is never priced at one.
                DraftCurrency = matched.CurrencyCode.Trim().ToUpperInvariant();
            }
        }
        AllocationRows.Clear();
        foreach (var invoice in page.Items ?? [])
        {
            var id = invoice.InvoiceId ?? Guid.Empty;
            if (kept.Remove(id, out var selected)) { AllocationRows.Add(selected); continue; }
            var sameCurrency = string.IsNullOrWhiteSpace(invoice.CurrencyCode)
                || string.IsNullOrWhiteSpace(DraftCurrency)
                || string.Equals(invoice.CurrencyCode, DraftCurrency.Trim(), StringComparison.OrdinalIgnoreCase);
            var amount = preselect == id && CanEditAllocations && sameCurrency ? invoice.Outstanding ?? string.Empty : string.Empty;
            AllocationRows.Add(new VoucherAllocationItem
            {
                InvoiceId = id, InvoiceNumber = invoice.Number ?? string.Empty,
                CurrencyCode = invoice.CurrencyCode ?? string.Empty, TotalDisplay = invoice.Total ?? string.Empty,
                PaidDisplay = invoice.Paid ?? string.Empty, StateKey = "ARP_" + invoice.State, StateDisplay = Localization["ARP_" + invoice.State],
                Outstanding = Parse(invoice.Outstanding) ?? 0m,
                DateDisplay = invoice.DateUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                Amount = amount, SettlementAmount = amount, CrossRate = sameCurrency ? "1" : string.Empty, Changed = DraftChanged,
            });
        }
        // Preserve selected allocations while paging/filtering; both amount cells must be cleared to remove a selection.
        foreach (var selected in kept.Values) AllocationRows.Add(selected);
        if (preselect is not null && (string.IsNullOrWhiteSpace(DraftAmount) || DraftAmount == "0")) DraftAmount = SelectionTotalDisplay;
        RefreshEditor();
    }
    private VoucherAllocationItem FromApplication(InvoiceSettlementDto application) => new()
    {
        InvoiceId = application.InvoiceId ?? Guid.Empty, InvoiceNumber = application.InvoiceNumber ?? string.Empty,
        DateDisplay = application.InvoiceDateUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
        CurrencyCode = application.InvoiceCurrency ?? string.Empty, Amount = application.InvoiceAmount ?? string.Empty,
        SettlementAmount = application.SettlementAmount ?? string.Empty, CrossRate = application.CrossRate ?? "1", Changed = DraftChanged,
    };
    private async Task ApplyAdvanceAsync(CancellationToken cancellationToken)
    {
        if (Current is not { Id: { } id, Version: { } version } || !ApplicationDate.HasValue) return;
        var lines = AllocationInputs(); var date = UtcDate(ApplicationDate.Value); var reason = ReverseReason.Trim();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, version, date, reason, lines }))));
        if (advanceFingerprint != fingerprint) { advanceFingerprint = fingerprint; advanceKey = $"voucher-apply:{id:N}:{Guid.NewGuid():N}"; }
        var result = await client.ApplyAdvanceAsync(CompanyId, BranchId, id, new ApplyAdvanceRequest
        { ExpectedVersion = version, IdempotencyKey = advanceKey!, Reason = reason, DateUtc = date, Lines = lines }, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested(); advanceKey = null; advanceFingerprint = null;
        Show(result); await ListAsync(cancellationToken).ConfigureAwait(true);
    }
    private async Task ReverseSettlementAsync(CancellationToken cancellationToken)
    {
        if (Current?.Id is not { } voucherId || SelectedBatch?.Source is not { Id: { } id, Version: { } version } batch || batch.IsInitial == true) return;
        var result = await client.ReverseSettlementAsync(CompanyId, BranchId, voucherId, id,
            new VoucherActionRequest { ExpectedVersion = version, Reason = ReverseReason.Trim() }, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested(); Show(result); await ListAsync(cancellationToken).ConfigureAwait(true);
    }
    partial void OnIsApplyingAdvanceChanged(bool value) => RefreshEditor();
    partial void OnApplicationDateChanged(DateTime? value) => ApplyAdvanceCommand?.NotifyCanExecuteChanged();
    partial void OnInvoiceCurrencyFilterChanged(string value) { invoicePage = 1; RefreshEditor(); }
    partial void OnInvoiceStateFilterChanged(SettlementStateFilter value) { invoicePage = 1; RefreshEditor(); }
    partial void OnSelectedBatchChanged(VoucherSettlementHistory? value)
    {
        SelectedHistoryAllocation = null; RefreshEditor();
    }
    partial void OnSelectedHistoryAllocationChanged(InvoiceSettlementDto? value) => OpenHistoryInvoiceCommand?.NotifyCanExecuteChanged();
}
