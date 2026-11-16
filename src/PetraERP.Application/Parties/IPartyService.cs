using PetraERP.Domain.Parties;

namespace PetraERP.Application.Parties;

public interface IPartyService
{
    Task<PartyPageResult> GetPartiesAsync(Guid companyId, PartySearchQuery query, CancellationToken cancellationToken = default);
    Task<PartyDetailResult> GetPartyByIdAsync(Guid companyId, Guid partyId, CancellationToken cancellationToken = default);
    Task<PartyDetailResult?> FindPartyByCodeAsync(Guid companyId, string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PartyLookupItem>> LookupPartiesAsync(Guid companyId, string? searchTerm, PartyRole? roleFilter = null, int maxResults = 20, CancellationToken cancellationToken = default);
    Task<PartyDetailResult> CreatePartyAsync(Guid companyId, CreatePartyCommand command, CancellationToken cancellationToken = default);
    Task<PartyDetailResult> UpdatePartyAsync(Guid companyId, Guid partyId, UpdatePartyCommand command, CancellationToken cancellationToken = default);
    Task DeactivatePartyAsync(Guid companyId, Guid partyId, string? reason = null, CancellationToken cancellationToken = default);
    Task ReactivatePartyAsync(Guid companyId, Guid partyId, CancellationToken cancellationToken = default);
    /// <summary>Read-only preview of the account a party saved without one would get.</summary>
    Task<PartyAccountPlanResult> GetAccountPlanAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<PartyCreditExposureResult> GetPartyCreditExposureAsync(Guid companyId, Guid partyId, CancellationToken cancellationToken = default);
}
