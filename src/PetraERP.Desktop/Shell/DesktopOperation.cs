using CommunityToolkit.Mvvm.ComponentModel;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Localization;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// Runs one user operation at a time, tracks busy/online state and turns failures into a localized business message.
/// When a <see cref="NotificationCenter"/> is supplied, failures also raise a notification whose optional details carry
/// safe technical identifiers (problem code, HTTP status, correlation id) and the server's user-facing problem detail —
/// never server exception text or stack traces.
/// </summary>
public sealed class DesktopOperation(ILocalizationService localization, NotificationCenter? notifications = null) : ObservableObject
{
    private bool busy;
    private bool online;
    private string? error;
    public bool IsBusy { get => busy; private set => SetProperty(ref busy, value); }
    public bool IsOnline { get => online; private set => SetProperty(ref online, value); }
    public string Error => error is null ? string.Empty : localization["D_" + error];
    /// <summary>Stable key of the last failure (for example "Conflict", "ServerUnavailable"), or null.</summary>
    public string? ErrorKey => error;
    public string Connection => localization[IsOnline ? "D_Connected" : "D_Disconnected"];
    public event EventHandler<Exception>? Failed;
    public void SetError(string key) { error = key; OnPropertyChanged(nameof(Error)); OnPropertyChanged(nameof(ErrorKey)); }
    public void Localize() { OnPropertyChanged(nameof(Error)); OnPropertyChanged(nameof(Connection)); }
    /// <summary>The shell-wide state of the operation in flight (first load, refresh, save, post, outcome).</summary>
    public PetraLoadingState Loading { get; } = new();

    public Task<bool> RunAsync(Func<Task> action) => RunAsync(action, OperationKind.Read);

    /// <summary>
    /// Runs one operation. <paramref name="kind"/> selects the in-progress state; <paramref name="page"/> (optional) is the
    /// screen's own state, updated alongside the shell state. A second call while one is running is rejected, so a save or
    /// post can never be submitted twice. An unconfirmed save/post is reported as "outcome unknown", never as failed.
    /// </summary>
    public async Task<bool> RunAsync(Func<Task> action, OperationKind kind, PetraLoadingState? page = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsBusy || page?.IsSubmitting == true) return false;
        IsBusy = true; error = null; OnPropertyChanged(nameof(Error)); OnPropertyChanged(nameof(ErrorKey));
        Loading.Begin(kind); page?.Begin(kind);
        try
        {
            await action().ConfigureAwait(true); IsOnline = true;
            Loading.Complete(kind); page?.Complete(kind);
            return true;
        }
        catch (Exception exception)
        {
            var key = DesktopErrors.Key(exception); SetError(key);
            Loading.Fail(exception, kind, "D_" + key); page?.Fail(exception, kind, "D_" + key);
            if (key is "ServerUnavailable" or "TlsFailure" or "SessionEnded" or "PermissionDenied" or "ContextUnavailable" or "AccountDisabled") IsOnline = false;
            if (Loading.IsOutcomeUnknown) notifications?.Warning("UI_OutcomeUnknownMessage", Details(exception));
            else notifications?.Error("D_" + key, TitleKey(key), Details(exception));
            Failed?.Invoke(this, exception); return false;
        }
        finally { IsBusy = false; OnPropertyChanged(nameof(Connection)); }
    }

    /// <summary>
    /// Reports a failure of a document-local operation (for example a card's post that keeps its own retry state): sets
    /// the localized error and raises the notification, but does not mark the shell offline or raise <see cref="Failed"/>,
    /// so the open document and the user's input stay in place. An unconfirmed save/post is reported as "outcome unknown".
    /// </summary>
    public void Report(Exception exception, OperationKind kind)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var key = DesktopErrors.Key(exception); SetError(key);
        if (PetraLoadingState.Classify(exception, kind) == LoadState.OutcomeUnknown) notifications?.Warning("UI_OutcomeUnknownMessage", Details(exception));
        else notifications?.Error("D_" + key, TitleKey(key), Details(exception));
    }

    /// <summary>Groups failures for the notification title: network, conflict, session, validation or general.</summary>
    public static string TitleKey(string key) => key switch
    {
        "ServerUnavailable" or "TlsFailure" => "D_Toast_Network",
        "Conflict" or "IdempotencyConflict" or "PostingConflict" => "D_Toast_Conflict",
        "SessionEnded" or "AccountDisabled" => "D_Toast_Session",
        "Validation" or "PasswordRule" or "InsufficientStock" or "PeriodClosed" or "PeriodMissing" or "AccountInvalid" or "CostCenterRequired" or "CostCenterNotAllowed"
            or "CostCenterInvalid" or "CurrencyInvalid" or "JournalStateInvalid" or "JournalUnbalanced" or "ScopeInactive"
            or "AccountCodeTaken" or "CostCenterCodeTaken" or "AccountParentInvalid" or "AccountHierarchyCycle" or "AccountInUse"
            or "AccountHasChildren" or "CostCenterInUse" or "FiscalYearOverlap" or "FiscalYearInvalid" or "PeriodTransitionInvalid"
            or "OperationDenied" or "NegativeStockReasonRequired" or "NegativeStockCostUnknown" or "ValuationMismatch" => "D_Toast_Validation",
        "PermissionDenied" or "ContextUnavailable" or "NegativeStockApprovalDenied" => "D_Toast_Permission",
        _ => "D_Toast_Error",
    };

    /// <summary>Safe technical identifiers plus the server's user-facing problem detail, never exception text.</summary>
    public static string? Details(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is not PetraApiException api)
        {
            // The readable cause of a local failure sits in its innermost exception (a XamlParseException hides the
            // missing resource key there), and the operator's screenshot is the only evidence support receives.
            var inner = exception;
            while (inner.InnerException is { } next && next is not PetraApiException) inner = next;
            var message = inner.Message.ReplaceLineEndings(" ").Trim();
            if (message.Length > 200) message = message[..200] + "…";
            return message.Length == 0 ? inner.GetType().Name : inner.GetType().Name + ": " + message;
        }

        var technical = $"HTTP {api.StatusCode} · {DesktopErrors.Code(api) ?? "-"}";
        // The problem detail carries the actionable business reason (for example the item, warehouse and available
        // quantity). Keeping it out of the notification body preserves the localized message; dropping it entirely left
        // the operator with a bare code and no way to act.
        return DesktopErrors.Diagnostics(api) is { } reason ? technical + " · " + reason : technical;
    }
}
