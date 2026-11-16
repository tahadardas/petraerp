using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Accounting;

/// <summary>One row of the backup catalogue as shown to the operator (no identifiers).</summary>
public sealed record BackupRow(Guid Id, string DatabaseName, string BackupType, string FilePath, string Checksum, string Created, string Verified, bool IsVerified, string Protection, bool IsEncrypted)
{
    public static BackupRow From(BackupCatalogItemResponse item, string notVerified, string compressed, string protectedText) => new(
        item.Id, item.DatabaseName, item.BackupType, item.BackupFilePath,
        item.ChecksumSha256.Length > 16 ? item.ChecksumSha256[..16] + "…" : item.ChecksumSha256,
        item.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        item.VerifiedAtUtc is { } at ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : notVerified,
        item.VerifiedAtUtc.HasValue,
        item.IsEncrypted == true ? protectedText : item.IsCompressed == true ? compressed : "—",
        item.IsEncrypted == true);
}

/// <summary>
/// Backup and restore checks (Al-Ameen "النسخ الاحتياطي"): create a native backup with checksum, verify an existing one, and
/// restore it into a disposable database to prove it can be restored (DBCC CHECKDB). The server never overwrites the live
/// database from this screen, so a test restore cannot damage working data.
/// </summary>
public sealed partial class BackupViewModel : PageViewModel
{
    private readonly DesktopSession session;

    [ObservableProperty] private string databaseName = string.Empty;
    [ObservableProperty] private string backupDirectory = string.Empty;
    /// <summary>Optional: the new backup is encrypted with it and cannot be restored without it. Kept only in memory.</summary>
    [ObservableProperty] private string newBackupPassword = string.Empty;
    /// <summary>The password of the selected backup when it is protected.</summary>
    [ObservableProperty] private string selectedBackupPassword = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool lastActionFailed;
    [ObservableProperty] private BackupRow? selectedBackup;

    public BackupViewModel(DesktopSession session, DesktopOperation operation, ILocalizationService localization) : base(operation, localization)
    {
        this.session = session;
        BackupCommand = new AsyncRelayCommand(CreateAsync, () => HasContext && Idle && !string.IsNullOrWhiteSpace(DatabaseName));
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(LoadListAsync), () => HasContext && Idle);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, () => HasContext && Idle && SelectedBackup is not null);
        TestRestoreCommand = new AsyncRelayCommand(TestRestoreAsync, () => HasContext && Idle && SelectedBackup is not null);
    }

    protected override string RequiredPermission => "System.Database.Manage";

    public ObservableCollection<BackupRow> Backups { get; } = [];
    public IAsyncRelayCommand BackupCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand VerifyCommand { get; }
    public IAsyncRelayCommand TestRestoreCommand { get; }
    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    partial void OnDatabaseNameChanged(string value) => NotifyCommands();
    partial void OnSelectedBackupChanged(BackupRow? value) => NotifyCommands();
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken) => LoadListAsync(cancellationToken);

    private async Task LoadListAsync(CancellationToken token)
    {
        var items = await session.Api.ListBackupsAsync(string.IsNullOrWhiteSpace(DatabaseName) ? null : DatabaseName.Trim(), token).ConfigureAwait(true);
        var keep = SelectedBackup?.Id;
        Backups.Clear();
        var notVerified = Localization["BK_NotVerified"];
        var compressed = Localization["BK_Compressed"]; var protectedText = Localization["BK_Protected"];
        foreach (var item in items.OrderByDescending(item => item.CreatedAtUtc)) Backups.Add(BackupRow.From(item, notVerified, compressed, protectedText));
        SelectedBackup = Backups.FirstOrDefault(row => row.Id == keep);
    }

    private Task<bool> CreateAsync() => RunAsync(async token =>
    {
        var result = await session.Api.BackupDatabaseAsync(new BackupDatabaseRequest
        {
            DatabaseName = DatabaseName.Trim(),
            BackupDirectory = string.IsNullOrWhiteSpace(BackupDirectory) ? null : BackupDirectory.Trim(),
            Password = string.IsNullOrEmpty(NewBackupPassword) ? null : NewBackupPassword,
        }, token).ConfigureAwait(true);
        NewBackupPassword = string.Empty;
        LastActionFailed = false;
        StatusMessage = string.Format(CultureInfo.CurrentCulture, Localization[result.IsEncrypted == true ? "BK_CreatedProtectedMessage" : "BK_CreatedMessage"], result.BackupFilePath);
        await LoadListAsync(token).ConfigureAwait(true);
    });

    private Task<bool> VerifyAsync() => RunAsync(async token =>
    {
        if (SelectedBackup is not { } row) return;
        var result = await session.Api.VerifyBackupAsync(new VerifyBackupRequest { BackupCatalogId = row.Id, Password = PasswordFor(row) }, token).ConfigureAwait(true);
        LastActionFailed = !result.IsReadSuccess;
        StatusMessage = result.IsReadSuccess ? Localization["BK_VerifyOk"] : Localization["BK_VerifyFailed"] + " " + result.ErrorDetails;
        await LoadListAsync(token).ConfigureAwait(true);
    });

    private Task<bool> TestRestoreAsync() => RunAsync(async token =>
    {
        if (SelectedBackup is not { } row) return;
        var result = await session.Api.TestRestoreBackupAsync(new TestRestoreRequest { BackupCatalogId = row.Id, Password = PasswordFor(row) }, token).ConfigureAwait(true);
        LastActionFailed = !(result.RestoreSuccess && result.DbccCheckdbPass);
        StatusMessage = (LastActionFailed ? Localization["BK_RestoreFailed"] : Localization["BK_RestoreOk"]) + " " + result.Message;
    });

    private string? PasswordFor(BackupRow row) => row.IsEncrypted && !string.IsNullOrEmpty(SelectedBackupPassword) ? SelectedBackupPassword : null;

    public void Localize() => NotifyCommands();

    protected override void ClearPage()
    {
        Backups.Clear(); SelectedBackup = null; StatusMessage = string.Empty; LastActionFailed = false; NewBackupPassword = string.Empty; SelectedBackupPassword = string.Empty;
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        BackupCommand?.NotifyCanExecuteChanged(); RefreshCommand?.NotifyCanExecuteChanged();
        VerifyCommand?.NotifyCanExecuteChanged(); TestRestoreCommand?.NotifyCanExecuteChanged();
    }
}
