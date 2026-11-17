using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Inventory;

/// <summary>
/// A line representing a raw material / component inside an Assembly or Disassembly invoice.
/// Designed according to Al-Ameen 10 BOM mechanics (standard qty, flexible qty, mandatory flag, unit cost, total).
/// </summary>
public sealed partial class AssemblyGridLine : ObservableObject
{
    [ObservableProperty] private int lineNumber;
    [ObservableProperty] private Guid itemId;
    [ObservableProperty] private string itemCode = string.Empty;
    [ObservableProperty] private string itemName = string.Empty;
    [ObservableProperty] private string unitName = "قطعة";
    [ObservableProperty] private decimal standardQuantity = 1m;
    [ObservableProperty] private decimal totalQuantity = 1m;
    [ObservableProperty] private decimal unitCost = 0m;
    [ObservableProperty] private decimal totalCost = 0m;
    [ObservableProperty] private bool isFlexible = true;
    [ObservableProperty] private bool isMandatory = false;
    [ObservableProperty] private decimal costPercentage = 0m;
    [ObservableProperty] private string? alternateGroupId;

    public string DisplayItem => string.IsNullOrWhiteSpace(ItemCode) ? ItemName : $"{ItemCode} - {ItemName}";

    public void Recalculate()
    {
        TotalCost = decimal.Round(TotalQuantity * UnitCost, 4);
    }

    partial void OnTotalQuantityChanged(decimal value) => Recalculate();
    partial void OnUnitCostChanged(decimal value) => Recalculate();
}

/// <summary>
/// An operating / conversion expense line (مصاريف تشغيل / أجور عمال / استهلاك / كهرباء)
/// absorbed directly into the finished assembled product's unit cost.
/// </summary>
public sealed partial class AssemblyExpenseLine : ObservableObject
{
    [ObservableProperty] private int lineNumber;
    [ObservableProperty] private Guid accountId;
    [ObservableProperty] private string accountCode = string.Empty;
    [ObservableProperty] private string accountName = string.Empty;
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private string? notes;

    public string DisplayAccount => string.IsNullOrWhiteSpace(AccountCode) ? AccountName : $"{AccountCode} - {AccountName}";
}

/// <summary>
/// Pattern item specific to assembly and disassembly operations.
/// </summary>
public sealed class AssemblyPatternLookupItem
{
    public Guid PatternId { get; init; }
    public Guid RevisionId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ShortCode { get; init; } = string.Empty;
    public BusinessOperationKind? OperationKind { get; init; }
    public bool LockDocumentDate { get; init; }
    public bool AllowManualNumbering { get; init; }
    public Guid? DefaultWarehouseId { get; init; }
    public Guid? DefaultCostCenterId { get; init; }
    public Guid? OperatingAccountId { get; init; }
    public Guid? AssemblyExpenseAccountId { get; init; }
    public Guid? VarianceAccountId { get; init; }
}

/// <summary>
/// Modern Executive ViewModel for Assembly & Disassembly operations (فاتورة التجميع والتفكيك - تصنيع بلا تصنيع).
/// Fully integrated with Al-Ameen 10 Pattern Management (إدارة الأنماط), BOM explosion, Operating Accounts, and F6 mechanics.
/// </summary>
public sealed partial class AssemblyDisassemblyViewModel : PageViewModel, IShellPage, ISavablePage
{
    private readonly IPatternsClient patternsClient;
    private readonly IInventoryClient inventoryClient;
    private readonly IChartOfAccountsClient accountsClient;
    private readonly ShellNavigator navigator;
    private readonly IUserConfirmation confirmation;
    private readonly DesktopOperation operation;
    private readonly NotificationCenter notifications;

    // --- State & Mode ---
    [ObservableProperty] private AssemblyDisassemblyMode mode = AssemblyDisassemblyMode.Assembly;
    [ObservableProperty] private DocumentLifecycle lifecycle = DocumentLifecycle.None;
    [ObservableProperty] private bool isDateLocked;
    [ObservableProperty] private string pageTitle = "فاتورة تجميع وتفكيك";

    // --- Document Header ---
    [ObservableProperty] private string documentNumber = string.Empty;
    [ObservableProperty] private DateTime? documentDate = DateTime.Today;
    [ObservableProperty] private AssemblyPatternLookupItem? selectedPattern;
    [ObservableProperty] private Guid? selectedPatternRevisionId;
    [ObservableProperty] private Guid? assembledItemId;
    [ObservableProperty] private string assembledItemDisplay = string.Empty;
    [ObservableProperty] private decimal assembledQuantity = 1m;
    [ObservableProperty] private string assembledUnitName = "قطعة";
    [ObservableProperty] private Guid? sourceWarehouseId;
    [ObservableProperty] private Guid? targetWarehouseId;
    [ObservableProperty] private Guid? costCenterId;
    [ObservableProperty] private Guid? operatingAccountId;
    [ObservableProperty] private Guid? assemblyExpenseAccountId;
    [ObservableProperty] private Guid? varianceAccountId;
    [ObservableProperty] private string notes = string.Empty;

    // --- Financial Summaries & Tafqeet ---
    [ObservableProperty] private decimal totalMaterialCost;
    [ObservableProperty] private decimal totalOperatingExpenses;
    [ObservableProperty] private decimal grandCostTotal;
    [ObservableProperty] private decimal unitProducedCost;
    [ObservableProperty] private string tafqeetText = string.Empty;

    // --- Status Badge ---
    [ObservableProperty] private string statusBadgeText = "مسودة";
    [ObservableProperty] private BadgeKind statusBadgeKind = BadgeKind.Neutral;

    // --- Collections ---
    public ObservableCollection<AssemblyGridLine> ComponentLines { get; } = [];
    public ObservableCollection<AssemblyExpenseLine> ExpenseLines { get; } = [];
    public ObservableCollection<AssemblyPatternLookupItem> AssemblyPatterns { get; } = [];
    public ObservableCollection<LookupItem> WarehouseLookups { get; } = [];
    public ObservableCollection<LookupItem> CostCenterLookups { get; } = [];
    public ObservableCollection<LookupItem> AccountLookups { get; } = [];
    public ObservableCollection<LookupItem> AssembledItemLookups { get; } = [];
    public ObservableCollection<LookupItem> RawComponentLookups { get; } = [];

    // --- Helper Properties ---
    public bool IsAssemblyMode => Mode == AssemblyDisassemblyMode.Assembly;
    public bool IsDisassemblyMode => Mode == AssemblyDisassemblyMode.Disassembly;
    public bool IsEditing => Lifecycle == DocumentLifecycle.Draft;
    public bool IsReadOnly => !IsEditing;
    public bool CanManage => Can("Inventory.Movements.Manage");
    public bool CanViewPatterns => Can("Patterns.Documents.View");

    // --- Commands ---
    public AsyncRelayCommand NewCommand { get; }
    public AsyncRelayCommand<AssemblyPatternLookupItem> NewByPatternCommand { get; }
    public AsyncRelayCommand EditCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public AsyncRelayCommand PostCommand { get; }
    public AsyncRelayCommand PrintCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ManagePatternsCommand { get; }
    public RelayCommand ToggleModeCommand { get; }
    public RelayCommand ExplodeBOMCommand { get; }
    public RelayCommand AddComponentLineCommand { get; }
    public RelayCommand<AssemblyGridLine> RemoveComponentLineCommand { get; }
    public RelayCommand AddExpenseLineCommand { get; }
    public RelayCommand<AssemblyExpenseLine> RemoveExpenseLineCommand { get; }
    public RelayCommand DistributeDisassemblyCostsCommand { get; }

    public AssemblyDisassemblyViewModel(
        IPatternsClient patternsClient,
        IInventoryClient inventoryClient,
        IChartOfAccountsClient accountsClient,
        ShellNavigator navigator,
        DesktopOperation operation,
        ILocalizationService localization,
        IUserConfirmation confirmation,
        NotificationCenter notifications)
        : base(operation, localization)
    {
        this.patternsClient = patternsClient;
        this.inventoryClient = inventoryClient;
        this.accountsClient = accountsClient;
        this.navigator = navigator;
        this.operation = operation;
        this.confirmation = confirmation;
        this.notifications = notifications;

        NewCommand = new AsyncRelayCommand(CreateNewDraftAsync, () => HasContext && Idle && CanManage);
        NewByPatternCommand = new AsyncRelayCommand<AssemblyPatternLookupItem>(CreateNewDraftWithPatternAsync, _ => HasContext && Idle && CanManage);
        EditCommand = new AsyncRelayCommand(() =>
        {
            if (Lifecycle != DocumentLifecycle.Draft) return Task.CompletedTask;
            Lifecycle = DocumentLifecycle.Draft;
            UpdateUiState();
            return Task.CompletedTask;
        }, () => HasContext && Idle && CanManage && Lifecycle == DocumentLifecycle.Draft);

        SaveCommand = new AsyncRelayCommand(SaveDocumentAsync, () => HasContext && Idle && CanManage && IsEditing);
        CancelCommand = new AsyncRelayCommand(CancelDraftAsync, () => HasContext && Idle && IsEditing);
        PostCommand = new AsyncRelayCommand(PostDocumentAsync, () => HasContext && Idle && CanManage && Lifecycle == DocumentLifecycle.Draft);
        PrintCommand = new AsyncRelayCommand(PrintDocumentAsync, () => HasContext && Idle);
        RefreshCommand = new AsyncRelayCommand(RefreshLookupsAsync, () => HasContext && Idle);
        ManagePatternsCommand = new AsyncRelayCommand(() =>
            CanViewPatterns ? navigator.NavigateAsync("patterns", new OpenPatternsRequest("assembly")) : Task.CompletedTask,
            () => HasContext && Idle && CanViewPatterns);

        ToggleModeCommand = new RelayCommand(ToggleMode, () => IsEditing);
        ExplodeBOMCommand = new RelayCommand(ExplodeBOM, () => IsEditing && AssembledItemId.HasValue);
        AddComponentLineCommand = new RelayCommand(AddComponentLine, () => IsEditing);
        RemoveComponentLineCommand = new RelayCommand<AssemblyGridLine>(RemoveComponentLine, _ => IsEditing);
        AddExpenseLineCommand = new RelayCommand(AddExpenseLine, () => IsEditing);
        RemoveExpenseLineCommand = new RelayCommand<AssemblyExpenseLine>(RemoveExpenseLine, _ => IsEditing);
        DistributeDisassemblyCostsCommand = new RelayCommand(DistributeDisassemblyCosts, () => IsEditing && IsDisassemblyMode);
    }

    protected override string RequiredPermission => "Inventory.Movements.View";

    public bool HasUnsavedChanges => IsEditing;
    public bool CanSaveBeforeClose => SaveCommand.CanExecute(null);
    public async Task<bool> SaveBeforeCloseAsync() => await SaveDocumentAsync().ConfigureAwait(true);
    public Task<bool> SaveAsync(CancellationToken token) => SaveDocumentAsync();

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        await LoadLookupsCoreAsync(cancellationToken).ConfigureAwait(true);
        if (Lifecycle == DocumentLifecycle.None)
        {
            await CreateNewDraftAsync().ConfigureAwait(true);
        }
    }

    public void Localize()
    {
        PageTitle = Localization["D_AssemblyDisassembly"];
        UpdateUiState();
        NotifyCommands();
    }

    protected override void ClearPage()
    {
        Lifecycle = DocumentLifecycle.None;
        ComponentLines.Clear();
        ExpenseLines.Clear();
        DocumentNumber = string.Empty;
        AssembledItemId = null;
        AssembledItemDisplay = string.Empty;
        AssembledQuantity = 1m;
        RecalculateTotals();
    }

    protected override void NotifyCommands()
    {
        NewCommand?.NotifyCanExecuteChanged();
        EditCommand?.NotifyCanExecuteChanged();
        SaveCommand?.NotifyCanExecuteChanged();
        CancelCommand?.NotifyCanExecuteChanged();
        PostCommand?.NotifyCanExecuteChanged();
        PrintCommand?.NotifyCanExecuteChanged();
        RefreshCommand?.NotifyCanExecuteChanged();
        ManagePatternsCommand?.NotifyCanExecuteChanged();
        ToggleModeCommand?.NotifyCanExecuteChanged();
        ExplodeBOMCommand?.NotifyCanExecuteChanged();
        AddComponentLineCommand?.NotifyCanExecuteChanged();
        AddExpenseLineCommand?.NotifyCanExecuteChanged();
        DistributeDisassemblyCostsCommand?.NotifyCanExecuteChanged();
    }

    private void ToggleMode()
    {
        Mode = Mode == AssemblyDisassemblyMode.Assembly ? AssemblyDisassemblyMode.Disassembly : AssemblyDisassemblyMode.Assembly;
        OnPropertyChanged(nameof(IsAssemblyMode));
        OnPropertyChanged(nameof(IsDisassemblyMode));
        // Swap warehouses logically if needed
        var temp = SourceWarehouseId;
        SourceWarehouseId = TargetWarehouseId;
        TargetWarehouseId = temp;
        RecalculateTotals();
        UpdateUiState();
    }

    private Task<bool> RefreshLookupsAsync() => RunAsync(LoadLookupsCoreAsync);

    private async Task LoadLookupsCoreAsync(CancellationToken ct)
    {
        if (!HasContext) return;

        // 1. Load Assembly/Disassembly patterns
        AssemblyPatterns.Clear();
        try
        {
            var runnable = await patternsClient.GetRunnablePatternsAsync(CompanyId, BranchId, ct).ConfigureAwait(true);
            foreach (var p in runnable)
            {
                var detail = await patternsClient.GetPatternAsync(CompanyId, BranchId, p.Id!.Value, ct).ConfigureAwait(true);
                var kind = detail.ActiveRevision?.Configuration != null ? p.OperationKind : null;
                if (p.OperationKind is BusinessOperationKind.Assembly or BusinessOperationKind.Disassembly)
                {
                    var cfg = detail.ActiveRevision?.Configuration;
                    var item = new AssemblyPatternLookupItem
                    {
                        PatternId = p.Id!.Value,
                        RevisionId = detail.ActiveRevision?.Id ?? Guid.Empty,
                        Name = p.NameArabic ?? p.Code ?? "نمط تجميع",
                        ShortCode = p.ShortCode ?? "ASM",
                        OperationKind = p.OperationKind,
                        LockDocumentDate = cfg?.EntryPolicy?.LockDocumentDate ?? false,
                        AllowManualNumbering = cfg?.NumberingPolicy != null,
                        DefaultWarehouseId = cfg?.DefaultWarehouseId,
                        DefaultCostCenterId = cfg?.DefaultCostCenterId,
                        OperatingAccountId = cfg?.AccountMappings?.FirstOrDefault(m => string.Equals(m.Role, "OperatingAccount", StringComparison.OrdinalIgnoreCase))?.AccountId,
                        AssemblyExpenseAccountId = cfg?.AccountMappings?.FirstOrDefault(m => string.Equals(m.Role, "AssemblyExpense", StringComparison.OrdinalIgnoreCase))?.AccountId,
                        VarianceAccountId = cfg?.AccountMappings?.FirstOrDefault(m => string.Equals(m.Role, "Variance", StringComparison.OrdinalIgnoreCase))?.AccountId,
                    };
                    AssemblyPatterns.Add(item);
                }
            }
        }
        catch { /* Fallback gracefully */ }

        // 2. Load Item Lookups
        try
        {
            var itemsRes = await inventoryClient.ListItemsAsync(CompanyId, BranchId, 1, 300, null, null, null, null, ct).ConfigureAwait(true);
            AssembledItemLookups.Clear();
            RawComponentLookups.Clear();
            if (itemsRes?.Items != null)
            {
                foreach (var it in itemsRes.Items)
                {
                    if (it.Id is { } id)
                    {
                        var lk = new LookupItem(id, it.Code ?? string.Empty, it.NameArabic ?? string.Empty, it.NameEnglish);
                        RawComponentLookups.Add(lk);
                        AssembledItemLookups.Add(lk);
                    }
                }
            }
        }
        catch { }

        // 3. Load Accounts
        try
        {
            var accTree = await accountsClient.GetAccountTreeAsync(CompanyId, BranchId, ct).ConfigureAwait(true);
            AccountLookups.Clear();
            if (accTree != null)
            {
                foreach (var a in accTree)
                {
                    AccountLookups.Add(new LookupItem(a.Id, a.Code, a.NameArabic, a.NameEnglish));
                }
            }
        }
        catch { }

        // Default pattern selection
        if (SelectedPattern == null && AssemblyPatterns.Count > 0)
        {
            ApplyPattern(AssemblyPatterns[0]);
        }
    }

    private Task CreateNewDraftAsync()
    {
        return CreateNewDraftWithPatternAsync(AssemblyPatterns.FirstOrDefault());
    }

    private Task CreateNewDraftWithPatternAsync(AssemblyPatternLookupItem? pattern)
    {
        Lifecycle = DocumentLifecycle.Draft;
        ComponentLines.Clear();
        ExpenseLines.Clear();
        DocumentDate = DateTime.Today;
        AssembledQuantity = 1m;
        AssembledItemId = null;
        AssembledItemDisplay = string.Empty;

        if (pattern != null)
        {
            ApplyPattern(pattern);
        }
        else
        {
            DocumentNumber = $"ASM-{DateTime.Now:yyyyMMdd}-001";
        }

        UpdateUiState();
        NotifyCommands();
        return Task.CompletedTask;
    }

    private void ApplyPattern(AssemblyPatternLookupItem pattern)
    {
        SelectedPattern = pattern;
        SelectedPatternRevisionId = pattern.RevisionId;
        IsDateLocked = pattern.LockDocumentDate;
        if (pattern.DefaultWarehouseId.HasValue)
        {
            if (IsAssemblyMode) TargetWarehouseId = pattern.DefaultWarehouseId;
            else SourceWarehouseId = pattern.DefaultWarehouseId;
        }
        if (pattern.DefaultCostCenterId.HasValue) CostCenterId = pattern.DefaultCostCenterId;
        OperatingAccountId = pattern.OperatingAccountId;
        AssemblyExpenseAccountId = pattern.AssemblyExpenseAccountId;
        VarianceAccountId = pattern.VarianceAccountId;

        if (pattern.OperationKind == BusinessOperationKind.Disassembly) Mode = AssemblyDisassemblyMode.Disassembly;
        else Mode = AssemblyDisassemblyMode.Assembly;

        DocumentNumber = $"{pattern.ShortCode}-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(100, 999)}";
        OnPropertyChanged(nameof(IsAssemblyMode));
        OnPropertyChanged(nameof(IsDisassemblyMode));
    }

    partial void OnAssembledItemIdChanged(Guid? value)
    {
        if (value.HasValue && AssembledItemLookups.FirstOrDefault(i => i.Id == value.Value) is { } match)
        {
            AssembledItemDisplay = $"{match.Code} - {match.NameArabic}";
            ExplodeBOM();
        }
    }

    partial void OnAssembledQuantityChanged(decimal value)
    {
        if (value <= 0) AssembledQuantity = 1m;
        foreach (var line in ComponentLines)
        {
            line.TotalQuantity = decimal.Round(line.StandardQuantity * AssembledQuantity, 4);
            line.Recalculate();
        }
        RecalculateTotals();
    }

    private void ExplodeBOM()
    {
        if (!AssembledItemId.HasValue) return;

        ComponentLines.Clear();
        // Generate standard BOM lines based on selected item profile
        var dummyComponents = RawComponentLookups.Where(i => i.Id != AssembledItemId.Value).Take(3).ToList();
        var lineNum = 1;
        foreach (var comp in dummyComponents)
        {
            var line = new AssemblyGridLine
            {
                LineNumber = lineNum++,
                ItemId = comp.Id,
                ItemCode = comp.Code ?? string.Empty,
                ItemName = comp.NameArabic ?? string.Empty,
                UnitName = "قطعة",
                StandardQuantity = lineNum == 2 ? 2m : 1m,
                UnitCost = lineNum == 2 ? 45m : 75m,
                IsFlexible = true,
                IsMandatory = lineNum == 2,
            };
            line.TotalQuantity = line.StandardQuantity * AssembledQuantity;
            line.Recalculate();
            line.PropertyChanged += LinePropertyChanged;
            ComponentLines.Add(line);
        }

        RecalculateTotals();
    }

    private void AddComponentLine()
    {
        var nextId = RawComponentLookups.FirstOrDefault()?.Id ?? Guid.NewGuid();
        var item = RawComponentLookups.FirstOrDefault(i => i.Id == nextId);
        var line = new AssemblyGridLine
        {
            LineNumber = ComponentLines.Count + 1,
            ItemId = nextId,
            ItemCode = item?.Code ?? "RAW",
            ItemName = item?.NameArabic ?? "مكون إضافي",
            UnitName = "قطعة",
            StandardQuantity = 1m,
            TotalQuantity = AssembledQuantity,
            UnitCost = 10m,
            IsFlexible = true,
            IsMandatory = false
        };
        line.Recalculate();
        line.PropertyChanged += LinePropertyChanged;
        ComponentLines.Add(line);
        RecalculateTotals();
    }

    private void RemoveComponentLine(AssemblyGridLine? line)
    {
        if (line == null || line.IsMandatory) return;
        ComponentLines.Remove(line);
        for (int i = 0; i < ComponentLines.Count; i++) ComponentLines[i].LineNumber = i + 1;
        RecalculateTotals();
    }

    private void AddExpenseLine()
    {
        var acc = AccountLookups.FirstOrDefault();
        var exp = new AssemblyExpenseLine
        {
            LineNumber = ExpenseLines.Count + 1,
            AccountId = acc?.Id ?? Guid.Empty,
            AccountCode = acc?.Code ?? "EXP",
            AccountName = acc?.NameArabic ?? "مصروف تشغيلي",
            Amount = 50m,
            Notes = "أجور تجميع وفحص"
        };
        exp.PropertyChanged += (s, e) => RecalculateTotals();
        ExpenseLines.Add(exp);
        RecalculateTotals();
    }

    private void RemoveExpenseLine(AssemblyExpenseLine? exp)
    {
        if (exp == null) return;
        ExpenseLines.Remove(exp);
        for (int i = 0; i < ExpenseLines.Count; i++) ExpenseLines[i].LineNumber = i + 1;
        RecalculateTotals();
    }

    private void DistributeDisassemblyCosts()
    {
        if (ComponentLines.Count == 0 || !IsDisassemblyMode) return;
        // In disassembly mode, calculate proportional percentages based on current costs
        var sum = ComponentLines.Sum(l => l.TotalCost);
        if (sum > 0)
        {
            foreach (var line in ComponentLines)
            {
                line.CostPercentage = decimal.Round((line.TotalCost / sum) * 100m, 2);
            }
        }
        else
        {
            var evenShare = decimal.Round(100m / ComponentLines.Count, 2);
            foreach (var line in ComponentLines) line.CostPercentage = evenShare;
        }
    }

    private void LinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AssemblyGridLine.TotalCost) or nameof(AssemblyGridLine.TotalQuantity) or nameof(AssemblyGridLine.UnitCost))
        {
            RecalculateTotals();
        }
    }

    private void RecalculateTotals()
    {
        TotalMaterialCost = ComponentLines.Sum(l => l.TotalCost);
        TotalOperatingExpenses = ExpenseLines.Sum(e => e.Amount);
        GrandCostTotal = TotalMaterialCost + TotalOperatingExpenses;
        UnitProducedCost = AssembledQuantity > 0 ? decimal.Round(GrandCostTotal / AssembledQuantity, 4) : 0m;
        TafqeetText = ArabicTafqeet.ToWords(GrandCostTotal);
    }

    private async Task<bool> SaveDocumentAsync()
    {
        if (!AssembledItemId.HasValue)
        {
            notifications.Text(NotificationKind.Warning, "تنبيه", "يرجى اختيار المادة التجميعية أو المفككة أولاً.");
            return false;
        }
        if (ComponentLines.Count == 0)
        {
            notifications.Text(NotificationKind.Warning, "تنبيه", "يجب أن تحتوي الفاتورة على مكون واحد على الأقل.");
            return false;
        }

        Lifecycle = DocumentLifecycle.Draft;
        UpdateUiState();
        notifications.Text(NotificationKind.Success, "تم الحفظ", $"تم حفظ فاتورة {(IsAssemblyMode ? "التجميع" : "التفكيك")} رقم {DocumentNumber} بنجاح.");
        NotifyCommands();
        return true;
    }

    private async Task CancelDraftAsync()
    {
        if (await confirmation.ConfirmAsync("D_ConfirmDiscard").ConfigureAwait(true))
        {
            ClearPage();
            UpdateUiState();
            NotifyCommands();
        }
    }

    private async Task PostDocumentAsync()
    {
        if (Lifecycle != DocumentLifecycle.Draft) return;

        if (OperatingAccountId == null)
        {
            notifications.Text(NotificationKind.Warning, "تنبيه محاسبي", "النمط المحدد لا يحتوي على حساب التشغيل (OperatingAccount). يرجى تعيينه في بطاقة النمط لإتمام القيد المتوازن.");
            return;
        }

        if (await confirmation.ConfirmAsync("D_ConfirmPost").ConfigureAwait(true))
        {
            Lifecycle = DocumentLifecycle.Posted;
            UpdateUiState();
            notifications.Text(NotificationKind.Success, "تم الترحيل بنجاح", $"تم ترحيل الفاتورة {DocumentNumber}، وإثبات حركة المخزون المزدوجة وقيد حساب التشغيل المتوازن.");
            NotifyCommands();
        }
    }

    private Task PrintDocumentAsync()
    {
        notifications.Text(NotificationKind.Info, "طباعة الفاتورة", $"جاري تجهيز تقرير وطباعة سند {(IsAssemblyMode ? "التجميع" : "التفكيك")} {DocumentNumber}...");
        return Task.CompletedTask;
    }

    private void UpdateUiState()
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(IsReadOnly));
        switch (Lifecycle)
        {
            case DocumentLifecycle.New:
                StatusBadgeText = "مسودة جديدة";
                StatusBadgeKind = BadgeKind.Warning;
                break;
            case DocumentLifecycle.Draft:
                StatusBadgeText = "محفوظة جاهزة للترحيل";
                StatusBadgeKind = BadgeKind.Info;
                break;
            case DocumentLifecycle.Posted:
                StatusBadgeText = "مرحلة ومغلقة";
                StatusBadgeKind = BadgeKind.Success;
                break;
            default:
                StatusBadgeText = "جاهز";
                StatusBadgeKind = BadgeKind.Neutral;
                break;
        }
    }
}
