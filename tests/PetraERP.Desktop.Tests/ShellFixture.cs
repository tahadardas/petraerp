using PetraERP.ApiClient;
using PetraERP.Desktop.Administration;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Productivity;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Tests;

internal sealed class ShellFixture : IDesktopClient, IContextPreferenceStore, IUserConfirmation, IThemeService
{
    public CompanyContextResponse Company { get; } = new() { Id = Guid.NewGuid(), Code = "A", NameArabic = "شركة أ", NameEnglish = "Company A", BaseCurrencyCode = "JOD" };
    public BranchContextResponse Branch { get; }
    public CurrentUserResponse User { get; } = new() { UserId = Guid.NewGuid(), SessionId = Guid.NewGuid(), UserName = "admin", DisplayNameArabic = "مدير", DisplayNameEnglish = "Administrator", Permissions = ["System.Users.View", "System.Users.Manage", "System.Roles.View", "System.Roles.Manage", "Audit.Security.View"] };
    public TaskCompletionSource<bool>? LoginGate { get; set; }
    public Exception? LoginFailure { get; set; }
    public Exception? ProfileFailure { get; set; }
    public bool ForcePasswordChange { get; set; }
    public bool CompanyRevoked { get; set; }
    public List<CompanyContextResponse> AdditionalCompanies { get; } = [];
    public List<BranchContextResponse> AdditionalBranches { get; } = [];
    public bool Confirm { get; set; } = true;
    public string? LastConfirmation { get; private set; }
    public bool WasCleared { get; private set; }
    public bool? LogoutAll { get; private set; }
    public int LoginCount { get; private set; }

    /// <summary>Counts identity round trips so a test can prove opening a window no longer waits on one.</summary>
    public int ProfileCalls { get; private set; }

    public int CompanyScopeCalls { get; private set; }
    public ContextPreference? Saved { get; set; }
    public ThemePreference Preference { get; private set; }
    public AppTheme EffectiveTheme => ThemeResolver.Resolve(Preference, true);
    public event EventHandler? ThemeChanged;
    public ShellFixture() => Branch = new() { Id = Guid.NewGuid(), CompanyId = Company.Id, Code = "MAIN", NameArabic = "رئيسي" };
    public void Apply(ThemePreference preference) { Preference = preference; ThemeChanged?.Invoke(this, EventArgs.Empty); }
    /// <summary>The company currency register the document pages read; tests assert against this rather than a guessed default.</summary>
    internal CurrencyFixture Currencies { get; private set; } = new();

    public DesktopViewModel Create(IAdministrationClient administration, IGeneralLedgerClient? ledger = null, IWorkspaceClient? workspace = null, AccountingFixture? accounting = null, Sales.ISalesDocumentLinksClient? salesLinks = null, TreasuryFixture? treasury = null, PurchaseFixture? purchases = null,
        Relationships.IRelatedDocumentsClient? relatedDocuments = null, CurrencyFixture? currencies = null,
        Func<LocalizationService, UserPreferencesService>? preferences = null, Desktop.Currencies.IFxRevaluationClient? fxRevaluation = null, Desktop.Inventory.IInventoryDocumentsClient? inventoryDocuments = null)
    {
        Currencies = currencies ?? new CurrencyFixture();
        var localization = new LocalizationService("ar"); var operation = new DesktopOperation(localization);
        var ledgerClient = ledger ?? new NullGeneralLedgerClient();
        var navigator = new ShellNavigator();
        var pages = TestPages.Create(new AdministrationViewModel(administration, this, operation, localization),
            new GeneralLedgerViewModel(ledgerClient, this, operation, localization), ledgerClient, this, operation, localization, navigator, accounting, accounting, accounting, accounting, accounting, accounting, accounting, salesLinks, treasury, purchases, relatedDocuments, Currencies, fxRevaluation, inventoryDocuments);
        return new(this, this, this, localization, this, new NavigationService(localization), operation, pages,
            new WorkspaceViewModel(workspace ?? new WorkspaceFixture(), localization, this), navigator, new NotificationCenter(localization), TimeProvider.System, preferences?.Invoke(localization));
    }
    public Task<bool> LoginAsync(string userName, string password, CancellationToken cancellationToken) { LoginCount++; if (LoginGate is not null) return LoginGate.Task; return LoginFailure is null ? Task.FromResult(ForcePasswordChange) : Task.FromException<bool>(LoginFailure); }
    public Task<bool> ResumeAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    public Task ChangePasswordAsync(string current, string replacement, CancellationToken cancellationToken) { ForcePasswordChange = false; return Task.CompletedTask; }
    public Task LogoutAsync(bool all, CancellationToken cancellationToken) { LogoutAll = all; return Task.CompletedTask; }
    public Task ClearAsync(CancellationToken cancellationToken) { WasCleared = true; return Task.CompletedTask; }
    public Task<CurrentUserResponse> ProfileAsync(CancellationToken cancellationToken)
    {
        ProfileCalls++;
        return ProfileFailure is null ? Task.FromResult(User) : Task.FromException<CurrentUserResponse>(ProfileFailure);
    }

    public Task<CompanyContextListResponse> CompaniesAsync(CancellationToken cancellationToken)
    {
        CompanyScopeCalls++;
        return Task.FromResult(new CompanyContextListResponse { Items = CompanyRevoked ? [] : new[] { Company }.Concat(AdditionalCompanies).ToArray() });
    }
    public Task<BranchContextListResponse> BranchesAsync(Guid companyId, CancellationToken cancellationToken) => Task.FromResult(new BranchContextListResponse { Items = new[] { Branch }.Concat(AdditionalBranches).Where(branch => branch.CompanyId == companyId).ToArray() });
    public Task<ServiceStatusResponse> StatusAsync(CancellationToken cancellationToken) => Task.FromResult(new ServiceStatusResponse { Status = "Available" });
    public ContextPreference? Read(Guid userId) => Saved;
    public void Save(Guid userId, ContextPreference preference) => Saved = preference;
    public Task<bool> ConfirmAsync(string key) { LastConfirmation = key; LastAlternate = null; return Task.FromResult(Confirm); }
    /// <summary>Answer to a Save/Discard/Cancel prompt; null follows <see cref="Confirm"/> (Discard or Cancel).</summary>
    public DialogChoice? Choice { get; set; }
    public string? LastAlternate { get; private set; }
    public Task<DialogChoice> ChooseAsync(string key, string alternateKey)
    {
        LastConfirmation = key; LastAlternate = alternateKey;
        return Task.FromResult(Choice ?? (Confirm ? DialogChoice.Confirm : DialogChoice.Cancel));
    }
}
