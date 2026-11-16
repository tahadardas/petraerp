using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Parties;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Workspaces;

namespace PetraERP.Desktop.Shell;

public sealed partial class DesktopViewModel
{
    /// <summary>How long a validated session may serve navigation before the profile has to be checked again.</summary>
    private static readonly TimeSpan IdentityFreshness = TimeSpan.FromSeconds(30);

    private DateTimeOffset lastIdentityValidatedUtc;
    private Guid? reportingFiscalYearId;
    private Guid? selectedFinancialWorkspaceId;
    private DateOnly? fiscalYearStart;
    private DateOnly? fiscalYearEnd;
    private ShellContext? ShellContextOrNull => Profile is not null && Company is not null && Branch is not null
        && (client is not IFinancialWorkspaceSelector || selectedFinancialWorkspaceId is not null && reportingFiscalYearId is not null)
        ? new ShellContext(Company, Branch, new HashSet<string>(Profile.Permissions, StringComparer.Ordinal))
            { UserId = Profile.UserId, FiscalYearId = reportingFiscalYearId, FiscalYearStart = fiscalYearStart, FiscalYearEnd = fiscalYearEnd }
        : null;

    private async Task LoadIdentityAsync()
    {
        var identity = await client.ProfileAsync(lifetime.Token).ConfigureAwait(true);
        if (identity.MustChangePassword) { ClearFinancialWorkspace(); PasswordChangeRequired = true; IsAuthenticated = false; return; }
        var changed = Profile?.UserId != identity.UserId || Profile is null || !validatedPermissions.SetEquals(identity.Permissions);
        if (Profile?.UserId != identity.UserId) ClearFinancialWorkspace();
        if (changed) { Pages.ClearLedger(); Documents.Reset(); DisposeRetainedJournals(); }
        validatedPermissions = new(identity.Permissions, StringComparer.Ordinal);
        // Reference lists were loaded under the previous permission set; retire them together with anything still in flight.
        if (Caching.PermissionFingerprint.Update(identity.Permissions))
        {
            Caching.ReferenceDataCache.Instance.InvalidateAll();
            Caching.ReferenceDataCache.Instance.AdvanceContext();
        }

        Profile = identity;
        if (changed) Preferences?.Load(identity.UserId);
        Navigation.Configure(identity.Permissions);
        if (changed) Navigation.Navigate("home");
        if (changed || !Administration.IsConfigured) Administration.Configure(identity.Permissions);
        Ledger.Configure(identity.Permissions);
        var authorized = await client.CompaniesAsync(lifetime.Token).ConfigureAwait(true);
        var preferred = preferences.Read(identity.UserId);
        var previousCompany = Company?.Id; var previousBranch = Branch?.Id;
        var companyId = Company?.Id ?? preferred?.CompanyId;
        var branchId = Branch?.Id ?? preferred?.BranchId;
        Company = null; Companies.Clear(); foreach (var row in authorized.Items) Companies.Add(row);
        Company = Companies.FirstOrDefault(item => item.Id == companyId) ?? (Companies.Count == 1 ? Companies[0] : null);
        Branch = null; Branches.Clear();
        if (Company is not null)
        {
            var branches = await client.BranchesAsync(Company.Id, lifetime.Token).ConfigureAwait(true);
            foreach (var row in branches.Items) Branches.Add(row);
            Branch = Branches.FirstOrDefault(item => item.Id == branchId) ?? (Branches.Count == 1 ? Branches[0] : null);
        }

        if (previousCompany != Company?.Id || previousBranch != Branch?.Id) { reportingFiscalYearId = null; Pages.ClearLedger(); }
        await ValidateFinancialWorkspaceAsync().ConfigureAwait(true);
        if (ShowWorkspaceSelection || Company is null && Companies.Count > 1)
        {
            await WorkspaceSelection.LoadWorkspacesCoreAsync().ConfigureAwait(true);
            ShowWorkspaceSelection = true;
        }
        IsAuthenticated = true;
        lastIdentityValidatedUtc = time.GetUtcNow();
        if (Pages.LedgerPages.Any(page => page is PageViewModel model && model.Context is { } old && (old.CompanyId != Company?.Id || old.BranchId != Branch?.Id || old.FiscalYearId != reportingFiscalYearId))) Pages.ClearLedger();
        // A document never changes owner: tabs of another company/branch, or of a route no longer permitted, are closed.
        Documents.RemoveWhere(document => (document.IsScoped && (document.CompanyId != Company?.Id || document.BranchId != Branch?.Id || (document.FiscalYearId is not null && document.FiscalYearId != reportingFiscalYearId)))
            || (document.Route is { } route && !Navigation.Allows(route)));
        RestoreRetainedJournals();
        SyncWorkspace();
        BuildQuickActions();
        await LoadContextDataAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Navigation must not wait on an identity round trip first: the busy bar that shows while it does is what the
    /// operator reads as a stop. A shell with no context still has to load one, otherwise a fresh profile check runs
    /// only when the last one has aged out. The server remains the authority for every call the opened page makes.
    /// </summary>
    private Task EnsureIdentityForNavigationAsync()
    {
        if (ShellContextOrNull is null) return LoadIdentityAsync();
        return time.GetUtcNow() - lastIdentityValidatedUtc > IdentityFreshness ? RevalidateIdentityAsync() : Task.CompletedTask;
    }

    /// <summary>Checks the session without rebuilding the shell: an unchanged profile must not redraw menus, tabs or scope.</summary>
    private async Task RevalidateIdentityAsync()
    {
        var identity = await client.ProfileAsync(lifetime.Token).ConfigureAwait(true);
        lastIdentityValidatedUtc = time.GetUtcNow();
        if (identity.MustChangePassword || Profile?.UserId != identity.UserId || !validatedPermissions.SetEquals(identity.Permissions))
        {
            await LoadIdentityAsync().ConfigureAwait(true);
            return;
        }

        // Access to a company can be withdrawn without the profile changing, so the authorised scope is re-read; only a
        // difference re-enters the full path, which closes the tabs and documents that no longer belong here.
        var authorized = await client.CompaniesAsync(lifetime.Token).ConfigureAwait(true);
        if (!Companies.Select(item => item.Id).SequenceEqual(authorized.Items.Select(item => item.Id)) || Company?.Id != null && !authorized.Items.Any(item => item.Id == Company.Id))
            await LoadIdentityAsync().ConfigureAwait(true);
    }

    /// <summary>Background session check from the shell timer; failures are reported without covering the screen.</summary>
    public async Task RevalidateSessionAsync()
    {
        if (!IsAuthenticated || Operation.IsBusy || ShowWorkspaceSelection) return;
        try
        {
            await RevalidateIdentityAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Operation.SetError(DesktopErrors.Key(exception));
        }
    }

    /// <summary>Workspace (palette/recent/favorites) and the current fiscal period for the top bar.</summary>
    private async Task LoadContextDataAsync()
    {
        if (!IsAuthenticated || PasswordChangeRequired || ShellContextOrNull is null) { CurrentPeriod = null; _ = Dashboard.RefreshAsync(null); return; }
        await Workspace.SetContextAsync(Company, Branch).ConfigureAwait(true);
        _ = Dashboard.RefreshAsync(ShellContextOrNull);
        if (Navigation.Has("Ledger.Periods.View"))
        {
            var years = await Pages.Calendar.ListFiscalYearsAsync(Company!.Id, Branch!.Id, lifetime.Token).ConfigureAwait(true);
            var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
            CurrentPeriod = FiscalPeriodsViewModel.PeriodFor(years, today);
            // The working year's range feeds the document date rules (outside it a document is refused before the server is asked).
            var workingYear = years.FirstOrDefault(year => year.Id == reportingFiscalYearId) ?? years.FirstOrDefault(year => year.RangeStart <= today && today <= year.RangeEnd);
            fiscalYearStart = workingYear?.RangeStart;
            fiscalYearEnd = workingYear?.RangeEnd;
        }
        else
        {
            CurrentPeriod = null;
            fiscalYearStart = null;
            fiscalYearEnd = null;
        }
    }

    public async Task RefreshAsync()
    {
        if (Operation.IsBusy || !IsAuthenticated) return;
        await Operation.RunAsync(async () =>
        {
            ServerStatus = await client.StatusAsync(lifetime.Token).ConfigureAwait(true);
            await LoadIdentityAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task<bool> SelectCompanyAsync(CompanyContextResponse? selection)
    {
        if (!await ConfirmDiscardAsync().ConfigureAwait(true)) { OnPropertyChanged(nameof(Company)); return false; }
        var route = Documents.Active.Route;
        return await Operation.RunAsync(async () =>
    {
        // Company switch: drop every company-bound page state (journals, lines, lookups, currency, reports) before loading.
        ClearFinancialWorkspace();
        if (selection?.Id != Company?.Id) { reportingFiscalYearId = null; Pages.ClearLedger(); Documents.RemoveWhere(document => document.IsScoped); }
        Branch = null; Company = null; Branches.Clear(); SyncWorkspace(); CurrentPeriod = null;
        var allowed = Companies.FirstOrDefault(item => item.Id == selection?.Id);
        if (allowed is null) return;
        var current = await client.CompaniesAsync(lifetime.Token).ConfigureAwait(true);
        if (!current.Items.Any(item => item.Id == allowed.Id)) { Companies.Clear(); Operation.SetError("ContextUnavailable"); return; }
        var rows = await client.BranchesAsync(allowed.Id, lifetime.Token).ConfigureAwait(true);
        Company = allowed;
        foreach (var row in rows.Items) Branches.Add(row);
        if (Branches.Count == 1) { Branch = Branches[0]; SaveContext(); }
        await ValidateFinancialWorkspaceAsync().ConfigureAwait(true);
        SyncWorkspace();
        await AfterContextChangedAsync(route).ConfigureAwait(true);
    }).ConfigureAwait(true);
    }

    private async Task<bool> SelectBranchAsync(BranchContextResponse? selection)
    {
        if (!await ConfirmDiscardAsync().ConfigureAwait(true)) { OnPropertyChanged(nameof(Branch)); return false; }
        var route = Documents.Active.Route;
        return await Operation.RunAsync(async () =>
    {
        if (selection?.Id != Branch?.Id) { if (client is not IFinancialWorkspaceSelector) reportingFiscalYearId = null; Pages.ClearLedger(); Documents.RemoveWhere(document => document.IsScoped); }
        Branch = null;
        if (Company is null || selection is null || selection.CompanyId != Company.Id || !Branches.Any(item => item.Id == selection.Id)) { SyncWorkspace(); return; }
        var allowed = await client.BranchesAsync(Company.Id, lifetime.Token).ConfigureAwait(true);
        Branch = allowed.Items.FirstOrDefault(item => item.Id == selection.Id);
        if (Branch is null) { Branches.Clear(); Operation.SetError("ContextUnavailable"); SyncWorkspace(); return; }
        SaveContext();
        await ValidateFinancialWorkspaceAsync().ConfigureAwait(true);
        SyncWorkspace();
        await AfterContextChangedAsync(route).ConfigureAwait(true);
    }).ConfigureAwait(true);
    }

    internal async Task<bool> SwitchWorkspaceAsync(WorkspaceSummaryResponse workspace)
    {
        if (!await ConfirmDiscardAsync().ConfigureAwait(true)) return false;
        var route = Documents.Active.Route;
        return await Operation.RunAsync(async () =>
        {
            ClearFinancialWorkspace();
            Pages.ClearLedger();
            Documents.RemoveWhere(document => document.IsScoped);
            Branch = null; Company = null; Branches.Clear(); SyncWorkspace(); CurrentPeriod = null;
            var current = await client.CompaniesAsync(lifetime.Token).ConfigureAwait(true);
            if (client is IFinancialWorkspaceSelector)
            {
                var registered = await client.WorkspacesAsync(lifetime.Token).ConfigureAwait(true);
                var actual = registered.Items.SingleOrDefault(item => item.WorkspaceId == workspace.WorkspaceId
                    && item.CompanyId == workspace.CompanyId && item.FiscalYearId == workspace.FiscalYearId && IsActiveWorkspace(item));
                if (actual is null) { Operation.SetError("ContextUnavailable"); return; }
                workspace = actual;
            }
            var allowed = current.Items.FirstOrDefault(item => item.Id == workspace.CompanyId);
            if (allowed is null)
            {
                Companies.Clear();
                Operation.SetError("ContextUnavailable");
                return;
            }
            Company = allowed;
            reportingFiscalYearId = workspace.FiscalYearId;
            fiscalYearLabel = workspace.FiscalYearCode;
            Companies.Clear();
            foreach (var item in current.Items) Companies.Add(item);

            var rows = await client.BranchesAsync(allowed.Id, lifetime.Token).ConfigureAwait(true);
            foreach (var row in rows.Items) Branches.Add(row);
            Branch = Branches.FirstOrDefault();

            if (client is IFinancialWorkspaceSelector selector)
            {
                selector.SelectWorkspace(workspace.WorkspaceId, workspace.CompanyId, workspace.FiscalYearId);
                selectedFinancialWorkspaceId = workspace.WorkspaceId;
            }

            SaveContext();
            ShowWorkspaceSelection = false;
            SyncWorkspace();
            await AfterContextChangedAsync(route).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private void ClearFinancialWorkspace()
    {
        if (client is IFinancialWorkspaceSelector selector) selector.ClearWorkspace();
        selectedFinancialWorkspaceId = null;
        reportingFiscalYearId = null;
    }

    private async Task ValidateFinancialWorkspaceAsync()
    {
        if (client is not IFinancialWorkspaceSelector selector) return;
        var previousWorkspace = selectedFinancialWorkspaceId;
        var previousYear = reportingFiscalYearId;
        selector.ClearWorkspace();
        selectedFinancialWorkspaceId = null;
        reportingFiscalYearId = null;
        if (Company is null) return;
        var registered = await client.WorkspacesAsync(lifetime.Token).ConfigureAwait(true);
        var candidates = registered.Items.Where(item => item.CompanyId == Company.Id && IsActiveWorkspace(item)).ToArray();
        var actual = candidates.SingleOrDefault(item => item.WorkspaceId == previousWorkspace && item.FiscalYearId == previousYear)
            ?? candidates.SingleOrDefault(item => item.IsDefault)
            ?? (candidates.Length == 1 ? candidates[0] : null);
        if (actual is null) { ShowWorkspaceSelection = true; return; }
        selector.SelectWorkspace(actual.WorkspaceId, actual.CompanyId, actual.FiscalYearId);
        selectedFinancialWorkspaceId = actual.WorkspaceId;
        reportingFiscalYearId = actual.FiscalYearId;
        fiscalYearLabel = actual.FiscalYearCode;
    }

    private static bool IsActiveWorkspace(WorkspaceSummaryResponse workspace) => workspace.WorkspaceId != Guid.Empty
        && workspace.CompanyId != Guid.Empty && workspace.FiscalYearId != Guid.Empty
        && string.Equals(workspace.State, "Active", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reloads context data and reopens the visible route as a new document of the newly selected company/branch.</summary>
    private async Task AfterContextChangedAsync(string? route)
    {
        await LoadContextDataAsync().ConfigureAwait(true);
        if (route is not null && Navigation.Allows(route) && ShellContextOrNull is { } context && Pages.Page(route) is { } page)
        {
            OpenRouteDocument(route, page);
            await page.ActivateAsync(context, null, lifetime.Token).ConfigureAwait(true);
        }
    }

    private void SaveContext()
    {
        if (Profile is not null && Company is not null && Branch is not null)
            preferences.Save(Profile.UserId, new ContextPreference(Company.Id, Branch.Id, DocumentReferences()));
    }

    /// <summary>
    /// Remembers open document identifiers and geometry for this user, company and branch. Nothing is written while signed out,
    /// offline or restoring, so a logout, an outage or a partial restore never overwrites the list.
    /// </summary>
    private void PersistWorkspace()
    {
        if (restoringWorkspace || !IsAuthenticated || !Operation.IsOnline || Profile is null || Company is null || Branch is null) return;
        try { SaveContext(); }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { /* Best effort: a convenience, never a blocker. */ }
    }

    private List<WorkspaceDocumentReference> DocumentReferences() => Documents.Documents
        .Where(document => document.IsClosable)
        .Select(document => document.Kind switch
        {
            WorkspaceDocumentKind.Route when document.Route is { } route => new WorkspaceDocumentReference("route", route, null, document.IsDetached, document.IsActive, document.Placement),
            WorkspaceDocumentKind.AccountCard when document.Content is AccountCardViewModel { AccountId: { } account } =>
                new WorkspaceDocumentReference("account", null, account, document.IsDetached, document.IsActive, document.Placement),
            // Only saved journals (server id); a new draft has no id and its input is never written to disk.
            WorkspaceDocumentKind.Journal when document.Content is GeneralLedgerViewModel { CurrentJournalId: { } journal } =>
                new WorkspaceDocumentReference("journal", null, journal, document.IsDetached, document.IsActive, document.Placement),
            _ => null,
        })
        .OfType<WorkspaceDocumentReference>()
        .Take(ContextPreference.MaximumDocuments)
        .ToList();

    internal void RememberWindowPlacement(WorkspaceDocument document, DetachedWindowPlacement placement)
    {
        if (!placement.IsValid || !Documents.Documents.Contains(document) || !document.IsClosable
            || document.Route is not { } route || !Navigation.Allows(route)
            || document.IsScoped && (document.CompanyId != Company?.Id || document.BranchId != Branch?.Id)) return;
        document.Placement = placement;
    }

    internal void PersistWindowPlacements() => PersistWorkspace();

    /// <summary>
    /// After sign-in: reopens the remembered documents of the same company and branch through the normal open path, so
    /// permission, scope and existence are checked again and every document is re-read from the server.
    /// </summary>
    private async Task RestoreWorkspaceAsync()
    {
        if (Profile is null || ShellContextOrNull is null || Documents.Count > 1) return;
        var saved = preferences.Read(Profile.UserId);
        if (saved?.Documents is not { Count: > 0 } items || saved.CompanyId != Company!.Id || saved.BranchId != Branch!.Id) return;
        restoringWorkspace = true;
        WorkspaceDocument? active = null;
        var detach = new List<WorkspaceDocument>();
        try
        {
            foreach (var item in items.Take(ContextPreference.MaximumDocuments))
            {
                var before = Documents.Active;
                var (route, parameter) = item.Kind switch
                {
                    "route" when item.Route is { } id && id != "home" => (id, (object?)null),
                    "account" when item.Id is { } account => ("accounts", new OpenAccountCardRequest(account)),
                    "journal" when item.Id is { } journal => ("journals", new OpenJournalRequest(journal)),
                    _ => ((string?)null, (object?)null),
                };
                if (route is null || !Navigation.Allows(route)) continue;
                await NavigateToAsync(route, parameter).ConfigureAwait(true);
                var opened = Documents.Active;
                if (ReferenceEquals(opened, before) || !opened.IsClosable) continue;
                opened.Placement = item.Placement is { IsValid: true } placement ? placement : null;
                if (item.Detached) detach.Add(opened);
                if (item.Active) active = opened;
            }

            Documents.Activate(active is { IsDetached: false } ? active : Documents.Home);
            foreach (var document in detach.Where(Documents.Documents.Contains)) Documents.DetachDocument(document);
        }
        finally
        {
            restoringWorkspace = false;
        }

        PersistWorkspace();
    }

    /// <summary>Revalidation keeping the same company/branch preserves the workspace; any other context clears it.</summary>
    private void SyncWorkspace()
    {
        if (Workspace.Company?.Id != Company?.Id || Workspace.Branch?.Id != Branch?.Id) Workspace.Reset();
    }

    private Task NavigateAsync(string? id) => NavigateToAsync(id, null);

    private Task OnNavigationRequested(string route, object? parameter) => NavigateToAsync(route, parameter);

    /// <summary>
    /// Revalidates permissions and context, then opens (or activates) the route's document with an optional drill-down
    /// parameter. Opening another tab never discards input: a document with unsaved changes is only activated, and is
    /// reloaded with a new parameter only after explicit confirmation.
    /// </summary>
    public async Task NavigateToAsync(string? id, object? parameter)
    {
        if (id == "financial-reports" && parameter is Reports.OpenPartyStatementRequest statement && Pages.ProfessionalReports is not null)
            id = statement.Kind == PartyStatementKind.Supplier ? "supplier-statement" : "customer-statement";
        if (id == "journals" && parameter is OpenJournalRequest or NewJournalRequest)
        {
            // Each journal and each new draft is its own document; the "journals" route page stays the list.
            await Operation.RunAsync(async () =>
            {
                await EnsureIdentityForNavigationAsync().ConfigureAwait(true);
                await OpenJournalDocumentAsync(parameter).ConfigureAwait(true);
            }).ConfigureAwait(true);
            return;
        }

        if (id == "preferences")
        {
            // Presentation-only shell page: available to every signed-in user, outside the permission route table.
            if (IsAuthenticated && PreferencesPage is not null)
                Documents.Open(new WorkspaceDocument(RouteKey(id), WorkspaceDocumentKind.Route, id, PreferencesPage, Localization["UI_Preferences"], "", null, null));
            return;
        }

        var existing = id is null ? null : id == "home" ? Documents.Home : Documents.Find(RouteKey(id));
        if (parameter is not OpenAccountCardRequest && parameter is not OpenPatternCardRequest && parameter is not NewPatternCardRequest && parameter is not OpenPartyCardRequest && parameter is not NewPartyCardRequest && parameter is not OpenItemCardRequest && parameter is not NewItemCardRequest && parameter is not OpenItemGroupCardRequest && parameter is not NewItemGroupCardRequest && parameter is not OpenSalesInvoiceCardRequest && parameter is not NewSalesInvoiceCardRequest && existing is { IsDirty: true })
        {
            Documents.Activate(existing);
            if (parameter is null || !await confirmation.ConfirmAsync("D_ConfirmDiscardChanges").ConfigureAwait(true)) return;
        }

        await Operation.RunAsync(async () =>
        {
            await EnsureIdentityForNavigationAsync().ConfigureAwait(true);
            if (parameter is OpenAccountCardRequest card) { await OpenAccountCardAsync(card).ConfigureAwait(true); return; }
            if (parameter is OpenPatternCardRequest or NewPatternCardRequest) { await OpenPatternCardAsync(parameter).ConfigureAwait(true); return; }
            if (parameter is OpenPartyCardRequest or NewPartyCardRequest) { await OpenPartyCardAsync(parameter).ConfigureAwait(true); return; }
            if (parameter is OpenItemCardRequest or NewItemCardRequest) { await OpenItemCardAsync(parameter).ConfigureAwait(true); return; }
            if (parameter is OpenItemGroupCardRequest or NewItemGroupCardRequest) { await OpenItemGroupCardAsync(parameter).ConfigureAwait(true); return; }
            if (parameter is OpenSalesInvoiceCardRequest or NewSalesInvoiceCardRequest) { await OpenSalesInvoiceCardAsync(parameter).ConfigureAwait(true); return; }
            if (id is null || !Navigation.Allows(id)) return;
            if (id == "home")
            {
                Documents.Activate(Documents.Home);
            }
            else if (id is "users" or "roles" or "audit")
            {
                OpenRouteDocument(id, Pages.View(id));
                await Administration.LoadPageAsync(id, lifetime.Token).ConfigureAwait(true);
            }
            else if (Pages.Page(id) is { } page)
            {
                if (ShellContextOrNull is not { } context) { Documents.Activate(Documents.Home); Operation.SetError("ContextRequired"); return; }
                OpenRouteDocument(id, page);
                await page.ActivateAsync(context, parameter, lifetime.Token).ConfigureAwait(true);
                if (parameter is OpenJournalRequest journal) await Workspace.RecordRecentAsync(SearchCategory.JournalEntries, journal.JournalId).ConfigureAwait(true);
                else if (parameter is OpenAccountRequest account) await Workspace.RecordRecentAsync(SearchCategory.Accounts, account.AccountId).ConfigureAwait(true);
            }
            else
            {
                // The menu offered a route this build cannot show (for example a statement whose report catalogue is
                // unavailable). A click that appears to do nothing is worse than a refusal the operator can read.
                Documents.Activate(Documents.Home);
                Operation.SetError("RouteUnavailable");
            }
        }).ConfigureAwait(true);
    }

    /// <summary>Installation-wide administration routes are unscoped; every ledger route is keyed by company and branch.</summary>
    private string RouteKey(string route) => IsInstallationRoute(route)
        ? WorkspaceDocument.RouteKey(route, null, null)
        : WorkspaceDocument.RouteKey(route, Company?.Id, Branch?.Id, reportingFiscalYearId);

    private static bool IsInstallationRoute(string route) => route is "users" or "roles" or "audit" or "preferences";

    private WorkspaceDocument OpenRouteDocument(string route, object? content)
    {
        var scoped = !IsInstallationRoute(route);
        var item = Navigation.Items.FirstOrDefault(entry => entry.Id == route);
        var document = Documents.Open(new WorkspaceDocument(RouteKey(route), WorkspaceDocumentKind.Route, route, content, item?.Label ?? route, item?.Glyph ?? string.Empty,
            scoped ? Company?.Id : null, scoped ? Branch?.Id : null, scoped ? reportingFiscalYearId : null));
        // In classic MDI, documents stay inside the workspace canvas; outside classic MDI, reports open as popups.
        AutoDetachIfConfigured(document, OpensAsWindow(route) && !IsClassicMdi);
        return document;
    }

    /// <summary>Report routes open detached only when explicitly running outside classic MDI.</summary>
    internal static bool OpensAsWindow(string route) => route is "account-ledger" or "trial-balance" or "customer-statement" or "supplier-statement" or "party-statement" or "currency-balance";

    private void AutoDetachIfConfigured(WorkspaceDocument document, bool forced = false)
    {
        // Al-Ameen style: classic MDI keeps all documents, cards, and reports floating inside the application canvas.
        if (IsClassicMdi) return;
        if ((forced || Preferences?.WindowMode == WindowPresentationMode.SeparateWindows) && document.IsClosable && !document.IsDetached)
        {
            Documents.DetachDocument(document);
        }
    }

    /// <summary>One card per account and scope: an open card is activated and refreshed, a new one gets its own view model.</summary>
    private async Task OpenAccountCardAsync(OpenAccountCardRequest request)
    {
        if (!Navigation.Allows("accounts")) { Operation.SetError("PermissionDenied"); return; }
        if (ShellContextOrNull is not { } context) { Operation.SetError("ContextRequired"); return; }
        var key = WorkspaceDocument.AccountKey(context.CompanyId, context.BranchId, request.AccountId, context.FiscalYearId);
        if (Documents.Find(key) is { Content: AccountCardViewModel open } existing)
        {
            Documents.Activate(existing);
            await open.ActivateAsync(context, null, lifetime.Token).ConfigureAwait(true);
            return;
        }

        // Record navigation (first/previous/next/last) moves the source card to the target account: same tab, new key.
        if (request.ReplacingCardOf is { } source
            && Documents.Find(WorkspaceDocument.AccountKey(context.CompanyId, context.BranchId, source, context.FiscalYearId)) is { Content: AccountCardViewModel moving } sourceDocument
            && Documents.Rekey(sourceDocument, key))
        {
            Documents.Activate(sourceDocument);
            await moving.ActivateAsync(context, request, lifetime.Token).ConfigureAwait(true);
            if (moving.HasAccount) await Workspace.RecordRecentAsync(SearchCategory.Accounts, request.AccountId).ConfigureAwait(true);
            else Documents.Remove(sourceDocument);
            PersistWorkspace();
            return;
        }

        var card = Pages.Cards.Create();
        var document = Documents.Open(new WorkspaceDocument(key, WorkspaceDocumentKind.AccountCard, "accounts", card, Localization["D_AccountCard"], "", context.CompanyId, context.BranchId, context.FiscalYearId));
        AutoDetachIfConfigured(document);
        card.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(AccountCardViewModel.Title)) document.Title = card.Title; };
        await card.ActivateAsync(context, request, lifetime.Token).ConfigureAwait(true);
        if (!card.HasAccount) { Documents.Remove(document); return; }
        await Workspace.RecordRecentAsync(SearchCategory.Accounts, request.AccountId).ConfigureAwait(true);
    }


    private long draftPatternSequence;

    /// <summary>One card per pattern and scope: an open card is activated and refreshed, a new one gets its own view model.</summary>
    private async Task OpenPatternCardAsync(object parameter)
    {
        if (!Navigation.Allows("patterns")) { Operation.SetError("PermissionDenied"); return; }
        if (ShellContextOrNull is not { } context) { Operation.SetError("ContextRequired"); return; }

        string key;
        if (parameter is OpenPatternCardRequest open)
        {
            key = WorkspaceDocument.PatternKey(context.CompanyId, context.BranchId, open.PatternId);
            if (Documents.Find(key) is { Content: PatternCardViewModel openCard } existing)
            {
                Documents.Activate(existing);
                await openCard.ActivateAsync(context, null, lifetime.Token).ConfigureAwait(true);
                return;
            }
        }
        else
        {
            key = WorkspaceDocument.DraftPatternKey(context.CompanyId, context.BranchId, ++draftPatternSequence);
        }

        var card = Pages.PatternCards.Create();
        var document = Documents.Open(new WorkspaceDocument(key, WorkspaceDocumentKind.PatternCard, "patterns", card, Localization["D_PatternCard"], "", context.CompanyId, context.BranchId));
        AutoDetachIfConfigured(document);
        card.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(PatternCardViewModel.Title)) document.Title = card.Title; };
        await card.ActivateAsync(context, parameter, lifetime.Token).ConfigureAwait(true);
    }

    private long draftPartySequence;

    /// <summary>One card per party and scope: an open card is activated and refreshed, a new one gets its own view model.</summary>
    private async Task OpenPartyCardAsync(object parameter)
    {
        if (!Navigation.Allows("parties")) { Operation.SetError("PermissionDenied"); return; }
        if (ShellContextOrNull is not { } context) { Operation.SetError("ContextRequired"); return; }

        string key;
        if (parameter is OpenPartyCardRequest open)
        {
            await Workspace.RecordRecentAsync(SearchCategory.Parties, open.PartyId).ConfigureAwait(true);
            key = WorkspaceDocument.PartyKey(context.CompanyId, context.BranchId, open.PartyId);
            if (Documents.Find(key) is { Content: PartyCardViewModel openCard } existing)
            {
                Documents.Activate(existing);
                await openCard.ActivateAsync(context, null, lifetime.Token).ConfigureAwait(true);
                return;
            }
        }
        else
        {
            key = WorkspaceDocument.DraftPartyKey(context.CompanyId, context.BranchId, ++draftPartySequence);
        }

        var card = Pages.PartyCards.Create();
        var document = Documents.Open(new WorkspaceDocument(key, WorkspaceDocumentKind.PartyCard, "parties", card, Localization["D_PartyCard"], "", context.CompanyId, context.BranchId));
        AutoDetachIfConfigured(document);
        card.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(PartyCardViewModel.Title)) document.Title = card.Title; };
        await card.ActivateAsync(context, parameter, lifetime.Token).ConfigureAwait(true);
    }

    private long draftItemSequence;

    /// <summary>One card per item and scope: an open card is activated and refreshed, a new one gets its own view model.</summary>
    private async Task OpenItemCardAsync(object parameter)
    {
        if (!Navigation.Allows("items")) { Operation.SetError("PermissionDenied"); return; }
        if (ShellContextOrNull is not { } context) { Operation.SetError("ContextRequired"); return; }

        string key;
        if (parameter is OpenItemCardRequest open)
        {
            await Workspace.RecordRecentAsync(SearchCategory.Items, open.ItemId).ConfigureAwait(true);
            key = WorkspaceDocument.ItemKey(context.CompanyId, context.BranchId, open.ItemId);
            if (Documents.Find(key) is { Content: ItemCardViewModel openCard } existing)
            {
                Documents.Activate(existing);
                await openCard.ActivateAsync(context, null, lifetime.Token).ConfigureAwait(true);
                if (open.MovementId is { } movementId) openCard.FocusMovement(movementId);
                return;
            }
        }
        else
        {
            key = WorkspaceDocument.DraftItemKey(context.CompanyId, context.BranchId, ++draftItemSequence);
        }

        var card = Pages.ItemCards.Create();
        var document = Documents.Open(new WorkspaceDocument(key, WorkspaceDocumentKind.ItemCard, "items", card, Localization["D_ItemCard"], "", context.CompanyId, context.BranchId));
        card.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(ItemCardViewModel.Title)) document.Title = card.Title; };
        await card.ActivateAsync(context, parameter, lifetime.Token).ConfigureAwait(true);
    }

        private long draftItemGroupSequence;

    /// <summary>One card per item group and scope: an open card is activated and refreshed, a new one gets its own view model.</summary>
    private async Task OpenItemGroupCardAsync(object parameter)
    {
        if (!Navigation.Allows("items")) { Operation.SetError("PermissionDenied"); return; }
        if (ShellContextOrNull is not { } context) { Operation.SetError("ContextRequired"); return; }

        string key;
        if (parameter is OpenItemGroupCardRequest open)
        {
            key = WorkspaceDocument.ItemGroupKey(context.CompanyId, context.BranchId, open.GroupId);
            if (Documents.Find(key) is { Content: ItemGroupCardViewModel openCard } existing)
            {
                Documents.Activate(existing);
                await openCard.ActivateAsync(context, null, lifetime.Token).ConfigureAwait(true);
                return;
            }
        }
        else
        {
            key = WorkspaceDocument.DraftItemGroupKey(context.CompanyId, context.BranchId, ++draftItemGroupSequence);
        }

        var card = Pages.ItemGroupCards.Create();
        var document = Documents.Open(new WorkspaceDocument(key, WorkspaceDocumentKind.ItemGroupCard, "items", card, Localization["D_ItemGroupCard"], "\uE8B7", context.CompanyId, context.BranchId));
        card.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(ItemGroupCardViewModel.Title)) document.Title = card.Title; };
        await card.ActivateAsync(context, parameter, lifetime.Token).ConfigureAwait(true);
    }

    private long draftSalesInvoiceSequence;

    /// <summary>One card per sales invoice and scope: an open card is activated and refreshed, a new one gets its own view model.</summary>
    private async Task OpenSalesInvoiceCardAsync(object parameter)
    {
        if (!Navigation.Allows("sales-invoices")) { Operation.SetError("PermissionDenied"); return; }
        if (ShellContextOrNull is not { } context) { Operation.SetError("ContextRequired"); return; }

        string key;
        if (parameter is OpenSalesInvoiceCardRequest open)
        {
            await Workspace.RecordRecentAsync(SearchCategory.Invoices, open.InvoiceId).ConfigureAwait(true);
            key = WorkspaceDocument.SalesInvoiceKey(context.CompanyId, context.BranchId, open.InvoiceId, context.FiscalYearId);
            if (Documents.Find(key) is { Content: SalesInvoiceCardViewModel openCard } existing)
            {
                Documents.Activate(existing);
                await openCard.ActivateAsync(context, null, lifetime.Token).ConfigureAwait(true);
                return;
            }
        }
        else
        {
            key = WorkspaceDocument.DraftSalesInvoiceKey(context.CompanyId, context.BranchId, ++draftSalesInvoiceSequence, context.FiscalYearId);
        }

        var card = Pages.SalesInvoiceCards.Create();
        card.PrintRequested += OnPrintRequested;
        var document = Documents.Open(new WorkspaceDocument(key, WorkspaceDocumentKind.SalesInvoiceCard, "sales-invoices", card, Localization["D_SalesInvoiceCard"], "", context.CompanyId, context.BranchId, context.FiscalYearId));
        card.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(SalesInvoiceCardViewModel.Title)) document.Title = card.Title; };
        // Like every other document: in "separate windows" mode the invoice opens in its own window beside its list.
        AutoDetachIfConfigured(document);
        await card.ActivateAsync(context, parameter, lifetime.Token).ConfigureAwait(true);
    }

    /// <summary>
    /// One document per journal (<c>Journal/{company}/{branch}/{id}</c>) and per new draft (<c>JournalDraft/.../{n}</c>),
    /// each with its own view model from <see cref="JournalDocumentFactory"/>. An open journal is activated, and refreshed
    /// from the server only when it has no unsaved input; a retained (ambiguous-posting) journal reuses its view model.
    /// </summary>
    private async Task OpenJournalDocumentAsync(object parameter)
    {
        if (!Navigation.Allows("journals")) { Operation.SetError("PermissionDenied"); return; }
        if (ShellContextOrNull is not { } context) { Operation.SetError("ContextRequired"); return; }
        string key;
        if (parameter is OpenJournalRequest open)
        {
            key = WorkspaceDocument.JournalKey(context.CompanyId, context.BranchId, open.JournalId, context.FiscalYearId);
            if (Documents.Find(key) is { Content: GeneralLedgerViewModel opened } existing)
            {
                Documents.Activate(existing);
                if (!existing.IsDirty) await opened.ActivateAsync(context, open, lifetime.Token).ConfigureAwait(true);
                await Workspace.RecordRecentAsync(SearchCategory.JournalEntries, open.JournalId).ConfigureAwait(true);
                return;
            }
        }
        else
        {
            if (!context.Can(JournalCommandPolicy.Manage)) { Operation.SetError("PermissionDenied"); return; }
            key = WorkspaceDocument.DraftKey(context.CompanyId, context.BranchId, ++draftSequence, context.FiscalYearId);
        }

        var journal = Pages.JournalDocuments.Create();
        journal.PropertyChanged += JournalDocumentChanged; journal.DocumentCloseRequested += JournalCloseRequested; journal.JournalChanged += JournalSaved;
        var document = OpenJournal(key, journal, context.CompanyId, context.BranchId);
        try
        {
            await journal.ActivateAsync(context, parameter, lifetime.Token).ConfigureAwait(true);
        }
        finally
        {
            // A journal that could not be shown (not found, no permission, failed read) leaves no empty tab behind.
            if (!journal.IsEditorOpen && !journal.HasPendingPostingAttempt) Documents.Remove(document);
        }

        if (parameter is OpenJournalRequest recent && journal.IsEditorOpen)
            await Workspace.RecordRecentAsync(SearchCategory.JournalEntries, recent.JournalId).ConfigureAwait(true);
    }

    private WorkspaceDocument OpenJournal(string key, GeneralLedgerViewModel journal, Guid companyId, Guid branchId)
    {
        var glyph = Navigation.Items.FirstOrDefault(entry => entry.Id == "journals")?.Glyph ?? string.Empty;
        var document = Documents.Open(new WorkspaceDocument(key, WorkspaceDocumentKind.Journal, "journals", journal, journal.DocumentTitle, glyph, companyId, branchId, reportingFiscalYearId));
        journalDocuments[journal] = document;
        AutoDetachIfConfigured(document);
        return document;
    }

    private void JournalDocumentChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (sender is not GeneralLedgerViewModel journal || !journalDocuments.TryGetValue(journal, out var document) || !Documents.Documents.Contains(document)) return;
        switch (args.PropertyName)
        {
            case nameof(GeneralLedgerViewModel.DocumentTitle):
                document.Title = journal.DocumentTitle;
                break;
            case nameof(GeneralLedgerViewModel.CurrentJournalId) when journal.CurrentJournalId is { } id && document.CompanyId is { } companyId && document.BranchId is { } branchId:
                // A saved draft takes its server key, so opening that journal elsewhere activates this tab.
                Documents.Rekey(document, WorkspaceDocument.JournalKey(companyId, branchId, id, document.FiscalYearId));
                PersistWorkspace();
                break;
            case nameof(GeneralLedgerViewModel.IsEditorOpen) when !journal.IsEditorOpen && !journal.HasPendingPostingAttempt:
                // The draft was deleted (or the page cleared): the document has nothing left to show.
                Documents.Remove(document);
                break;
        }
    }

    private void JournalCloseRequested(object? sender, EventArgs args)
    {
        if (sender is GeneralLedgerViewModel journal && journalDocuments.TryGetValue(journal, out var document)) _ = CloseDocumentAsync(document);
    }

    /// <summary>A journal document wrote to the server: the list page reloads when it is shown (now, if it is the visible tab).</summary>
    private void JournalSaved(object? sender, EventArgs args)
    {
        Ledger.MarkListStale();
        RefreshVisibleJournalList();
    }

    private void RefreshVisibleJournalList()
    {
        if (Ledger.IsListStale && ReferenceEquals(Documents.Active.Content, Ledger) && !Operation.IsBusy && Operation.ErrorKey is null)
            _ = Operation.RunAsync(() => Ledger.RefreshListAsync(lifetime.Token));
    }

    /// <summary>Stops observing a journal view model once its document is gone for good (closed, not retained).</summary>
    private void DocumentsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        PersistWorkspace();
        foreach (var document in args.OldItems?.OfType<WorkspaceDocument>() ?? [])
        {
            if (document.Content is GeneralLedgerViewModel { IsDocument: true } journal && !retainedJournals.ContainsKey(document.Key)
                && journalDocuments.TryGetValue(journal, out var current) && ReferenceEquals(current, document))
                ReleaseJournal(journal);
        }
    }

    private void ReleaseJournal(GeneralLedgerViewModel journal)
    {
        journal.PropertyChanged -= JournalDocumentChanged; journal.DocumentCloseRequested -= JournalCloseRequested; journal.JournalChanged -= JournalSaved;
        journalDocuments.Remove(journal);
    }

    private void RetainPendingJournals()
    {
        foreach (var document in Documents.Documents.Where(document => document.Content is GeneralLedgerViewModel { IsDocument: true, HasPendingPostingAttempt: true }).ToArray())
        {
            retainedJournals[document.Key] = new RetainedJournal((GeneralLedgerViewModel)document.Content!, document.CompanyId!.Value, document.BranchId!.Value);
            Documents.Remove(document, disposeContent: false);
        }
    }

    /// <summary>After reauthorization: retained journals of the current scope reopen as documents; any other scope drops them.</summary>
    private void RestoreRetainedJournals()
    {
        foreach (var (key, retained) in retainedJournals.ToArray())
        {
            retainedJournals.Remove(key);
            if (retained.CompanyId == Company?.Id && retained.BranchId == Branch?.Id && Navigation.Allows("journals"))
                OpenJournal(key, retained.Journal, retained.CompanyId, retained.BranchId);
            else { ReleaseJournal(retained.Journal); retained.Journal.Dispose(); }
        }
    }

    private void DisposeRetainedJournals()
    {
        foreach (var retained in retainedJournals.Values) { ReleaseJournal(retained.Journal); retained.Journal.Dispose(); }
        retainedJournals.Clear();
    }

    private sealed record RetainedJournal(GeneralLedgerViewModel Journal, Guid CompanyId, Guid BranchId);

    /// <summary>Ctrl+W / tab close. Unsaved input needs confirmation; a journal whose posting outcome is unknown cannot be closed.</summary>
    private async Task CloseDocumentAsync(WorkspaceDocument? document)
    {
        if (document is null || !document.IsClosable || !Documents.Documents.Contains(document)) return;
        if (document.Content is GeneralLedgerViewModel { HasPendingPostingAttempt: true })
        {
            Documents.Activate(document); Notifications.Warning("D_PostingOutcomePending"); return;
        }

        if (document.IsDirty)
        {
            Documents.Activate(document);
            if (document.Content is ISavablePage { CanSaveBeforeClose: true } savable)
            {
                // Save / Discard / Cancel: Cancel is the default; a failed or incomplete save keeps the tab and its input.
                var choice = await confirmation.ChooseAsync("D_ConfirmCloseDirtyDocument", "D_SaveAndClose").ConfigureAwait(true);
                if (choice == DialogChoice.Cancel) return;
                if (choice == DialogChoice.Alternate && !await savable.SaveBeforeCloseAsync().ConfigureAwait(true)) return;
            }
            else if (!await confirmation.ConfirmAsync("D_ConfirmCloseDirtyDocument").ConfigureAwait(true))
            {
                return;
            }

            if (!Documents.Documents.Contains(document)) return;
        }

        Documents.Remove(document);
        // Route pages are shared view models: closing their tab drops their state so a reopened tab starts clean.
        if (document.Kind == WorkspaceDocumentKind.Route && document.Content is IShellPage page) page.Clear();
    }

    /// <summary>Closes the matching closable tabs in order; stops at the first one the user keeps (cancelled or blocked).</summary>
    private async Task CloseDocumentsAsync(Func<WorkspaceDocument, bool> predicate)
    {
        var keep = Documents.Active;
        foreach (var document in Documents.Documents.Where(document => document.IsClosable && predicate(document)).ToArray())
        {
            await CloseDocumentAsync(document).ConfigureAwait(true);
            if (Documents.Documents.Contains(document)) return;
        }

        Documents.Activate(keep);
    }

    private Task OpenRecentAsync(SearchHitResponse? hit)
    {
        if (hit is null) return Task.CompletedTask;
        var (route, parameter, _) = Palette.Target(hit);
        return route is null ? Task.CompletedTask : NavigateToAsync(route, parameter);
    }

    /// <summary>Home quick actions: only permitted, implemented destinations and actions.</summary>
    private void BuildQuickActions()
    {
        QuickActions.Clear();
        OnPropertyChanged(nameof(CanCreateJournal)); NewJournalCommand.NotifyCanExecuteChanged();
        if (Navigation.Has("Ledger.Journals.Manage") && Navigation.Allows("journals"))
            QuickActions.Add(new QuickAction("journal.new", Localization["D_NewJournal"], "", "journals", NewJournalRequest.Instance));
        foreach (var item in Navigation.Items.Where(item => item.Id is "trial-balance" or "account-ledger" or "general-journal" or "accounts" or "periods"))
            QuickActions.Add(new QuickAction(item.Id, item.Label, item.Glyph, item.Id, null));
    }
}
