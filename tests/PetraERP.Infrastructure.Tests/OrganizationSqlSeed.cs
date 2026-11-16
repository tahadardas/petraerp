using Microsoft.EntityFrameworkCore;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

internal sealed record OrganizationSqlSeed(Guid FirstCompanyId, Guid SecondCompanyId)
{
    internal static async Task<OrganizationSqlSeed> CreateAsync(PetraDbContext context)
    {
        await context.Database.MigrateAsync().ConfigureAwait(false);
        context.Currencies.AddRange(
            new CurrencyRow { Code = "USD", NameArabic = "دولار أمريكي", IsActive = true },
            new CurrencyRow { Code = "SYP", NameArabic = "ليرة سورية", IsActive = true },
            new CurrencyRow { Code = "EUR", NameArabic = "يورو", IsActive = true });
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        context.Companies.AddRange(
            new CompanyRow { Id = first, Code = "البتراء", NameArabic = "شركة البتراء 🏛", BaseCurrencyCode = "USD", IsActive = true },
            new CompanyRow { Id = second, Code = "OTHER", NameArabic = "شركة أخرى", BaseCurrencyCode = "USD", IsActive = true });
        context.Branches.AddRange(
            new BranchRow { Id = Guid.CreateVersion7(), CompanyId = first, Code = "BR", NameArabic = "فرع المَزّة", IsActive = true },
            new BranchRow { Id = Guid.CreateVersion7(), CompanyId = second, Code = "BR", NameArabic = "فرع حلب", IsActive = true });
        context.CompanyAdditionalCurrencies.AddRange(
            new CompanyAdditionalCurrencyRow { CompanyId = first, CurrencyCode = "SYP" },
            new CompanyAdditionalCurrencyRow { CompanyId = second, CurrencyCode = "EUR" });
        await context.SaveChangesAsync().ConfigureAwait(false);
        return new OrganizationSqlSeed(first, second);
    }
}
