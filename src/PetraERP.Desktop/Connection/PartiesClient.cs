using PetraERP.ApiClient;
using PetraERP.Desktop.Parties;

namespace PetraERP.Desktop.Connection;

public sealed class PartiesClient(DesktopSession session) : IPartiesClient
{
    public Task<PartyListResponse> ListPartiesAsync(Guid companyId, Guid branchId, int page, int pageSize, string? search, PartyRole? role, bool? isActive, CancellationToken cancellationToken) =>
        session.Api.ListPartiesAsync(companyId, branchId, page, pageSize, search, role, isActive, cancellationToken);

    public Task<PartyDetailDto> GetPartyAsync(Guid companyId, Guid branchId, Guid partyId, CancellationToken cancellationToken) =>
        session.Api.GetPartyAsync(companyId, branchId, partyId, cancellationToken);

    public Task<IReadOnlyList<PartyLookupDto>> LookupPartiesAsync(Guid companyId, Guid branchId, int maxResults, string? search, PartyRole? role, CancellationToken cancellationToken) =>
        session.Api.LookupPartiesAsync(companyId, branchId, maxResults, search, role, cancellationToken);

    // Saving a party can create its account or rename it on the server, so the cached chart of accounts is stale afterwards.
    public async Task<PartyDetailDto> CreatePartyAsync(Guid companyId, Guid branchId, CreatePartyRequest request, CancellationToken cancellationToken)
    {
        var created = await session.Api.CreatePartyAsync(companyId, branchId, request, cancellationToken).ConfigureAwait(false);
        session.InvalidateCached("AccountsTree");
        return created;
    }

    public async Task<PartyDetailDto> UpdatePartyAsync(Guid companyId, Guid branchId, Guid partyId, UpdatePartyRequest request, CancellationToken cancellationToken)
    {
        var updated = await session.Api.UpdatePartyAsync(companyId, branchId, partyId, request, cancellationToken).ConfigureAwait(false);
        session.InvalidateCached("AccountsTree");
        return updated;
    }

    public Task DeactivatePartyAsync(Guid companyId, Guid branchId, Guid partyId, CancellationToken cancellationToken) =>
        session.Api.DeactivatePartyAsync(companyId, branchId, partyId, cancellationToken);

    public Task ReactivatePartyAsync(Guid companyId, Guid branchId, Guid partyId, CancellationToken cancellationToken) =>
        session.Api.ReactivatePartyAsync(companyId, branchId, partyId, cancellationToken);

    public Task<PartyAccountPlanDto> GetAccountPlanAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.Api.GetPartyAccountPlanAsync(companyId, branchId, cancellationToken);

    public Task<PartyCreditExposureDto> GetPartyCreditExposureAsync(Guid companyId, Guid branchId, Guid partyId, CancellationToken cancellationToken) =>
        session.Api.GetPartyCreditExposureAsync(companyId, branchId, partyId, cancellationToken);
}
