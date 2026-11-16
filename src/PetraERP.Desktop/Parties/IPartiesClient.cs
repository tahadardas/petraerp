using PetraERP.ApiClient;

namespace PetraERP.Desktop.Parties;

/// <summary>
/// Client contract for Parties operations through the generated API client.
/// Server is authoritative for credit checks, derived balances, and validation.
/// </summary>
public interface IPartiesClient
{
    Task<PartyListResponse> ListPartiesAsync(Guid companyId, Guid branchId, int page, int pageSize, string? search, PartyRole? role, bool? isActive, CancellationToken cancellationToken);
    Task<PartyDetailDto> GetPartyAsync(Guid companyId, Guid branchId, Guid partyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PartyLookupDto>> LookupPartiesAsync(Guid companyId, Guid branchId, int maxResults, string? search, PartyRole? role, CancellationToken cancellationToken);
    Task<PartyDetailDto> CreatePartyAsync(Guid companyId, Guid branchId, CreatePartyRequest request, CancellationToken cancellationToken);
    Task<PartyDetailDto> UpdatePartyAsync(Guid companyId, Guid branchId, Guid partyId, UpdatePartyRequest request, CancellationToken cancellationToken);
    Task DeactivatePartyAsync(Guid companyId, Guid branchId, Guid partyId, CancellationToken cancellationToken);
    Task ReactivatePartyAsync(Guid companyId, Guid branchId, Guid partyId, CancellationToken cancellationToken);
    /// <summary>Read-only preview of the account a party saved without one would get.</summary>
    Task<PartyAccountPlanDto> GetAccountPlanAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<PartyCreditExposureDto> GetPartyCreditExposureAsync(Guid companyId, Guid branchId, Guid partyId, CancellationToken cancellationToken);
}
