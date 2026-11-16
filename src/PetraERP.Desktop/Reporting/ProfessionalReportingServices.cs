using PetraERP.ApiClient;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Reporting;

/// <summary>Every result is authorized and computed by the API; the desktop only collects criteria.</summary>
public interface IProfessionalReportsClient
{
    Task<ProfessionalReportCatalogResponse> CatalogAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<ProfessionalReportExecutionResponse> ExecuteAsync(Guid companyId, Guid branchId, ProfessionalReportRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> PartyActivityCurrenciesAsync(Guid companyId, Guid branchId, Guid partyId, Guid fiscalYearId,
        ProfessionalReportKind kind, DateOnly asOfDate, CancellationToken cancellationToken);
}

public enum ProfessionalLookupKind { Account, Customer, Supplier, CostCenter, Party }
public sealed record ProfessionalLookupItem(Guid Id, string Code, string Name, string? Context = null, string? CurrencyCode = null)
{
    public string Label => Code + " · " + Name + (string.IsNullOrEmpty(Context) ? string.Empty : " — " + Context);
}

public sealed record ProfessionalLookupPage(IReadOnlyList<ProfessionalLookupItem> Items, long? Total, bool HasMore, bool Truncated);

public enum PartyControlAccountState { Available, Missing, Ineligible }
public sealed record PartyControlAccount(PartyControlAccountState State, string? Code = null, string? Name = null, string? CurrencyCode = null);

/// <summary>Bounded, scoped code/name lookup. Implementations must call the existing authorized APIs.</summary>
public interface IProfessionalReportLookups
{
    Task<IReadOnlyList<ProfessionalLookupItem>> SearchAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind,
        string text, CancellationToken cancellationToken);
    Task<ProfessionalLookupPage> SearchPageAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind,
        string text, int page, CancellationToken cancellationToken);
    Task<ProfessionalLookupItem> ResolveAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind,
        Guid id, CancellationToken cancellationToken);
    Task<PartyControlAccount> GetPartyControlAccountAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind,
        Guid partyId, CancellationToken cancellationToken);
    /// <summary>Active company currency codes offered in the report currency drop-down.</summary>
    Task<IReadOnlyList<string>> ListCurrenciesAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}

public sealed record ProfessionalReportPreset(string Name, ProfessionalReportRequest Parameters);

/// <summary>Criteria only, scoped to the authenticated user, company, branch and fiscal workspace.</summary>
public interface IProfessionalReportPresetStore
{
    Task<IReadOnlyList<ProfessionalReportPreset>> LoadAsync(ShellContext context, ProfessionalReportKind kind, CancellationToken cancellationToken);
    Task SaveAsync(ShellContext context, ProfessionalReportPreset preset, CancellationToken cancellationToken);
}

public sealed record ProfessionalPreviewRequest(ProfessionalReportExecutionResponse Snapshot, string Title,
    IReadOnlyDictionary<ProfessionalReportField, string> Headings, Guid ActorId = default);
