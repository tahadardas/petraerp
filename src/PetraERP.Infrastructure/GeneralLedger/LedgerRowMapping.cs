using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Organizations;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>Shared row-to-Domain mapping for ledger master data.</summary>
internal static class LedgerRowMapping
{
    internal static async Task<Company> LoadCompanyAsync(FinancialDbContext context, Guid companyId, CancellationToken cancellationToken)
    {
        var row = await context.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken).ConfigureAwait(false)
                  ?? throw new ResourceNotFoundException("company");
        var additional = await context.CompanyAdditionalCurrencies.AsNoTracking()
            .Where(c => c.CompanyId == companyId).Select(c => c.CurrencyCode)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return Company.Reconstitute(row.Id, EntityCode.Create(row.Code), BilingualName.Create(row.NameArabic, row.NameEnglish),
            CurrencyCode.Create(row.BaseCurrencyCode), additional.Select(CurrencyCode.Create), row.IsActive);
    }

    internal static AccountCurrencyPolicy CurrencyPolicy(byte mode, string? fixedCurrency) => (AccountCurrencyMode)mode switch
    {
        AccountCurrencyMode.BaseCurrencyOnly => AccountCurrencyPolicy.BaseOnly(),
        AccountCurrencyMode.AnyEnabledCurrency => AccountCurrencyPolicy.AnyEnabled(),
        AccountCurrencyMode.FixedCurrency => AccountCurrencyPolicy.Fixed(CurrencyCode.Create(fixedCurrency!)),
        _ => throw new InvalidOperationException("Unknown persisted currency mode."),
    };

    internal static Account ToDomain(AccountRow row) => Account.Reconstitute(row.Id, row.CompanyId, EntityCode.Create(row.Code),
        BilingualName.Create(row.NameArabic, row.NameEnglish), (AccountType)row.AccountType, row.PostingAllowed,
        (CostCenterPolicy)row.CostCenterPolicy, CurrencyPolicy(row.CurrencyMode, row.FixedCurrencyCode), row.ParentId, row.IsActive);

    internal static CostCenter ToDomain(CostCenterRow row) => CostCenter.Reconstitute(row.Id, row.CompanyId, EntityCode.Create(row.Code),
        BilingualName.Create(row.NameArabic, row.NameEnglish), row.IsActive, row.ParentId, row.CenterType, row.Description, row.StartDate, row.EndDate, row.ResponsiblePerson);

    /// <summary>Translates unique-index races into a 409 with a stable code; other failures propagate.</summary>
    internal static async Task SaveAsync(FinancialDbContext context, string duplicateCode, string resource, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // A rejected change must not be retried by a later save on the same context.
            context.ChangeTracker.Clear();
            throw new ConcurrencyConflictException($"The {resource} was changed by another operation.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            context.ChangeTracker.Clear();
            throw new ResourceConflictException(duplicateCode, $"The {resource} code is already in use.");
        }
    }

    internal static void VerifyVersion(byte[] current, ConcurrencyToken expected, string resource)
    {
        if (!expected.Matches(current))
        {
            throw new ConcurrencyConflictException(resource);
        }
    }

    internal static EntityCode Code(string value) => Required(() => EntityCode.Create(value), "code");

    internal static BilingualName Name(string arabic, string? english) => Required(() => BilingualName.Create(arabic, english), "name");

    private static T Required<T>(Func<T> create, string field)
    {
        try
        {
            return create();
        }
        catch (ArgumentException exception)
        {
            throw new RequestValidationException("request." + field + "_invalid", exception.Message, field);
        }
    }
}
