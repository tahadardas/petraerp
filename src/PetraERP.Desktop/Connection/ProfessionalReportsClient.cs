using PetraERP.ApiClient;
using PetraERP.Desktop.Reporting;

namespace PetraERP.Desktop.Connection;

/// <summary>Authenticated transport through the generated, versioned reporting contract.</summary>
public sealed class ProfessionalReportsClient(DesktopSession session) : IProfessionalReportsClient, IProfessionalReportLookups
{
    private const int LookupLimit = 50;

    public async Task<PartyControlAccount> GetPartyControlAccountAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind,
        Guid partyId, CancellationToken cancellationToken)
    {
        if (kind is not (ProfessionalLookupKind.Customer or ProfessionalLookupKind.Supplier))
            throw new ArgumentOutOfRangeException(nameof(kind));
        var party = await session.Api.GetPartyAsync(companyId, branchId, partyId, cancellationToken).ConfigureAwait(false);
        var roleMatches = party.Role == PartyRole.Both || party.Role ==
            (kind == ProfessionalLookupKind.Customer ? PartyRole.Customer : PartyRole.Supplier);
        if (party.Id != partyId || party.CompanyId != companyId || !roleMatches)
            throw new InvalidOperationException("lookup.inactive_or_invalid");
        var accountId = kind == ProfessionalLookupKind.Customer ? party.ReceivableAccountId : party.PayableAccountId;
        if (accountId is null || accountId == Guid.Empty) return new(PartyControlAccountState.Missing);
        var account = await session.Api.GetAccountAsync(companyId, branchId, accountId.Value, cancellationToken).ConfigureAwait(false);
        if (account.Id != accountId || account.CompanyId != companyId || !account.IsActive || !account.PostingAllowed)
            return new(PartyControlAccountState.Ineligible);
        return new(PartyControlAccountState.Available, account.Code, account.NameArabic, account.FixedCurrencyCode);
    }

    public async Task<ProfessionalLookupItem> ResolveAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind,
        Guid id, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case ProfessionalLookupKind.Account:
                var account = await session.Api.GetAccountAsync(companyId, branchId, id, cancellationToken).ConfigureAwait(false);
                if (account.Id != id || account.CompanyId != companyId || !account.IsActive)
                    throw new InvalidOperationException("lookup.inactive_or_invalid");
                return new(account.Id, account.Code, account.NameArabic, null, account.FixedCurrencyCode);
            case ProfessionalLookupKind.CostCenter:
                var center = await session.Api.GetCostCenterAsync(companyId, branchId, id, cancellationToken).ConfigureAwait(false);
                if (center.Id != id || center.CompanyId != companyId || !center.IsActive)
                    throw new InvalidOperationException("lookup.inactive_or_invalid");
                return new(center.Id, center.Code, center.NameArabic);
            case ProfessionalLookupKind.Customer:
            case ProfessionalLookupKind.Supplier:
            case ProfessionalLookupKind.Party:
                var party = await session.Api.GetPartyAsync(companyId, branchId, id, cancellationToken).ConfigureAwait(false);
                var roleMatches = kind == ProfessionalLookupKind.Party || party.Role == PartyRole.Both || party.Role ==
                    (kind == ProfessionalLookupKind.Customer ? PartyRole.Customer : PartyRole.Supplier);
                if (party.Id != id || party.CompanyId != companyId || !roleMatches ||
                    string.IsNullOrWhiteSpace(party.Code) || string.IsNullOrWhiteSpace(party.NameArabic))
                    throw new InvalidOperationException("lookup.inactive_or_invalid");
                return new(id, party.Code, party.NameArabic);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    public async Task<IReadOnlyList<string>> ListCurrenciesAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken)
    {
        var settings = await session.Api.GetCompanyCurrencySettingsAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);
        return (settings.Currencies ?? []).Where(c => c.Code is { Length: 3 } && c.IsActive == true).Select(c => c.Code!).Order(StringComparer.Ordinal).ToArray();
    }

    public Task<ProfessionalReportCatalogResponse> CatalogAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.Api.GetProfessionalReportCatalogAsync(companyId, branchId, cancellationToken);

    public Task<ProfessionalReportExecutionResponse> ExecuteAsync(Guid companyId, Guid branchId, ProfessionalReportRequest request,
        CancellationToken cancellationToken) => session.Api.ExecuteProfessionalReportAsync(companyId, branchId, request, cancellationToken);

    public Task<IReadOnlyList<string>> PartyActivityCurrenciesAsync(Guid companyId, Guid branchId, Guid partyId, Guid fiscalYearId,
        ProfessionalReportKind kind, DateOnly asOfDate, CancellationToken cancellationToken) =>
        session.Api.GetProfessionalPartyActivityCurrenciesAsync(companyId, branchId, partyId, fiscalYearId, kind, asOfDate, cancellationToken);

    public async Task<ProfessionalLookupPage> SearchPageAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind,
        string text, int page, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        if (kind == ProfessionalLookupKind.Account)
        {
            var result = await session.Api.SearchAccountsAsync(companyId, branchId, search: text, page: page, pageSize: LookupLimit,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var items = result.Items.Select(a => new ProfessionalLookupItem(a.Id, a.Code, a.NameArabic,
                $"{(a.PostingAllowed ? "ترحيلي / Posting" : "رئيسي / Parent")} · {(a.IsActive ? "نشط / Active" : "غير نشط / Inactive")} · L{a.Level}" + (a.FixedCurrencyCode is { Length: 3 } fc ? " · " + fc : string.Empty), a.FixedCurrencyCode)).ToArray();
            return new(items, result.TotalCount, (long)page * LookupLimit < result.TotalCount, false);
        }
        if (kind == ProfessionalLookupKind.CostCenter)
        {
            var result = await session.Api.SearchCostCentersAsync(companyId, branchId, search: text, page: page, pageSize: LookupLimit,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var items = result.Items.Select(c => new ProfessionalLookupItem(c.Id, c.Code, c.NameArabic,
                c.IsActive ? "نشط / Active" : "غير نشط / Inactive")).ToArray();
            return new(items, result.TotalCount, (long)page * LookupLimit < result.TotalCount, false);
        }
        if (kind is not (ProfessionalLookupKind.Customer or ProfessionalLookupKind.Supplier or ProfessionalLookupKind.Party))
            throw new ArgumentOutOfRangeException(nameof(kind));
        // Statements are read-only history, so archived parties that still carry balances stay reachable; they are marked inactive.
        var parties = await session.Api.ListPartiesAsync(companyId, branchId, page, LookupLimit, text,
            kind == ProfessionalLookupKind.Party ? null : kind == ProfessionalLookupKind.Customer ? PartyRole.Customer : PartyRole.Supplier, null, cancellationToken).ConfigureAwait(false);
        var rows = (parties.Items ?? throw new InvalidOperationException("lookup.invalid_page"))
            .Where(p => p.Id.HasValue && p.Code is not null && p.NameArabic is not null)
            .Select(p => new ProfessionalLookupItem(p.Id!.Value, p.Code!, p.NameArabic!,
                p.IsActive == true ? "نشط / Active" : "غير نشط / Inactive")).ToArray();
        var total = parties.TotalCount ?? throw new InvalidOperationException("lookup.invalid_page");
        return new(rows, total, (long)page * LookupLimit < total, false);
    }

    public async Task<IReadOnlyList<ProfessionalLookupItem>> SearchAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind,
        string text, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case ProfessionalLookupKind.Account:
                var accounts = await session.Api.SearchAccountsAsync(companyId, branchId, search: text, page: 1, pageSize: LookupLimit,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return accounts.Items.Select(a => new ProfessionalLookupItem(a.Id, a.Code, a.NameArabic, null, a.FixedCurrencyCode)).ToArray();
            case ProfessionalLookupKind.CostCenter:
                var centers = await session.Api.SearchCostCentersAsync(companyId, branchId, search: text, page: 1, pageSize: LookupLimit,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return centers.Items.Select(c => new ProfessionalLookupItem(c.Id, c.Code, c.NameArabic)).ToArray();
            case ProfessionalLookupKind.Customer:
            case ProfessionalLookupKind.Supplier:
            case ProfessionalLookupKind.Party:
                var parties = await session.Api.LookupPartiesAsync(companyId, branchId, LookupLimit, text,
                    kind == ProfessionalLookupKind.Party ? null : kind == ProfessionalLookupKind.Customer ? PartyRole.Customer : PartyRole.Supplier, cancellationToken).ConfigureAwait(false);
                return parties.Where(p => p.Id.HasValue && p.Code is not null && p.NameArabic is not null)
                    .Select(p => new ProfessionalLookupItem(p.Id!.Value, p.Code!, p.NameArabic!,
                        p.IsActive == true ? "نشط / Active" : "غير نشط / Inactive")).ToArray();
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
