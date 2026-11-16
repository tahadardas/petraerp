using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;

namespace PetraERP.Desktop.Shell;

public sealed partial class PreferencesViewModel
{
    private const string CompanyOptionsManagePermission = "System.CompanyOptions.Manage";
    private const string ManualPostingExplanation = "عند حفظ مستند مكتمل سيتم اعتماده، ولن يؤثر على الحسابات أو المخزون حتى ترحيله.";
    private const string AutomaticPostingExplanation = "عند حفظ مستند مكتمل سيتم اعتماده وترحيله تلقائياً بعد نجاح جميع التحققات المحاسبية.";
    private ICompanyOptionsClient? companyOptionsClient;
    private DesktopOperation? desktopOperation;
    private Func<(Guid CompanyId, Guid BranchId, Guid FiscalYearId)>? contextProvider;
    private Func<string, bool>? permissionChecker;

    [ObservableProperty] private string policyDocumentPostingMode = "ApproveOnly";
    [ObservableProperty] private bool policyAllowSaveAsDraft = true;
    [ObservableProperty] private string policyPostingModeExplanation = ManualPostingExplanation;
    [ObservableProperty] private string policyNegativeIssuePolicy = "Block";
    [ObservableProperty] private bool policyRequireDocumentApproval;
    [ObservableProperty] private bool policyRequireProfitLossCostCenter;
    [ObservableProperty] private bool policyAllowCreditSales = true;
    [ObservableProperty] private bool policyAllowCreditPurchases = true;
    [ObservableProperty] private bool policyAllowManualNumbering;
    [ObservableProperty] private bool policyAllowPatternNumberingOverrides = true;
    [ObservableProperty] private int policyDefaultNumberingScope = 4;
    [ObservableProperty] private long policyInitialNumberSequence = 1;
    [ObservableProperty] private Guid? policyDefaultSalesPatternId;
    [ObservableProperty] private Guid? policyDefaultPurchasePatternId;
    [ObservableProperty] private Guid? policyDefaultReceiptPatternId;
    [ObservableProperty] private Guid? policyDefaultPaymentPatternId;
    [ObservableProperty] private Guid? policyCustomersParentAccountId;
    [ObservableProperty] private Guid? policySuppliersParentAccountId;
    [ObservableProperty] private string policyReason = string.Empty;
    [ObservableProperty] private string policyVersion = string.Empty;
    [ObservableProperty] private string policyPrecedence = "Installation security > company policy > permitted branch/pattern override > user display preference";
    [ObservableProperty] private string policyStatusMessage = string.Empty;

    private sealed record PolicyState(string Mode, bool Draft, string NegativeIssue, bool Approval, bool CostCenter, bool CreditSales, bool CreditPurchases,
        bool ManualNumbering, bool PatternOverrides, int Scope, long InitialSequence, Guid? Sales, Guid? Purchase, Guid? Receipt, Guid? Payment, Guid? CustomersParent, Guid? SuppliersParent);

    /// <summary>The policy as last loaded from or saved to the server; edits are compared against it.</summary>
    private PolicyState? loadedPolicy;

    private PolicyState CapturePolicy() => new(PolicyDocumentPostingMode, PolicyAllowSaveAsDraft, PolicyNegativeIssuePolicy, PolicyRequireDocumentApproval,
        PolicyRequireProfitLossCostCenter, PolicyAllowCreditSales, PolicyAllowCreditPurchases, PolicyAllowManualNumbering, PolicyAllowPatternNumberingOverrides,
        PolicyDefaultNumberingScope, PolicyInitialNumberSequence, PolicyDefaultSalesPatternId, PolicyDefaultPurchasePatternId, PolicyDefaultReceiptPatternId, PolicyDefaultPaymentPatternId,
        PolicyCustomersParentAccountId, PolicySuppliersParentAccountId);

    /// <summary>True when a company policy field differs from the server value; nothing is sent until Apply / OK.</summary>
    public bool HasPolicyChanges => loadedPolicy is not null && CapturePolicy() != loadedPolicy;

    public bool HasCompanyContext => (contextProvider?.Invoke().CompanyId ?? Guid.Empty) != Guid.Empty;
    public bool CanManageCompanyPolicy => HasCompanyContext && (permissionChecker?.Invoke(CompanyOptionsManagePermission) ?? false);

    public ObservableCollection<DocumentPatternSummaryResponse> CompanyPolicyPatterns { get; } = [];

    /// <summary>Active headings (non-posting accounts) a customers / suppliers parent may be chosen from; the first entry means "chart convention".</summary>
    public ObservableCollection<PolicyAccountChoice> CompanyPolicyHeadingAccounts { get; } = [];

    public IReadOnlyList<PreferenceOption<string>> DocumentPostingModeOptions { get; } =
    [
        new("ApproveOnly", "اعتماد المستند عند الحفظ وانتظار الترحيل — Approve only"),
        new("ApproveAndPost", "اعتماد المستند وترحيله مباشرة عند الحفظ — Approve and post")
    ];

    public IReadOnlyList<PreferenceOption<string>> NegativeIssuePolicyOptions { get; } =
    [
        new("Block", "حظر الإخراج بالسالب — Block (الافتراضي)"),
        new("RequestApproval", "طلب موافقة مسبقة — Request approval (يبقى الترحيل محظوراً)"),
        new("AuthorizedException", "السماح بالإخراج بالسالب — Allow negative issue (يُرحَّل ويظهر المخزون سالباً في التقارير)")
    ];

    public IReadOnlyList<PreferenceOption<int>> NumberingScopeOptions { get; } =
    [
        new(1, "تسلسلي بسيط — Simple sequential"),
        new(2, "على مستوى الفرع — Branch-specific"),
        new(3, "على مستوى السنة المالية — Fiscal-year-specific"),
        new(4, "الفرع والسنة المالية — Branch + fiscal year")
    ];

    public IAsyncRelayCommand LoadCompanyPolicyCommand { get; private set; } = null!;
    public IAsyncRelayCommand SaveCompanyPolicyCommand { get; private set; } = null!;

    public void ConfigureCompanyOptions(
        ICompanyOptionsClient? client,
        DesktopOperation operation,
        Func<(Guid CompanyId, Guid BranchId, Guid FiscalYearId)> context,
        Func<string, bool> hasPermission)
    {
        companyOptionsClient = client;
        desktopOperation = operation;
        contextProvider = context;
        permissionChecker = hasPermission;

        LoadCompanyPolicyCommand = new AsyncRelayCommand(LoadCompanyPolicyAsync, () => HasCompanyContext);
        SaveCompanyPolicyCommand = new AsyncRelayCommand(SaveCompanyPolicyAsync, () => CanManageCompanyPolicy && !string.IsNullOrWhiteSpace(PolicyReason));

        RefreshCompanyOptionsState();
        if (HasCompanyContext)
        {
            _ = LoadCompanyPolicyAsync();
        }
    }

    public void RefreshCompanyOptionsState()
    {
        OnPropertyChanged(nameof(HasCompanyContext));
        OnPropertyChanged(nameof(CanManageCompanyPolicy));
        LoadCompanyPolicyCommand?.NotifyCanExecuteChanged();
        SaveCompanyPolicyCommand?.NotifyCanExecuteChanged();
    }

    partial void OnPolicyReasonChanged(string value)
    {
        SaveCompanyPolicyCommand?.NotifyCanExecuteChanged();
    }

    public bool PolicyApproveOnlySelected
    {
        get => PolicyDocumentPostingMode == "ApproveOnly";
        set { if (value) PolicyDocumentPostingMode = "ApproveOnly"; }
    }

    public bool PolicyApproveAndPostSelected
    {
        get => PolicyDocumentPostingMode == "ApproveAndPost";
        set { if (value) PolicyDocumentPostingMode = "ApproveAndPost"; }
    }

    partial void OnPolicyDocumentPostingModeChanged(string value)
    {
        PolicyPostingModeExplanation = value == "ApproveAndPost" ? AutomaticPostingExplanation : ManualPostingExplanation;
        OnPropertyChanged(nameof(PolicyApproveOnlySelected));
        OnPropertyChanged(nameof(PolicyApproveAndPostSelected));
    }

    public async Task LoadCompanyPolicyAsync()
    {
        if (companyOptionsClient is null || contextProvider is null) return;
        var (companyId, branchId, _) = contextProvider();
        if (companyId == Guid.Empty) return;

        var run = desktopOperation is not null
            ? desktopOperation.RunAsync(() => LoadCompanyPolicyCoreAsync(companyId, branchId, CancellationToken.None))
            : LoadCompanyPolicyCoreAsync(companyId, branchId, CancellationToken.None);

        await run.ConfigureAwait(true);
    }

    private async Task LoadCompanyPolicyCoreAsync(Guid companyId, Guid branchId, CancellationToken ct)
    {
        try
        {
            var patterns = await companyOptionsClient!.CompanyPolicyPatternsAsync(companyId, branchId, ct).ConfigureAwait(true);
            CompanyPolicyPatterns.Clear();
            foreach (var p in patterns) CompanyPolicyPatterns.Add(p);

            CompanyPolicyHeadingAccounts.Clear();
            CompanyPolicyHeadingAccounts.Add(new(null, "حسب عرف الدليل (121 للزبائن، 221 للموردين)"));
            try
            {
                foreach (var account in (await companyOptionsClient.CompanyPolicyAccountsAsync(companyId, branchId, ct).ConfigureAwait(true))
                    .Where(a => a.IsActive && !a.PostingAllowed).OrderBy(a => a.Code, StringComparer.Ordinal))
                    CompanyPolicyHeadingAccounts.Add(new(account.Id, account.Code + " - " + account.NameArabic));
            }
            catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or PetraApiException or NotSupportedException)
            {
                // Without the chart (no permission or no connection) the stored choice is kept as it is and cannot be changed here.
            }

            var state = await companyOptionsClient.GetCompanyPolicyAsync(companyId, branchId, ct).ConfigureAwait(true);
            if (state is not null)
            {
                PolicyNegativeIssuePolicy = state.Policy.NegativeIssuePolicy ?? "Block";
                PolicyDocumentPostingMode = state.Policy.DocumentPostingMode ?? "ApproveOnly";
                PolicyAllowSaveAsDraft = state.Policy.AllowSaveAsDraft;
                PolicyRequireDocumentApproval = state.Policy.RequireDocumentApproval;
                PolicyRequireProfitLossCostCenter = state.Policy.RequireProfitLossCostCenter;
                PolicyAllowCreditSales = state.Policy.AllowCreditSales;
                PolicyAllowCreditPurchases = state.Policy.AllowCreditPurchases;
                PolicyAllowManualNumbering = state.Policy.AllowManualNumbering;
                PolicyAllowPatternNumberingOverrides = state.Policy.AllowPatternNumberingOverrides;
                PolicyDefaultNumberingScope = state.Policy.DefaultNumberingScope;
                PolicyInitialNumberSequence = state.Policy.InitialNumberSequence;
                PolicyDefaultSalesPatternId = state.Policy.DefaultSalesPatternId;
                PolicyDefaultPurchasePatternId = state.Policy.DefaultPurchasePatternId;
                PolicyDefaultReceiptPatternId = state.Policy.DefaultReceiptPatternId;
                PolicyDefaultPaymentPatternId = state.Policy.DefaultPaymentPatternId;
                foreach (var stored in new[] { state.Policy.CustomersParentAccountId, state.Policy.SuppliersParentAccountId })
                    if (stored is { } id && CompanyPolicyHeadingAccounts.All(a => a.Id != id)) CompanyPolicyHeadingAccounts.Add(new(id, "الحساب المحدد حاليًا"));
                PolicyCustomersParentAccountId = state.Policy.CustomersParentAccountId;
                PolicySuppliersParentAccountId = state.Policy.SuppliersParentAccountId;
                PolicyVersion = state.Version;
                PolicyPrecedence = state.PolicyPrecedence ?? PolicyPrecedence;
                PolicyStatusMessage = string.Empty;
                loadedPolicy = CapturePolicy();
            }
        }
        catch (Exception ex)
        {
            PolicyStatusMessage = ex.Message;
        }
        finally
        {
            RefreshCompanyOptionsState();
        }
    }

    public async Task SaveCompanyPolicyAsync()
    {
        if (!CanManageCompanyPolicy || companyOptionsClient is null || contextProvider is null) return;
        var (companyId, branchId, _) = contextProvider();
        if (companyId == Guid.Empty) return;

        var policyData = new CompanyFinancialPolicyData
        {
            DocumentPostingMode = PolicyDocumentPostingMode,
            AllowSaveAsDraft = PolicyAllowSaveAsDraft,
            NegativeIssuePolicy = PolicyNegativeIssuePolicy,
            RequireDocumentApproval = PolicyRequireDocumentApproval,
            RequireProfitLossCostCenter = PolicyRequireProfitLossCostCenter,
            AllowCreditSales = PolicyAllowCreditSales,
            AllowCreditPurchases = PolicyAllowCreditPurchases,
            AllowManualNumbering = PolicyAllowManualNumbering,
            AllowPatternNumberingOverrides = PolicyAllowPatternNumberingOverrides,
            DefaultNumberingScope = PolicyDefaultNumberingScope,
            InitialNumberSequence = PolicyInitialNumberSequence,
            DefaultSalesPatternId = PolicyDefaultSalesPatternId,
            DefaultPurchasePatternId = PolicyDefaultPurchasePatternId,
            DefaultReceiptPatternId = PolicyDefaultReceiptPatternId,
            DefaultPaymentPatternId = PolicyDefaultPaymentPatternId,
            CustomersParentAccountId = PolicyCustomersParentAccountId,
            SuppliersParentAccountId = PolicySuppliersParentAccountId,
        };

        var reason = PolicyReason.Trim();
        var run = desktopOperation is not null
            ? desktopOperation.RunAsync(async () =>
            {
                var result = await companyOptionsClient.SaveCompanyPolicyAsync(companyId, branchId, policyData, PolicyVersion, reason, CancellationToken.None).ConfigureAwait(true);
                PolicyVersion = result.Version;
                PolicyReason = string.Empty;
                loadedPolicy = CapturePolicy();
                PolicyStatusMessage = "تم حفظ سياسات الشركة المالية بنجاح.";
                notifications.Success("UI_PolicySaved");
                RefreshCompanyOptionsState();
            })
            : Task.CompletedTask;

        await run.ConfigureAwait(true);
    }
}

/// <summary>An account offered as a party heading; a null id is the "chart convention" choice.</summary>
public sealed record PolicyAccountChoice(Guid? Id, string Caption);
