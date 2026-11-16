using CommunityToolkit.Mvvm.ComponentModel;
using PetraERP.Desktop.GeneralLedger;

namespace PetraERP.Desktop.Shell;

/// <summary>The states every screen operation can be in. There are no progress percentages: the server reports none.</summary>
public enum LoadState
{
    Idle,
    /// <summary>First load of a screen: skeleton placeholders are shown.</summary>
    Loading,
    /// <summary>Reload of a screen that already shows data: the data stays visible with a thin activity bar.</summary>
    Refreshing,
    Saving,
    Posting,
    Succeeded,
    Failed,
    Cancelled,
    /// <summary>A save or post whose result could not be confirmed (timeout, transport loss, 408/5xx). Never shown as failed.</summary>
    OutcomeUnknown,
}

/// <summary>What an operation does; decides the in-progress state and how an unconfirmed result is classified.</summary>
public enum OperationKind
{
    Read,
    Save,
    Post,
}

/// <summary>
/// Observable operation state for one screen (or for the shell). Begin/Complete/Fail are called by the shared operation
/// runner. Saving and posting block a second submit until the first finishes, and nothing here touches the user's input:
/// a failed or unknown save leaves the form exactly as typed.
/// </summary>
public sealed class PetraLoadingState : ObservableObject
{
    private LoadState state;
    private bool hasLoadedOnce;
    private string? detailKey;

    public LoadState State
    {
        get => state;
        private set
        {
            if (!SetProperty(ref state, value)) return;
            OnPropertyChanged(nameof(ShowSkeleton)); OnPropertyChanged(nameof(IsRefreshing)); OnPropertyChanged(nameof(IsSubmitting));
            OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(IsProblem)); OnPropertyChanged(nameof(IsOutcomeUnknown));
            OnPropertyChanged(nameof(MessageKey));
        }
    }

    /// <summary>True after the first successful read; later reads are refreshes and never show skeletons again.</summary>
    public bool HasLoadedOnce { get => hasLoadedOnce; private set { if (SetProperty(ref hasLoadedOnce, value)) OnPropertyChanged(nameof(ShowSkeleton)); } }

    public bool ShowSkeleton => State == LoadState.Loading && !HasLoadedOnce;
    public bool IsRefreshing => State == LoadState.Refreshing;
    public bool IsSubmitting => State is LoadState.Saving or LoadState.Posting;
    public bool IsActive => State is LoadState.Loading or LoadState.Refreshing or LoadState.Saving or LoadState.Posting;
    public bool IsProblem => State is LoadState.Failed or LoadState.OutcomeUnknown;
    public bool IsOutcomeUnknown => State == LoadState.OutcomeUnknown;
    /// <summary>Localization key of the state message ("UI_State_Saving", "UI_State_OutcomeUnknown", ...).</summary>
    public string MessageKey => "UI_State_" + State;
    /// <summary>Localization key of the failure reason ("D_ServerUnavailable", ...), when known.</summary>
    public string? DetailKey { get => detailKey; private set => SetProperty(ref detailKey, value); }

    /// <summary>Starts an operation. Returns false (and changes nothing) when a save or post is already in flight.</summary>
    public bool Begin(OperationKind kind)
    {
        if (IsSubmitting) return false;
        DetailKey = null;
        State = kind switch
        {
            OperationKind.Save => LoadState.Saving,
            OperationKind.Post => LoadState.Posting,
            _ => HasLoadedOnce ? LoadState.Refreshing : LoadState.Loading,
        };
        return true;
    }

    public void Complete(OperationKind kind)
    {
        if (kind == OperationKind.Read) HasLoadedOnce = true;
        State = LoadState.Succeeded;
    }

    public void Fail(Exception exception, OperationKind kind, string? detailKey = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        DetailKey = detailKey;
        State = Classify(exception, kind);
    }

    public void Cancel() => State = LoadState.Cancelled;

    /// <summary>Forgets everything (context change, sign-out): the next read is a first load again.</summary>
    public void Reset()
    {
        DetailKey = null; HasLoadedOnce = false; State = LoadState.Idle;
    }

    /// <summary>
    /// A cancelled read is Cancelled. A mutation whose request may have reached the server (timeout, cancellation,
    /// transport failure, 408/5xx) is OutcomeUnknown — the user must refresh and check before retrying. A read never
    /// changes data, so its timeout is a retryable failure.
    /// </summary>
    public static LoadState Classify(Exception exception, OperationKind kind)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var timedOut = exception is TimeoutException || exception.InnerException is TimeoutException;
        if (kind == OperationKind.Read) return !timedOut && exception is OperationCanceledException ? LoadState.Cancelled : LoadState.Failed;
        // A cancelled mutation may already have been committed by the server, so it is unknown rather than cancelled.
        return timedOut || exception is OperationCanceledException || PostingAttemptTracker.IsAmbiguous(exception)
            ? LoadState.OutcomeUnknown : LoadState.Failed;
    }
}

/// <summary>A page that exposes its own operation state to the shell's loading host.</summary>
public interface ILoadingAware
{
    PetraLoadingState Loading { get; }
}
