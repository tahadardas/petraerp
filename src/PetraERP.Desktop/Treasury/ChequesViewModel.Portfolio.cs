using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Treasury;

/// <summary>
/// Al-Ameen 10 Commercial Papers & Checks Portfolio enhancements:
/// Real-time Arabic Tafqeet on check cards, interactive due date severity filters,
/// portfolio statistics & aggregates, and direct journal & party ledger audit navigation.
/// </summary>
public sealed partial class ChequesViewModel
{
    private readonly List<CommercialPaperDto> allLoadedPapers = [];

    [ObservableProperty] private string newAmountTafqeet = string.Empty;
    [ObservableProperty] private CommercialPaperAlertSeverity? selectedSeverityFilter;

    // Portfolio Totals & Statistics
    [ObservableProperty] private string totalReceivableDisplay = "0.00";
    [ObservableProperty] private string totalPayableDisplay = "0.00";
    [ObservableProperty] private string totalUnderCollectionDisplay = "0.00";
    [ObservableProperty] private string totalBouncedDisplay = "0.00";
    [ObservableProperty] private string totalAmountDisplay = "0.00";
    [ObservableProperty] private int totalCountDisplay;

    // Audit & Drill-down Commands
    private AsyncRelayCommand? openJournalCommand;
    private AsyncRelayCommand? openPartyStatementCommand;
    private AsyncRelayCommand? openPartyCardCommand;
    private RelayCommand? printChequeReceiptCommand;
    private RelayCommand<CommercialPaperAlertSeverity?>? filterSeverityCommand;

    public bool HasLinkedJournal => SelectedPaper?.LastJournalEntryId.HasValue == true;
    public bool HasSelectedParty => SelectedPaper?.PartyId.HasValue == true;

    public AsyncRelayCommand OpenJournalCommand => openJournalCommand ??= new(
        async () =>
        {
            if (SelectedPaper?.LastJournalEntryId is { } jId)
                await navigator.NavigateAsync("journals", new OpenJournalRequest(jId));
        },
        () => HasLinkedJournal);

    public AsyncRelayCommand OpenPartyStatementCommand => openPartyStatementCommand ??= new(
        async () =>
        {
            if (SelectedPaper?.PartyId is { } pId)
            {
                var kind = SelectedPaper.PaperKind == CommercialPaperKind.ChequeIssued
                    ? PartyStatementKind.Supplier
                    : PartyStatementKind.Customer;
                await navigator.NavigateAsync("customer-statement", new Reports.OpenPartyStatementRequest(pId, kind));
            }
        },
        () => HasSelectedParty);

    public AsyncRelayCommand OpenPartyCardCommand => openPartyCardCommand ??= new(
        async () =>
        {
            if (SelectedPaper?.PartyId is { } pId)
                await navigator.NavigateAsync("party-card", new OpenPartyCardRequest(pId));
        },
        () => HasSelectedParty);

    public RelayCommand PrintChequeReceiptCommand => printChequeReceiptCommand ??= new(
        () =>
        {
            if (SelectedPaper is null) return;
            // Print notice or display prompt
        },
        () => SelectedPaper is not null);

    public RelayCommand<CommercialPaperAlertSeverity?> FilterSeverityCommand => filterSeverityCommand ??= new(
        severity =>
        {
            SelectedSeverityFilter = severity;
            ApplyLocalSeverityFilter();
        });

    public void RefreshTafqeet()
    {
        if (decimal.TryParse(NewAmount, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amt) && amt > 0m)
        {
            NewAmountTafqeet = ArabicTafqeet.ToWordsOrEmpty(amt, NewCurrencyCode);
        }
        else
        {
            NewAmountTafqeet = string.Empty;
        }
    }

    public void UpdatePortfolioResults(IReadOnlyList<CommercialPaperDto> items)
    {
        allLoadedPapers.Clear();
        allLoadedPapers.AddRange(items);
        CalculatePortfolioTotals();
        ApplyLocalSeverityFilter();
    }

    private void CalculatePortfolioTotals()
    {
        decimal receivable = 0m;
        decimal payable = 0m;
        decimal underCollection = 0m;
        decimal bounced = 0m;
        decimal total = 0m;

        foreach (var paper in allLoadedPapers)
        {
            if (!decimal.TryParse(paper.Amount, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amt))
                continue;

            total += amt;
            if (paper.PaperKind == CommercialPaperKind.ChequeReceived)
                receivable += amt;
            else if (paper.PaperKind == CommercialPaperKind.ChequeIssued)
                payable += amt;

            if (paper.Status == CommercialPaperStatus.UnderCollection)
                underCollection += amt;
            else if (paper.Status == CommercialPaperStatus.Bounced)
                bounced += amt;
        }

        TotalReceivableDisplay = receivable.ToString("N2", CultureInfo.InvariantCulture);
        TotalPayableDisplay = payable.ToString("N2", CultureInfo.InvariantCulture);
        TotalUnderCollectionDisplay = underCollection.ToString("N2", CultureInfo.InvariantCulture);
        TotalBouncedDisplay = bounced.ToString("N2", CultureInfo.InvariantCulture);
        TotalAmountDisplay = total.ToString("N2", CultureInfo.InvariantCulture);
        TotalCountDisplay = allLoadedPapers.Count;
    }

    private void ApplyLocalSeverityFilter()
    {
        Papers.Clear();
        var query = SelectedSeverityFilter.HasValue
            ? allLoadedPapers.Where(p => p.Severity == SelectedSeverityFilter.Value)
            : allLoadedPapers;

        foreach (var p in query)
        {
            Papers.Add(p);
        }

        OnPropertyChanged(nameof(IsFilterAllActive));
        OnPropertyChanged(nameof(IsFilterOverdueActive));
        OnPropertyChanged(nameof(IsFilterDueTodayActive));
        OnPropertyChanged(nameof(IsFilterDueSoonActive));
    }

    public bool IsFilterAllActive => !SelectedSeverityFilter.HasValue;
    public bool IsFilterOverdueActive => SelectedSeverityFilter == CommercialPaperAlertSeverity.Overdue;
    public bool IsFilterDueTodayActive => SelectedSeverityFilter == CommercialPaperAlertSeverity.DueToday;
    public bool IsFilterDueSoonActive => SelectedSeverityFilter == CommercialPaperAlertSeverity.DueSoon;

    public void NotifyAuditCommands()
    {
        OnPropertyChanged(nameof(HasLinkedJournal));
        OnPropertyChanged(nameof(HasSelectedParty));
        openJournalCommand?.NotifyCanExecuteChanged();
        openPartyStatementCommand?.NotifyCanExecuteChanged();
        openPartyCardCommand?.NotifyCanExecuteChanged();
        printChequeReceiptCommand?.NotifyCanExecuteChanged();
    }
}
