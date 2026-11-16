using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Localization;

namespace PetraERP.Desktop.Shell;

/// <summary>The explicit mode of an entity card: read-only view, editing the shown record, or creating a new one.</summary>
public enum EntityCardMode
{
    View,
    Edit,
    New,
}

/// <summary>
/// Reusable card mechanics for master records: explicit View / Edit / New modes, Save and Cancel commands, unsaved-change
/// tracking (so tab close, logout and context switches apply the shell's dirty rules) and Save from the close dialog.
/// Primary Save command is accessible in authorized editable idle states and displays actionable validation feedback.
/// </summary>
public abstract class EntityCardViewModel : WorkspaceOwnedContent, ISavablePage
{
    private readonly IUserConfirmation confirmation;
    private EntityCardMode mode;
    private string validationMessage = string.Empty;
    private IReadOnlyList<string> validationErrors = [];
    private string validationFocusTarget = string.Empty;
    private int validationFocusRequest;

    protected EntityCardViewModel(DesktopOperation operation, ILocalizationService localization, IUserConfirmation confirmation) : base(operation, localization)
    {
        this.confirmation = confirmation;
        BeginEditCommand = new RelayCommand(() => Enter(EntityCardMode.Edit), () => Mode == EntityCardMode.View && Idle && CanEditRecord);
        BeginNewCommand = new RelayCommand(() => Enter(EntityCardMode.New), () => Mode == EntityCardMode.View && Idle && CanCreateRecord);
        SaveCardCommand = new AsyncRelayCommand(SaveCardAsync, () => Mode != EntityCardMode.View && Idle);
        SaveAsDraftCommand = new AsyncRelayCommand(SaveAsDraftOnlyAsync,
            () => SupportsSaveAsDraft && Mode != EntityCardMode.View && Idle);
        CancelCardCommand = new AsyncRelayCommand(CancelCardAsync, () => Mode != EntityCardMode.View && Idle);
    }

    public EntityCardMode Mode
    {
        get => mode;
        private set
        {
            if (!SetProperty(ref mode, value)) return;
            foreach (var name in new[] { nameof(IsViewMode), nameof(IsEditMode), nameof(IsNewMode), nameof(IsEditing), nameof(HasUnsavedChanges) }) OnPropertyChanged(name);
            NotifyCommands();
            OnModeChanged(value);
        }
    }

    /// <summary>Runs once the new mode is applied, so work that needs the final draft fields (not the transitional ones) has a hook.</summary>
    protected virtual void OnModeChanged(EntityCardMode mode) { }

    public bool IsViewMode => Mode == EntityCardMode.View;
    public bool IsEditMode => Mode == EntityCardMode.Edit;
    public bool IsNewMode => Mode == EntityCardMode.New;
    public bool IsEditing => Mode != EntityCardMode.View;
    public bool HasUnsavedChanges => IsEditing && HasDraftChanges;
    public bool CanSaveBeforeClose => HasUnsavedChanges && CanSaveDraft;

    public string ValidationMessage
    {
        get => validationMessage;
        set { if (SetProperty(ref validationMessage, value)) OnPropertyChanged(nameof(HasValidationErrors)); }
    }

    public IReadOnlyList<string> ValidationErrors
    {
        get => validationErrors;
        set { if (SetProperty(ref validationErrors, value)) OnPropertyChanged(nameof(HasValidationErrors)); }
    }

    public bool HasValidationErrors => ValidationErrors.Count > 0 || !string.IsNullOrWhiteSpace(ValidationMessage);
    public string ValidationFocusTarget { get => validationFocusTarget; private set => SetProperty(ref validationFocusTarget, value); }
    public int ValidationFocusRequest { get => validationFocusRequest; private set => SetProperty(ref validationFocusRequest, value); }

    public RelayCommand BeginEditCommand { get; }
    public RelayCommand BeginNewCommand { get; }
    public AsyncRelayCommand SaveCardCommand { get; }

    /// <summary>
    /// Explicit Save as Draft. It never approves or posts, regardless of the company posting mode, and is only
    /// shown by cards whose documents have a draft state.
    /// </summary>
    public AsyncRelayCommand SaveAsDraftCommand { get; }

    public AsyncRelayCommand CancelCardCommand { get; }

    /// <summary>True when the card exposes the separate Save as Draft action (documents with a draft state).</summary>
    public virtual bool SupportsSaveAsDraft => false;

    /// <summary>Permission and record state allow editing the shown record.</summary>
    protected abstract bool CanEditRecord { get; }
    /// <summary>Permission and record state allow creating a new record from this card.</summary>
    protected abstract bool CanCreateRecord { get; }
    /// <summary>The draft is complete enough to send (local checks only; the server validates again).</summary>
    public abstract bool CanSaveDraft { get; }
    protected abstract bool HasDraftChanges { get; }

    /// <summary>Returns individual actionable validation error messages when the draft is invalid.</summary>
    public virtual IReadOnlyList<string> GetValidationErrors() => [];

    /// <summary>Returns the attached validation-focus key for the first invalid field.</summary>
    protected virtual string? GetValidationFocusTarget() => null;

    /// <summary>Fills the draft fields for the mode (the shown record for Edit, defaults for New).</summary>
    protected abstract void LoadDraft(EntityCardMode mode);
    /// <summary>Sends the draft; returns false when nothing was saved (validation message already shown).</summary>
    protected abstract Task<bool> SaveDraftAsync(EntityCardMode mode);

    /// <summary>Sends the draft as a draft without approval or posting; override together with <see cref="SupportsSaveAsDraft"/>.</summary>
    protected virtual Task<bool> SaveDraftOnlyAsync(EntityCardMode mode) => SaveDraftAsync(mode);

    public async Task<bool> SaveBeforeCloseAsync()
    {
        if (!CanSaveBeforeClose) return false;
        await SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        return !HasUnsavedChanges;
    }

    /// <summary>Leaves Edit/New without asking (record reloaded, context cleared or the card moved to another record).</summary>
    protected void ResetMode() => Mode = EntityCardMode.View;

    /// <summary>Call when a draft field changes so the dirty marker and Save availability follow.</summary>
    protected void DraftChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanSaveDraft));
        SaveCardCommand.NotifyCanExecuteChanged();
    }

    protected void NotifyCardCommands()
    {
        BeginEditCommand?.NotifyCanExecuteChanged(); BeginNewCommand?.NotifyCanExecuteChanged();
        SaveCardCommand?.NotifyCanExecuteChanged(); SaveAsDraftCommand?.NotifyCanExecuteChanged();
        CancelCardCommand?.NotifyCanExecuteChanged();
    }

    private void Enter(EntityCardMode target)
    {
        ValidationErrors = [];
        ValidationMessage = string.Empty;
        LoadDraft(target);
        Mode = target;
    }

    private Task SaveCardAsync() => SaveCoreAsync(SaveDraftAsync);

    private Task SaveAsDraftOnlyAsync() => SaveCoreAsync(SaveDraftOnlyAsync);

    private async Task SaveCoreAsync(Func<EntityCardMode, Task<bool>> persist)
    {
        if (Mode == EntityCardMode.View || !Idle) return;
        var errors = GetValidationErrors();
        if (errors.Count > 0 || !CanSaveDraft)
        {
            ValidationErrors = errors;
            ValidationMessage = errors.Count > 0 ? string.Join("\n", errors) : Localization["D_RequiredFieldsIncomplete"];
            ValidationFocusTarget = GetValidationFocusTarget() ?? string.Empty;
            ValidationFocusRequest++;
            return;
        }

        ValidationErrors = [];
        ValidationMessage = string.Empty;

        var saving = Mode;
        if (saving == EntityCardMode.New)
        {
            if (EntryOptionsHub.Current.ConfirmOnAdd && !await confirmation.ConfirmAsync("D_ConfirmAdd").ConfigureAwait(true)) return;
        }
        else if (saving == EntityCardMode.Edit)
        {
            if (EntryOptionsHub.Current.ConfirmOnEdit && !await confirmation.ConfirmAsync("D_ConfirmEdit").ConfigureAwait(true)) return;
        }

        var saved = false;
        var operation = RunDocumentAsync(async () => saved = await persist(saving).ConfigureAwait(true), OperationKind.Save);
        NotifyCardCommands();
        var completed = await operation.ConfigureAwait(true);
        NotifyCardCommands();
        if (completed && saved && Mode == saving)
        {
            Mode = EntityCardMode.View;
        }
    }

    private async Task CancelCardAsync()
    {
        if (HasUnsavedChanges && EntryOptionsHub.Current.ConfirmOnDiscard && !await confirmation.ConfirmAsync("D_ConfirmDiscardChanges").ConfigureAwait(true)) return;
        ValidationErrors = [];
        ValidationMessage = string.Empty;
        Mode = EntityCardMode.View;
    }
}
