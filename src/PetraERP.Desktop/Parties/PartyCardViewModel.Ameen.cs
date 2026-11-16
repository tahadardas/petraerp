using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;

namespace PetraERP.Desktop.Parties;

/// <summary>One price type the party's sales price can follow (the same list codes as the price page of the item card).</summary>
public sealed record PartyPriceTypeOption(string Code, string Caption);

/// <summary>Al-Ameen customer / supplier card: title, suffix, barcode, hidden-from-search flag, discount percentage, sales price type, extra accounts, notes; record navigation.</summary>
public sealed partial class PartyCardViewModel
{
    private bool loadingProfile;
    private List<Guid>? navigationIds;
    private AsyncRelayCommand? firstPartyCommand;
    private AsyncRelayCommand? previousPartyCommand;
    private AsyncRelayCommand? nextPartyCommand;
    private AsyncRelayCommand? lastPartyCommand;

    [ObservableProperty] private string? draftTitle;
    [ObservableProperty] private string? draftSuffix;
    [ObservableProperty] private string? draftBarcode;
    [ObservableProperty] private bool draftHideInSearch;
    [ObservableProperty] private string? draftDiscountPercent;
    [ObservableProperty] private string draftSalesPriceType = "CONSUMER";
    [ObservableProperty] private Guid? draftConditionalDiscountAccountId;
    [ObservableProperty] private Guid? draftVatAccountId;

    public IReadOnlyList<PartyPriceTypeOption> PriceTypeOptions { get; } =
        [new("WHOLESALE", "الجملة"), new("SEMI_WHOLESALE", "نصف الجملة"), new("DISTRIBUTOR", "الموزع"), new("EXPORT", "التصدير"), new("RETAIL", "المفرق"), new("CONSUMER", "المستهلك")];

    private static readonly HashSet<string> ProfileDraftNames =
    [
        nameof(DraftTitle), nameof(DraftSuffix), nameof(DraftBarcode), nameof(DraftHideInSearch), nameof(DraftDiscountPercent),
        nameof(DraftSalesPriceType), nameof(DraftConditionalDiscountAccountId), nameof(DraftVatAccountId),
    ];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!loadingProfile && e.PropertyName is { } name && ProfileDraftNames.Contains(name)) DraftChanged();
    }

    private void LoadProfileDraft(PartyProfileDto? profile)
    {
        loadingProfile = true;
        try
        {
            DraftTitle = profile?.Title;
            DraftSuffix = profile?.Suffix;
            DraftBarcode = profile?.Barcode;
            DraftHideInSearch = profile?.HideInSearch ?? false;
            DraftDiscountPercent = profile?.DiscountPercent;
            DraftSalesPriceType = string.IsNullOrWhiteSpace(profile?.SalesPriceType) ? "CONSUMER" : profile!.SalesPriceType!;
            DraftConditionalDiscountAccountId = profile?.ConditionalDiscountAccountId;
            DraftVatAccountId = profile?.VatAccountId;
            DraftNotes = profile?.Notes;
        }
        finally
        {
            loadingProfile = false;
        }
    }

    private PartyProfileDto BuildProfile() => new()
    {
        Title = Text(DraftTitle),
        Suffix = Text(DraftSuffix),
        Barcode = Text(DraftBarcode),
        HideInSearch = DraftHideInSearch,
        DiscountPercent = Text(DraftDiscountPercent) ?? "0.0000",
        SalesPriceType = DraftSalesPriceType,
        ConditionalDiscountAccountId = DraftConditionalDiscountAccountId,
        VatAccountId = DraftVatAccountId,
        Notes = Text(DraftNotes),
    };

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private bool ProfileHasChanges()
    {
        var saved = Party?.Profile;
        static string N(string? value) => value?.Trim() ?? string.Empty;
        static string D(string? value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number.ToString("0.####", CultureInfo.InvariantCulture) : "0";
        return N(DraftTitle) != N(saved?.Title) || N(DraftSuffix) != N(saved?.Suffix) || N(DraftBarcode) != N(saved?.Barcode)
            || DraftHideInSearch != (saved?.HideInSearch ?? false) || D(DraftDiscountPercent) != D(saved?.DiscountPercent)
            || DraftSalesPriceType != (string.IsNullOrWhiteSpace(saved?.SalesPriceType) ? "CONSUMER" : saved!.SalesPriceType)
            || DraftConditionalDiscountAccountId != saved?.ConditionalDiscountAccountId || DraftVatAccountId != saved?.VatAccountId
            || N(DraftNotes) != N(saved?.Notes);
    }

    private IEnumerable<string> ProfileValidationErrors()
    {
        if (!string.IsNullOrWhiteSpace(DraftDiscountPercent))
        {
            if (!RequiredInput.AmountText(DraftDiscountPercent)) yield return "نسبة الحسم: قيمة غير صحيحة";
            else if (decimal.Parse(DraftDiscountPercent, NumberStyles.Number, CultureInfo.InvariantCulture) > 100m) yield return "نسبة الحسم: لا تزيد عن 100";
        }
    }

    // ---- record navigation (saved records only) ----

    public bool CanNavigate => IsViewMode && HasContext && Idle && PartyId is not null;
    public string RecordCode => DraftCode;

    public AsyncRelayCommand FirstPartyCommand => firstPartyCommand ??= new(() => MoveAsync(_ => 0), () => CanNavigate);
    public AsyncRelayCommand PreviousPartyCommand => previousPartyCommand ??= new(() => MoveAsync(index => index - 1), () => CanNavigate);
    public AsyncRelayCommand NextPartyCommand => nextPartyCommand ??= new(() => MoveAsync(index => index + 1), () => CanNavigate);
    public AsyncRelayCommand LastPartyCommand => lastPartyCommand ??= new(() => MoveAsync(_ => int.MaxValue), () => CanNavigate);

    private async Task<List<Guid>> EnsureNavigationIdsAsync(CancellationToken token)
    {
        if (navigationIds is not null) return navigationIds;
        var ids = new List<Guid>();
        for (var page = 1; ; page++)
        {
            var response = await partiesClient.ListPartiesAsync(CompanyId, BranchId, page, 200, null, null, null, token).ConfigureAwait(true);
            var items = response.Items ?? [];
            ids.AddRange(items.Where(i => i.Id is not null).Select(i => i.Id!.Value));
            if (items.Count == 0 || ids.Count >= (response.TotalCount ?? 0)) break;
        }

        return navigationIds = ids;
    }

    private async Task MoveAsync(Func<int, int> target)
    {
        await RunAsync(async token =>
        {
            var ids = await EnsureNavigationIdsAsync(token).ConfigureAwait(true);
            if (ids.Count == 0 || PartyId is not { } current) return;
            var next = Math.Clamp(target(Math.Max(ids.IndexOf(current), 0)), 0, ids.Count - 1);
            if (ids[next] == current) return;
            PartyId = ids[next];
            await LoadPartyAsync(token).ConfigureAwait(true);
        }).ConfigureAwait(true);
        NotifyNavigation();
    }

    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(CanNavigate));
        OnPropertyChanged(nameof(RecordCode));
        firstPartyCommand?.NotifyCanExecuteChanged();
        previousPartyCommand?.NotifyCanExecuteChanged();
        nextPartyCommand?.NotifyCanExecuteChanged();
        lastPartyCommand?.NotifyCanExecuteChanged();
    }
}
