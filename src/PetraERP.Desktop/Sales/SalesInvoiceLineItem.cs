using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PetraERP.Desktop.Controls;

namespace PetraERP.Desktop.Sales;

public sealed class SalesInvoiceLineItem : ObservableObject, IDocumentEntryRow, IDataErrorInfo
{
    private Guid id = Guid.NewGuid();
    private Guid? itemId;
    private string itemCode = string.Empty;
    private string itemNameArabic = string.Empty;
    private Guid? unitId;
    private string unitCode = string.Empty;
    private string? concreteGrade;
    private string quantity = "0.0000";
    private string unitPrice = "0.0000";
    private string discountPercentage = "0.0000";
    private string discountAmount = "0.0000";
    private string taxPercentage = "0.0000";
    private string taxAmount = "0.0000";
    private string pumpCost = "0.0000";
    private string transportCost = "0.0000";
    private string lineTotal = "0.0000";
    private Guid? costCenterId;
    private Guid? warehouseId;
    private string? notes;
    private int displayOrder;
    private string priceSource = string.Empty;

    private bool isPersisted;

    public Action? Changed { get; set; }

    /// <summary>The item the line's unit was filled for; a different item means the unit must be filled again.</summary>
    internal Guid? UnitAssignedFor { get; set; }

    private IReadOnlyList<LookupItem> unitOptions = [];
    private IReadOnlyList<Guid> allowedUnits = [];
    private bool unitsKnown;

    /// <summary>The units the line's item accepts (base unit first); never the whole company list.</summary>
    public IReadOnlyList<LookupItem> UnitOptions { get => unitOptions; private set => SetProperty(ref unitOptions, value); }

    /// <summary>The chosen unit has no conversion to the item's base unit; the server would refuse the document.</summary>
    public bool HasDisallowedUnit => unitsKnown && UnitId is { } id && !allowedUnits.Contains(id);

    /// <summary>Nothing is known about the item's units (another item, or none).</summary>
    internal void ResetUnits()
    {
        unitsKnown = false; allowedUnits = []; UnitOptions = []; unitSetupMissing = false;
        NotifyRowState();
    }

    /// <summary>Shows the line's own unit while the item's units are not known (a viewed document, or no answer yet).</summary>
    internal void ShowUnit(LookupItem? current)
    {
        if (!unitsKnown) UnitOptions = current is null ? [] : [current];
    }

    /// <summary>
    /// Applies the server's answer. An empty unit takes the base unit. A stored unit that does not fit is never rewritten
    /// silently: the row reports it. An item without a base unit cannot be judged, only reported.
    /// </summary>
    internal void ApplyUnits(Inventory.ItemUnitChoices choices, IReadOnlyList<LookupItem> options)
    {
        if (choices.Resolved && choices.BaseUnitId is null) { UnitSetupMissing = true; return; }
        if (UnitId is null && choices.BaseUnitId is { } baseUnit) UnitId = baseUnit;
        if (!choices.Resolved) return;
        unitsKnown = true; allowedUnits = choices.UnitIds; unitSetupMissing = false;
        UnitOptions = options;
        NotifyRowState();
    }

    private bool unitSetupMissing;
    /// <summary>The server stated the item has no base unit, so the line cannot get one until the item card is completed.</summary>
    internal bool UnitSetupMissing
    {
        get => unitSetupMissing;
        set { if (unitSetupMissing != value) { unitSetupMissing = value; NotifyRowState(); } }
    }

    public static SalesInvoiceLineItem CreateBlankSlot(int displayOrder = 0) => new()
    {
        DisplayOrder = displayOrder,
        quantity = "0.0000",
        unitPrice = "0.0000",
    };

    /// <summary>
    /// Carries the header warehouse and cost centre onto a fresh entry slot. The row stays pristine: a pre-filled
    /// default is not operator input, and treating it as such turned all fifty empty buffers into incomplete lines.
    /// </summary>
    public void ApplyDocumentDefaults(Guid? warehouseId, Guid? costCenterId)
    {
        if (!IsPristineBlank) return;
        this.warehouseId = warehouseId;
        OnPropertyChanged(nameof(WarehouseId));
        this.costCenterId = costCenterId;
        OnPropertyChanged(nameof(CostCenterId));
    }

    public bool IsPersisted
    {
        get => isPersisted;
        set { if (SetProperty(ref isPersisted, value)) NotifyRowState(); }
    }

    /// <summary>
    /// An entry slot the operator left without data. Judged on the entered values alone — never on which cells were
    /// clicked or on header defaults — so blank rows are discarded instead of blocking the document, and a row
    /// cleared again is blank once more.
    /// </summary>
    public bool IsPristineBlank =>
        !IsPersisted && !ItemId.HasValue && !UnitId.HasValue &&
        string.IsNullOrWhiteSpace(Notes) && string.IsNullOrWhiteSpace(ConcreteGrade) &&
        IsBlankOrZero(Quantity) && IsBlankOrZero(UnitPrice) && IsBlankOrZero(DiscountPercentage) &&
        IsBlankOrZero(DiscountAmount) && IsBlankOrZero(TaxPercentage) && IsBlankOrZero(PumpCost) &&
        IsBlankOrZero(TransportCost);

    private static bool IsBlankOrZero(string? text) =>
        string.IsNullOrWhiteSpace(text)
        || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value == 0m;

    public bool IsComplete =>
        ItemId.HasValue && UnitId.HasValue && !HasDisallowedUnit &&
        RequiredInput.AmountText(Quantity, positive: true) &&
        RequiredInput.AmountText(UnitPrice) &&
        (RequiredInput.AmountText(DiscountPercentage) || RequiredInput.DiscountExpression(DiscountPercentage)) &&
        RequiredInput.AmountText(DiscountAmount) &&
        RequiredInput.AmountText(TaxPercentage) &&
        RequiredInput.AmountText(PumpCost) &&
        RequiredInput.AmountText(TransportCost);

    public bool IsIncomplete => !IsPristineBlank && !IsComplete;

    public DocumentRowState State =>
        IsPersisted ? DocumentRowState.Persisted :
        IsPristineBlank ? DocumentRowState.PristineBlank :
        IsComplete ? DocumentRowState.Complete :
        DocumentRowState.Incomplete;

    public string? RowErrorCode =>
        IsPristineBlank || IsComplete ? null :
        !ItemId.HasValue ? "D_LineItemRequired" :
        !UnitId.HasValue ? (UnitSetupMissing ? "D_LineUnitSetupMissing" : "D_LineUnitRequired") :
        HasDisallowedUnit ? "D_LineUnitNotAllowed" :
        !RequiredInput.AmountText(Quantity, positive: true) ? "D_LineQuantityPositive" :
        !RequiredInput.AmountText(UnitPrice) ? "D_LinePriceInvalid" :
        "D_LineDataIncomplete";

    public string? RowError => RowErrorCode switch
    {
        null => null,
        "D_LineItemRequired" => "المادة مطلوبة",
        "D_LineUnitRequired" => "الوحدة مطلوبة",
        "D_LineUnitSetupMissing" => "لا توجد وحدة أساسية معرّفة لهذه المادة. عرّف وحدات المادة من بطاقتها ثم أعد الاختيار.",
        "D_LineUnitNotAllowed" => "الوحدة المختارة غير معرّفة لهذه المادة. اختر إحدى الوحدات المعتمدة.",
        "D_LineQuantityPositive" => "الكمية يجب أن تكون أكبر من الصفر",
        "D_LinePriceInvalid" => "السعر غير صالح",
        _ => "بيانات السطر غير مكتملة",
    };

    public string Error => RowError ?? string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(ItemId) when !IsPristineBlank && !ItemId.HasValue => "المادة مطلوبة",
        nameof(UnitId) when !IsPristineBlank && !UnitId.HasValue => "الوحدة مطلوبة",
        nameof(Quantity) when !IsPristineBlank && !RequiredInput.AmountText(Quantity, positive: true) => "الكمية يجب أن تكون أكبر من الصفر",
        nameof(UnitPrice) when !IsPristineBlank && !RequiredInput.AmountText(UnitPrice) => "السعر غير صالح",
        nameof(DiscountPercentage) when !IsPristineBlank && !(RequiredInput.AmountText(DiscountPercentage) || RequiredInput.DiscountExpression(DiscountPercentage)) => "نسبة الخصم غير صالحة",
        _ => string.Empty,
    };

    private void NotifyRowState()
    {
        OnPropertyChanged(nameof(IsPristineBlank));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsIncomplete));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(RowErrorCode));
        OnPropertyChanged(nameof(RowError));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasDisallowedUnit));
    }

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public Guid? ItemId
    {
        get => itemId;
        set
        {
            if (value.HasValue
                && (quantity == "0.0000" || string.IsNullOrWhiteSpace(quantity)))
            {
                quantity = "1.0000";
                OnPropertyChanged(nameof(Quantity));
            }
            if (SetProperty(ref itemId, value))
            {
                Recalculate();
                NotifyRowState();
                NotifyChanged();
            }
        }
    }

    public string ItemCode
    {
        get => itemCode;
        set => SetProperty(ref itemCode, value);
    }

    public string ItemNameArabic
    {
        get => itemNameArabic;
        set => SetProperty(ref itemNameArabic, value);
    }

    public Guid? UnitId
    {
        get => unitId;
        set
        {
            if (SetProperty(ref unitId, value))
            {
                Recalculate();
                NotifyRowState();
                NotifyChanged();
            }
        }
    }

    public string UnitCode
    {
        get => unitCode;
        set => SetProperty(ref unitCode, value);
    }

    public string? ConcreteGrade
    {
        get => concreteGrade;
        set
        {
            if (SetProperty(ref concreteGrade, value)) { NotifyRowState(); NotifyChanged(); }
        }
    }

    public string Quantity
    {
        get => quantity;
        set
        {
            if (SetProperty(ref quantity, value))
            {
                Recalculate();
                NotifyRowState();
                NotifyChanged();
            }
        }
    }

    public string UnitPrice
    {
        get => unitPrice;
        set
        {
            var wasManual = HasManualPrice;
            if (!ApplyingQuote) HasManualPrice = true;
            if (SetProperty(ref unitPrice, value))
            {
                Recalculate();
                NotifyRowState();
                NotifyChanged();
            }
            else if (!wasManual && HasManualPrice) NotifyChanged();
        }
    }

    internal bool ApplyingQuote { get; set; }
    internal bool HasManualPrice { get; set; }
    internal int QuoteGeneration { get; set; }

    public string DiscountPercentage
    {
        get => discountPercentage;
        set
        {
            if (SetProperty(ref discountPercentage, value))
            {
                RecalculateDiscountFromPercentage();
                Recalculate();
                NotifyRowState();
                NotifyChanged();
            }
        }
    }

    public string DiscountAmount
    {
        get => discountAmount;
        set
        {
            if (SetProperty(ref discountAmount, value))
            {
                Recalculate();
                NotifyRowState();
                NotifyChanged();
            }
        }
    }

    public string TaxPercentage
    {
        get => taxPercentage;
        set
        {
            if (SetProperty(ref taxPercentage, value))
            {
                Recalculate();
                NotifyRowState();
                NotifyChanged();
            }
        }
    }

    public string TaxAmount
    {
        get => taxAmount;
        set => SetProperty(ref taxAmount, value);
    }

    public string PumpCost
    {
        get => pumpCost;
        set
        {
            if (SetProperty(ref pumpCost, value))
            {
                Recalculate();
                NotifyRowState();
                NotifyChanged();
            }
        }
    }

    public string TransportCost
    {
        get => transportCost;
        set
        {
            if (SetProperty(ref transportCost, value))
            {
                Recalculate();
                NotifyRowState();
                NotifyChanged();
            }
        }
    }

    public string LineTotal
    {
        get => lineTotal;
        set => SetProperty(ref lineTotal, value);
    }

    public Guid? CostCenterId
    {
        get => costCenterId;
        set
        {
            if (SetProperty(ref costCenterId, value)) { NotifyRowState(); NotifyChanged(); }
        }
    }

    public Guid? WarehouseId
    {
        get => warehouseId;
        set { if (SetProperty(ref warehouseId, value)) { NotifyRowState(); NotifyChanged(); } }
    }

    public string? Notes
    {
        get => notes;
        set { if (SetProperty(ref notes, value)) { NotifyRowState(); NotifyChanged(); } }
    }

    public int DisplayOrder
    {
        get => displayOrder;
        set => SetProperty(ref displayOrder, value);
    }

    /// <summary>Where the unit price came from (server price list or manual); display only.</summary>
    public string PriceSource
    {
        get => priceSource;
        set => SetProperty(ref priceSource, value);
    }

    /// <summary>The last server-quoted price; a manual price that differs is never overwritten by a new quote.</summary>
    public string? QuotedPrice { get; set; }

    /// <summary>The effective compounded discount percentage calculated from single or multi-tier expression.</summary>
    public decimal EffectiveDiscountPercentage =>
        CascadingDiscountCalculator.CalculateEffectivePercentage(discountPercentage);

    /// <summary>Effective discount percentage formatted for API/Wire transmission.</summary>
    public string EffectiveDiscountPercentageText =>
        EffectiveDiscountPercentage.ToString("0.0000", CultureInfo.InvariantCulture);

    /// <summary>Formatted summary of the effective discount rate for UI display/tooltips.</summary>
    public string EffectiveDiscountDisplay =>
        $"{EffectiveDiscountPercentage:0.00}%";

    private void RecalculateDiscountFromPercentage()
    {
        if (IsPristineBlank) return;
        var qty = ParseDecimal(Quantity);
        var price = ParseDecimal(UnitPrice);
        var subTotal = Math.Round(qty * price, 4, MidpointRounding.AwayFromZero);

        if (CascadingDiscountCalculator.TryParseTiers(discountPercentage, out var tiers) && tiers.Count > 0)
        {
            var calculatedDiscount = CascadingDiscountCalculator.CalculateTieredDiscountAmount(subTotal, discountPercentage, out _);
            discountAmount = calculatedDiscount.ToString("0.0000", CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(DiscountAmount));
            OnPropertyChanged(nameof(EffectiveDiscountPercentage));
            OnPropertyChanged(nameof(EffectiveDiscountPercentageText));
            OnPropertyChanged(nameof(EffectiveDiscountDisplay));
        }
        else if (string.IsNullOrWhiteSpace(discountPercentage) || discountPercentage == "0" || discountPercentage == "0.0000")
        {
            discountAmount = "0.0000";
            OnPropertyChanged(nameof(DiscountAmount));
            OnPropertyChanged(nameof(EffectiveDiscountPercentage));
            OnPropertyChanged(nameof(EffectiveDiscountPercentageText));
            OnPropertyChanged(nameof(EffectiveDiscountDisplay));
        }
    }

    public void Recalculate()
    {
        if (IsPristineBlank)
        {
            lineTotal = "0.0000";
            taxAmount = "0.0000";
            OnPropertyChanged(nameof(TaxAmount));
            OnPropertyChanged(nameof(LineTotal));
            return;
        }

        var qty = ParseDecimal(Quantity);
        var price = ParseDecimal(UnitPrice);

        if (!string.IsNullOrWhiteSpace(discountPercentage) && discountPercentage != "0" && discountPercentage != "0.0000")
        {
            var subTotalBefore = Math.Round(qty * price, 4, MidpointRounding.AwayFromZero);
            var tiered = CascadingDiscountCalculator.CalculateTieredDiscountAmount(subTotalBefore, discountPercentage, out _);
            discountAmount = tiered.ToString("0.0000", CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(DiscountAmount));
        }

        var discAmt = ParseDecimal(DiscountAmount);
        var taxPct = ParseDecimal(TaxPercentage);
        var pump = ParseDecimal(PumpCost);
        var transp = ParseDecimal(TransportCost);

        var subTotal = Math.Round(qty * price, 4, MidpointRounding.AwayFromZero);
        var net = subTotal - Math.Min(subTotal, Math.Round(discAmt, 4, MidpointRounding.AwayFromZero));
        if (net < 0m) net = 0m;

        var tax = Math.Round(net * (taxPct / 100m), 4, MidpointRounding.AwayFromZero);
        taxAmount = tax.ToString("0.0000", CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(TaxAmount));

        var total = net + tax + pump + transp;
        lineTotal = total.ToString("0.0000", CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(LineTotal));
    }

    private static decimal ParseDecimal(string text) =>
        decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;

    private void NotifyChanged() => Changed?.Invoke();
}
