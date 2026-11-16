using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Parties;

namespace PetraERP.Infrastructure.Parties;

/// <summary>Al-Ameen party profile: validated here, stored on the party row, read back as one record. A null profile leaves the row untouched.</summary>
public sealed partial class SqlPartyService
{
    private async Task ApplyProfileAsync(PartyRow row, PartyProfileData? profile, Guid companyId, CancellationToken cancellationToken)
    {
        if (profile is null) return;
        profile.Validate();
        await RequireAccountAsync(companyId, profile.ConditionalDiscountAccountId, "profile.conditionalDiscountAccountId", cancellationToken).ConfigureAwait(false);
        await RequireAccountAsync(companyId, profile.VatAccountId, "profile.vatAccountId", cancellationToken).ConfigureAwait(false);
        if (profile.Barcode is not null
            && await context.Parties.AsNoTracking().AnyAsync(p => p.CompanyId == companyId && p.Id != row.Id && p.Barcode == profile.Barcode, cancellationToken).ConfigureAwait(false))
            throw new ResourceConflictException("Party", "The barcode is already used by another customer or supplier.");
        row.Title = profile.Title;
        row.Suffix = profile.Suffix;
        row.Barcode = profile.Barcode;
        row.HideInSearch = profile.HideInSearch;
        row.DiscountPercent = profile.DiscountPercent;
        row.SalesPriceType = profile.SalesPriceType;
        row.ConditionalDiscountAccountId = profile.ConditionalDiscountAccountId;
        row.VatAccountId = profile.VatAccountId;
        row.Notes = profile.Notes;
    }

    private async Task RequireAccountAsync(Guid companyId, Guid? accountId, string field, CancellationToken cancellationToken)
    {
        if (accountId is not { } id) return;
        var ok = await context.Accounts.AsNoTracking().AnyAsync(a => a.CompanyId == companyId && a.Id == id && a.IsActive && a.PostingAllowed, cancellationToken).ConfigureAwait(false);
        if (!ok) throw new BusinessRuleException("parties.profile_account_invalid", "The account must be an active, postable account of this company.", field);
    }

    private static PartyProfileData ToProfile(PartyRow row) => new(
        Title: row.Title, Suffix: row.Suffix, Barcode: row.Barcode, HideInSearch: row.HideInSearch, DiscountPercent: row.DiscountPercent,
        SalesPriceType: row.SalesPriceType, ConditionalDiscountAccountId: row.ConditionalDiscountAccountId, VatAccountId: row.VatAccountId, Notes: row.Notes);
}
