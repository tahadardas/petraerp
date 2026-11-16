using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PetraERP.Desktop.Shell;

public enum WorkspaceDocumentKind
{
    Home,
    Route,
    AccountCard,
    /// <summary>One journal or new draft with its own view model (the "journals" route page is the list).</summary>
    Journal,
    /// <summary>One pattern card with its own view model for pattern configuration.</summary>
    PatternCard,
    /// <summary>One party card with its own view model for party details, contacts, accounting, and credit.</summary>
    PartyCard,
    /// <summary>One item card with its own view model for item details, barcodes, prices, units, stock, and movements.</summary>
    ItemCard,
    /// <summary>One item group card with its own view model for group details, subgroups, items, and accounting mappings.</summary>
    ItemGroupCard,
    /// <summary>One sales invoice card with its own view model for invoice details, concrete additions, lines, and posting.</summary>
    SalesInvoiceCard,
}

/// <summary>
/// One tab of the multi-document workspace. The key is stable and scoped (company/branch for context-bound documents),
/// so the same logical document is activated instead of duplicated and is never carried into another company or branch.
/// The content is a view model. The main content host renders only the active document, and a detached document is
/// rendered only by its own top-level window (the active document is never detached), so no view is in two trees.
/// </summary>
public sealed partial class WorkspaceDocument : ObservableObject
{
    [ObservableProperty] private string title;
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private bool isDetached;

    public WorkspaceDocument(string key, WorkspaceDocumentKind kind, string? route, object? content, string title, string glyph, Guid? companyId, Guid? branchId, Guid? fiscalYearId = null)
    {
        Key = key; Kind = kind; Route = route; Content = content; this.title = title; Glyph = glyph; CompanyId = companyId; BranchId = branchId; FiscalYearId = fiscalYearId;
        // The tab shows an unsaved-changes marker; any change notification of the page may change that state.
        if (content is System.ComponentModel.INotifyPropertyChanged observable) observable.PropertyChanged += ContentChanged;
    }

    /// <summary>Stable key; only a new draft is re-keyed once, when the server assigns its journal id.</summary>
    public string Key { get; internal set; }
    public WorkspaceDocumentKind Kind { get; }
    /// <summary>The navigation route that owns the document (account cards belong to "accounts"); null for Home.</summary>
    public string? Route { get; }
    public object? Content { get; }
    public string Glyph { get; }
    public Guid? CompanyId { get; }
    public Guid? BranchId { get; }
    public Guid? FiscalYearId { get; }
    /// <summary>Local geometry only; saved references carry it after authorization. New drafts keep it in memory only.</summary>
    public DetachedWindowPlacement? Placement { get; internal set; }
    /// <summary>Restored rectangle of the internal (classic MDI) window, in the MDI container's coordinates. In memory only.</summary>
    public MdiRect? MdiBounds { get; set; }
    /// <summary>State of the internal (classic MDI) window: normal, minimized to a chip, or maximized inside the container.</summary>
    public MdiWindowState MdiState { get; set; }
    public bool IsClosable => Kind != WorkspaceDocumentKind.Home;
    public bool IsDirty => Content is IUnsavedPage { HasUnsavedChanges: true };
    public bool IsScoped => CompanyId is not null || FiscalYearId is not null;
    /// <summary>Cards and journal documents own their view model, which is disposed with the tab; route pages are shared.</summary>
    public bool OwnsContent => Kind is WorkspaceDocumentKind.AccountCard or WorkspaceDocumentKind.Journal or WorkspaceDocumentKind.PatternCard or WorkspaceDocumentKind.PartyCard or WorkspaceDocumentKind.ItemCard or WorkspaceDocumentKind.ItemGroupCard or WorkspaceDocumentKind.SalesInvoiceCard;

    /// <summary>Stops observing the content (route pages outlive their tabs).</summary>
    internal void Detach()
    {
        if (Content is System.ComponentModel.INotifyPropertyChanged observable) observable.PropertyChanged -= ContentChanged;
    }

    private void ContentChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => OnPropertyChanged(nameof(IsDirty));

    public static string RouteKey(string route, Guid? companyId, Guid? branchId, Guid? fiscalYearId = null) =>
        companyId is null ? $"Route/{route}" : (fiscalYearId is null ? $"Route/{route}/{companyId:N}/{branchId:N}" : $"Route/{route}/{companyId:N}/{branchId:N}/{fiscalYearId:N}");

    public static string AccountKey(Guid companyId, Guid branchId, Guid accountId, Guid? fiscalYearId = null) =>
        fiscalYearId is null ? $"Account/{companyId:N}/{branchId:N}/{accountId:N}" : $"Account/{companyId:N}/{branchId:N}/{fiscalYearId:N}/{accountId:N}";

    public static string JournalKey(Guid companyId, Guid branchId, Guid journalId, Guid? fiscalYearId = null) =>
        fiscalYearId is null ? $"Journal/{companyId:N}/{branchId:N}/{journalId:N}" : $"Journal/{companyId:N}/{branchId:N}/{fiscalYearId:N}/{journalId:N}";

    public static string PatternKey(Guid companyId, Guid branchId, Guid patternId) => $"Pattern/{companyId:N}/{branchId:N}/{patternId:N}";

    public static string DraftPatternKey(Guid companyId, Guid branchId, long sequence) =>
        $"PatternDraft/{companyId:N}/{branchId:N}/{sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public static string PartyKey(Guid companyId, Guid branchId, Guid partyId) => $"Party/{companyId:N}/{branchId:N}/{partyId:N}";

    public static string DraftPartyKey(Guid companyId, Guid branchId, long sequence) =>
        $"PartyDraft/{companyId:N}/{branchId:N}/{sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public static string ItemKey(Guid companyId, Guid branchId, Guid itemId) => $"Item/{companyId:N}/{branchId:N}/{itemId:N}";

    public static string DraftItemKey(Guid companyId, Guid branchId, long sequence) =>
        $"ItemDraft/{companyId:N}/{branchId:N}/{sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public static string ItemGroupKey(Guid companyId, Guid branchId, Guid groupId) => $"ItemGroup/{companyId:N}/{branchId:N}/{groupId:N}";

    public static string DraftItemGroupKey(Guid companyId, Guid branchId, long sequence) =>
        $"ItemGroupDraft/{companyId:N}/{branchId:N}/{sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public static string SalesInvoiceKey(Guid companyId, Guid branchId, Guid invoiceId, Guid? fiscalYearId = null) =>
        fiscalYearId is null ? $"SalesInvoice/{companyId:N}/{branchId:N}/{invoiceId:N}" : $"SalesInvoice/{companyId:N}/{branchId:N}/{fiscalYearId:N}/{invoiceId:N}";

    public static string DraftSalesInvoiceKey(Guid companyId, Guid branchId, long sequence, Guid? fiscalYearId = null) =>
        fiscalYearId is null ? $"SalesInvoiceDraft/{companyId:N}/{branchId:N}/{sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                             : $"SalesInvoiceDraft/{companyId:N}/{branchId:N}/{fiscalYearId:N}/{sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>A new unsaved draft has no server id yet; each one gets its own key so several drafts can be open.</summary>
    public static string DraftKey(Guid companyId, Guid branchId, long sequence, Guid? fiscalYearId = null) =>
        fiscalYearId is null ? $"JournalDraft/{companyId:N}/{branchId:N}/{sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                             : $"JournalDraft/{companyId:N}/{branchId:N}/{fiscalYearId:N}/{sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Keeps the open documents, the active one and the activation order. It holds no UI and makes no server call; the shell
/// decides authorization, confirmation and loading before asking it to open, activate or close a document.
/// </summary>
public sealed class WorkspaceDocumentManager : ObservableObject
{
    private readonly List<WorkspaceDocument> history = [];
    private WorkspaceDocument active;

    public WorkspaceDocumentManager(string homeTitle)
    {
        Home = new WorkspaceDocument("Home", WorkspaceDocumentKind.Home, null, null, homeTitle, "", null, null);
        Documents.Add(Home);
        active = Home; Home.IsActive = true;
    }

    public WorkspaceDocument Home { get; }
    public ObservableCollection<WorkspaceDocument> Documents { get; } = [];
    public WorkspaceDocument Active { get => active; private set { if (SetProperty(ref active, value)) ActiveChanged?.Invoke(this, EventArgs.Empty); } }
    public bool HasDirtyDocuments => Documents.Any(document => document.IsDirty);
    public int Count => Documents.Count;

    public event EventHandler? ActiveChanged;
    /// <summary>The document that is already active was activated again (a tab click, the Window menu): an internal MDI window restores itself.</summary>
    public event EventHandler<WorkspaceDocument>? Reactivated;
    /// <summary>A detached document was activated (tab, Window menu, close request): its own window should come to front.</summary>
    public event EventHandler<WorkspaceDocument>? DetachedActivationRequested;

    public WorkspaceDocument? Find(string key) => Documents.FirstOrDefault(document => document.Key == key);

    /// <summary>Adds the document unless one with the same key is open, then activates the open instance.</summary>
    public WorkspaceDocument Open(WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var existing = Find(document.Key);
        if (existing is null) { Documents.Add(document); existing = document; OnPropertyChanged(nameof(Count)); }
        else if (!ReferenceEquals(existing, document)) document.Detach();
        Activate(existing);
        return existing;
    }

    public void Activate(WorkspaceDocument? document)
    {
        if (document is null || !Documents.Contains(document)) return;
        if (document.IsDetached) { DetachedActivationRequested?.Invoke(this, document); return; }
        var again = ReferenceEquals(Active, document);
        history.Remove(document); history.Add(document);
        foreach (var item in Documents) item.IsActive = ReferenceEquals(item, document);
        Active = document;
        if (again) Reactivated?.Invoke(this, document);
    }

    /// <summary>Ctrl+Tab / Ctrl+Shift+Tab order: tab strip order of the documents hosted in the main window, wrapping around.</summary>
    public WorkspaceDocument Neighbor(int direction)
    {
        var attached = Documents.Where(document => !document.IsDetached).ToList();
        var index = attached.IndexOf(Active);
        var count = attached.Count;
        return attached[((index + direction) % count + count) % count];
    }

    /// <summary>
    /// Moves a closable document to its own top-level window. The main host first switches to another document, so the
    /// view is released there before the window renders it. The document stays in <see cref="Documents"/>, so close,
    /// dirty, scope and permission rules apply to it unchanged.
    /// </summary>
    public bool DetachDocument(WorkspaceDocument document)
    {
        if (document is null || !document.IsClosable || document.IsDetached || !Documents.Contains(document)) return false;
        if (ReferenceEquals(Active, document)) Activate(Fallback(document));
        document.IsActive = false;
        document.IsDetached = true;
        return true;
    }

    /// <summary>Returns a detached document to the tab host: its window releases the view first, then the tab is activated.</summary>
    public bool AttachDocument(WorkspaceDocument document)
    {
        if (document is null || !document.IsDetached || !Documents.Contains(document)) return false;
        document.IsDetached = false;
        Activate(document);
        return true;
    }

    /// <summary>Gives a new draft its server key; refused when another open document already has that key.</summary>
    public bool Rekey(WorkspaceDocument document, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (document is null || !Documents.Contains(document) || document.Key == key || Find(key) is not null) return false;
        document.Key = key;
        return true;
    }

    /// <summary>
    /// Removes a closable document; the previously active document becomes active. Confirmation is the caller's job.
    /// Owned content is disposed unless <paramref name="disposeContent"/> is false (the shell retains it).
    /// </summary>
    public bool Remove(WorkspaceDocument document, bool disposeContent = true)
    {
        if (document is null || !document.IsClosable || !Documents.Remove(document)) return false;
        history.Remove(document);
        document.IsActive = false;
        document.IsDetached = false;
        document.Detach();
        if (disposeContent && document.OwnsContent) (document.Content as IDisposable)?.Dispose();
        OnPropertyChanged(nameof(Count));
        if (ReferenceEquals(Active, document)) Activate(Fallback(document));
        return true;
    }

    private WorkspaceDocument Fallback(WorkspaceDocument leaving) =>
        history.LastOrDefault(item => !ReferenceEquals(item, leaving) && !item.IsDetached && Documents.Contains(item)) ?? Home;

    /// <summary>Closes every document matching the predicate without confirmation (revoked permission, lost scope, session end).</summary>
    public IReadOnlyList<WorkspaceDocument> RemoveWhere(Func<WorkspaceDocument, bool> predicate)
    {
        var removed = Documents.Where(document => document.IsClosable && predicate(document)).ToArray();
        foreach (var document in removed) Remove(document);
        return removed;
    }

    public void Reset() => RemoveWhere(_ => true);
}

/// <summary>Content created for one document (not a shared route page); it is disposed when its tab closes.</summary>
public abstract class WorkspaceOwnedContent : PageViewModel
{
    protected WorkspaceOwnedContent(DesktopOperation operation, Localization.ILocalizationService localization) : base(operation, localization)
    {
    }
}
