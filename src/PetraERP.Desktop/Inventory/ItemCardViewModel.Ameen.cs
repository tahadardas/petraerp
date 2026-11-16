using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;

namespace PetraERP.Desktop.Inventory;

/// <summary>
/// One row of the Al-Ameen price page: a price type (wholesale, semi-wholesale, distributor, export, retail, consumer, last
/// purchase) with its price in each of the item's three units. Each filled cell is one server price row with no customer,
/// pattern, validity or minimum quantity.
/// </summary>
public sealed partial class ItemPriceMatrixRow(string code, string caption) : ObservableObject
{
    public string Code { get; } = code;
    public string Caption { get; } = caption;

    [ObservableProperty] private string? unit1;
    [ObservableProperty] private string? unit2;
    [ObservableProperty] private string? unit3;

    /// <summary>The price text of the unit at <paramref name="index"/> (0..2).</summary>
    public string? Cell(int index) => index switch { 0 => Unit1, 1 => Unit2, _ => Unit3 };
}

public sealed partial class ItemCardViewModel
{
    /// <summary>The list codes of the Al-Ameen price types, in the order the price page shows them.</summary>
    internal static readonly (string Code, string Caption)[] PriceTypes =
    [
        ("WHOLESALE", "الجملة"), ("SEMI_WHOLESALE", "نصف الجملة"), ("DISTRIBUTOR", "الموزع"), ("EXPORT", "التصدير"),
        ("RETAIL", "المفرق"), ("CONSUMER", "المستهلك"), ("LAST_PURCHASE", "آخر شراء"),
    ];

    private bool loadingProfile;
    private readonly string[] loadedSlotBarcodes = ["", "", ""];

    // --- Units page: unit 1 is the base unit; units 2 and 3 are item-specific conversions to it ---
    [ObservableProperty] private Guid? draftUnit2Id;
    [ObservableProperty] private string? draftUnit2Factor;
    [ObservableProperty] private bool draftUnit2Variable;
    [ObservableProperty] private Guid? draftUnit3Id;
    [ObservableProperty] private string? draftUnit3Factor;
    [ObservableProperty] private bool draftUnit3Variable;
    [ObservableProperty] private int draftDefaultUnitIndex = 1;
    [ObservableProperty] private string? draftUnit1Barcode;
    [ObservableProperty] private string? draftUnit2Barcode;
    [ObservableProperty] private string? draftUnit3Barcode;
    [ObservableProperty] private string? draftGiftQuantity;
    [ObservableProperty] private string? draftGiftPerQuantity;

    // --- General page: specification lists ---
    [ObservableProperty] private string? draftSpecifications;
    [ObservableProperty] private string? draftMeasure;
    [ObservableProperty] private string? draftSource;
    [ObservableProperty] private string? draftLocation;
    [ObservableProperty] private string? draftManufacturer;
    [ObservableProperty] private string? draftColor;
    [ObservableProperty] private string? draftOriginCountry;
    [ObservableProperty] private string? draftQuality;
    [ObservableProperty] private string? draftModel;

    // --- Options page ---
    [ObservableProperty] private bool draftIsAssembly;
    [ObservableProperty] private bool draftComponentPricing;
    [ObservableProperty] private bool draftTracksSerial;
    [ObservableProperty] private bool draftEnforceSerialOnReceipt;
    [ObservableProperty] private bool draftEnforceSerialOnIssue;
    [ObservableProperty] private bool draftTracksProductionDate;
    [ObservableProperty] private bool draftTracksExpiryDate;
    [ObservableProperty] private bool draftEnforceDatesOnReceipt;
    [ObservableProperty] private bool draftEnforceDatesOnIssue;
    [ObservableProperty] private bool draftHideInSearch;
    [ObservableProperty] private bool draftIntegerQuantity;
    [ObservableProperty] private string? draftImagePath;

    // --- Prices page ---
    [ObservableProperty] private string draftPriceCurrency = string.Empty;
    [ObservableProperty] private string? draftLastPurchaseRate;

    public ObservableCollection<ItemPriceMatrixRow> PriceMatrix { get; } = [.. PriceTypes.Select(t => new ItemPriceMatrixRow(t.Code, t.Caption))];

    public bool HasUnit2 => DraftUnit2Id is not null;
    public bool HasUnit3 => DraftUnit3Id is not null;
    public bool CanEditUnit2 => IsEditing && HasUnit2;
    public bool CanEditUnit3 => IsEditing && HasUnit3;
    public bool CanEditSerialEnforcement => IsEditing && DraftTracksSerial;
    public bool CanEditDateEnforcement => IsEditing && (DraftTracksProductionDate || DraftTracksExpiryDate);
    public bool CanEditComponentPricing => IsEditing && DraftIsAssembly;

    /// <summary>The matrix edits server price rows: drafts while creating, immediate row changes for a saved item.</summary>
    public bool CanEditPriceMatrix => IsEditing && (IsNewMode || Pricing is not null);

    public bool IsUnit1Default { get => DraftDefaultUnitIndex == 1; set { if (value) DraftDefaultUnitIndex = 1; } }
    public bool IsUnit2Default { get => DraftDefaultUnitIndex == 2; set { if (value) DraftDefaultUnitIndex = 2; } }
    public bool IsUnit3Default { get => DraftDefaultUnitIndex == 3; set { if (value) DraftDefaultUnitIndex = 3; } }

    /// <summary>"الكمية الحالية": the item's total stock in its base unit (read-only).</summary>
    public string CurrentQuantityDisplay => TotalStockDisplay;

    partial void OnDraftUnit2IdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(HasUnit2)); OnPropertyChanged(nameof(CanEditUnit2));
        if (value is null && !loadingProfile)
        {
            DraftUnit2Factor = null; DraftUnit2Variable = false; DraftUnit2Barcode = null;
            if (DraftUnit3Id is null && DraftDefaultUnitIndex == 2) DraftDefaultUnitIndex = 1;
        }
    }

    partial void OnDraftUnit3IdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(HasUnit3)); OnPropertyChanged(nameof(CanEditUnit3));
        if (value is null && !loadingProfile)
        {
            DraftUnit3Factor = null; DraftUnit3Variable = false; DraftUnit3Barcode = null;
            if (DraftDefaultUnitIndex == 3) DraftDefaultUnitIndex = 1;
        }
    }

    partial void OnDraftDefaultUnitIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsUnit1Default)); OnPropertyChanged(nameof(IsUnit2Default)); OnPropertyChanged(nameof(IsUnit3Default));
    }

    partial void OnDraftTracksSerialChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditSerialEnforcement));
        if (!value && !loadingProfile) { DraftEnforceSerialOnReceipt = false; DraftEnforceSerialOnIssue = false; }
    }

    partial void OnDraftTracksProductionDateChanged(bool value) => DatesChanged();
    partial void OnDraftTracksExpiryDateChanged(bool value) => DatesChanged();

    private void DatesChanged()
    {
        OnPropertyChanged(nameof(CanEditDateEnforcement));
        if (!DraftTracksProductionDate && !DraftTracksExpiryDate && !loadingProfile) { DraftEnforceDatesOnReceipt = false; DraftEnforceDatesOnIssue = false; }
    }

    partial void OnDraftIsAssemblyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditComponentPricing));
        if (!value && !loadingProfile) DraftComponentPricing = false;
    }

    /// <summary>Every profile draft member raises the card's draft-changed notification (dirty tracking, save enablement).</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!loadingProfile && e.PropertyName is { } name && ProfileDraftNames.Contains(name)) DraftChanged();
        if (e.PropertyName is nameof(Mode) or nameof(IsEditing))
        {
            OnPropertyChanged(nameof(CanEditUnit2)); OnPropertyChanged(nameof(CanEditUnit3));
            OnPropertyChanged(nameof(CanEditSerialEnforcement)); OnPropertyChanged(nameof(CanEditDateEnforcement));
            OnPropertyChanged(nameof(CanEditComponentPricing)); OnPropertyChanged(nameof(CanEditPriceMatrix));
        }
    }

    private static readonly HashSet<string> ProfileDraftNames =
    [
        nameof(DraftUnit2Id), nameof(DraftUnit2Factor), nameof(DraftUnit2Variable), nameof(DraftUnit3Id), nameof(DraftUnit3Factor),
        nameof(DraftUnit3Variable), nameof(DraftDefaultUnitIndex), nameof(DraftUnit1Barcode), nameof(DraftUnit2Barcode),
        nameof(DraftUnit3Barcode), nameof(DraftGiftQuantity), nameof(DraftGiftPerQuantity), nameof(DraftSpecifications),
        nameof(DraftMeasure), nameof(DraftSource), nameof(DraftLocation), nameof(DraftManufacturer), nameof(DraftColor),
        nameof(DraftOriginCountry), nameof(DraftQuality), nameof(DraftModel), nameof(DraftIsAssembly), nameof(DraftComponentPricing),
        nameof(DraftTracksSerial), nameof(DraftEnforceSerialOnReceipt), nameof(DraftEnforceSerialOnIssue),
        nameof(DraftTracksProductionDate), nameof(DraftTracksExpiryDate), nameof(DraftEnforceDatesOnReceipt),
        nameof(DraftEnforceDatesOnIssue), nameof(DraftHideInSearch), nameof(DraftIntegerQuantity), nameof(DraftImagePath),
        nameof(DraftPriceCurrency), nameof(DraftLastPurchaseRate),
    ];

    // ------------------------------------------------------------------ load

    /// <summary>Fills every Al-Ameen draft from the server profile (or the defaults when creating).</summary>
    private void LoadProfileDraft(ItemProfileDto? profile)
    {
        loadingProfile = true;
        try
        {
            DraftUnit2Id = profile?.Unit2Id;
            DraftUnit2Factor = profile?.Unit2Factor;
            DraftUnit2Variable = profile?.Unit2FactorVariable ?? false;
            DraftUnit3Id = profile?.Unit3Id;
            DraftUnit3Factor = profile?.Unit3Factor;
            DraftUnit3Variable = profile?.Unit3FactorVariable ?? false;
            DraftDefaultUnitIndex = profile?.DefaultUnitIndex ?? 1;
            DraftGiftQuantity = profile?.GiftQuantity;
            DraftGiftPerQuantity = profile?.GiftPerQuantity;
            DraftSpecifications = profile?.Specifications;
            DraftMeasure = profile?.Measure;
            DraftSource = profile?.Source;
            DraftLocation = profile?.Location;
            DraftManufacturer = profile?.Manufacturer;
            DraftColor = profile?.Color;
            DraftOriginCountry = profile?.OriginCountry;
            DraftQuality = profile?.Quality;
            DraftModel = profile?.Model;
            DraftIsAssembly = profile?.IsAssembly ?? false;
            DraftComponentPricing = profile?.ComponentPricing ?? false;
            DraftTracksSerial = profile?.TracksSerialNumber ?? false;
            DraftEnforceSerialOnReceipt = profile?.EnforceSerialOnReceipt ?? false;
            DraftEnforceSerialOnIssue = profile?.EnforceSerialOnIssue ?? false;
            DraftTracksProductionDate = profile?.TracksProductionDate ?? false;
            DraftTracksExpiryDate = profile?.TracksExpiryDate ?? false;
            DraftEnforceDatesOnReceipt = profile?.EnforceDatesOnReceipt ?? false;
            DraftEnforceDatesOnIssue = profile?.EnforceDatesOnIssue ?? false;
            DraftHideInSearch = profile?.HideInSearch ?? false;
            DraftIntegerQuantity = profile?.IntegerQuantityOnInvoice ?? false;
            DraftImagePath = profile?.ImagePath;
            DraftLastPurchaseRate = profile?.LastPurchaseRate;
            LoadSlotBarcodes();
            LoadPriceMatrix();
        }
        finally
        {
            loadingProfile = false;
        }

        OnPropertyChanged(nameof(CurrentQuantityDisplay));
        OnPropertyChanged(nameof(CanEditPriceMatrix));
    }

    private Guid?[] UnitSlots() => [DraftBaseUnitId, DraftUnit2Id, DraftUnit3Id];

    /// <summary>The barcode fields of the Units page show the default (else first) barcode of each unit slot.</summary>
    private void LoadSlotBarcodes()
    {
        var units = UnitSlots();
        var texts = new string?[3];
        for (var i = 0; i < 3; i++)
        {
            if (units[i] is not { } unitId) { loadedSlotBarcodes[i] = string.Empty; continue; }
            var forUnit = Barcodes.Where(b => b.UnitId == unitId).ToList();
            var chosen = forUnit.FirstOrDefault(b => b.IsDefault == true) ?? forUnit.FirstOrDefault();
            texts[i] = chosen?.Barcode;
            loadedSlotBarcodes[i] = chosen?.Barcode ?? string.Empty;
        }

        DraftUnit1Barcode = texts[0];
        DraftUnit2Barcode = texts[1];
        DraftUnit3Barcode = texts[2];
    }

    private static bool IsMatrixPrice(ItemPriceDto price, string code) =>
        string.Equals(price.PriceListCode, code, StringComparison.OrdinalIgnoreCase)
        && price.PartyId is null && price.PatternId is null && price.ValidFrom is null && price.ValidTo is null
        && decimal.TryParse(price.MinQuantity, NumberStyles.Number, CultureInfo.InvariantCulture, out var minimum) && minimum == 0m;

    private string? MatrixCell(string code, Guid? unitId) =>
        unitId is null ? null : Prices.FirstOrDefault(p => p.UnitId == unitId && IsMatrixPrice(p, code))?.Price;

    private void LoadPriceMatrix()
    {
        var units = UnitSlots();
        foreach (var row in PriceMatrix)
        {
            row.PropertyChanged -= PriceMatrixChanged;
            row.Unit1 = MatrixCell(row.Code, units[0]);
            row.Unit2 = MatrixCell(row.Code, units[1]);
            row.Unit3 = MatrixCell(row.Code, units[2]);
            row.PropertyChanged += PriceMatrixChanged;
        }

        var matrixCurrency = Prices.FirstOrDefault(p => PriceMatrix.Any(r => IsMatrixPrice(p, r.Code)))?.CurrencyCode;
        DraftPriceCurrency = !string.IsNullOrWhiteSpace(matrixCurrency) ? matrixCurrency
            : !string.IsNullOrWhiteSpace(NewPriceCurrency) ? NewPriceCurrency : Context?.BaseCurrencyCode ?? string.Empty;
    }

    private void PriceMatrixChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!loadingProfile) DraftChanged();
    }

    // ------------------------------------------------------------------ validation and dirty tracking

    private bool ProfileHasChanges()
    {
        var saved = Item?.Profile;
        static string N(string? value) => value?.Trim() ?? string.Empty;
        static string D(string? value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number.ToString("0.########", CultureInfo.InvariantCulture) : N(value);
        return DraftUnit2Id != saved?.Unit2Id || DraftUnit3Id != saved?.Unit3Id
            || D(DraftUnit2Factor) != D(saved?.Unit2Factor) || D(DraftUnit3Factor) != D(saved?.Unit3Factor)
            || DraftUnit2Variable != (saved?.Unit2FactorVariable ?? false) || DraftUnit3Variable != (saved?.Unit3FactorVariable ?? false)
            || DraftDefaultUnitIndex != (saved?.DefaultUnitIndex ?? 1)
            || D(DraftGiftQuantity) != D(saved?.GiftQuantity) || D(DraftGiftPerQuantity) != D(saved?.GiftPerQuantity)
            || N(DraftSpecifications) != N(saved?.Specifications) || N(DraftMeasure) != N(saved?.Measure) || N(DraftSource) != N(saved?.Source)
            || N(DraftLocation) != N(saved?.Location) || N(DraftManufacturer) != N(saved?.Manufacturer) || N(DraftColor) != N(saved?.Color)
            || N(DraftOriginCountry) != N(saved?.OriginCountry) || N(DraftQuality) != N(saved?.Quality) || N(DraftModel) != N(saved?.Model)
            || DraftIsAssembly != (saved?.IsAssembly ?? false) || DraftComponentPricing != (saved?.ComponentPricing ?? false)
            || DraftTracksSerial != (saved?.TracksSerialNumber ?? false) || DraftEnforceSerialOnReceipt != (saved?.EnforceSerialOnReceipt ?? false)
            || DraftEnforceSerialOnIssue != (saved?.EnforceSerialOnIssue ?? false) || DraftTracksProductionDate != (saved?.TracksProductionDate ?? false)
            || DraftTracksExpiryDate != (saved?.TracksExpiryDate ?? false) || DraftEnforceDatesOnReceipt != (saved?.EnforceDatesOnReceipt ?? false)
            || DraftEnforceDatesOnIssue != (saved?.EnforceDatesOnIssue ?? false) || DraftHideInSearch != (saved?.HideInSearch ?? false)
            || DraftIntegerQuantity != (saved?.IntegerQuantityOnInvoice ?? false) || N(DraftImagePath) != N(saved?.ImagePath)
            || D(DraftLastPurchaseRate) != D(saved?.LastPurchaseRate)
            || SlotBarcodesChanged() || MatrixChanged();
    }

    private bool SlotBarcodesChanged()
    {
        var texts = new[] { DraftUnit1Barcode, DraftUnit2Barcode, DraftUnit3Barcode };
        for (var i = 0; i < 3; i++)
            if (!string.Equals(texts[i]?.Trim() ?? string.Empty, loadedSlotBarcodes[i], StringComparison.Ordinal)) return true;
        return false;
    }

    private bool MatrixChanged()
    {
        var units = UnitSlots();
        foreach (var row in PriceMatrix)
            for (var i = 0; i < 3; i++)
            {
                var saved = MatrixCell(row.Code, units[i]);
                if (!SameAmount(saved, units[i] is null ? null : row.Cell(i))) return true;
            }

        var matrixCurrency = Prices.FirstOrDefault(p => PriceMatrix.Any(r => IsMatrixPrice(p, r.Code)))?.CurrencyCode;
        return matrixCurrency is not null && !string.Equals(matrixCurrency, DraftPriceCurrency.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameAmount(string? left, string? right)
    {
        var a = string.IsNullOrWhiteSpace(left) ? null : left.Trim();
        var b = string.IsNullOrWhiteSpace(right) ? null : right.Trim();
        if (a is null || b is null) return a is null && b is null;
        return decimal.TryParse(a, NumberStyles.Number, CultureInfo.InvariantCulture, out var x)
            && decimal.TryParse(b, NumberStyles.Number, CultureInfo.InvariantCulture, out var y) ? x == y : string.Equals(a, b, StringComparison.Ordinal);
    }

    private IEnumerable<string> ProfileValidationErrors()
    {
        if (DraftUnit2Id is { } unit2)
        {
            if (unit2 == DraftBaseUnitId) yield return "الوحدة 2: يجب أن تختلف عن الوحدة 1";
            if (!RequiredInput.AmountText(DraftUnit2Factor, 8, true)) yield return "الوحدة 2: عامل التحويل مطلوب وأكبر من صفر";
        }

        if (DraftUnit3Id is { } unit3)
        {
            if (unit3 == DraftBaseUnitId || unit3 == DraftUnit2Id) yield return "الوحدة 3: يجب أن تختلف عن الوحدتين 1 و2";
            if (DraftUnit2Id is null) yield return "الوحدة 3: تتطلب تحديد الوحدة 2 أولاً";
            if (!RequiredInput.AmountText(DraftUnit3Factor, 8, true)) yield return "الوحدة 3: عامل التحويل مطلوب وأكبر من صفر";
        }

        if (!string.IsNullOrWhiteSpace(DraftGiftQuantity) && !RequiredInput.AmountText(DraftGiftQuantity)) yield return "هدية: قيمة غير صحيحة";
        if (!string.IsNullOrWhiteSpace(DraftGiftPerQuantity) && !RequiredInput.AmountText(DraftGiftPerQuantity)) yield return "لكل: قيمة غير صحيحة";
        if (!string.IsNullOrWhiteSpace(DraftGiftQuantity) && !(decimal.TryParse(DraftGiftPerQuantity, NumberStyles.Number, CultureInfo.InvariantCulture, out var per) && per > 0)
            && decimal.TryParse(DraftGiftQuantity, NumberStyles.Number, CultureInfo.InvariantCulture, out var gift) && gift > 0)
            yield return "هدية: حدّد الكمية التي تُمنح عنها (لكل)";
        if (!string.IsNullOrWhiteSpace(DraftLastPurchaseRate) && !RequiredInput.AmountText(DraftLastPurchaseRate, 8, true)) yield return "تعادل آخر شراء: قيمة غير صحيحة";

        var anyPrice = false;
        foreach (var row in PriceMatrix)
            for (var i = 0; i < 3; i++)
            {
                var text = row.Cell(i);
                if (string.IsNullOrWhiteSpace(text)) continue;
                anyPrice = true;
                if (!RequiredInput.AmountText(text)) yield return $"{row.Caption}: سعر غير صحيح";
                else if (UnitSlots()[i] is null) yield return $"{row.Caption}: لا توجد وحدة {i + 1} لهذا السعر";
            }

        if (anyPrice && !RequiredInput.Currency(DraftPriceCurrency)) yield return "العملة: رمز عملة من 3 أحرف مطلوب لتسجيل الأسعار";
    }

    // ------------------------------------------------------------------ save

    /// <summary>Moves the slot barcode fields into the barcode collection so create and update send one consistent list.</summary>
    private void ApplySlotBarcodes()
    {
        var texts = new[] { DraftUnit1Barcode, DraftUnit2Barcode, DraftUnit3Barcode };
        var units = UnitSlots();
        for (var i = 0; i < 3; i++)
        {
            var text = texts[i]?.Trim() ?? string.Empty;
            if (!string.Equals(text, loadedSlotBarcodes[i], StringComparison.Ordinal))
            {
                if (loadedSlotBarcodes[i].Length > 0 && Barcodes.FirstOrDefault(b => string.Equals(b.Barcode, loadedSlotBarcodes[i], StringComparison.Ordinal)) is { } previous)
                    Barcodes.Remove(previous);
                if (text.Length > 0 && units[i] is { } unitId)
                {
                    var unit = UnitLookups.FirstOrDefault(u => u.Id == unitId);
                    Barcodes.Add(new ItemBarcodeDto { Id = Guid.NewGuid(), Barcode = text, UnitId = unitId, UnitCode = unit?.Code, IsDefault = false });
                }

                loadedSlotBarcodes[i] = text;
            }
        }

        // One default barcode per item: the one of the default unit when it has a slot barcode.
        var defaultUnit = UnitSlots()[Math.Clamp(DraftDefaultUnitIndex, 1, 3) - 1];
        var defaultText = new[] { DraftUnit1Barcode, DraftUnit2Barcode, DraftUnit3Barcode }[Math.Clamp(DraftDefaultUnitIndex, 1, 3) - 1]?.Trim();
        var current = Barcodes.FirstOrDefault(b => b.IsDefault == true);
        if (!string.IsNullOrEmpty(defaultText) && defaultUnit is not null)
        {
            foreach (var barcode in Barcodes) barcode.IsDefault = string.Equals(barcode.Barcode, defaultText, StringComparison.Ordinal);
        }
        else if (current is null && Barcodes.Count > 0 && Barcodes.All(b => b.IsDefault != true))
        {
            Barcodes[0].IsDefault = true;
        }
    }

    private List<CreateItemBarcodeRequest> BarcodeRequests() => Barcodes.Select(b => new CreateItemBarcodeRequest
    {
        Barcode = b.Barcode ?? string.Empty, UnitId = b.UnitId, IsDefault = b.IsDefault,
    }).ToList();

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private ItemProfileDto BuildProfile() => new()
    {
        Unit2Id = DraftUnit2Id,
        Unit2Factor = DraftUnit2Id is null ? null : Text(DraftUnit2Factor),
        Unit2FactorVariable = DraftUnit2Id is not null && DraftUnit2Variable,
        Unit3Id = DraftUnit3Id,
        Unit3Factor = DraftUnit3Id is null ? null : Text(DraftUnit3Factor),
        Unit3FactorVariable = DraftUnit3Id is not null && DraftUnit3Variable,
        DefaultUnitIndex = DraftDefaultUnitIndex,
        GiftQuantity = Text(DraftGiftQuantity),
        GiftPerQuantity = Text(DraftGiftPerQuantity),
        Specifications = Text(DraftSpecifications),
        Measure = Text(DraftMeasure),
        Source = Text(DraftSource),
        Location = Text(DraftLocation),
        Manufacturer = Text(DraftManufacturer),
        Color = Text(DraftColor),
        OriginCountry = Text(DraftOriginCountry),
        Quality = Text(DraftQuality),
        Model = Text(DraftModel),
        ItemKind = 1,
        IsAssembly = DraftIsAssembly,
        ComponentPricing = DraftIsAssembly && DraftComponentPricing,
        TracksSerialNumber = DraftTracksSerial,
        EnforceSerialOnReceipt = DraftTracksSerial && DraftEnforceSerialOnReceipt,
        EnforceSerialOnIssue = DraftTracksSerial && DraftEnforceSerialOnIssue,
        TracksProductionDate = DraftTracksProductionDate,
        TracksExpiryDate = DraftTracksExpiryDate,
        EnforceDatesOnReceipt = (DraftTracksProductionDate || DraftTracksExpiryDate) && DraftEnforceDatesOnReceipt,
        EnforceDatesOnIssue = (DraftTracksProductionDate || DraftTracksExpiryDate) && DraftEnforceDatesOnIssue,
        HideInSearch = DraftHideInSearch,
        IntegerQuantityOnInvoice = DraftIntegerQuantity,
        ImagePath = Text(DraftImagePath),
        PricingPolicy = 1,
        LastPurchaseRate = Text(DraftLastPurchaseRate),
    };

    private string MatrixCurrency => DraftPriceCurrency.Trim().ToUpperInvariant();

    private CreateItemPriceRequest MatrixPrice(string code, Guid unitId, string amount) => new()
    {
        PriceListCode = code, UnitId = unitId, Price = amount.Trim(), CurrencyCode = MatrixCurrency, MinQuantity = "0.0000",
    };

    /// <summary>New item: matrix prices travel with the create request next to any detailed (customer / pattern / dated) draft rows.</summary>
    private List<CreateItemPriceRequest> CreatePriceRequests()
    {
        var requests = Prices.Where(p => !PriceMatrix.Any(r => IsMatrixPrice(p, r.Code))).Select(ToPriceRequest).ToList();
        var units = UnitSlots();
        foreach (var row in PriceMatrix)
            for (var i = 0; i < 3; i++)
                if (units[i] is { } unitId && !string.IsNullOrWhiteSpace(row.Cell(i))) requests.Add(MatrixPrice(row.Code, unitId, row.Cell(i)!));
        return requests;
    }

    /// <summary>Saved item: each changed matrix cell replaces its server price row (delete then add), unchanged cells are untouched.</summary>
    private async Task SyncMatrixPricesAsync(Guid itemId, CancellationToken token)
    {
        if (Pricing is null) return;
        var units = UnitSlots();
        var currency = MatrixCurrency;
        var current = Prices.ToList();
        foreach (var row in PriceMatrix)
            for (var i = 0; i < 3; i++)
            {
                var unitId = units[i];
                var existing = unitId is null ? null : current.FirstOrDefault(p => p.UnitId == unitId && IsMatrixPrice(p, row.Code));
                var text = unitId is null ? null : row.Cell(i)?.Trim();
                if (existing is not null && !string.IsNullOrEmpty(text) && SameAmount(existing.Price, text)
                    && string.Equals(existing.CurrencyCode, currency, StringComparison.OrdinalIgnoreCase)) continue;
                if (existing is null && string.IsNullOrEmpty(text)) continue;
                if (existing?.Id is { } oldId) await Pricing.DeletePriceAsync(CompanyId, BranchId, itemId, oldId, token).ConfigureAwait(true);
                if (!string.IsNullOrEmpty(text) && unitId is { } unit)
                    await Pricing.AddPriceAsync(CompanyId, BranchId, itemId, MatrixPrice(row.Code, unit, text), token).ConfigureAwait(true);
            }
    }

    private void ResetProfileDraft()
    {
        LoadProfileDraft(null);
        foreach (var row in PriceMatrix)
        {
            row.PropertyChanged -= PriceMatrixChanged;
            row.Unit1 = null; row.Unit2 = null; row.Unit3 = null;
            row.PropertyChanged += PriceMatrixChanged;
        }
        CompositeComponents.Clear();
    }

    public ObservableCollection<CompositeItemComponentRow> CompositeComponents { get; } = [];

    /// <summary>Active items the operator can choose as a component; the card never asks for a raw identifier.</summary>
    public ObservableCollection<LookupItem> ComponentLookups { get; } = [];

    [ObservableProperty] private Guid? newComponentItemId;
    [ObservableProperty] private string newComponentQuantity = "1.00";
    [ObservableProperty] private string newComponentUnit = "قطعة";
    [ObservableProperty] private bool newComponentIsFlexible;
    [ObservableProperty] private bool newComponentIsMandatory;
    [ObservableProperty] private bool newComponentPrintInInvoice = true;

    public RelayCommand AddCompositeComponentCommand => new(AddCompositeComponent, () => IsEditing && NewComponentItemId is not null);
    public RelayCommand<CompositeItemComponentRow> RemoveCompositeComponentCommand => new(RemoveCompositeComponent, _ => IsEditing);

    private void AddCompositeComponent()
    {
        if (NewComponentItemId is not { } id || id == ItemId) return;
        decimal.TryParse(NewComponentQuantity, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty);
        if (qty <= 0) qty = 1.00m;
        var row = new CompositeItemComponentRow
        {
            LineNumber = CompositeComponents.Count + 1,
            ComponentItemId = id,
            ItemDisplay = ComponentLookups.FirstOrDefault(l => l.Id == id) is { } picked ? picked.Code + " - " + picked.NameArabic : id.ToString(),
            Quantity = qty,
            UnitName = string.IsNullOrWhiteSpace(NewComponentUnit) ? "قطعة" : NewComponentUnit,
            IsFlexible = NewComponentIsFlexible,
            IsMandatory = NewComponentIsMandatory,
            PrintInInvoice = NewComponentPrintInInvoice
        };
        CompositeComponents.Add(row);
        DraftChanged();
    }

    private void RemoveCompositeComponent(CompositeItemComponentRow? row)
    {
        if (row is null) return;
        CompositeComponents.Remove(row);
        for (int i = 0; i < CompositeComponents.Count; i++) CompositeComponents[i].LineNumber = i + 1;
        DraftChanged();
    }
}

public sealed partial class CompositeItemComponentRow : ObservableObject
{
    [ObservableProperty] private int lineNumber;
    [ObservableProperty] private Guid? componentItemId;
    [ObservableProperty] private string itemCode = string.Empty;
    [ObservableProperty] private string itemName = string.Empty;
    [ObservableProperty] private string itemDisplay = string.Empty;
    [ObservableProperty] private decimal quantity = 1.00m;
    [ObservableProperty] private string unitName = "قطعة";
    [ObservableProperty] private bool isFlexible;
    [ObservableProperty] private bool isMandatory;
    [ObservableProperty] private bool printInInvoice = true;

    public string FlexibleDisplay => IsFlexible ? "مرنة" : "غير مرنة";
    public string MandatoryDisplay => IsMandatory ? "إجبارية" : "غير إجبارية";
    public string PrintDisplay => PrintInInvoice ? "نعم" : "لا";
}
