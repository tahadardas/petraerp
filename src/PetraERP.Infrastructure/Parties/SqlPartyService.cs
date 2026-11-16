using System.Text.Json;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Parties;
using PetraERP.Domain.Parties;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Parties;

public sealed partial class SqlPartyService(FinancialDbContext context) : IPartyService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public async Task<PartyPageResult> GetPartiesAsync(
        Guid companyId,
        PartySearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);

        var baseQuery = context.Parties
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId);

        if (query.RoleFilter.HasValue)
        {
            var roleVal = (int)query.RoleFilter.Value;
            baseQuery = baseQuery.Where(p => p.Role == roleVal || p.Role == (int)PartyRole.Both);
        }

        if (query.IsActiveFilter.HasValue)
        {
            baseQuery = baseQuery.Where(p => p.IsActive == query.IsActiveFilter.Value);
        }

        foreach (var token in ArabicTextNormalizer.Tokenize(query.SearchText))
            baseQuery = baseQuery.Where(PartyTokenPrefix(token));

        await SyncMissingPartyAccountsAsync(companyId, cancellationToken).ConfigureAwait(false);
        var totalCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var offset = (long)(pageNumber - 1) * pageSize;
        var pagedRows = offset >= totalCount ? [] : await baseQuery.OrderBy(p => p.Code).ThenBy(p => p.Id)
            .Skip((int)offset).Take(pageSize).ToListAsync(cancellationToken).ConfigureAwait(false);

        var partyBalances = await PartyFinancialFacts.BaseBalancesAsync(context, companyId,
            pagedRows.Select(p => p.Id).ToArray(), cancellationToken).ConfigureAwait(false);

        var items = pagedRows.Select(p =>
        {
            var (balance, availableCredit) = CalculateBalances(p, partyBalances[p.Id]);
            return new PartySummaryResult(
                p.Id,
                p.CompanyId,
                p.Code,
                p.NameArabic,
                p.NameEnglish,
                (PartyRole)p.Role,
                p.IsActive,
                p.TaxNumber,
                p.DefaultCurrencyCode,
                p.ReceivableAccountId,
                p.PayableAccountId,
                p.CreditLimit,
                p.PaymentTermDays,
                (CreditPolicyKind)p.CreditPolicyKind,
                balance,
                availableCredit)
            {
                IsCashCustomer = p.IsCashCustomer, IsCreditBlocked = p.IsCreditBlocked,
                ReceivableBalance = partyBalances[p.Id].Receivable, PayableBalance = partyBalances[p.Id].Payable,
            };
        }).ToList();

        return new PartyPageResult(items, totalCount, pageNumber, pageSize);
    }

    private static Expression<Func<PartyRow, bool>> PartyTokenPrefix(string token)
    {
        // Keep these boundaries aligned with ArabicTextNormalizer.Tokenize. BIN2 preserves its ordinal comparison.
        char[] separators = [' ', '\t', '\r', '\n', '-', '_', '/', '\\', ',', '.', ';', ':', '(', ')', '[', ']', '{', '}'];
        Expression<Func<PartyRow, string>> field = p => EF.Functions.Collate(p.NormalizedSearchText, StorageConventions.OrdinalCollation);
        var prefix = Expression.Call(field.Body, nameof(string.StartsWith), Type.EmptyTypes, Expression.Constant(token));
        Expression match = Expression.OrElse(prefix, Expression.Call(field.Body, nameof(string.StartsWith), Type.EmptyTypes, Expression.Constant("ال" + token)));
        foreach (var separator in separators)
        {
            match = Expression.OrElse(match, Expression.Call(field.Body, nameof(string.Contains), Type.EmptyTypes, Expression.Constant(separator + token)));
            match = Expression.OrElse(match, Expression.Call(field.Body, nameof(string.Contains), Type.EmptyTypes, Expression.Constant(separator + "ال" + token)));
        }
        return Expression.Lambda<Func<PartyRow, bool>>(match, field.Parameters);
    }

    public async Task<PartyDetailResult> GetPartyByIdAsync(
        Guid companyId,
        Guid partyId,
        CancellationToken cancellationToken = default)
    {
        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);

        var row = await context.Parties
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Id == partyId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            throw new ResourceNotFoundException("Party");
        }

        if (((row.Role == (int)PartyRole.Customer || row.Role == (int)PartyRole.Both) && row.ReceivableAccountId == null) ||
            ((row.Role == (int)PartyRole.Supplier || row.Role == (int)PartyRole.Both) && row.PayableAccountId == null))
        {
            await SyncMissingPartyAccountsAsync(companyId, cancellationToken).ConfigureAwait(false);
            row = await context.Parties
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Id == partyId, cancellationToken)
                .ConfigureAwait(false) ?? throw new ResourceNotFoundException("Party");
        }

        var partyBalances = await PartyFinancialFacts.BaseBalancesAsync(context, companyId, [row.Id], cancellationToken).ConfigureAwait(false);
        var facts = partyBalances[row.Id];
        var (balance, availableCredit) = CalculateBalances(row, facts);
        var history = await context.PartyPolicyAudit.AsNoTracking().Where(a => a.CompanyId == companyId && a.PartyId == row.Id)
            .OrderByDescending(a => a.DateUtc).ThenByDescending(a => a.Id).Take(100)
            .Select(a => new PartyPolicyAuditResult(a.Id,a.ActorId,a.BeforeCreditLimit,a.AfterCreditLimit,a.BeforePolicy,a.AfterPolicy,a.BeforeBlocked,a.AfterBlocked,a.DateUtc))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var contacts = DeserializeList<PartyContactResult>(row.ContactsJson);
        var addresses = DeserializeList<PartyAddressResult>(row.AddressesJson);

        return new PartyDetailResult(
            row.Id,
            row.CompanyId,
            row.Code,
            row.NameArabic,
            row.NameEnglish,
            (PartyRole)row.Role,
            row.IsActive,
            row.TaxNumber,
            row.CommercialRegistration,
            row.DefaultCurrencyCode,
            row.ReceivableAccountId,
            row.PayableAccountId,
            row.CreditLimit,
            row.PaymentTermDays,
            (CreditPolicyKind)row.CreditPolicyKind,
            balance,
            availableCredit,
            contacts,
            addresses,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            ConcurrencyToken.FromBytes(row.RowVersion).ToBase64())
        {
            IsCashCustomer = row.IsCashCustomer, IsCreditBlocked = row.IsCreditBlocked, Profile = ToProfile(row),
            ReceivableBalance = facts.Receivable, PayableBalance = facts.Payable, PolicyHistory = history,
        };
    }

    public async Task<PartyDetailResult?> FindPartyByCodeAsync(
        Guid companyId,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);

        var trimmedCode = code.Trim();
        var row = await context.Parties
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Code == trimmedCode, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        return await GetPartyByIdAsync(companyId, row.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PartyLookupItem>> LookupPartiesAsync(
        Guid companyId,
        string? searchTerm,
        PartyRole? roleFilter = null,
        int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        await SyncMissingPartyAccountsAsync(companyId, cancellationToken).ConfigureAwait(false);

        var query = context.Parties
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.IsActive && !p.HideInSearch);

        if (roleFilter.HasValue)
        {
            var roleVal = (int)roleFilter.Value;
            query = query.Where(p => p.Role == roleVal || p.Role == (int)PartyRole.Both);
        }

        var clampedLimit = Math.Clamp(maxResults, 1, 100);
        List<PartyRow> matches;

        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            matches = await query
                .OrderBy(p => p.Code)
                .Take(clampedLimit)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var raw = searchTerm.Trim();
            var norm = ArabicTextNormalizer.Normalize(raw);

            var candidates = await query
                .Where(p => p.Code.Contains(raw)
                    || p.NormalizedSearchText.Contains(norm)
                    || p.NameArabic.Contains(raw)
                    || (p.NameEnglish != null && p.NameEnglish.Contains(raw)))
                .OrderBy(p => p.Code)
                .Take(clampedLimit * 3)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            matches = candidates
                .Where(p => ArabicTextNormalizer.MatchesTokenPrefixes(searchTerm, p.NormalizedSearchText))
                .Take(clampedLimit)
                .ToList();

            if (matches.Count == 0 && candidates.Count > 0)
            {
                matches = candidates.Take(clampedLimit).ToList();
            }
        }

        var partyBalances = await PartyFinancialFacts.BaseBalancesAsync(context, companyId,
            matches.Select(p => p.Id).ToArray(), cancellationToken).ConfigureAwait(false);

        return matches.Select(p =>
        {
            var (balance, availableCredit) = CalculateBalances(p, partyBalances[p.Id]);
            return new PartyLookupItem(
                p.Id,
                p.Code,
                p.NameArabic,
                p.NameEnglish,
                (PartyRole)p.Role,
                p.IsActive,
                p.ReceivableAccountId,
                p.PayableAccountId,
                p.CreditLimit,
                balance,
                availableCredit)
            {
                IsCashCustomer = p.IsCashCustomer, IsCreditBlocked = p.IsCreditBlocked,
                ReceivableBalance = partyBalances[p.Id].Receivable, PayableBalance = partyBalances[p.Id].Payable,
            };
        }).ToList();
    }

    public async Task<PartyDetailResult> CreatePartyAsync(
        Guid companyId,
        CreatePartyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var transactionLifetime = transaction.ConfigureAwait(false);
        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);

        ValidateCreditPolicy(command.CreditLimit, command.PaymentTermDays, command.PolicyKind);
        var trimmedCode = command.Code.Trim();
        if (string.Equals(trimmedCode, "CASH-CUSTOMER", StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("parties.cash_code_reserved", "هذا الرمز محجوز للزبون النقدي العام / This code is reserved for the system walk-in cash customer.");
        var exists = await context.Parties
            .AnyAsync(p => p.CompanyId == companyId && p.Code == trimmedCode, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            throw new ResourceConflictException("Party", $"A party with code '{trimmedCode}' already exists for this company.");
        }

        await ValidateAccountsAsync(companyId, command.Role, command.ReceivableAccountId, command.PayableAccountId, cancellationToken).ConfigureAwait(false);
        await ValidateDefaultCurrencyAsync(companyId, command.DefaultCurrencyCode, cancellationToken).ConfigureAwait(false);

        var partyId = Guid.NewGuid();
        var normalizedSearch = BuildNormalizedSearch(trimmedCode, command.NameArabic, command.NameEnglish, command.TaxNumber, command.CommercialRegistration);

        var contactsJson = command.Contacts is { Count: > 0 }
            ? JsonSerializer.Serialize(command.Contacts.Select(c => new PartyContactResult(c.Id ?? Guid.NewGuid(), c.Name, c.Role, c.Phone, c.Mobile, c.Email)), JsonOptions)
            : null;

        var addressesJson = command.Addresses is { Count: > 0 }
            ? JsonSerializer.Serialize(command.Addresses.Select(a => new PartyAddressResult(a.Id ?? Guid.NewGuid(), a.Label, a.City, a.Street, a.PostalCode, a.Notes)), JsonOptions)
            : null;

        var row = new PartyRow
        {
            Id = partyId,
            CompanyId = companyId,
            Code = trimmedCode,
            NameArabic = command.NameArabic.Trim(),
            NameEnglish = string.IsNullOrWhiteSpace(command.NameEnglish) ? null : command.NameEnglish.Trim(),
            NormalizedSearchText = normalizedSearch,
            Role = (int)command.Role,
            IsActive = true,
            TaxNumber = string.IsNullOrWhiteSpace(command.TaxNumber) ? null : command.TaxNumber.Trim(),
            CommercialRegistration = string.IsNullOrWhiteSpace(command.CommercialRegistration) ? null : command.CommercialRegistration.Trim(),
            DefaultCurrencyCode = string.IsNullOrWhiteSpace(command.DefaultCurrencyCode) ? null : command.DefaultCurrencyCode.Trim().ToUpperInvariant(),
            ReceivableAccountId = command.ReceivableAccountId,
            PayableAccountId = command.PayableAccountId,
            CreditLimit = command.CreditLimit,
            PaymentTermDays = command.PaymentTermDays,
            CreditPolicyKind = (int)command.PolicyKind,
            IsCreditBlocked = command.IsCreditBlocked,
            ContactsJson = contactsJson,
            AddressesJson = addressesJson,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        if ((row.Role == (int)PartyRole.Customer || row.Role == (int)PartyRole.Both) && row.ReceivableAccountId == null)
        {
            row.ReceivableAccountId = await EnsureCustomerAccountAsync(companyId, row.NameArabic, row.NameEnglish, cancellationToken).ConfigureAwait(false);
        }
        if ((row.Role == (int)PartyRole.Supplier || row.Role == (int)PartyRole.Both) && row.PayableAccountId == null)
        {
            row.PayableAccountId = await EnsureSupplierAccountAsync(companyId, row.NameArabic, row.NameEnglish, cancellationToken).ConfigureAwait(false);
        }

        await ApplyProfileAsync(row, command.Profile, companyId, cancellationToken).ConfigureAwait(false);
        context.Parties.Add(row);
        await AddPolicyAuditAsync(row, 0m, (int)CreditPolicyKind.NoLimit, false,
            command.ChangedByUserId, command.ChangedInBranchId, true, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPartyByIdAsync(companyId, partyId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PartyDetailResult> UpdatePartyAsync(
        Guid companyId,
        Guid partyId,
        UpdatePartyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var transactionLifetime = transaction.ConfigureAwait(false);
        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);

        var row = await context.Parties
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Id == partyId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            throw new ResourceNotFoundException("Party");
        }

        if (((row.Role == (int)PartyRole.Customer || row.Role == (int)PartyRole.Both) && row.ReceivableAccountId == null) ||
            ((row.Role == (int)PartyRole.Supplier || row.Role == (int)PartyRole.Both) && row.PayableAccountId == null))
        {
            await SyncMissingPartyAccountsAsync(companyId, cancellationToken).ConfigureAwait(false);
            row = await context.Parties
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Id == partyId, cancellationToken)
                .ConfigureAwait(false) ?? throw new ResourceNotFoundException("Party");
        }

        RequireMutableParty(row);
        ValidateCreditPolicy(command.CreditLimit, command.PaymentTermDays, command.PolicyKind);
        if (string.IsNullOrWhiteSpace(command.ExpectedVersion))
            throw new RequestValidationException("parties.version_required", "أعد تحميل البطاقة لإرسال النسخة الحالية / Reload the party and send its current version.", "expectedVersion");
        if (command.ExpectedVersion is not null)
        {
            var currentToken = ConcurrencyToken.FromBytes(row.RowVersion).ToBase64();
            if (!string.Equals(currentToken, command.ExpectedVersion, StringComparison.Ordinal))
            {
                throw new ConcurrencyConflictException("Party");
            }
        }

        await ValidateAccountsAsync(companyId, command.Role, command.ReceivableAccountId, command.PayableAccountId, cancellationToken).ConfigureAwait(false);
        await ValidateDefaultCurrencyAsync(companyId, command.DefaultCurrencyCode, cancellationToken).ConfigureAwait(false);

        var normalizedSearch = BuildNormalizedSearch(row.Code, command.NameArabic, command.NameEnglish, command.TaxNumber, command.CommercialRegistration);

        var contactsJson = command.Contacts is { Count: > 0 }
            ? JsonSerializer.Serialize(command.Contacts.Select(c => new PartyContactResult(c.Id ?? Guid.NewGuid(), c.Name, c.Role, c.Phone, c.Mobile, c.Email)), JsonOptions)
            : null;

        var addressesJson = command.Addresses is { Count: > 0 }
            ? JsonSerializer.Serialize(command.Addresses.Select(a => new PartyAddressResult(a.Id ?? Guid.NewGuid(), a.Label, a.City, a.Street, a.PostalCode, a.Notes)), JsonOptions)
            : null;

        await AddPolicyAuditAsync(row, command.CreditLimit, (int)command.PolicyKind, command.IsCreditBlocked,
            command.ChangedByUserId, command.ChangedInBranchId, false, cancellationToken).ConfigureAwait(false);
        row.NameArabic = command.NameArabic.Trim();
        row.NameEnglish = string.IsNullOrWhiteSpace(command.NameEnglish) ? null : command.NameEnglish.Trim();
        row.NormalizedSearchText = normalizedSearch;
        row.Role = (int)command.Role;
        row.TaxNumber = string.IsNullOrWhiteSpace(command.TaxNumber) ? null : command.TaxNumber.Trim();
        row.CommercialRegistration = string.IsNullOrWhiteSpace(command.CommercialRegistration) ? null : command.CommercialRegistration.Trim();
        row.DefaultCurrencyCode = string.IsNullOrWhiteSpace(command.DefaultCurrencyCode) ? null : command.DefaultCurrencyCode.Trim().ToUpperInvariant();
        row.ReceivableAccountId = command.ReceivableAccountId;
        row.PayableAccountId = command.PayableAccountId;
        if ((row.Role == (int)PartyRole.Customer || row.Role == (int)PartyRole.Both) && row.ReceivableAccountId == null)
        {
            row.ReceivableAccountId = await EnsureCustomerAccountAsync(companyId, row.NameArabic, row.NameEnglish, cancellationToken).ConfigureAwait(false);
        }
        if ((row.Role == (int)PartyRole.Supplier || row.Role == (int)PartyRole.Both) && row.PayableAccountId == null)
        {
            row.PayableAccountId = await EnsureSupplierAccountAsync(companyId, row.NameArabic, row.NameEnglish, cancellationToken).ConfigureAwait(false);
        }
        if (row.ReceivableAccountId != null)
        {
            var acc = await context.Accounts.FirstOrDefaultAsync(a => a.Id == row.ReceivableAccountId, cancellationToken).ConfigureAwait(false);
            // The account follows the party's name only when it is the party's own; a shared account keeps its name.
            if (acc is not null && acc.NameArabic != row.NameArabic
                && await IsOwnPartyAccountAsync(companyId, row.Id, acc.Id, cancellationToken).ConfigureAwait(false))
            {
                acc.NameArabic = row.NameArabic;
                acc.NameEnglish = row.NameEnglish;
                acc.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }
        if (row.PayableAccountId != null)
        {
            var acc = await context.Accounts.FirstOrDefaultAsync(a => a.Id == row.PayableAccountId, cancellationToken).ConfigureAwait(false);
            if (acc is not null && acc.NameArabic != row.NameArabic
                && await IsOwnPartyAccountAsync(companyId, row.Id, acc.Id, cancellationToken).ConfigureAwait(false))
            {
                acc.NameArabic = row.NameArabic;
                acc.NameEnglish = row.NameEnglish;
                acc.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }
        row.CreditLimit = command.CreditLimit;
        row.PaymentTermDays = command.PaymentTermDays;
        row.CreditPolicyKind = (int)command.PolicyKind;
        row.IsCreditBlocked = command.IsCreditBlocked;
        await ApplyProfileAsync(row, command.Profile, companyId, cancellationToken).ConfigureAwait(false);
        row.ContactsJson = contactsJson;
        row.AddressesJson = addressesJson;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPartyByIdAsync(companyId, partyId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeactivatePartyAsync(
        Guid companyId,
        Guid partyId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var transactionLifetime = transaction.ConfigureAwait(false);
        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);

        var row = await context.Parties
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Id == partyId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            throw new ResourceNotFoundException("Party");
        }

        if (((row.Role == (int)PartyRole.Customer || row.Role == (int)PartyRole.Both) && row.ReceivableAccountId == null) ||
            ((row.Role == (int)PartyRole.Supplier || row.Role == (int)PartyRole.Both) && row.PayableAccountId == null))
        {
            await SyncMissingPartyAccountsAsync(companyId, cancellationToken).ConfigureAwait(false);
            row = await context.Parties
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Id == partyId, cancellationToken)
                .ConfigureAwait(false) ?? throw new ResourceNotFoundException("Party");
        }

        RequireMutableParty(row);
        row.IsActive = false;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReactivatePartyAsync(
        Guid companyId,
        Guid partyId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var transactionLifetime = transaction.ConfigureAwait(false);
        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);

        var row = await context.Parties
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Id == partyId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            throw new ResourceNotFoundException("Party");
        }

        if (((row.Role == (int)PartyRole.Customer || row.Role == (int)PartyRole.Both) && row.ReceivableAccountId == null) ||
            ((row.Role == (int)PartyRole.Supplier || row.Role == (int)PartyRole.Both) && row.PayableAccountId == null))
        {
            await SyncMissingPartyAccountsAsync(companyId, cancellationToken).ConfigureAwait(false);
            row = await context.Parties
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Id == partyId, cancellationToken)
                .ConfigureAwait(false) ?? throw new ResourceNotFoundException("Party");
        }

        RequireMutableParty(row);
        row.IsActive = true;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PartyCreditExposureResult> GetPartyCreditExposureAsync(
        Guid companyId,
        Guid partyId,
        CancellationToken cancellationToken = default)
    {
        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);

        var row = await context.Parties
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Id == partyId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            throw new ResourceNotFoundException("Party");
        }

        if (((row.Role == (int)PartyRole.Customer || row.Role == (int)PartyRole.Both) && row.ReceivableAccountId == null) ||
            ((row.Role == (int)PartyRole.Supplier || row.Role == (int)PartyRole.Both) && row.PayableAccountId == null))
        {
            await SyncMissingPartyAccountsAsync(companyId, cancellationToken).ConfigureAwait(false);
            row = await context.Parties
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Id == partyId, cancellationToken)
                .ConfigureAwait(false) ?? throw new ResourceNotFoundException("Party");
        }

        var partyBalances = await PartyFinancialFacts.BaseBalancesAsync(context, companyId, [row.Id], cancellationToken).ConfigureAwait(false);
        var facts = partyBalances[row.Id];
        var balance = facts.Receivable;
        var totalDebit = facts.ReceivableDebit;
        var totalCredit = facts.ReceivableCredit;
        var (_, availableCredit) = CalculateBalances(row, facts);
        var policyKind = (CreditPolicyKind)row.CreditPolicyKind;
        var isExceeded = row.IsCreditBlocked || (policyKind != CreditPolicyKind.NoLimit && balance > row.CreditLimit);

        return new PartyCreditExposureResult(
            row.Id,
            row.CreditLimit,
            totalDebit,
            totalCredit,
            balance,
            availableCredit,
            policyKind,
            isExceeded);
    }

    private async Task ValidateCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var companyExists = await context.Companies
            .AnyAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false);

        if (!companyExists)
        {
            throw new ResourceNotFoundException("Company");
        }
    }

    private async Task ValidateAccountsAsync(
        Guid companyId,
        PartyRole role,
        Guid? receivableAccountId,
        Guid? payableAccountId,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(role))
            throw new BusinessRuleException("parties.role_invalid", "صفة الجهة غير صالحة / Invalid party role.");
        if (role == PartyRole.Customer && payableAccountId.HasValue)
        {
            throw RuleViolation.Create(
                RuleViolation.PartyRoleInvalid,
                "A party with Customer-only role cannot have a payable account assigned.");
        }

        if (role == PartyRole.Supplier && receivableAccountId.HasValue)
        {
            throw RuleViolation.Create(
                RuleViolation.PartyRoleInvalid,
                "A party with Supplier-only role cannot have a receivable account assigned.");
        }

        if (receivableAccountId.HasValue)
        {
            var acc = await context.Accounts
                .FirstOrDefaultAsync(a => a.Id == receivableAccountId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (acc is null)
            {
                throw new ResourceNotFoundException("Account");
            }

            if (acc.CompanyId != companyId)
            {
                throw RuleViolation.Create(
                    RuleViolation.PartyAccountCrossCompany,
                    "Receivable account must belong to the same company as the party.");
            }

            if (!acc.IsActive || !acc.PostingAllowed)
            {
                throw RuleViolation.Create(
                    RuleViolation.PartyAccountInvalid,
                    "Receivable account must be an active posting account.");
            }
        }

        if (payableAccountId.HasValue)
        {
            var acc = await context.Accounts
                .FirstOrDefaultAsync(a => a.Id == payableAccountId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (acc is null)
            {
                throw new ResourceNotFoundException("Account");
            }

            if (acc.CompanyId != companyId)
            {
                throw RuleViolation.Create(
                    RuleViolation.PartyAccountCrossCompany,
                    "Payable account must belong to the same company as the party.");
            }

            if (!acc.IsActive || !acc.PostingAllowed)
            {
                throw RuleViolation.Create(
                    RuleViolation.PartyAccountInvalid,
                    "Payable account must be an active posting account.");
            }
        }
    }

    private static (decimal balance, decimal availableCredit) CalculateBalances(PartyRow party, PartyFinancialBalance facts)
    {
        // Legacy CurrentBalance is customer-side for Both; explicit receivable/payable fields expose both sides independently.
        var balance = party.Role == (int)PartyRole.Supplier ? facts.Payable : facts.Receivable;
        var available = party.IsCreditBlocked || party.CreditPolicyKind == (int)CreditPolicyKind.NoLimit
            ? 0m : Math.Max(0m, party.CreditLimit - facts.Receivable);
        return (balance, available);
    }

    private static string BuildNormalizedSearch(string code, string arabic, string? english, string? tax, string? cr)
    {
        var raw = $"{code} {arabic} {english} {tax} {cr}";
        var normalized = ArabicTextNormalizer.Normalize(raw);
        if (normalized.Length > 500)
        {
            normalized = normalized[..500];
        }

        return normalized;
    }

    private static List<T> DeserializeList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>A party default currency is optional; when set it must be the base or an enabled company currency (never assumed).</summary>
    private async Task ValidateDefaultCurrencyAsync(Guid companyId, string? currency, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currency)) return;
        var code = currency.Trim().ToUpperInvariant();
        var baseCurrency = await context.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.BaseCurrencyCode)
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        await CompanyCurrencies.RequireEnabledAsync(context, companyId, baseCurrency, code, "parties", cancellationToken).ConfigureAwait(false);
    }
}
