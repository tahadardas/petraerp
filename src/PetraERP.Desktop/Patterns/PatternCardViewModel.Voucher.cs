using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;

namespace PetraERP.Desktop.Patterns;

/// <summary>
/// Voucher settings of the pattern card (AMN-VPAT-GATE-005). The same card, not a second screen: it edits the real
/// <c>PatternVoucherPolicyDto</c> of a revision, and the preview is the server's own effective-pattern answer, never a local guess.
/// Only the settings the server enforces are offered; the reserved ones (per-line journal, auto-post, multi-method forms) are not shown.
/// </summary>
public sealed partial class PatternCardViewModel
{
    public sealed record ChoiceItem<T>(T Value, string Label);

    public IReadOnlyList<ChoiceItem<VoucherCaptionMode>> CaptionModeChoices { get; } =
    [
        new(VoucherCaptionMode.DebitCredit, "مدين / دائن"),
        new(VoucherCaptionMode.FromTo, "من / إلى (عناوين فقط، لا يغيّر اتجاه القيد)"),
    ];

    public IReadOnlyList<ChoiceItem<VoucherPostingMode>> PostingModeChoices { get; } =
    [
        new(VoucherPostingMode.InheritFromCompany, "حسب سياسة الشركة"),
        new(VoucherPostingMode.ManualOnly, "ترحيل يدوي فقط (لا يُرحَّل تلقائياً)"),
    ];

    [ObservableProperty] private VoucherCaptionMode draftCaptionMode = VoucherCaptionMode.DebitCredit;
    [ObservableProperty] private VoucherPostingMode draftPostingMode = VoucherPostingMode.InheritFromCompany;
    [ObservableProperty] private DateTime? draftCutoffDate;
    [ObservableProperty] private Guid? accountToAdd;
    [ObservableProperty] private Guid? costCenterToAdd;
    [ObservableProperty] private string effectivePreviewText = string.Empty;

    public ObservableCollection<Guid> DraftEligibleAccountIds { get; } = [];
    public ObservableCollection<Guid> DraftEligibleCostCenterIds { get; } = [];

    /// <summary>The voucher operations: receipts, payments and the two journal forms. Other operations have no voucher settings.</summary>
    public bool IsVoucherOperation => DraftOperationKind is BusinessOperationKind.CashReceipt or BusinessOperationKind.CashPayment
        or BusinessOperationKind.CashJournal or BusinessOperationKind.ManualJournal;

    public IEnumerable<LookupItem> EligibleAccountItems => DraftEligibleAccountIds.Select(id => AccountLookups.FirstOrDefault(a => a.Id == id) ?? new LookupItem(id, "?", id.ToString("N")[..8], null));
    public IEnumerable<LookupItem> EligibleCostCenterItems => DraftEligibleCostCenterIds.Select(id => CostCenterLookups.FirstOrDefault(a => a.Id == id) ?? new LookupItem(id, "?", id.ToString("N")[..8], null));

    public AsyncRelayCommand LoadEffectivePreviewCommand => loadEffectivePreviewCommand ??= new AsyncRelayCommand(LoadEffectivePreviewAsync, () => HasContext && Idle && Pattern?.ActiveRevision is not null);
    private AsyncRelayCommand? loadEffectivePreviewCommand;

    public RelayCommand AddEligibleAccountCommand => addEligibleAccountCommand ??= new RelayCommand(() =>
    {
        if (AccountToAdd is { } id && !DraftEligibleAccountIds.Contains(id)) { DraftEligibleAccountIds.Add(id); AccountToAdd = null; LookupChanged(); }
    }, () => IsEditing);
    private RelayCommand? addEligibleAccountCommand;

    public RelayCommand<Guid> RemoveEligibleAccountCommand => removeEligibleAccountCommand ??= new RelayCommand<Guid>(id =>
    {
        if (DraftEligibleAccountIds.Remove(id)) LookupChanged();
    }, _ => IsEditing);
    private RelayCommand<Guid>? removeEligibleAccountCommand;

    public RelayCommand AddEligibleCostCenterCommand => addEligibleCostCenterCommand ??= new RelayCommand(() =>
    {
        if (CostCenterToAdd is { } id && !DraftEligibleCostCenterIds.Contains(id)) { DraftEligibleCostCenterIds.Add(id); CostCenterToAdd = null; LookupChanged(); }
    }, () => IsEditing);
    private RelayCommand? addEligibleCostCenterCommand;

    public RelayCommand<Guid> RemoveEligibleCostCenterCommand => removeEligibleCostCenterCommand ??= new RelayCommand<Guid>(id =>
    {
        if (DraftEligibleCostCenterIds.Remove(id)) LookupChanged();
    }, _ => IsEditing);
    private RelayCommand<Guid>? removeEligibleCostCenterCommand;

    public RelayCommand ClearCutoffCommand => clearCutoffCommand ??= new RelayCommand(() => DraftCutoffDate = null, () => IsEditing && DraftCutoffDate is not null);
    private RelayCommand? clearCutoffCommand;

    partial void OnDraftCaptionModeChanged(VoucherCaptionMode value) => DraftChanged();
    partial void OnDraftPostingModeChanged(VoucherPostingMode value) => DraftChanged();
    partial void OnDraftCutoffDateChanged(DateTime? value) { DraftChanged(); ClearCutoffCommand.NotifyCanExecuteChanged(); }

    private void LookupChanged()
    {
        OnPropertyChanged(nameof(EligibleAccountItems));
        OnPropertyChanged(nameof(EligibleCostCenterItems));
        DraftChanged();
    }

    private void NotifyVoucherKind() => OnPropertyChanged(nameof(IsVoucherOperation));

    /// <summary>Loads the revision's voucher block into the draft; a revision without one shows the defaults (what the server runs it with).</summary>
    private void ApplyVoucherToDraft(PatternConfigurationDto? config)
    {
        var voucher = config?.Voucher;
        DraftCaptionMode = voucher?.CaptionMode ?? VoucherCaptionMode.DebitCredit;
        DraftPostingMode = voucher?.PostingMode ?? VoucherPostingMode.InheritFromCompany;
        DraftCutoffDate = voucher?.DenyMutationsOnOrBefore is { } cutoff ? cutoff.ToDateTime(TimeOnly.MinValue) : null;
        DraftEligibleAccountIds.Clear();
        foreach (var id in voucher?.Lookup?.EligibleAccountRootIds ?? []) DraftEligibleAccountIds.Add(id);
        DraftEligibleCostCenterIds.Clear();
        foreach (var id in voucher?.Lookup?.EligibleCostCenterIds ?? []) DraftEligibleCostCenterIds.Add(id);
        LookupChanged();
    }

    private void ResetVoucherDraft() => ApplyVoucherToDraft(null);

    /// <summary>The block sent with a revision; null for operations that are not vouchers (the server refuses one there).</summary>
    private PatternVoucherPolicyDto? BuildVoucherFromDraft() => !IsVoucherOperation ? null : new PatternVoucherPolicyDto
    {
        FormKind = VoucherFormKind.GenericVoucher, // multi-method forms are not enabled
        CaptionMode = DraftCaptionMode,
        PostingMode = DraftPostingMode,
        DenyMutationsOnOrBefore = DraftCutoffDate is { } date ? DateOnly.FromDateTime(date) : null,
        PerLineJournal = false,
        AutoPost = false,
        Lookup = new PatternLookupPolicyDto
        {
            EligibleAccountRootIds = DraftEligibleAccountIds.ToList(),
            EligibleCostCenterIds = DraftEligibleCostCenterIds.ToList(),
        },
    };

    private async Task LoadEffectivePreviewAsync(CancellationToken cancellationToken)
    {
        if (Pattern?.Id is not { } id) return;
        var effective = await patternsClient.GetEffectivePatternAsync(CompanyId, BranchId, id, cancellationToken).ConfigureAwait(true);
        EffectivePreviewText = DescribeEffective(effective);
    }

    /// <summary>A readable summary of what the server says this pattern runs with right now (the published revision, defaults resolved).</summary>
    internal static string DescribeEffective(EffectivePatternResponse effective)
    {
        var config = effective.Configuration;
        var voucher = config?.Voucher;
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"النسخة المنشورة الفعالة: {effective.RevisionNumber} · {effective.Code}\n");
        builder.Append("نوع التشغيل: ").Append(voucher?.FormKind == VoucherFormKind.GenericVoucher || voucher?.FormKind is null ? "سند عادي" : voucher.FormKind.ToString()).Append('\n');
        builder.Append("العناوين: ").Append(voucher?.CaptionMode == VoucherCaptionMode.FromTo ? "من / إلى" : "مدين / دائن").Append('\n');
        builder.Append("الترحيل: ").Append(voucher?.PostingMode == VoucherPostingMode.ManualOnly ? "يدوي فقط" : "حسب سياسة الشركة").Append('\n');
        builder.Append("تاريخ تثبيت السندات: ").Append(voucher?.DenyMutationsOnOrBefore is { } cutoff ? cutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "بدون").Append('\n');
        builder.Append("تثبيت تاريخ الإضافة: ").Append(config?.EntryPolicy?.LockDocumentDate == true ? "نعم" : "لا").Append('\n');
        var approval = config?.ApprovalPolicy;
        builder.Append("الاعتماد: ").Append(approval?.RequiresApproval == true
            ? string.IsNullOrWhiteSpace(approval.ThresholdAmount) ? "مطلوب لكل سند" : $"مطلوب من مبلغ {approval.ThresholdAmount}"
            : "غير مطلوب").Append('\n');
        var accounts = voucher?.Lookup?.EligibleAccountRootIds?.Count ?? 0;
        var centers = voucher?.Lookup?.EligibleCostCenterIds?.Count ?? 0;
        builder.Append("الحسابات المسموحة: ").Append(accounts == 0 ? "بلا تقييد" : $"{accounts} جذر حساب").Append(" · مراكز الكلفة: ").Append(centers == 0 ? "بلا تقييد" : $"{centers} مركز").Append('\n');
        var rules = (config?.LineFields ?? []).Where(f => f.IsRequired == true || f.IsVisible == false)
            .Select(f => f.IsVisible == false ? $"{f.FieldName} (مخفي)" : $"{f.FieldName} (إلزامي)").ToList();
        builder.Append("قواعد الحقول: ").Append(rules.Count == 0 ? "لا قواعد خاصة" : string.Join("، ", rules));
        return builder.ToString();
    }
}
