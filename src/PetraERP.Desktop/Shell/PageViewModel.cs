using CommunityToolkit.Mvvm.ComponentModel;
using PetraERP.Desktop.Localization;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// Shared page mechanics: the authorized <see cref="ShellContext"/>, a cancellation scope renewed on every context
/// change (late responses of a previous company/branch are dropped), and operations routed through
/// <see cref="DesktopOperation"/> so busy state and localized failures are uniform across screens.
/// </summary>
public abstract class PageViewModel : ObservableObject, IShellPage, ILoadingAware, IDisposable
{
    private CancellationTokenSource scope = new();
    private ShellContext? context;

    protected PageViewModel(DesktopOperation operation, ILocalizationService localization)
    {
        Operation = operation;
        Localization = localization;
        operation.PropertyChanged += OperationChanged;
    }

    protected abstract string RequiredPermission { get; }
    public DesktopOperation Operation { get; }
    /// <summary>This screen's own operation state: skeletons on first load, refresh bar later, save/post/unknown outcome.</summary>
    public PetraLoadingState Loading { get; } = new();
    public ILocalizationService Localization { get; }
    public ShellContext? Context { get => context; private set { if (SetProperty(ref context, value)) { OnPropertyChanged(nameof(HasContext)); NotifyCommands(); } } }
    public bool HasContext => Context is not null;
    protected CancellationToken Token => scope.Token;
    protected Guid CompanyId => Context?.CompanyId ?? Guid.Empty;
    protected Guid BranchId => Context?.BranchId ?? Guid.Empty;
    protected bool Can(string permission) => Context?.Can(permission) == true;
    /// <summary>
    /// True only when neither a shell-wide operation nor this document's local save/post is active. Document-local
    /// operations intentionally keep the document open on transport failures, but their commands must still disable
    /// immediately so a rapid click or shortcut cannot submit the same mutation twice.
    /// </summary>
    protected bool Idle => !Operation.IsBusy && !Loading.IsSubmitting;

    public async Task ActivateAsync(ShellContext shellContext, object? parameter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shellContext);
        if (!shellContext.Can(RequiredPermission)) { Clear(); Operation.SetError("PermissionDenied"); return; }
        if (Context is null || Context.CompanyId != shellContext.CompanyId || Context.BranchId != shellContext.BranchId
            || Context.FiscalYearId != shellContext.FiscalYearId)
        {
            Clear();
        }

        Context = shellContext;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(Token, cancellationToken);
        Loading.Begin(OperationKind.Read);
        try
        {
            await LoadAsync(parameter, linked.Token).ConfigureAwait(true);
            Loading.Complete(OperationKind.Read);
        }
        catch (Exception exception)
        {
            Loading.Fail(exception, OperationKind.Read);
            throw;
        }
    }

    public void Clear()
    {
        scope.Cancel();
        scope.Dispose();
        scope = new CancellationTokenSource();
        Context = null;
        Loading.Reset();
        ClearPage();
        NotifyCommands();
    }

    /// <summary>Loads page data for the current context; <paramref name="parameter"/> is a typed drill-down request.</summary>
    protected abstract Task LoadAsync(object? parameter, CancellationToken cancellationToken);

    protected abstract void ClearPage();

    protected virtual void NotifyCommands()
    {
    }

    /// <summary>Runs through the shared operation; returns false when busy or failed (failure is already presented).</summary>
    protected Task<bool> RunAsync(Func<CancellationToken, Task> action) => RunAsync(action, OperationKind.Read);

    /// <summary>
    /// Runs a document-local save or post that must survive a lost response (the document keeps its own retry/idempotency
    /// state). Updates this screen's state and blocks a second submit, but never triggers the shell's offline reset.
    /// </summary>
    protected async Task<bool> RunDocumentAsync(Func<Task> action, OperationKind kind)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!Loading.Begin(kind)) return false;
        try
        {
            await action().ConfigureAwait(true);
            Loading.Complete(kind);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Loading.Fail(exception, kind, "D_" + Connection.DesktopErrors.Key(exception));
            Operation.Report(exception, kind);
            return false;
        }
    }

    /// <summary>Runs a read, save or post; a second save/post on this screen is rejected while one is in flight.</summary>
    protected Task<bool> RunAsync(Func<CancellationToken, Task> action, OperationKind kind)
    {
        ArgumentNullException.ThrowIfNull(action);
        var token = Token;
        return Operation.RunAsync(() => action(token), kind, Loading);
    }

    private void OperationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DesktopOperation.IsBusy)) NotifyCommands();
    }

    public void Dispose()
    {
        Operation.PropertyChanged -= OperationChanged;
        scope.Cancel();
        scope.Dispose();
        GC.SuppressFinalize(this);
    }
}
