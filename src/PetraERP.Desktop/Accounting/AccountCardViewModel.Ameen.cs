using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;

namespace PetraERP.Desktop.Accounting;

public sealed record AmeenChoice(int Value, string Label);

/// <summary>
/// Al-Ameen account-card attributes: notes, nature, closing account, default cost center, estimated budget and the financial-statement
/// placement. They are stored with the account and edited with it; they do not affect posting or balances.
/// </summary>
public sealed partial class AccountCardViewModel
{
    private bool loadingProfile;
    private bool lookupsLoaded;
    private AccountProfileDto? savedProfile;

    [ObservableProperty] private string? draftNotes;
    [ObservableProperty] private int draftNature = 3;
    [ObservableProperty] private Guid? draftClosingAccountId;
    [ObservableProperty] private Guid? draftDefaultCostCenterId;
    [ObservableProperty] private int draftBudgetKind;
    [ObservableProperty] private string? draftBudgetAmount;
    [ObservableProperty] private string? draftBudgetCurrency;
    [ObservableProperty] private string? draftBudgetRate;
    [ObservableProperty] private bool draftIncludePapers;
    [ObservableProperty] private string? draftIncomeCategory;
    [ObservableProperty] private string? draftEquityCategory;
    [ObservableProperty] private string? draftPositionCategory;
    [ObservableProperty] private string? draftCashFlowCategory;

    public IReadOnlyList<AmeenChoice> NatureChoices { get; } = [new(1, "مدين"), new(2, "دائن"), new(3, "مدين ودائن")];
    public IReadOnlyList<AmeenChoice> BudgetChoices { get; } = [new(0, "بدون"), new(1, "مدينة"), new(2, "دائنة")];
    public ObservableCollection<LookupItem> ClosingAccountLookups { get; } = [];
    public ObservableCollection<LookupItem> CostCenterLookups { get; } = [];

    public bool IsNatureDebit { get => DraftNature == 1; set { if (value) { DraftNature = 1; NotifyNatureRadio(); } } }
    public bool IsNatureCredit { get => DraftNature == 2; set { if (value) { DraftNature = 2; NotifyNatureRadio(); } } }
    public bool IsNatureBoth { get => DraftNature == 3; set { if (value) { DraftNature = 3; NotifyNatureRadio(); } } }

    public bool IsBudgetNone { get => DraftBudgetKind == 0; set { if (value) { DraftBudgetKind = 0; NotifyBudgetRadio(); } } }
    public bool IsBudgetDebit { get => DraftBudgetKind == 1; set { if (value) { DraftBudgetKind = 1; NotifyBudgetRadio(); } } }
    public bool IsBudgetCredit { get => DraftBudgetKind == 2; set { if (value) { DraftBudgetKind = 2; NotifyBudgetRadio(); } } }

    [ObservableProperty] private bool draftHideInSearch;
    [ObservableProperty] private bool draftRequireCostCenter;

    private void NotifyNatureRadio()
    {
        OnPropertyChanged(nameof(IsNatureDebit));
        OnPropertyChanged(nameof(IsNatureCredit));
        OnPropertyChanged(nameof(IsNatureBoth));
        OnPropertyChanged(nameof(HasNatureViolation));
        OnPropertyChanged(nameof(NatureViolationWarning));
    }

    public bool HasNatureViolation
    {
        get
        {
            if (Summary?.ClosingBalance is not { } closing ||
                !decimal.TryParse(closing, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var bal) ||
                bal == 0m)
            {
                return false;
            }

            return (DraftNature == 1 && bal < 0m) || (DraftNature == 2 && bal > 0m);
        }
    }

    public string NatureViolationWarning
    {
        get
        {
            if (Summary?.ClosingBalance is not { } closing ||
                !decimal.TryParse(closing, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var bal) ||
                bal == 0m)
            {
                return string.Empty;
            }

            if (DraftNature == 1 && bal < 0m)
                return "تنبيه رقابي (الأمين): رصيد الحساب دائن خلافاً لطبيعته المدينة المحددة في البطاقة (كشف رصيد)!";
            if (DraftNature == 2 && bal > 0m)
                return "تنبيه رقابي (الأمين): رصيد الحساب مدين خلافاً لطبيعته الدائنة المحددة في البطاقة!";

            return string.Empty;
        }
    }

    private void NotifyBudgetRadio()
    {
        OnPropertyChanged(nameof(IsBudgetNone));
        OnPropertyChanged(nameof(IsBudgetDebit));
        OnPropertyChanged(nameof(IsBudgetCredit));
    }

    public bool HasBudget => DraftBudgetKind != 0;
    public bool CanEditBudget => IsEditing && HasBudget;

    private static readonly HashSet<string> ProfileDraftNames =
    [
        nameof(DraftNotes), nameof(DraftNature), nameof(DraftClosingAccountId), nameof(DraftDefaultCostCenterId), nameof(DraftBudgetKind),
        nameof(DraftBudgetAmount), nameof(DraftBudgetCurrency), nameof(DraftBudgetRate), nameof(DraftIncludePapers),
        nameof(DraftIncomeCategory), nameof(DraftEquityCategory), nameof(DraftPositionCategory), nameof(DraftCashFlowCategory),
        nameof(DraftHideInSearch), nameof(DraftRequireCostCenter),
    ];

    protected override bool HasDraftChanges => CoreHasDraftChanges || ProfileHasChanges();

    private void ProfileChanged(string? name)
    {
        if (name is nameof(DraftBudgetKind))
        {
            NotifyBudgetRadio();
            OnPropertyChanged(nameof(HasBudget)); OnPropertyChanged(nameof(CanEditBudget));
            if (!loadingProfile)
            {
                if (DraftBudgetKind == 0) { DraftBudgetAmount = null; DraftBudgetCurrency = null; DraftBudgetRate = null; }
                else
                {
                    DraftBudgetAmount ??= "0.0000";
                    if (string.IsNullOrWhiteSpace(DraftBudgetCurrency)) DraftBudgetCurrency = Context?.BaseCurrencyCode;
                    if (string.IsNullOrWhiteSpace(DraftBudgetRate)) DraftBudgetRate = "1";
                }
            }
        }

        if (name is nameof(Mode) or nameof(IsEditing))
        {
            OnPropertyChanged(nameof(CanEditBudget));
            if (IsEditing) _ = LoadFullLookupsAsync();
        }
        if (!loadingProfile && name is not null && ProfileDraftNames.Contains(name)) DraftChanged();
    }

    /// <summary>
    /// Opening a card never reads the whole chart: only the profile and the (at most two) records it points at are read. The full
    /// pick lists load once, when the card first enters edit mode.
    /// </summary>
    private async Task LoadProfileAsync(Guid accountId, CancellationToken token)
    {
        try
        {
            savedProfile = await accounts.GetAccountProfileAsync(CompanyId, BranchId, accountId, token).ConfigureAwait(true);
            if (!lookupsLoaded)
            {
                ClosingAccountLookups.Clear();
                CostCenterLookups.Clear();
                if (savedProfile?.ClosingAccountId is { } closingId)
                {
                    var closing = await accounts.GetAccountAsync(CompanyId, BranchId, closingId, token).ConfigureAwait(true);
                    ClosingAccountLookups.Add(new LookupItem(closing.Id, closing.Code, closing.NameArabic, closing.NameEnglish));
                }

                if (savedProfile?.DefaultCostCenterId is { } centerId)
                {
                    var center = await accounts.GetCostCenterAsync(CompanyId, BranchId, centerId, token).ConfigureAwait(true);
                    CostCenterLookups.Add(new LookupItem(center.Id, center.Code, center.NameArabic, center.NameEnglish));
                }
            }
        }
        catch (NotSupportedException)
        {
            savedProfile = null;
        }

        LoadProfileDraft(savedProfile);
    }

    private async Task LoadFullLookupsAsync()
    {
        if (lookupsLoaded || !HasContext) return;
        lookupsLoaded = true;
        try
        {
            var tree = await accounts.GetAccountTreeAsync(CompanyId, BranchId, CancellationToken.None).ConfigureAwait(true);
            var centers = await accounts.GetCostCenterTreeAsync(CompanyId, BranchId, CancellationToken.None).ConfigureAwait(true);
            ClosingAccountLookups.Clear();
            foreach (var item in tree.Where(a => a.IsActive)) ClosingAccountLookups.Add(new LookupItem(item.Id, item.Code, item.NameArabic, item.NameEnglish));
            CostCenterLookups.Clear();
            foreach (var center in centers.Where(c => c.IsActive)) CostCenterLookups.Add(new LookupItem(center.Id, center.Code, center.NameArabic, center.NameEnglish));
        }
        catch (Exception)
        {
            // The pick lists are a convenience; the card stays usable and the next edit retries.
            lookupsLoaded = false;
        }
    }

    private void LoadProfileDraft(AccountProfileDto? profile)
    {
        loadingProfile = true;
        try
        {
            DraftNotes = profile?.Notes;
            DraftNature = profile?.Nature ?? 3;
            DraftClosingAccountId = profile?.ClosingAccountId;
            DraftDefaultCostCenterId = profile?.DefaultCostCenterId;
            DraftBudgetKind = profile?.BudgetKind ?? 0;
            DraftBudgetAmount = profile?.BudgetAmount;
            DraftBudgetCurrency = profile?.BudgetCurrencyCode;
            DraftBudgetRate = profile?.BudgetRate;
            DraftIncludePapers = profile?.IncludeCommercialPapers ?? false;
            DraftIncomeCategory = profile?.IncomeStatementCategory;
            DraftEquityCategory = profile?.EquityCategory;
            DraftPositionCategory = profile?.FinancialPositionCategory;
            DraftCashFlowCategory = profile?.CashFlowCategory;
        }
        finally
        {
            loadingProfile = false;
        }

        OnPropertyChanged(nameof(HasBudget));
        OnPropertyChanged(nameof(CanEditBudget));
        NotifyNatureRadio();
        NotifyBudgetRadio();
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private AccountProfileDto BuildProfile() => new()
    {
        Notes = Text(DraftNotes), Nature = DraftNature, ClosingAccountId = DraftClosingAccountId, DefaultCostCenterId = DraftDefaultCostCenterId,
        BudgetKind = DraftBudgetKind,
        BudgetAmount = DraftBudgetKind == 0 ? null : Text(DraftBudgetAmount),
        BudgetCurrencyCode = DraftBudgetKind == 0 ? null : Text(DraftBudgetCurrency)?.ToUpperInvariant(),
        BudgetRate = DraftBudgetKind == 0 ? null : Text(DraftBudgetRate),
        IncludeCommercialPapers = DraftIncludePapers, IncomeStatementCategory = Text(DraftIncomeCategory), EquityCategory = Text(DraftEquityCategory),
        FinancialPositionCategory = Text(DraftPositionCategory), CashFlowCategory = Text(DraftCashFlowCategory),
    };

    /// <summary>Writes the profile only when it differs from what was loaded, so an edit of the core data never depends on it.</summary>
    private Task SaveProfileAsync(Guid accountId, CancellationToken token) =>
        ProfileHasChanges() ? accounts.SetAccountProfileAsync(CompanyId, BranchId, accountId, BuildProfile(), token) : Task.CompletedTask;

    private bool ProfileHasChanges()
    {
        var saved = IsNewMode ? null : savedProfile;
        static string N(string? value) => value?.Trim() ?? string.Empty;
        static string D(string? value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number.ToString("0.########", CultureInfo.InvariantCulture) : N(value);
        return N(DraftNotes) != N(saved?.Notes) || DraftNature != (saved?.Nature ?? 3) || DraftClosingAccountId != saved?.ClosingAccountId
            || DraftDefaultCostCenterId != saved?.DefaultCostCenterId || DraftBudgetKind != (saved?.BudgetKind ?? 0)
            || D(DraftBudgetAmount) != D(saved?.BudgetAmount) || N(DraftBudgetCurrency) != N(saved?.BudgetCurrencyCode) || D(DraftBudgetRate) != D(saved?.BudgetRate)
            || DraftIncludePapers != (saved?.IncludeCommercialPapers ?? false) || N(DraftIncomeCategory) != N(saved?.IncomeStatementCategory)
            || N(DraftEquityCategory) != N(saved?.EquityCategory) || N(DraftPositionCategory) != N(saved?.FinancialPositionCategory)
            || N(DraftCashFlowCategory) != N(saved?.CashFlowCategory);
    }

    private IEnumerable<string> ProfileValidationErrors()
    {
        if (DraftBudgetKind != 0)
        {
            if (!RequiredInput.AmountText(DraftBudgetAmount)) yield return "الموازنة التقديرية: القيمة غير صحيحة";
            if (!RequiredInput.Currency(DraftBudgetCurrency)) yield return "الموازنة التقديرية: رمز العملة من 3 أحرف مطلوب";
            if (!RequiredInput.AmountText(DraftBudgetRate, 8, true)) yield return "الموازنة التقديرية: التعادل مطلوب وأكبر من صفر";
        }

        if (DraftClosingAccountId is { } closing && Account is { } current && closing == current.Id && !IsNewMode) yield return "الحساب الختامي: لا يمكن أن يكون الحساب نفسه";
    }
}
