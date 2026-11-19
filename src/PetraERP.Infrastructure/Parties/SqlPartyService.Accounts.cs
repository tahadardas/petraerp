using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Parties;
using PetraERP.Domain.Parties;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Parties;

public sealed partial class SqlPartyService
{
    /// <summary>
    /// The heading new party accounts are created under. The company policy decides when it names one (a missing, inactive or
    /// posting account is an error, never silently replaced). Without a policy the chart convention applies: code 121 / 221, then
    /// a non-posting heading named for customers / suppliers. Read-only: returns null when neither exists.
    /// </summary>
    private async Task<AccountRow?> FindPartyParentAsync(Guid companyId, bool customers, CancellationToken cancellationToken)
    {
        var policy = await CompanyOptions.SqlCompanyOptionsService.EffectiveAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        if ((customers ? policy.CustomersParentAccountId : policy.SuppliersParentAccountId) is { } configured)
        {
            var chosen = await context.Accounts.FirstOrDefaultAsync(a => a.Id == configured && a.CompanyId == companyId, cancellationToken).ConfigureAwait(false);
            if (chosen is null || !chosen.IsActive || chosen.PostingAllowed)
                throw new BusinessRuleException("parties.account_parent_invalid", customers
                    ? "حساب الأب للزبائن المحدد في خيارات الشركة لم يعد صالحًا (محذوف أو موقوف أو صار حساب ترحيل)؛ صحّحه من خيارات الشركة / The customers parent account set in company options is no longer valid."
                    : "حساب الأب للموردين المحدد في خيارات الشركة لم يعد صالحًا (محذوف أو موقوف أو صار حساب ترحيل)؛ صحّحه من خيارات الشركة / The suppliers parent account set in company options is no longer valid.");
            return chosen;
        }

        var code = customers ? "121" : "221";
        var byCode = await context.Accounts.FirstOrDefaultAsync(a => a.CompanyId == companyId && a.Code == code, cancellationToken).ConfigureAwait(false);
        if (byCode is not null) return byCode;
        return customers
            ? await context.Accounts.FirstOrDefaultAsync(a => a.CompanyId == companyId && (a.NameArabic.Contains("الزبائن") || a.NameArabic.Contains("العملاء")) && !a.PostingAllowed, cancellationToken).ConfigureAwait(false)
            : await context.Accounts.FirstOrDefaultAsync(a => a.CompanyId == companyId && (a.NameArabic.Contains("الموردون") || a.NameArabic.Contains("الموردين")) && !a.PostingAllowed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What saving a party without an account would do, for the card to show before the operator agrees: the heading, whether
    /// the company policy or the chart convention chose it, and the code the new account would get. Nothing is written.
    /// </summary>
    public async Task<PartyAccountPlanResult> GetAccountPlanAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        var policy = await CompanyOptions.SqlCompanyOptionsService.EffectiveAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        async Task<PartyAccountPlanSide> SideAsync(bool customers)
        {
            var fromPolicy = (customers ? policy.CustomersParentAccountId : policy.SuppliersParentAccountId) is not null;
            var parent = await FindPartyParentAsync(companyId, customers, cancellationToken).ConfigureAwait(false);
            if (parent is null)
                return new(null, customers ? "121" : "221", customers ? "الزبائن" : "الموردون", (customers ? "121" : "221") + "001", fromPolicy, ParentWillBeCreated: true);
            var next = await GenerateNextChildAccountCodeAsync(companyId, parent, cancellationToken).ConfigureAwait(false);
            return new(parent.Id, parent.Code, parent.NameArabic, next, fromPolicy, ParentWillBeCreated: false);
        }

        return new(await SideAsync(customers: true).ConfigureAwait(false), await SideAsync(customers: false).ConfigureAwait(false));
    }

    /// <summary>True when no other party is tied to the account, so it is this party's own account and may follow its name.</summary>
    private Task<bool> IsOwnPartyAccountAsync(Guid companyId, Guid partyId, Guid accountId, CancellationToken cancellationToken) =>
        context.Parties.AllAsync(p => p.CompanyId != companyId || p.Id == partyId || (p.ReceivableAccountId != accountId && p.PayableAccountId != accountId), cancellationToken);

    private async Task<AccountRow> GetOrCreateCustomerParentAccountAsync(Guid companyId, CancellationToken cancellationToken)
    {
        // 1-2. The company policy, else the chart convention (code 121, then a heading named for customers).
        if (await FindPartyParentAsync(companyId, customers: true, cancellationToken).ConfigureAwait(false) is { } known) return known;

        AccountRow? parent;
        // 3. Look for existing parent "12" (الموجودات المتداولة) or "11" or "1" (الموجودات) to attach under
        var grandParent = await context.Accounts
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && (a.Code == "12" || a.Code == "11" || a.Code == "1"), cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        parent = new AccountRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Code = "121",
            NameArabic = "الزبائن",
            NameEnglish = "Customers",
            AccountType = grandParent?.AccountType ?? 1, // Assets
            PostingAllowed = false,
            CostCenterPolicy = 1, // Not allowed for parent
            CurrencyMode = 2,
            FxMonetaryEligibility = 1,
            ParentId = grandParent?.Id,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.Accounts.Add(parent);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return parent;
    }

    private async Task<AccountRow> GetOrCreateSupplierParentAccountAsync(Guid companyId, CancellationToken cancellationToken)
    {
        // 1-2. The company policy, else the chart convention (code 221, then a heading named for suppliers).
        if (await FindPartyParentAsync(companyId, customers: false, cancellationToken).ConfigureAwait(false) is { } known) return known;

        AccountRow? parent;
        // 3. Look for parent "22" or "21" or "2" (المطاليب / الخصوم)
        var grandParent = await context.Accounts
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && (a.Code == "22" || a.Code == "21" || a.Code == "2"), cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        parent = new AccountRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Code = "221",
            NameArabic = "الموردون",
            NameEnglish = "Suppliers",
            AccountType = grandParent?.AccountType ?? 2, // Liabilities
            PostingAllowed = false,
            CostCenterPolicy = 1,
            CurrencyMode = 2,
            FxMonetaryEligibility = 1,
            ParentId = grandParent?.Id,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.Accounts.Add(parent);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return parent;
    }

    private async Task<string> GenerateNextChildAccountCodeAsync(Guid companyId, AccountRow parent, CancellationToken cancellationToken)
    {
        var existingCodes = await context.Accounts
            .Where(a => a.CompanyId == companyId && (a.ParentId == parent.Id || a.Code.StartsWith(parent.Code)))
            .Select(a => a.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        long maxSuffix = 0;
        foreach (var code in existingCodes)
        {
            if (code.StartsWith(parent.Code, StringComparison.OrdinalIgnoreCase) && code.Length > parent.Code.Length)
            {
                var suffixStr = code[parent.Code.Length..];
                if (long.TryParse(suffixStr, out var suffixVal))
                {
                    if (suffixVal > maxSuffix) maxSuffix = suffixVal;
                }
            }
        }

        long next = maxSuffix + 1;
        var candidate = parent.Code + next.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
        while (existingCodes.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            next++;
            candidate = parent.Code + next.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
        }
        return candidate;
    }

    private async Task<Guid> EnsureCustomerAccountAsync(Guid companyId, string nameArabic, string? nameEnglish, CancellationToken cancellationToken)
    {
        var parent = await GetOrCreateCustomerParentAccountAsync(companyId, cancellationToken).ConfigureAwait(false);
        var code = await GenerateNextChildAccountCodeAsync(companyId, parent, cancellationToken).ConfigureAwait(false);
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var account = new AccountRow
        {
            Id = accountId,
            CompanyId = companyId,
            Code = code,
            NameArabic = nameArabic.Trim(),
            NameEnglish = string.IsNullOrWhiteSpace(nameEnglish) ? null : nameEnglish.Trim(),
            AccountType = parent.AccountType,
            PostingAllowed = true,
            CostCenterPolicy = 2, // Optional
            CurrencyMode = 2,     // AnyEnabledCurrency
            FxMonetaryEligibility = 1,
            ParentId = parent.Id,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.Accounts.Add(account);

        var profile = new AccountProfileRow
        {
            AccountId = accountId,
            CompanyId = companyId,
            Nature = 1, // Debit (مدين)
            IncludeCommercialPapers = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.AccountProfiles.Add(profile);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return accountId;
    }

    private async Task<Guid> EnsureSupplierAccountAsync(Guid companyId, string nameArabic, string? nameEnglish, CancellationToken cancellationToken)
    {
        var parent = await GetOrCreateSupplierParentAccountAsync(companyId, cancellationToken).ConfigureAwait(false);
        var code = await GenerateNextChildAccountCodeAsync(companyId, parent, cancellationToken).ConfigureAwait(false);
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var account = new AccountRow
        {
            Id = accountId,
            CompanyId = companyId,
            Code = code,
            NameArabic = nameArabic.Trim(),
            NameEnglish = string.IsNullOrWhiteSpace(nameEnglish) ? null : nameEnglish.Trim(),
            AccountType = parent.AccountType,
            PostingAllowed = true,
            CostCenterPolicy = 2, // Optional
            CurrencyMode = 2,     // AnyEnabledCurrency
            FxMonetaryEligibility = 1,
            ParentId = parent.Id,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.Accounts.Add(account);

        var profile = new AccountProfileRow
        {
            AccountId = accountId,
            CompanyId = companyId,
            Nature = 2, // Credit (دائن)
            IncludeCommercialPapers = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.AccountProfiles.Add(profile);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return accountId;
    }

    /// <summary>
    /// A party that is both customer and supplier has ONE ledger account (owner decision, AMN-VPAT, option A). Only a missing side is ever
    /// filled in, from the side that exists or with one new account under the customers heading: a side that already has an account
    /// may carry posted movement and is never touched here. An existing pair of different accounts is left as it is until a reviewed
    /// merge (backup, restore rehearsal and a linked transfer entry) is approved.
    /// </summary>
    private async Task EnsureUnifiedAccountAsync(Guid companyId, PartyRow row, CancellationToken cancellationToken)
    {
        if (row.Role != (int)PartyRole.Both) return;
        if (row.ReceivableAccountId is null && row.PayableAccountId is null)
        {
            var account = await EnsureCustomerAccountAsync(companyId, row.NameArabic, row.NameEnglish, cancellationToken).ConfigureAwait(false);
            row.ReceivableAccountId = account;
            row.PayableAccountId = account;
        }
        else if (row.ReceivableAccountId is null) row.ReceivableAccountId = row.PayableAccountId;
        else if (row.PayableAccountId is null) row.PayableAccountId = row.ReceivableAccountId;
    }

    public async Task SyncMissingPartyAccountsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var incompleteBoth = await context.Parties
            .Where(p => p.CompanyId == companyId && p.IsActive && !p.IsCashCustomer && p.Role == (int)PartyRole.Both
                && (p.ReceivableAccountId == null || p.PayableAccountId == null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var party in incompleteBoth)
            await EnsureUnifiedAccountAsync(companyId, party, cancellationToken).ConfigureAwait(false);
        if (incompleteBoth.Count > 0)
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var unlinkedCustomers = await context.Parties
            .Where(p => p.CompanyId == companyId && p.IsActive && !p.IsCashCustomer
                && (p.Role == (int)PartyRole.Customer || p.Role == (int)PartyRole.Both)
                && p.ReceivableAccountId == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var party in unlinkedCustomers)
        {
            party.ReceivableAccountId = await EnsureCustomerAccountAsync(companyId, party.NameArabic, party.NameEnglish, cancellationToken).ConfigureAwait(false);
        }

        var unlinkedSuppliers = await context.Parties
            .Where(p => p.CompanyId == companyId && p.IsActive && !p.IsCashCustomer
                && (p.Role == (int)PartyRole.Supplier || p.Role == (int)PartyRole.Both)
                && p.PayableAccountId == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var party in unlinkedSuppliers)
        {
            party.PayableAccountId = await EnsureSupplierAccountAsync(companyId, party.NameArabic, party.NameEnglish, cancellationToken).ConfigureAwait(false);
        }

        if (unlinkedCustomers.Count > 0 || unlinkedSuppliers.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
