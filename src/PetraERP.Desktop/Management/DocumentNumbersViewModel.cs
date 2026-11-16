using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Management;

/// <summary>
/// Document-number maintenance: preview (collisions, blocking), request, second-person approval / rejection / cancellation, hash-chained audit.
/// The server enforces that the approver differs from the requester; the document's Guid, type and journal relation never change.
/// </summary>
public sealed partial class DocumentNumbersViewModel : PageViewModel
{
    public const string ViewPermission = "Documents.Numbers.View";
    public const string RequestPermission = "Documents.Numbers.Request";
    public const string ApprovePermission = "Documents.Numbers.Approve";

    private readonly IManagementClient client;
    private readonly NotificationCenter notifications;

    [ObservableProperty] private string family = "SalesInvoice";
    [ObservableProperty] private string documentIdText = string.Empty;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private DateTime searchFrom = DateTime.Today.AddMonths(-3);
    [ObservableProperty] private DateTime searchTo = DateTime.Today;
    [ObservableProperty] private DocumentNumberCandidate? selectedDocument;
    [ObservableProperty] private string searchStatus = string.Empty;
    [ObservableProperty] private string newNumber = string.Empty;
    [ObservableProperty] private string reason = string.Empty;
    [ObservableProperty] private DocumentNumberPreviewResponse? preview;
    [ObservableProperty] private DocumentNumberChangeDto? selectedChange;
    [ObservableProperty] private string? chainStatus;

    public DocumentNumbersViewModel(IManagementClient client, NotificationCenter notifications, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.notifications = notifications;
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(LoadAllAsync), () => HasContext && Idle);
        FindCommand = new AsyncRelayCommand(() => RunAsync(FindAsync), () => HasContext && Idle);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => HasContext && Idle && DocumentId.HasValue && !string.IsNullOrWhiteSpace(NewNumber));
        RequestCommand = new AsyncRelayCommand(RequestAsync, () => HasContext && Idle && Can(RequestPermission) && DocumentId.HasValue && !string.IsNullOrWhiteSpace(NewNumber)
            && !string.IsNullOrWhiteSpace(Reason) && Preview is { CanRequest: true });
        ApproveCommand = new AsyncRelayCommand(() => DecideAsync("approve"), () => CanDecide && Can(ApprovePermission));
        RejectCommand = new AsyncRelayCommand(() => DecideAsync("reject"), () => CanDecide && Can(ApprovePermission));
        CancelCommand = new AsyncRelayCommand(() => DecideAsync("cancel"), () => CanDecide);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, () => HasContext && Idle);
    }

    public IReadOnlyList<string> Families { get; } = ["SalesInvoice", "PurchaseInvoice", "Voucher"];
    public ObservableCollection<DocumentNumberChangeDto> Changes { get; } = [];
    public ObservableCollection<DocumentNumberAuditDto> Audit { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public ObservableCollection<DocumentNumberCandidate> Candidates { get; } = [];
    public AsyncRelayCommand FindCommand { get; }
    public AsyncRelayCommand PreviewCommand { get; }
    public AsyncRelayCommand RequestCommand { get; }
    public AsyncRelayCommand ApproveCommand { get; }
    public AsyncRelayCommand RejectCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public AsyncRelayCommand VerifyCommand { get; }
    public bool HasPreview => Preview is not null;
    public bool CanRequest => Can(RequestPermission);
    public bool CanApprove => Can(ApprovePermission);
    private Guid? DocumentId => Guid.TryParse(DocumentIdText, out var id) && id != Guid.Empty ? id : null;
    private bool CanDecide => HasContext && Idle && SelectedChange is { State: "Pending" } && !string.IsNullOrWhiteSpace(Reason);

    protected override string RequiredPermission => ViewPermission;
    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken) => RunAsync(LoadAllAsync);

    protected override void ClearPage()
    {
        Changes.Clear(); Audit.Clear(); Candidates.Clear(); SelectedDocument = null; SearchStatus = string.Empty; Preview = null; SelectedChange = null; ChainStatus = null; DocumentIdText = string.Empty; NewNumber = string.Empty; Reason = string.Empty;
    }

    protected override void NotifyCommands()
    {
        RefreshCommand?.NotifyCanExecuteChanged(); FindCommand?.NotifyCanExecuteChanged(); PreviewCommand?.NotifyCanExecuteChanged(); RequestCommand?.NotifyCanExecuteChanged();
        ApproveCommand?.NotifyCanExecuteChanged(); RejectCommand?.NotifyCanExecuteChanged(); CancelCommand?.NotifyCanExecuteChanged(); VerifyCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRequest)); OnPropertyChanged(nameof(CanApprove));
    }

    // Any input change invalidates the previous preview: a request is only possible for the exact previewed inputs.
    partial void OnFamilyChanged(string value) { Preview = null; Candidates.Clear(); SelectedDocument = null; NotifyCommands(); }
    partial void OnSelectedDocumentChanged(DocumentNumberCandidate? value) => DocumentIdText = value?.Id.ToString() ?? string.Empty;
    partial void OnDocumentIdTextChanged(string value) { Preview = null; NotifyCommands(); }
    partial void OnNewNumberChanged(string value) { Preview = null; NotifyCommands(); }
    partial void OnReasonChanged(string value) => NotifyCommands();
    partial void OnPreviewChanged(DocumentNumberPreviewResponse? value) { OnPropertyChanged(nameof(HasPreview)); NotifyCommands(); }
    partial void OnSelectedChangeChanged(DocumentNumberChangeDto? value) => NotifyCommands();

    private async Task LoadAllAsync(CancellationToken token)
    {
        var changes = await client.ListDocumentNumberChangesAsync(CompanyId, BranchId, token).ConfigureAwait(true);
        var audit = await client.ListDocumentNumberAuditAsync(CompanyId, BranchId, token).ConfigureAwait(true);
        Changes.Clear(); foreach (var c in changes) Changes.Add(c);
        Audit.Clear(); foreach (var a in audit) Audit.Add(a);
    }

    private async Task FindAsync(CancellationToken token)
    {
        if (SearchFrom > SearchTo) { SearchStatus = "تاريخ البداية بعد تاريخ النهاية"; return; }
        var found = await client.FindDocumentsAsync(CompanyId, BranchId, Family, SearchText, DateOnly.FromDateTime(SearchFrom), DateOnly.FromDateTime(SearchTo), token).ConfigureAwait(true);
        Candidates.Clear();
        foreach (var d in found.OrderByDescending(d => d.Date).ThenBy(d => d.Number, StringComparer.Ordinal)) Candidates.Add(d);
        SearchStatus = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Candidates.Count} مستند");
    }

    private async Task PreviewAsync()
    {
        if (DocumentId is not { } id) return;
        await RunAsync(async token => Preview = await client.PreviewDocumentNumberAsync(CompanyId, BranchId, Family, id, NewNumber.Trim(), token).ConfigureAwait(true)).ConfigureAwait(true);
    }

    private async Task RequestAsync()
    {
        if (DocumentId is not { } id) return;
        var ok = await RunAsync(async token =>
        {
            await client.RequestDocumentNumberChangeAsync(CompanyId, BranchId, new RequestDocumentNumberChangeRequest { Family = Family, DocumentId = id, NewNumber = NewNumber.Trim(), Reason = Reason.Trim() }, token).ConfigureAwait(true);
            await LoadAllAsync(token).ConfigureAwait(true);
        }, OperationKind.Save).ConfigureAwait(true);
        if (ok) { notifications.Success("D_Saved"); Preview = null; Reason = string.Empty; }
    }

    private async Task DecideAsync(string decision)
    {
        if (SelectedChange?.Id is not { } id) return;
        var ok = await RunAsync(async token =>
        {
            await client.DecideDocumentNumberChangeAsync(CompanyId, BranchId, id, decision, Reason.Trim(), token).ConfigureAwait(true);
            await LoadAllAsync(token).ConfigureAwait(true);
        }, OperationKind.Save).ConfigureAwait(true);
        if (ok) { notifications.Success("D_Saved"); Reason = string.Empty; }
    }

    private async Task VerifyAsync()
    {
        await RunAsync(async token =>
        {
            var result = await client.VerifyDocumentNumberAuditAsync(CompanyId, BranchId, token).ConfigureAwait(true);
            ChainStatus = result.Intact == true
                ? $"سلسلة التدقيق سليمة — {result.EntriesChecked} قيد"
                : $"السلسلة مكسورة عند التسلسل {result.FirstBrokenSequence}";
        }).ConfigureAwait(true);
    }
}
