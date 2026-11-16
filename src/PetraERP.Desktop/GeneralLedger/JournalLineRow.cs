using CommunityToolkit.Mvvm.ComponentModel;
using PetraERP.Desktop.Controls;

namespace PetraERP.Desktop.GeneralLedger;

public sealed class JournalLineRow : ObservableObject, System.ComponentModel.IDataErrorInfo, IDocumentEntryRow
{
    // Mirrors the public decimal storage contract: money (19,4), rates (19,8).
    private const decimal MoneyExclusiveLimit = 1_000_000_000_000_000m;
    private const decimal RateExclusiveLimit = 100_000_000_000m;
    public Func<string, string>? Localize { get; init; }

    public bool IsPersisted { get; set; }

    public bool IsPristineBlank =>
        !IsPersisted
        && AccountId == Guid.Empty
        && CostCenterId is null
        && Debit == 0m
        && Credit == 0m
        && string.IsNullOrWhiteSpace(Description);

    public bool IsComplete =>
        AccountId != Guid.Empty
        && Rate > 0 && Rate < RateExclusiveLimit && decimal.Round(Rate, 8) == Rate
        && ((Debit > 0 && Credit == 0) || (Credit > 0 && Debit == 0))
        && Debit >= 0 && Debit < MoneyExclusiveLimit && decimal.Round(Debit, 4) == Debit
        && Credit >= 0 && Credit < MoneyExclusiveLimit && decimal.Round(Credit, 4) == Credit
        && string.IsNullOrEmpty(Error);

    public bool IsIncomplete => !IsPristineBlank && !IsComplete;

    public DocumentRowState State =>
        IsPersisted ? DocumentRowState.Persisted :
        IsPristineBlank ? DocumentRowState.PristineBlank :
        IsComplete ? DocumentRowState.Complete :
        DocumentRowState.Incomplete;

    public DocumentRowState RowState => State;

    public string? RowError => string.IsNullOrEmpty(Error) ? null : Error;

    /// <summary>The first reason this row cannot post, as a localization key, so the banner can name the row and the field.</summary>
    public string? RowErrorCode =>
        IsPristineBlank || IsComplete ? null :
        AccountId == Guid.Empty ? "D_LineAccountRequired" :
        Rate <= 0 || Rate >= RateExclusiveLimit || decimal.Round(Rate, 8) != Rate ? "D_LineRateInvalid" :
        Debit > 0 && Credit > 0 ? "D_LineAmountBothSides" :
        Debit == 0 && Credit == 0 ? "D_LineAmountRequired" :
        "D_LineAmountInvalid";

    public string Error => IsPristineBlank ? string.Empty
        : this[nameof(Rate)] is { Length: > 0 } rateError ? rateError
        : this[nameof(Debit)] is { Length: > 0 } debitError ? debitError
        : this[nameof(Credit)];

    public string this[string columnName]
    {
        get
        {
            if (IsPristineBlank) return string.Empty;
            return columnName switch
            {
                nameof(AccountId) when AccountId == Guid.Empty => InvalidValue(),
                nameof(Rate) when Rate <= 0 || Rate >= RateExclusiveLimit || decimal.Round(Rate, 8) != Rate => InvalidValue(),
                nameof(Debit) when Debit < 0 || Debit >= MoneyExclusiveLimit || decimal.Round(Debit, 4) != Debit || (Debit > 0 && Credit > 0)  => InvalidValue(),
                nameof(Credit) when Credit < 0 || Credit >= MoneyExclusiveLimit || decimal.Round(Credit, 4) != Credit || (Debit > 0 && Credit > 0)  => InvalidValue(),
                _ => string.Empty,
            };
        }
    }

    private string InvalidValue() => Localize?.Invoke("D_JournalNumericValidation") ?? "Invalid financial value.";
    private Guid accountId;
    private Guid? costCenterId;
    private string currencyCode = string.Empty;
    private decimal debit;
    private decimal credit;
    private decimal rate = 1.0m;
    private string description = string.Empty;

    public Guid AccountId
    {
        get => accountId;
        set { SetProperty(ref accountId, value); OnLineChanged(); }
    }

    public Guid? CostCenterId
    {
        get => costCenterId;
        set { SetProperty(ref costCenterId, value); OnLineChanged(); }
    }

    public string CurrencyCode
    {
        get => currencyCode;
        set { SetProperty(ref currencyCode, value); OnLineChanged(); }
    }

    public decimal Debit
    {
        get => debit;
        set { SetProperty(ref debit, value); OnLineChanged(); }
    }

    public decimal Credit
    {
        get => credit;
        set { SetProperty(ref credit, value); OnLineChanged(); }
    }

    public decimal Rate
    {
        get => rate;
        set { SetProperty(ref rate, value); OnLineChanged(); }
    }

    public string Description
    {
        get => description;
        set { SetProperty(ref description, value); OnLineChanged(); }
    }

    public decimal BaseDebit => Math.Round(Debit * Rate, 4, MidpointRounding.ToEven);
    public decimal BaseCredit => Math.Round(Credit * Rate, 4, MidpointRounding.ToEven);

    public event Action? Changed;

    private void OnLineChanged()
    {
        OnPropertyChanged(nameof(BaseDebit));
        OnPropertyChanged(nameof(BaseCredit));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(RowState));
        OnPropertyChanged(nameof(RowError));
        OnPropertyChanged(nameof(RowErrorCode));
        OnPropertyChanged(nameof(IsPristineBlank));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsIncomplete));
        Changed?.Invoke();
    }

    public static JournalLineRow CreateBlankSlot(Func<string, string>? localize, string currencyCode, Action? changed = null)
    {
        var row = new JournalLineRow
        {
            Localize = localize,
            CurrencyCode = currencyCode,
            Rate = 1.0m,
        };
        if (changed != null) row.Changed += changed;
        return row;
    }
}
