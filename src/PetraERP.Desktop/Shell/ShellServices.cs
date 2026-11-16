using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;

namespace PetraERP.Desktop.Shell;

/// <summary>The authorized working context the shell hands to every page. Pages never choose company/branch themselves.</summary>
public sealed record ShellContext(CompanyContextResponse Company, BranchContextResponse Branch, IReadOnlySet<string> Permissions)
{
    public Guid UserId { get; init; }
    public Guid? FiscalYearId { get; init; }
    /// <summary>The working fiscal year's range when known; documents outside it are refused before the server is asked.</summary>
    public DateOnly? FiscalYearStart { get; init; }
    public DateOnly? FiscalYearEnd { get; init; }
    public Guid CompanyId => Company.Id;
    public Guid BranchId => Branch.Id;
    public string BaseCurrencyCode => Company.BaseCurrencyCode;
    public bool Can(string permission) => Permissions.Contains(permission);
}

/// <summary>A navigable workspace page. Activation receives the shell context and an optional typed drill-down parameter.</summary>
public interface IShellPage
{
    /// <summary>Loads or refreshes the page for the given context. Implementations clear stale data when the company changes.</summary>
    Task ActivateAsync(ShellContext shellContext, object? parameter, CancellationToken cancellationToken);

    /// <summary>Drops all company-bound data (logout, session loss, company switch).</summary>
    void Clear();
}

/// <summary>A page with user input that must be confirmed before leaving it.</summary>
public interface IUnsavedPage
{
    bool HasUnsavedChanges { get; }
}

/// <summary>
/// A page whose unsaved input can be saved from the close dialog (Save / Discard / Cancel). Saving goes through the page's
/// own server command, so the same validation, permission and concurrency checks apply; the tab closes only when the save
/// succeeded and nothing is left unsaved.
/// </summary>
public interface ISavablePage : IUnsavedPage
{
    /// <summary>True when the page's save command can run now (permission, valid input, not busy).</summary>
    bool CanSaveBeforeClose { get; }

    /// <summary>Runs the save command; returns true only when the input is now saved.</summary>
    Task<bool> SaveBeforeCloseAsync();
}

/// <summary>Typed drill-down parameters between screens.</summary>
public sealed record OpenJournalRequest(Guid JournalId);

public sealed record OpenAccountLedgerRequest(Guid AccountId, DateOnly? From, DateOnly? To, bool Consolidated);

public sealed record OpenAccountRequest(Guid AccountId);

/// <summary>Opens (or activates) the account card document; the shell keys it by company, branch and account.</summary>
/// <summary>Opens an account card. <paramref name="ReplacingCardOf"/> moves that open card to the account (record navigation).</summary>
public sealed record OpenAccountCardRequest(Guid AccountId, Guid? ReplacingCardOf = null);

public sealed record OpenPatternCardRequest(Guid PatternId);

public sealed record NewPatternCardRequest(Guid? BasedOnPatternId = null, ApiClient.BusinessOperationKind? InitialOperationKind = null);

public sealed record OpenPartyCardRequest(Guid PartyId);

public sealed record NewPartyCardRequest();

public sealed record OpenItemCardRequest(Guid ItemId, Guid? MovementId = null);

public sealed record NewItemCardRequest();

public sealed record OpenItemGroupCardRequest(Guid GroupId);

public sealed record NewItemGroupCardRequest(Guid? ParentId = null);

/// <summary>Opens a sales invoice; <paramref name="Scope"/> carries the originating list's filters so previous/next stay inside that list.</summary>
public sealed record OpenSalesInvoiceCardRequest(Guid InvoiceId, SalesInvoiceListScope? Scope = null);

/// <summary>Opens a new sales invoice; <paramref name="PatternId"/> is the pattern the list button was made for (fixed on the card).</summary>
public sealed record NewSalesInvoiceCardRequest(Guid? PatternId = null);

/// <summary>Opens the general journal report; null optional criteria leave the page's current value unchanged.</summary>
public sealed record OpenGeneralJournalRequest(Guid? CostCenterId = null, Guid? AccountId = null, DateOnly? From = null, DateOnly? To = null, bool? Consolidated = null);

/// <summary>Opens the trial balance; null optional criteria leave the page's current value unchanged (saved filters set them).</summary>
public sealed record OpenTrialBalanceRequest(Guid? PeriodId = null, DateOnly? From = null, DateOnly? To = null, bool? IncludeZeroRows = null,
    int? MaximumLevel = null, bool? Consolidated = null);

/// <summary>Decouples pages from the shell: a page asks for navigation; the shell revalidates and performs it.</summary>
public sealed class ShellNavigator
{
    public event Func<string, object?, Task>? Requested;

    public Task NavigateAsync(string route, object? parameter = null) => Requested?.Invoke(route, parameter) ?? Task.CompletedTask;
}

public enum NotificationKind
{
    Success,
    Info,
    Warning,
    Error,
}

public sealed record AppNotification(Guid Id, NotificationKind Kind, string Title, string Message, string? Details,
    int Count = 1, string? ActionLabel = null, Func<Task>? Action = null, DateTimeOffset Time = default)
{
    public bool HasDetails => !string.IsNullOrEmpty(Details);
    /// <summary>True when the same message was raised more than once in a row ("×3" instead of three toasts).</summary>
    public bool IsRepeated => Count > 1;
    public string CountText => "×" + Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public bool HasAction => Action is not null && !string.IsNullOrEmpty(ActionLabel);
    public string TimeText => Time == default ? string.Empty : Time.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Non-blocking feedback. Success/info toasts dismiss themselves; warnings and errors stay until dismissed. Technical
/// details are optional and secondary to the localized business message.
/// </summary>
public sealed partial class NotificationCenter : ObservableObject
{
    private readonly ILocalizationService localization;
    private readonly TimeSpan autoDismiss;

    public NotificationCenter(ILocalizationService localization)
        : this(localization, TimeSpan.FromSeconds(5))
    {
    }

    public NotificationCenter(ILocalizationService localization, TimeSpan autoDismiss)
    {
        this.localization = localization;
        this.autoDismiss = autoDismiss;
        DismissCommand = new RelayCommand<AppNotification>(Dismiss);
        InvokeActionCommand = new AsyncRelayCommand<AppNotification>(InvokeActionAsync, item => item?.HasAction == true);
        TogglePanelCommand = new RelayCommand(() => { IsPanelOpen = !IsPanelOpen; if (IsPanelOpen) MarkAllRead(); });
        ClearHistoryCommand = new RelayCommand(() => { History.Clear(); MarkAllRead(); });
    }

    /// <summary>Visible toasts (at most five).</summary>
    public ObservableCollection<AppNotification> Items { get; } = [];

    /// <summary>The session's notification history (newest first, at most 50), shown in the notification panel.</summary>
    public ObservableCollection<AppNotification> History { get; } = [];

    public RelayCommand<AppNotification> DismissCommand { get; }
    public AsyncRelayCommand<AppNotification> InvokeActionCommand { get; }
    public RelayCommand TogglePanelCommand { get; }
    public RelayCommand ClearHistoryCommand { get; }

    [ObservableProperty] private int unreadCount;
    [ObservableProperty] private bool isPanelOpen;

    public bool HasUnread => UnreadCount > 0;
    partial void OnUnreadCountChanged(int value) => OnPropertyChanged(nameof(HasUnread));

    public void MarkAllRead() => UnreadCount = 0;

    /// <summary>Raises a notification whose message is already localized and formatted (for example with counts).</summary>
    public AppNotification Text(NotificationKind kind, string titleKey, string message) => Publish(kind, titleKey, message, null);

    /// <summary>
    /// Raises a notification with an optional action (for example "Refresh" after an unknown outcome). The action runs only
    /// when the user clicks it; it is never invoked automatically.
    /// </summary>
    public AppNotification Show(NotificationKind kind, string titleKey, string messageKey, string? details = null, string? actionLabelKey = null, Func<Task>? action = null) =>
        Publish(kind, titleKey, localization[messageKey], details, actionLabelKey is null ? null : localization[actionLabelKey], action);

    public AppNotification Success(string key) => Publish(NotificationKind.Success, "D_Toast_Success", localization[key], null);

    public AppNotification Info(string key) => Publish(NotificationKind.Info, "D_Toast_Info", localization[key], null);

    public AppNotification Warning(string key, string? details = null) => Publish(NotificationKind.Warning, "D_Toast_Warning", localization[key], details);

    public AppNotification Error(string messageKey, string titleKey, string? details) => Publish(NotificationKind.Error, titleKey, localization[messageKey], details);

    public void Dismiss(AppNotification? notification)
    {
        if (notification is not null) Items.Remove(notification);
    }

    public void Clear() { Items.Clear(); History.Clear(); IsPanelOpen = false; MarkAllRead(); }

    private async Task InvokeActionAsync(AppNotification? notification)
    {
        if (notification?.Action is not { } action) return;
        Dismiss(notification);
        await action().ConfigureAwait(true);
    }

    private AppNotification Publish(NotificationKind kind, string titleKey, string message, string? details, string? actionLabel = null, Func<Task>? action = null)
    {
        var title = localization[titleKey];
        // The same message again (for example a repeated network failure) updates the newest toast instead of stacking.
        var repeated = Items.FirstOrDefault() is { } top && top.Kind == kind && top.Title == title && top.Message == message ? top : null;
        var notification = new AppNotification(Guid.NewGuid(), kind, title, message, details, (repeated?.Count ?? 0) + 1, actionLabel, action, DateTimeOffset.Now);
        if (repeated is not null) Items.Remove(repeated);
        Items.Insert(0, notification);
        while (Items.Count > 5) Items.RemoveAt(Items.Count - 1);
        if (repeated is not null && History.FirstOrDefault() is { } last && last.Id == repeated.Id) History.RemoveAt(0);
        History.Insert(0, notification);
        while (History.Count > 50) History.RemoveAt(History.Count - 1);
        if (!IsPanelOpen) UnreadCount++;
        if (kind is NotificationKind.Success or NotificationKind.Info && autoDismiss > TimeSpan.Zero)
        {
            _ = DismissLaterAsync(notification);
        }

        return notification;
    }

    private async Task DismissLaterAsync(AppNotification notification)
    {
        await Task.Delay(autoDismiss).ConfigureAwait(true);
        Items.Remove(notification);
    }
}

/// <summary>A structured modal request rendered inside the shell (not a raw MessageBox).</summary>
public sealed partial class DialogRequest : ObservableObject
{
    private readonly TaskCompletionSource<DialogChoice> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DialogRequest(string title, string message, string confirmLabel, bool isDestructive, string? alternateLabel = null)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        IsDestructive = isDestructive;
        AlternateLabel = alternateLabel;
        ConfirmCommand = new RelayCommand(() => completion.TrySetResult(DialogChoice.Confirm));
        CancelCommand = new RelayCommand(() => completion.TrySetResult(DialogChoice.Cancel));
        AlternateCommand = new RelayCommand(() => completion.TrySetResult(DialogChoice.Alternate), () => alternateLabel is not null);
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmLabel { get; }
    public bool IsDestructive { get; }
    /// <summary>Label of the optional third action (null when the dialog is a plain confirmation).</summary>
    public string? AlternateLabel { get; }
    public bool HasAlternate => AlternateLabel is not null;
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand AlternateCommand { get; }
    public Task<DialogChoice> Choice => completion.Task;
}

/// <summary>
/// In-shell confirmation for destructive and financial actions. Business pages ask by resource key; the dialog shows
/// localized text and returns only after an explicit choice. Cancel is the default.
/// </summary>
public sealed partial class ShellDialogService(ILocalizationService localization) : ObservableObject, IUserConfirmation
{
    [ObservableProperty] private DialogRequest? current;

    public async Task<bool> ConfirmAsync(string key) => await AskAsync(key, null).ConfigureAwait(true) == DialogChoice.Confirm;

    public Task<DialogChoice> ChooseAsync(string key, string alternateKey) => AskAsync(key, alternateKey);

    private async Task<DialogChoice> AskAsync(string key, string? alternateKey)
    {
        Current?.CancelCommand.Execute(null);
        var request = new DialogRequest(localization["D_ConfirmTitle"], localization[key], localization[ConfirmLabelKey(key)], IsDestructive(key),
            alternateKey is null ? null : localization[alternateKey]);
        Current = request;
        try
        {
            return await request.Choice.ConfigureAwait(true);
        }
        finally
        {
            if (ReferenceEquals(Current, request)) Current = null;
        }
    }

    private static bool IsDestructive(string key) => key is "D_ConfirmDeleteDraft" or "D_ConfirmCloseDocument" or "D_ConfirmCloseDirtyDocument" or "D_ConfirmDeleteAccount" or "D_ConfirmLogoutAll" or "D_ConfirmClosePeriod"
        or "D_ConfirmPostJournal" or "D_ConfirmPostReversal" or "D_ConfirmCreateReversal" or "D_ConfirmReopenPeriod" or "D_ConfirmSoftClosePeriod"
        or "D_ConfirmBulkPosting";

    private static string ConfirmLabelKey(string key) => key switch
    {
        "D_ConfirmDeleteDraft" => "D_DeleteDraft",
        "D_ConfirmPostJournal" => "D_PostJournal",
        "D_ConfirmPostReversal" => "D_PostReversal",
        "D_ConfirmCreateReversal" => "D_CreateReversal",
        "D_ConfirmBulkPosting" => "D_PostAll",
        "D_ConfirmClosePeriod" => "D_ClosePeriod",
        "D_ConfirmSoftClosePeriod" => "D_SoftClosePeriod",
        "D_ConfirmReopenPeriod" => "D_ReopenPeriod",
        "D_ConfirmLogoutAll" => "D_LogoutAll",
        "D_ConfirmCloseDocument" => "D_CloseTab",
        "D_ConfirmCloseDirtyDocument" => "D_DiscardAndClose",
        "D_ConfirmDeleteAccount" => "D_DeletePermanently",
        "UI_ConfirmResetLayouts" => "UI_ResetLayouts",
        "UI_ConfirmResetAppearance" => "UI_ResetAppearance",
        _ => "D_Confirm",
    };
}
