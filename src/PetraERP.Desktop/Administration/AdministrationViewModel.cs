using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Administration;

public sealed partial class AdministrationViewModel : ObservableObject, IDisposable
{
    private readonly IAdministrationClient client;
    private readonly IUserConfirmation confirmation;
    private readonly DesktopOperation operation;
    private readonly ILocalizationService localization;
    private HashSet<string> permissions = new(StringComparer.Ordinal);
    private CancellationTokenSource context = new();
    private UserDetailsResponse? user;
    private RoleDetailsResponse? role;
    private IReadOnlyList<OrganizationCompanyResponse> organization = [];
    private IReadOnlyList<RoleSummaryResponse> roleCatalog = [];
    private IReadOnlyList<PermissionResponse> permissionCatalog = [];
    private long epoch;
    public AdministrationViewModel(IAdministrationClient client, IUserConfirmation confirmation, DesktopOperation operation, ILocalizationService localization)
    {
        this.client = client; this.confirmation = confirmation; this.operation = operation; this.localization = localization;
        SearchUsersCommand = new AsyncRelayCommand(() => RunAsync(() => LoadUsersAsync(1, context.Token)), () => Can("System.Users.View"));
        PreviousUsersCommand = new AsyncRelayCommand(() => RunAsync(() => LoadUsersAsync(UserPage - 1, context.Token)), () => Can("System.Users.View") && UserPage > 1);
        NextUsersCommand = new AsyncRelayCommand(() => RunAsync(() => LoadUsersAsync(UserPage + 1, context.Token)), () => Can("System.Users.View") && UserPage * 50L < UserTotal);
        OpenUserCommand = new AsyncRelayCommand(() => RunAsync(OpenUserAsync), () => Can("System.Users.View") && SelectedUser is not null);
        NewUserCommand = new RelayCommand(NewUser, () => CanManageUsers);
        SaveProfileCommand = new AsyncRelayCommand(() => MutateUserAsync(UserMutation.Profile), () => CanManageUsers && IsUserEditorOpen);
        SaveUserRolesCommand = new AsyncRelayCommand(() => MutateUserAsync(UserMutation.Roles), () => CanManageUsers && CanViewRoles && user is not null);
        SaveScopesCommand = new AsyncRelayCommand(() => MutateUserAsync(UserMutation.Scopes), () => CanManageUsers && user is not null);
        SetStatusCommand = new AsyncRelayCommand(() => MutateUserAsync(UserMutation.Status), () => CanManageUsers && user is not null);
        UnlockCommand = new AsyncRelayCommand(() => MutateUserAsync(UserMutation.Unlock), () => CanManageUsers && user is not null);
        ResetPasswordCommand = new AsyncRelayCommand(() => MutateUserAsync(UserMutation.Password), () => CanManageUsers && user is not null);
        RevokeCommand = new AsyncRelayCommand(() => MutateUserAsync(UserMutation.Revoke), () => CanManageUsers && user is not null);
        OpenRoleCommand = new AsyncRelayCommand(() => RunAsync(OpenRoleAsync), () => Can("System.Roles.View") && SelectedRole is not null);
        NewRoleCommand = new RelayCommand(NewRole, () => CanManageRoles);
        SaveRoleCommand = new AsyncRelayCommand(SaveRoleAsync, () => CanManageRoles && IsRoleEditorOpen);
        RefreshRolesCommand = new AsyncRelayCommand(() => RunAsync(() => LoadPageAsync("roles", context.Token)), () => Can("System.Roles.View"));
        ClearAuditFiltersCommand = new AsyncRelayCommand(() => { AuditUser = AuditFrom = AuditTo = string.Empty; AuditType = null; return RunAsync(() => LoadAuditAsync(1, context.Token)); }, () => Can("Audit.Security.View"));
        SearchAuditCommand = new AsyncRelayCommand(() => RunAsync(() => LoadAuditAsync(1, context.Token)), () => Can("Audit.Security.View"));
        PreviousAuditCommand = new AsyncRelayCommand(() => RunAsync(() => LoadAuditAsync(AuditPage - 1, context.Token)), () => Can("Audit.Security.View") && AuditPage > 1);
        NextAuditCommand = new AsyncRelayCommand(() => RunAsync(() => LoadAuditAsync(AuditPage + 1, context.Token)), () => Can("Audit.Security.View") && AuditPage * 50L < AuditTotal);
        operation.PropertyChanged += OperationChanged;
    }
    public bool IsConfigured { get; private set; }
    public bool CanManageUsers => Can("System.Users.View") && permissions.Contains("System.Users.Manage");
    public bool CanManageRoles => Can("System.Roles.View") && permissions.Contains("System.Roles.Manage");
    public bool CanViewRoles => permissions.Contains("System.Roles.View");
    public bool IsExistingUser => user is not null;
    public bool IsNewUser => IsUserEditorOpen && user is null;
    public bool IsNewRole => IsRoleEditorOpen && role is null;
    public string UserState => user is null ? string.Empty : localization[user.IsActive ? "D_Enabled" : "D_Disabled"] + " · " + localization["D_PasswordRequired"] + ": " + localization[user.MustChangePassword ? "D_Yes" : "D_No"] + " · " + localization["D_Locked"] + ": " + localization[user.LockoutEndUtc > DateTimeOffset.UtcNow ? "D_Yes" : "D_No"];
    public string UserVersion => user?.Version ?? string.Empty;
    public string RoleVersion => role?.Version ?? string.Empty;
    public ObservableCollection<UserSummaryResponse> Users { get; } = [];
    public ObservableCollection<RoleSummaryResponse> Roles { get; } = [];
    public ObservableCollection<GrantOption> UserRoles { get; } = [];
    public ObservableCollection<CompanyGrantOption> Scopes { get; } = [];
    public ObservableCollection<GrantOption> RolePermissions { get; } = [];
    public ObservableCollection<SecurityAuditEventResponse> Audit { get; } = [];
    public IReadOnlyList<SecurityAuditEventType> AuditTypes { get; } = Enum.GetValues<SecurityAuditEventType>();
    public string UserPageStatus => $"{UserPage} · {Users.Count} / {UserTotal}";
    public string AuditPageStatus => $"{AuditPage} · {Audit.Count} / {AuditTotal}";
    [ObservableProperty] private string userSearch = string.Empty;
    [ObservableProperty] private UserSummaryResponse? selectedUser;
    [ObservableProperty] private RoleSummaryResponse? selectedRole;
    [ObservableProperty] private bool isUserEditorOpen;
    [ObservableProperty] private bool isRoleEditorOpen;
    [ObservableProperty] private string loginName = string.Empty;
    [ObservableProperty] private string displayArabic = string.Empty;
    [ObservableProperty] private string? displayEnglish;
    [ObservableProperty] private string temporaryPassword = string.Empty;
    [ObservableProperty] private string reason = string.Empty;
    [ObservableProperty] private string roleCode = string.Empty;
    [ObservableProperty] private string roleArabic = string.Empty;
    [ObservableProperty] private string? roleEnglish;
    [ObservableProperty] private SecurityAuditEventResponse? selectedAudit;
    [ObservableProperty] private string auditUser = string.Empty;
    [ObservableProperty] private SecurityAuditEventType? auditType;
    [ObservableProperty] private string auditFrom = string.Empty;
    [ObservableProperty] private string auditTo = string.Empty;
    [ObservableProperty] private int userPage = 1;
    [ObservableProperty] private long userTotal;
    [ObservableProperty] private int auditPage = 1;
    [ObservableProperty] private long auditTotal;
    partial void OnSelectedUserChanged(UserSummaryResponse? value) => Notify();
    partial void OnSelectedRoleChanged(RoleSummaryResponse? value) => Notify();
    public AsyncRelayCommand SearchUsersCommand { get; }
    public AsyncRelayCommand PreviousUsersCommand { get; }
    public AsyncRelayCommand NextUsersCommand { get; }
    public AsyncRelayCommand OpenUserCommand { get; }
    public RelayCommand NewUserCommand { get; }
    public AsyncRelayCommand SaveProfileCommand { get; }
    public AsyncRelayCommand SaveUserRolesCommand { get; }
    public AsyncRelayCommand SaveScopesCommand { get; }
    public AsyncRelayCommand SetStatusCommand { get; }
    public AsyncRelayCommand UnlockCommand { get; }
    public AsyncRelayCommand ResetPasswordCommand { get; }
    public AsyncRelayCommand RevokeCommand { get; }
    public AsyncRelayCommand OpenRoleCommand { get; }
    public RelayCommand NewRoleCommand { get; }
    public AsyncRelayCommand SaveRoleCommand { get; }
    public AsyncRelayCommand RefreshRolesCommand { get; }
    public AsyncRelayCommand ClearAuditFiltersCommand { get; }
    public AsyncRelayCommand SearchAuditCommand { get; }
    public AsyncRelayCommand PreviousAuditCommand { get; }
    public AsyncRelayCommand NextAuditCommand { get; }
    public void Configure(IReadOnlyCollection<string> granted)
    {
        Clear(); permissions = new HashSet<string>(granted, StringComparer.Ordinal); IsConfigured = true; Notify();
    }
    public void Clear()
    {
        epoch++; context.Cancel(); context.Dispose(); context = new CancellationTokenSource(); permissions.Clear(); IsConfigured = false;
        Users.Clear(); Roles.Clear(); UserRoles.Clear(); Scopes.Clear(); RolePermissions.Clear(); Audit.Clear(); SelectedAudit = null;
        user = null; role = null; organization = []; roleCatalog = []; permissionCatalog = [];
        IsUserEditorOpen = IsRoleEditorOpen = false; SelectedUser = null; SelectedRole = null;
        LoginName = DisplayArabic = TemporaryPassword = Reason = RoleCode = RoleArabic = UserSearch = AuditUser = AuditFrom = AuditTo = string.Empty;
        DisplayEnglish = RoleEnglish = null; AuditType = null; UserTotal = AuditTotal = 0; Notify();
    }
    private bool Can(string permission) => permissions.Contains(permission) && !operation.IsBusy;
    private Task<bool> RunAsync(Func<Task> action) => operation.RunAsync(action);
    private void OperationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => Notify();
    private void Notify()
    {
        OnPropertyChanged(nameof(CanManageUsers)); OnPropertyChanged(nameof(CanManageRoles)); OnPropertyChanged(nameof(CanViewRoles));
        OnPropertyChanged(nameof(IsExistingUser)); OnPropertyChanged(nameof(IsNewUser)); OnPropertyChanged(nameof(IsNewRole));
        OnPropertyChanged(nameof(UserState)); OnPropertyChanged(nameof(UserVersion)); OnPropertyChanged(nameof(RoleVersion)); OnPropertyChanged(nameof(UserPageStatus)); OnPropertyChanged(nameof(AuditPageStatus));
        foreach (var command in new IRelayCommand[] { SearchUsersCommand, PreviousUsersCommand, NextUsersCommand, OpenUserCommand, NewUserCommand, SaveProfileCommand,
            SaveUserRolesCommand, SaveScopesCommand, SetStatusCommand, UnlockCommand, ResetPasswordCommand, RevokeCommand, OpenRoleCommand, NewRoleCommand,
            SaveRoleCommand, RefreshRolesCommand, ClearAuditFiltersCommand, SearchAuditCommand, PreviousAuditCommand, NextAuditCommand }) command?.NotifyCanExecuteChanged();
    }
    public void Dispose() { operation.PropertyChanged -= OperationChanged; context.Cancel(); context.Dispose(); GC.SuppressFinalize(this); }
}
